using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SpireTerraria.Next {
 internal static class BattleProbe {
  private const Operation StartOperation=(Operation)7,StepOperation=(Operation)8,EndOperation=(Operation)9;
  private static readonly List<string> Checks=new List<string>();
  private static readonly Dictionary<string,object> Report=new Dictionary<string,object>();
  private static readonly Stopwatch Elapsed=Stopwatch.StartNew();
  private static string outputDirectory;
  private static int processId;
  private sealed class State {
   internal int Tick,PlayerLife,PlayerMaxLife,NpcLife,NpcMaxLife,ActiveProjectiles;
   internal float PlayerX,PlayerY,NpcX,NpcY;
   internal bool PlayerDead,NpcActive;
   internal long FrameId;
  }
  private sealed class Session {
   internal string Name;
   internal int Width,Height;
   internal State State;
  }
  private static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);Checks.Add(name);Console.WriteLine("PASS "+name);}
  private static void Deadline(){if(Elapsed.ElapsedMilliseconds>60000)throw new TimeoutException("M1 battle probe exceeded its 60 second suite budget.");}
  private static byte[] Empty(){return new byte[0];}
  private static void Success(Packet packet){if(packet.IsError){using(BinaryReader reader=Protocol.Reader(packet)){throw new InvalidOperationException(reader.ReadString()+": "+reader.ReadString());}}}
  private static bool Finite(float value){return !Single.IsNaN(value)&&!Single.IsInfinity(value);}
  private static State ReadState(BinaryReader reader){
   State value=new State{Tick=reader.ReadInt32(),PlayerLife=reader.ReadInt32(),PlayerMaxLife=reader.ReadInt32(),PlayerX=reader.ReadSingle(),PlayerY=reader.ReadSingle(),PlayerDead=reader.ReadBoolean(),NpcLife=reader.ReadInt32(),NpcMaxLife=reader.ReadInt32(),NpcX=reader.ReadSingle(),NpcY=reader.ReadSingle(),NpcActive=reader.ReadBoolean(),ActiveProjectiles=reader.ReadInt32(),FrameId=reader.ReadInt64()};
   if(value.Tick<0||value.PlayerMaxLife<1||value.PlayerLife>value.PlayerMaxLife||value.NpcMaxLife<1||value.NpcLife>value.NpcMaxLife||value.ActiveProjectiles<0||value.FrameId<1||!Finite(value.PlayerX)||!Finite(value.PlayerY)||!Finite(value.NpcX)||!Finite(value.NpcY))throw new InvalidDataException("BattleState contains invalid bounds or nonfinite positions.");
   return value;
  }
  private static byte[] StartBody(int seed,string weapon,string npc){return Protocol.Body(delegate(BinaryWriter writer){writer.Write(seed);writer.Write(weapon);writer.Write(npc);});}
  private static byte[] StepBody(int ticks,byte flags,float aimX,float aimY){return Protocol.Body(delegate(BinaryWriter writer){writer.Write(ticks);writer.Write(flags);writer.Write(aimX);writer.Write(aimY);});}
  private static Session Start(BridgeClient client,int seed,string weapon){
   Deadline();Stopwatch startElapsed=Stopwatch.StartNew();Packet packet=client.Call(StartOperation,StartBody(seed,weapon,"BlueSlime"));Success(packet);
   using(BinaryReader reader=Protocol.Reader(packet)){
    Session result=new Session{Name=reader.ReadString(),Width=reader.ReadInt32(),Height=reader.ReadInt32(),State=ReadState(reader)};Protocol.RequireEnd(reader);if(!Report.ContainsKey("firstStartMilliseconds"))Report["firstStartMilliseconds"]=startElapsed.ElapsedMilliseconds;return result;
   }
  }
  private static State Step(BridgeClient client,int ticks,byte flags,float aimX,float aimY){
   Deadline();Packet packet=client.Call(StepOperation,StepBody(ticks,flags,aimX,aimY));Success(packet);
   using(BinaryReader reader=Protocol.Reader(packet)){State result=ReadState(reader);Protocol.RequireEnd(reader);Report["lastCompletedTick"]=result.Tick;Report["lastPlayerLife"]=result.PlayerLife;Report["lastPlayerDead"]=result.PlayerDead;return result;}
  }
  private static void End(BridgeClient client){
   Deadline();Packet packet=client.Call(EndOperation,Empty());Success(packet);using(BinaryReader reader=Protocol.Reader(packet)){Check(reader.ReadString()=="ended","battle End acknowledges cleanup");Protocol.RequireEnd(reader);}
  }
  private static SharedFrame ReadFrame(FrameExchange exchange,State state){
   SharedFrame frame;FrameReadStatus status=exchange.ReadLatest(0,out frame);
   if(status!=FrameReadStatus.Available||frame==null||frame.FrameId!=state.FrameId||frame.Width!=640||frame.Height!=360||frame.Pixels.Length!=640*360*4)throw new InvalidDataException("Published preview does not match BattleState or expected 640x360 BGRA32 bounds.");
   return frame;
  }
  private static string Digest(SharedFrame frame){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(frame.Pixels)).Replace("-","");}
  private static bool IsNonuniform(SharedFrame frame){byte[] pixels=frame.Pixels;for(int i=4;i<pixels.Length;i+=4)if(pixels[i]!=pixels[0]||pixels[i+1]!=pixels[1]||pixels[i+2]!=pixels[2])return true;return false;}
  private static string SavePng(string filename,SharedFrame frame){
   string path=Path.Combine(outputDirectory,filename);
   using(Bitmap bitmap=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb)){
    BitmapData data=bitmap.LockBits(new Rectangle(0,0,frame.Width,frame.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
    try{for(int y=0;y<frame.Height;y++)Marshal.Copy(frame.Pixels,y*frame.Stride,IntPtr.Add(data.Scan0,y*data.Stride),frame.Stride);}finally{bitmap.UnlockBits(data);}
    bitmap.Save(path,ImageFormat.Png);
   }return path;
  }
  private static void ExpectError(BridgeClient client,Operation operation,byte[] body,string name){Deadline();Packet packet=client.Call(operation,body);Check(packet.IsError,name);using(BinaryReader reader=Protocol.Reader(packet)){string error=reader.ReadString()+": "+reader.ReadString();Protocol.RequireEnd(reader);Console.WriteLine("  expected "+error);}}
  private static void MappingGone(string session,string name){bool missing=false;try{using(MemoryMappedFile.OpenExisting(FrameExchange.MapObjectName(session))){} }catch(FileNotFoundException){missing=true;}Check(missing,name);}
  private static void InitialBounds(Session session){
   Guid id;string randomPart=session.Name.Length>=32?session.Name.Substring(session.Name.Length-32):session.Name;Check(Guid.TryParseExact(randomPart,"N",out id)&&id!=Guid.Empty,"frame session contains a nonempty random GUID");
   Check(session.Width==640&&session.Height==360,"native scene preview declares 640x360 BGRA32");
   Check(session.State.Tick==0&&session.State.PlayerLife==300&&session.State.PlayerMaxLife==300,"new battle starts at tick zero with 300 player HP");
   Check(session.State.NpcLife==25&&session.State.NpcMaxLife==25&&session.State.NpcActive,"BlueSlime retains native 25 HP");
   Check(Math.Abs(session.State.PlayerX-4096)<0.01f&&Math.Abs(session.State.PlayerY-2646)<0.01f,"player starts on fixed arena floor");
   Check(Math.Abs(session.State.NpcX-4352)<0.01f&&Math.Abs(session.State.NpcY-2648)<0.01f,"native NPC starts at fixed arena position");
  }
  private static void Run(string worker,string game){
   Check(IntPtr.Size==8,"battle host is x64");
   using(BridgeClient client=new BridgeClient(worker,game)){
    processId=client.Process.Id;Report["workerProcessId"]=processId;
    Packet hello=client.Hello(client.Secret);Success(hello);
    using(BinaryReader reader=Protocol.Reader(hello)){Check(reader.ReadString()=="SpireTerraria.Next.Worker","authenticated independent worker handshake");Check(reader.ReadUInt16()==Protocol.Version,"battle host agrees protocol version");Check(reader.ReadInt32()==32,"native simulation worker is x86");Protocol.RequireEnd(reader);}
    ExpectError(client,StepOperation,StepBody(1,0,4352,2648),"step before Start is a recoverable error");
    Session first=Start(client,74219,"EnchantedSword");InitialBounds(first);Report["firstSession"]=first.Name;
    using(FrameExchange frames=FrameExchange.OpenReader(first.Name)){
     SharedFrame initial=ReadFrame(frames,first.State);string initialDigest=Digest(initial);Report["initialPreviewSha256"]=initialDigest;Report["initialPreview"]=SavePng("m1-initial.png",initial);
     Check(IsNonuniform(initial),"initial shared-memory preview is nonuniform");Report["previewSharedMemoryVerified"]=true;
     SharedFrame noChange;Check(frames.ReadLatest(initial.FrameId,out noChange)==FrameReadStatus.Unchanged&&noChange==null,"repeated frame read reports Unchanged");
     ExpectError(client,StartOperation,StartBody(74219,"EnchantedSword","BlueSlime"),"duplicate Start preserves existing battle");
     ExpectError(client,StepOperation,StepBody(0,0,4352,2648),"reject zero simulation ticks");
     ExpectError(client,StepOperation,StepBody(121,0,4352,2648),"reject excessive simulation ticks");
     ExpectError(client,StepOperation,StepBody(1,128,4352,2648),"reject unsupported input flag bits");
     ExpectError(client,StepOperation,StepBody(1,0,Single.NaN,2648),"reject nonfinite aim coordinates");
     ExpectError(client,StepOperation,StepBody(1,0,4352,Single.PositiveInfinity),"reject infinite aim coordinates");
     ExpectError(client,StepOperation,new byte[]{1},"reject truncated battle input");
     byte[] trailing=StepBody(1,0,4352,2648);Array.Resize(ref trailing,trailing.Length+1);ExpectError(client,StepOperation,trailing,"reject trailing battle input bytes");
     Packet ping=client.Call(Operation.Ping,Empty());Success(ping);using(BinaryReader reader=Protocol.Reader(ping)){Check(reader.ReadString()=="pong","worker responds after malformed battle requests");Protocol.RequireEnd(reader);}
     Check(frames.ReadLatest(initial.FrameId,out noChange)==FrameReadStatus.Unchanged&&noChange==null,"Ping does not publish or advance a battle frame");
     State current=Step(client,1,0,4352,2648);Check(current.Tick==1,"invalid input and Ping leave next Step at expected tick");Check(current.FrameId>initial.FrameId,"one valid Step publishes a newer frame after Ping");
     State left=Step(client,12,1,4352,2648);Check(left.PlayerX<current.PlayerX,"native player responds to left movement input");
     State right=Step(client,24,2,4352,2648);Check(right.PlayerX>left.PlayerX,"native player responds to right movement input");
     State grounded=Step(client,36,0,4352,2648);State jumping=Step(client,8,4,4352,2648);Check(jumping.PlayerY<grounded.PlayerY,"native jump moves player above floor");Report["jumpPreview"]=SavePng("m1-jump.png",ReadFrame(frames,jumping));
     current=Step(client,120,0,4352,2648);Check(Math.Abs(current.NpcX-first.State.NpcX)>0.01f||Math.Abs(current.NpcY-first.State.NpcY)>0.01f,"native NPC AI changes position without attack input");
     int projectileMaximum=0;bool damaged=false,killed=false;long previousFrame=current.FrameId;
     for(int cycle=0;cycle<40;cycle++){
      State attack=Step(client,1,8,current.NpcX+16,current.NpcY+16);projectileMaximum=Math.Max(projectileMaximum,attack.ActiveProjectiles);damaged|=attack.NpcLife<first.State.NpcLife;killed|=!attack.NpcActive||attack.NpcLife<=0;
      if(attack.ActiveProjectiles>0&&!Report.ContainsKey("projectilePreview"))Report["projectilePreview"]=SavePng("m1-projectile.png",ReadFrame(frames,attack));
      current=Step(client,12,0,attack.NpcX+16,attack.NpcY+16);projectileMaximum=Math.Max(projectileMaximum,current.ActiveProjectiles);damaged|=current.NpcLife<first.State.NpcLife;killed|=!current.NpcActive||current.NpcLife<=0;
      if(damaged&&killed&&projectileMaximum>0)break;
     }
     Report["maximumNativeProjectileCount"]=projectileMaximum;Report["enemyDeathTick"]=killed?current.Tick:-1;
     Check(projectileMaximum>0,"native weapon use creates an active projectile");Check(damaged,"native attack or projectile reduces BlueSlime HP");Check(killed,"native damage deactivates defeated BlueSlime");
     SharedFrame action=ReadFrame(frames,current);Check(action.FrameId>previousFrame,"battle steps publish increasing frame IDs");Check(Digest(action)!=initialDigest,"preview pixels change after native simulation input");Report["actionPreview"]=SavePng("m1-action.png",action);Report["actionPreviewSha256"]=Digest(action);
     State victoryFrozen=Step(client,120,8,current.NpcX+16,current.NpcY+16);Check(victoryFrozen.Tick==current.Tick&&victoryFrozen.PlayerLife==current.PlayerLife&&victoryFrozen.NpcLife==current.NpcLife&&!victoryFrozen.NpcActive,"victory freezes native tick and HP despite further attack input");
     End(client);SharedFrame closed;Check(frames.ReadLatest(0,out closed)==FrameReadStatus.Closed,"End publishes frame channel closure");
    }
    MappingGone(first.Name,"End and reader disposal reclaim first mapping");
    ExpectError(client,StepOperation,StepBody(1,0,4352,2648),"step after End is recoverable");
    Session second=Start(client,74220,"WoodenSword");InitialBounds(second);Check(second.Name!=first.Name,"restarting battle uses a new frame session");
    int minimumLife=second.State.PlayerLife;State hurt=second.State;int survivalSteps=0;
    using(FrameExchange frames=FrameExchange.OpenReader(second.Name)){
     for(int cycle=0;cycle<200&&!hurt.PlayerDead;cycle++){
      float distance=hurt.NpcX-hurt.PlayerX;byte movement=distance>16?(byte)2:distance<-16?(byte)1:(byte)0;
      hurt=Step(client,120,movement,hurt.NpcX+16,hurt.NpcY+16);minimumLife=Math.Min(minimumLife,hurt.PlayerLife);survivalSteps++;Report["minimumPlayerLife"]=minimumLife;Report["survivalSimulationTicks"]=hurt.Tick;
     }
     Report["minimumPlayerLife"]=minimumLife;Report["survivalSimulationTicks"]=hurt.Tick;Report["survivalStepRequests"]=survivalSteps;Report["playerDeathTick"]=hurt.PlayerDead?hurt.Tick:-1;
     Check(minimumLife<300,"native enemy contact reduces player HP");Check(hurt.PlayerDead&&hurt.PlayerLife<=0,"native player death is observable without manual HP edits");
     Report["deathPreview"]=SavePng("m1-death.png",ReadFrame(frames,hurt));State deathFrozen=Step(client,120,8,hurt.NpcX+16,hurt.NpcY+16);Check(deathFrozen.Tick==hurt.Tick&&deathFrozen.PlayerLife==hurt.PlayerLife&&deathFrozen.NpcLife==hurt.NpcLife&&deathFrozen.PlayerDead,"death freezes native tick and HP without automatic resurrection");End(client);SharedFrame closed;Check(frames.ReadLatest(0,out closed)==FrameReadStatus.Closed,"death scenario frame channel closes cleanly");
    }
    MappingGone(second.Name,"death scenario mapping is reclaimed");
    ExpectError(client,StartOperation,StartBody(74221,"SpireNext_MissingWeapon","BlueSlime"),"reject unknown weapon without poisoning next Start");
    ExpectError(client,StartOperation,StartBody(74221,"WaterBolt","SpireNext_MissingNpc"),"reject unknown NPC without poisoning next Start");
    Session third=Start(client,74221,"WaterBolt");Check(third.Name!=second.Name&&third.State.Tick==0&&third.State.PlayerLife==300,"clean restart resets HP and tick after death");
    State mage=third.State;bool magicProjectile=false;
    using(FrameExchange frames=FrameExchange.OpenReader(third.Name)){
     for(int cycle=0;cycle<8&&!magicProjectile;cycle++){
      mage=Step(client,1,8,mage.NpcX+16,mage.NpcY+16);magicProjectile|=mage.ActiveProjectiles>0;if(!magicProjectile){mage=Step(client,16,0,mage.NpcX+16,mage.NpcY+16);magicProjectile|=mage.ActiveProjectiles>0;}
     }
     Check(magicProjectile,"native WaterBolt weapon use creates a projectile");ReadFrame(frames,mage);End(client);
    }
    MappingGone(third.Name,"magic scenario mapping is reclaimed");
    Packet shutdown=client.Call(Operation.Shutdown,Empty());Success(shutdown);Check(client.Process.WaitForExit(3000)&&client.Process.ExitCode==0,"battle worker shuts down normally");
   }
   bool running=false;try{using(Process process=Process.GetProcessById(processId))running=!process.HasExited;}catch(ArgumentException){}Check(!running,"battle probe leaves no worker process");
  }
  private static int Main(string[] args){
   bool passed=false;string error=null;
   try{
    if(args.Length!=3)throw new ArgumentException("Expected worker executable, local game directory and report path.");
    outputDirectory=Path.GetDirectoryName(Path.GetFullPath(args[2]));Directory.CreateDirectory(outputDirectory);Run(args[0],args[1]);passed=true;
   }catch(Exception ex){error=ex.ToString();Console.Error.WriteLine(error);}
   Report["passed"]=passed;Report["checkCount"]=Checks.Count;Report["checks"]=Checks;Report["elapsedMilliseconds"]=Elapsed.ElapsedMilliseconds;Report["failure"]=error;Report["hostBits"]=IntPtr.Size*8;Report["nativeSimulationAcceptancePassed"]=passed;Report["nativeGameRenderingVerified"]=false;Report["scope"]="Native arena state and input acceptance with a shared-memory preview; this harness does not establish that preview artwork uses Terraria's complete native rendering pipeline.";
   if(args.Length==3)File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(Report),new UTF8Encoding(false));return passed?0:1;
  }
 }
}

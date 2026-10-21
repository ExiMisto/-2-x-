using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace SpireTerraria.Next {
 internal static class Probe {
  static readonly List<string> checks=new List<string>();
  static readonly Dictionary<string,object> report=new Dictionary<string,object>();
  static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);Console.WriteLine("PASS "+name);}
  static byte[] Empty(){return new byte[0];}
  static void Success(Packet packet){if(packet.IsError){using(var r=Protocol.Reader(packet))throw new InvalidOperationException(r.ReadString()+": "+r.ReadString());}}
  static void ExpectProtocolFailure(byte[] frame,string name){bool rejected=false;try{using(var s=new MemoryStream(frame))Protocol.Read(s);}catch(InvalidDataException){rejected=true;}catch(EndOfStreamException){rejected=true;}Check(rejected,name);}
  static byte[] Header(uint magic,ushort version,int size){return Protocol.Body(delegate(BinaryWriter w){w.Write(magic);w.Write(version);w.Write((byte)Operation.Ping);w.Write((byte)0);w.Write(1);w.Write(size);});}
  static int Main(string[] args){
   bool passed=false;string failure=null;var elapsed=Stopwatch.StartNew();
   try{
    if(args.Length!=3)throw new ArgumentException("Expected worker path, local Terraria directory and report path.");
    report["hostBits"]=IntPtr.Size*8;Check(!Environment.Is64BitOperatingSystem||IntPtr.Size==8,"host probe uses 64-bit process on 64-bit Windows");
    ExpectProtocolFailure(Header(0,Protocol.Version,0),"rejects wrong protocol magic");
    ExpectProtocolFailure(Header(Protocol.Magic,99,0),"rejects incompatible protocol version");
    ExpectProtocolFailure(Header(Protocol.Magic,Protocol.Version,Protocol.MaxBody+1),"rejects oversized frame before allocation");
    ExpectProtocolFailure(Header(Protocol.Magic,Protocol.Version,-1),"rejects negative frame size");
    ExpectProtocolFailure(Header(Protocol.Magic,Protocol.Version,10),"rejects truncated body");
    using(var s=new BridgeClient(args[0],args[1])){
     Packet hello=s.Hello(s.Secret);Success(hello);
     using(var r=Protocol.Reader(hello)){Check(r.ReadString()=="SpireTerraria.Next.Worker","independent worker handshake");Check(r.ReadUInt16()==Protocol.Version,"agreed protocol version");Check(r.ReadInt32()==32,"native worker runs in isolated x86 process");Protocol.RequireEnd(r);}
     Packet inspect=s.Call(Operation.Inspect,Empty());Success(inspect);
     using(var r=Protocol.Reader(inspect)){
      string name=r.ReadString(),assemblyVersion=r.ReadString(),fileVersion=r.ReadString();int bits=r.ReadInt32();
      Check(name=="Terraria"&&bits==32,"loads local Terraria assembly");
      report["terrariaAssemblyVersion"]=assemblyVersion;report["terrariaFileVersion"]=fileVersion;
      var types=new Dictionary<string,int>();for(int i=0;i<6;i++){string type=r.ReadString();int methods=r.ReadInt32();types[type]=methods;Check(methods>0||type=="Terraria.ID.ItemID","native API available: "+type);}Protocol.RequireEnd(r);report["nativeTypes"]=types;
     }
     Packet items=s.Call(Operation.ItemIds,Protocol.Body(delegate(BinaryWriter w){w.Write(4);w.Write("WoodenSword");w.Write("TerraBlade");w.Write("Zenith");w.Write("DD2BallistraTowerT1Popper");}));Success(items);
     using(var r=Protocol.Reader(items)){
      Check(r.ReadInt32()==4,"native item lookup returns requested entries");var actual=new Dictionary<string,int>();for(int i=0;i<4;i++)actual.Add(r.ReadString(),r.ReadInt32());Protocol.RequireEnd(r);
      Check(actual["WoodenSword"]==24,"wooden sword ID comes from original game");Check(actual["TerraBlade"]==757,"Terra Blade ID comes from original game");Check(actual["Zenith"]==4956,"Zenith ID comes from original game");Check(actual["DD2BallistraTowerT1Popper"]==3824,"Tavernkeep sentry ID comes from original game");report["nativeItemIds"]=actual;
     }
     Packet unknown=s.Call(Operation.ItemIds,Protocol.Body(delegate(BinaryWriter w){w.Write(1);w.Write("SpireNext_Missing_Item");}));Check(unknown.IsError,"unknown item yields recoverable error");
     Packet nativeStats=s.Call(Operation.ItemStats,Protocol.Body(delegate(BinaryWriter w){w.Write("WoodenSword");}));Success(nativeStats);
     using(var r=Protocol.Reader(nativeStats)){Check(r.ReadString()=="WoodenSword"&&r.ReadInt32()==24,"native Item.SetDefaults initializes actual item");var stats=new Dictionary<string,int>();foreach(string key in new[]{"damage","defense","useTime","useAnimation","mana","rare"})stats[key]=r.ReadInt32();Protocol.RequireEnd(r);Check(stats["damage"]>0&&stats["useTime"]>0,"native item damage and attack timing are available");report["woodenSwordNativeStats"]=stats;}
     var classSamples=new Dictionary<string,object>();
     foreach(string symbol in new[]{"Minishark","WaterBolt","FlinxStaff","IronHelmet"}){
      Packet sample=s.Call(Operation.ItemStats,Protocol.Body(delegate(BinaryWriter w){w.Write(symbol);}));Success(sample);
      using(var r=Protocol.Reader(sample)){string actualSymbol=r.ReadString();int id=r.ReadInt32();var stats=new Dictionary<string,int>();foreach(string key in new[]{"damage","defense","useTime","useAnimation","mana","rare"})stats[key]=r.ReadInt32();Protocol.RequireEnd(r);Check(actualSymbol==symbol&&id>0&&(symbol=="IronHelmet"?stats["defense"]>0:stats["damage"]>0),"native weapon or armor sample: "+symbol);classSamples[symbol]=stats;}
     }
     report["otherNativeItemSamples"]=classSamples;
     Packet trailing=s.Call(Operation.Ping,new byte[]{1});Check(trailing.IsError,"trailing request data is rejected");
     Packet ping=s.Call(Operation.Ping,Empty());Success(ping);using(var r=Protocol.Reader(ping)){Check(r.ReadString()=="pong","worker remains responsive after request errors");Protocol.RequireEnd(r);}
     Packet bye=s.Call(Operation.Shutdown,Empty());Success(bye);Check(s.Process.WaitForExit(3000)&&s.Process.ExitCode==0,"shutdown exits worker normally");
    }
    using(var s=new BridgeClient(args[0],args[1])){
     bool rejected=false;try{s.Hello(new string('0',64));}catch{rejected=true;}
     Check(rejected&&s.Process.WaitForExit(3000)&&s.Process.ExitCode!=0,"wrong session secret is rejected and worker exits");
    }
    using(var s=new BridgeClient(args[0],args[1])){
     bool rejected=false;try{s.Call(Operation.Ping,Empty());}catch{rejected=true;}
     Check(rejected&&s.Process.WaitForExit(3000),"commands before handshake are rejected");
    }
    using(var s=new BridgeClient(args[0],Path.Combine(args[1],"SpireNext_Missing_Game"))){
     Success(s.Hello(s.Secret));Check(s.Call(Operation.Inspect,Empty()).IsError,"missing local game returns actionable request error");
     Success(s.Call(Operation.Ping,Empty()));Check(true,"worker survives missing-game request");Success(s.Call(Operation.Shutdown,Empty()));
    }
    using(var s=new BridgeClient(args[0],args[1])){
     Success(s.Hello(s.Secret));s.Pipe.Dispose();Check(s.Process.WaitForExit(3000),"closed host pipe terminates worker without orphan process");
    }
    using(var s=new BridgeClient(args[0],args[1])){
     Success(s.Hello(s.Secret));Protocol.Write(s.Pipe,new Packet{Operation=Operation.Ping,Sequence=1,Body=Empty()});
     Check(s.Process.WaitForExit(3000)&&s.Process.ExitCode!=0,"replayed request sequence is rejected");
    }
    using(var s=new BridgeClient(args[0],args[1])){
     Success(s.Hello(s.Secret));Protocol.Write(s.Pipe,new Packet{Operation=Operation.Ping,Sequence=2,IsResponse=true,Body=Empty()});
     Check(s.Process.WaitForExit(3000)&&s.Process.ExitCode!=0,"host cannot send response-direction frames");
    }
    using(var s=new BridgeClient(args[0],args[1])){
     Success(s.Hello(s.Secret));s.Process.Kill();s.Process.WaitForExit(3000);var crash=Stopwatch.StartNew();bool disconnected=false;
     try{s.Call(Operation.Ping,Empty());}catch(IOException){disconnected=true;}catch(ObjectDisposedException){disconnected=true;}
     Check(disconnected&&crash.ElapsedMilliseconds<3000,"worker crash returns control promptly");
    }
    int stalledProcess;bool timedOut=false;var timeout=Stopwatch.StartNew();
    using(var s=new BridgeClient(Path.Combine(Path.GetDirectoryName(args[0]),"SpireNext.UnresponsiveFixture.exe"),args[1])){
     stalledProcess=s.Process.Id;try{s.Hello(s.Secret);}catch(TimeoutException){timedOut=true;}
     Check(timedOut&&timeout.ElapsedMilliseconds<8000,"unresponsive worker hits bounded host timeout");
    }
    bool orphan=false;try{using(var p=Process.GetProcessById(stalledProcess))orphan=!p.HasExited;}catch(ArgumentException){}
    Check(!orphan,"unresponsive worker is reaped after timeout");
    passed=true;
   }catch(Exception e){failure=e.ToString();Console.Error.WriteLine(failure);}
   report["passed"]=passed;report["checks"]=checks;report["checkCount"]=checks.Count;report["elapsedMilliseconds"]=elapsed.ElapsedMilliseconds;report["failure"]=failure;report["scope"]="Native API access, actual item initialization and IPC only; no gameplay simulation, rendering, audio or persistent run save yet.";
   if(args.Length==3)File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));
   return passed?0:1;
  }
 }
}

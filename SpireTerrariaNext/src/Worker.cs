using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace SpireTerraria.Next {
 internal static class Worker {
  [STAThread] static int Main(string[] args){
   NativeArena arena=null;NativeRenderer itemRenderer=null;Process parent=null;Timer parentWatcher=null;
   try{
    int parentId;long parentStarted;
    if(args.Length!=4||!args[0].StartsWith("SpireNext-",StringComparison.Ordinal)||!int.TryParse(args[2],out parentId)||!long.TryParse(args[3],out parentStarted)||parentId<=0||parentStarted<=0){Console.Error.WriteLine("Expected a generated session pipe, local game directory and parent process identity.");return 2;}
    parent=Process.GetProcessById(parentId);
    if(parent.HasExited||parent.StartTime.ToUniversalTime().Ticks!=parentStarted)throw new InvalidDataException("Parent process identity does not match.");
    // This runs independently of the graphics thread and the request timeout.
    // A hung renderer cannot leave an orphaned game window after the host exits.
    Process owner=parent;parentWatcher=new Timer(delegate(object unused){try{if(!owner.HasExited)return;}catch{}Environment.Exit(0);},null,250,250);
    string token=Console.ReadLine();if(token==null||token.Length!=64){Console.Error.WriteLine("Missing session secret.");return 2;}
    using(var pipe=new NamedPipeClientStream(".",args[0],PipeDirection.InOut,PipeOptions.Asynchronous)){
     pipe.Connect(5000);
     bool authenticated=false;int lastSequence=0;TerrariaRuntime runtime=null;
     while(true){
      Packet request=ReadWithDeadline(pipe,15000);
      if(request.IsResponse||request.IsError||request.Sequence<=lastSequence)throw new InvalidDataException("Invalid request direction or sequence.");
      lastSequence=request.Sequence;
      if(!authenticated){
       if(request.Operation!=Operation.Hello)throw new InvalidDataException("Handshake required.");
       using(var reader=Protocol.Reader(request)){if(!Protocol.EqualSecret(reader.ReadString(),token))throw new InvalidDataException("Session authentication failed.");Protocol.RequireEnd(reader);}
       authenticated=true;
       Reply(pipe,request,Protocol.Body(delegate(BinaryWriter w){w.Write("SpireTerraria.Next.Worker");w.Write(Protocol.Version);w.Write(IntPtr.Size*8);}),false);
       continue;
      }
      bool stop=false;
      try{
       byte[] body=Protocol.Body(delegate(BinaryWriter writer){using(var reader=Protocol.Reader(request)){
        switch(request.Operation){
         case Operation.Inspect:Protocol.RequireEnd(reader);if(runtime==null)runtime=new TerrariaRuntime(args[1]);runtime.Inspect(writer);break;
         case Operation.ItemIds:if(runtime==null)runtime=new TerrariaRuntime(args[1]);runtime.ItemIds(reader,writer);break;
         case Operation.ItemStats:if(runtime==null)runtime=new TerrariaRuntime(args[1]);runtime.ItemStats(reader,writer);break;
         case Operation.BattleStart:
          int seed=reader.ReadInt32();string weapon=reader.ReadString(),enemy=reader.ReadString();Protocol.RequireEnd(reader);
          if(arena!=null)throw new InvalidOperationException("End the current arena before starting another.");
          if(runtime==null)runtime=new TerrariaRuntime(args[1]);arena=new NativeArena(runtime,seed,weapon,enemy);arena.WriteStart(writer);break;
         case Operation.BattleStep:
          int ticks=reader.ReadInt32();byte input=reader.ReadByte();float aimX=reader.ReadSingle(),aimY=reader.ReadSingle();Protocol.RequireEnd(reader);
          if(arena==null)throw new InvalidOperationException("Start an arena before stepping it.");arena.Step(ticks,input,aimX,aimY);arena.WriteState(writer);break;
         case Operation.BattleEnd:
          Protocol.RequireEnd(reader);NativeArena released=arena;arena=null;if(released!=null)released.Dispose();writer.Write("ended");break;
         case (Operation)10:
          BattleConfiguration configuration=BattleConfiguration.Read(reader);Protocol.RequireEnd(reader);
          if(arena!=null)throw new InvalidOperationException("End the current arena before starting another.");
          if(runtime==null)runtime=new TerrariaRuntime(args[1]);arena=new NativeArena(runtime,configuration);arena.WriteAdventureStart(writer);break;
         case (Operation)11:
          int adventureTicks=reader.ReadInt32();byte adventureInput=reader.ReadByte();float adventureAimX=reader.ReadSingle(),adventureAimY=reader.ReadSingle();int selectedSlot=reader.ReadInt32();int selectedConsumable=reader.BaseStream.Position<reader.BaseStream.Length?reader.ReadInt32():-1;Protocol.RequireEnd(reader);
          if(arena==null)throw new InvalidOperationException("Start an arena before stepping it.");arena.AdventureStep(adventureTicks,adventureInput,adventureAimX,adventureAimY,selectedSlot,selectedConsumable);arena.WriteAdvancedState(writer);break;
         case (Operation)12:
          Protocol.RequireEnd(reader);if(arena==null)throw new InvalidOperationException("Start an arena before inspecting it.");arena.WriteDiagnostics(writer);break;
         case Operation.ItemIcons:
          if(arena!=null)arena.WriteItemIcons(reader,writer);
          else {if(runtime==null)runtime=new TerrariaRuntime(args[1]);if(itemRenderer==null)itemRenderer=new NativeRenderer(runtime.NativeAssembly,runtime.GameDirectory,64,64);WriteItemIcons(runtime,itemRenderer,reader,writer);}break;
         case Operation.SceneArt:
          int hero=reader.ReadInt32();Protocol.RequireEnd(reader);if(hero<0||hero>3)throw new InvalidDataException("Expected a Terraria character class from zero to three.");
          if(runtime==null)runtime=new TerrariaRuntime(args[1]);if(itemRenderer==null)itemRenderer=new NativeRenderer(runtime.NativeAssembly,runtime.GameDirectory,64,64);itemRenderer.WriteSceneArt(hero,writer);break;
         case Operation.Ping:Protocol.RequireEnd(reader);writer.Write("pong");break;
         case Operation.Shutdown:Protocol.RequireEnd(reader);writer.Write("bye");stop=true;break;
         default:throw new InvalidDataException("Operation unavailable after handshake.");
        }
       }});
       Reply(pipe,request,body,false);
      }catch(Exception e){Reply(pipe,request,Protocol.Body(delegate(BinaryWriter w){w.Write(e.GetType().Name);w.Write(e.Message);}),true);}
      if(stop)return 0;
     }
    }
   }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
   finally{if(arena!=null)try{arena.Dispose();}catch(Exception e){Console.Error.WriteLine("Arena cleanup failed: "+e.Message);}if(itemRenderer!=null)try{itemRenderer.Dispose();}catch(Exception e){Console.Error.WriteLine("Item renderer cleanup failed: "+e.Message);}if(parentWatcher!=null)parentWatcher.Dispose();if(parent!=null)parent.Dispose();}
  }
  static void WriteItemIcons(TerrariaRuntime runtime,NativeRenderer renderer,BinaryReader reader,BinaryWriter writer) {
   int count=reader.ReadInt32();if(count<1||count>12)throw new InvalidDataException("Expected one to twelve item icons.");
   var symbols=new string[count];var ids=new int[count];Type type=runtime.NativeAssembly.GetType("Terraria.ID.ItemID",true);
   for(int i=0;i<count;i++){symbols[i]=reader.ReadString();if(symbols[i].Length>128)throw new InvalidDataException("Oversized item symbol.");var field=type.GetField(symbols[i],System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);if(field==null||!field.IsLiteral)throw new InvalidDataException("Unknown native item symbol: "+symbols[i]);ids[i]=Convert.ToInt32(field.GetRawConstantValue());}Protocol.RequireEnd(reader);
   writer.Write(count);for(int i=0;i<count;i++){writer.Write(symbols[i]);renderer.WriteItemIcon(ids[i],writer);}
  }
  static Packet ReadWithDeadline(Stream stream,int milliseconds){
   using(var deadline=new Timer(delegate(object unused){try{stream.Dispose();}catch{}},null,milliseconds,Timeout.Infinite)){return Protocol.Read(stream);}
  }
  static void Reply(Stream stream,Packet request,byte[] body,bool error){Protocol.Write(stream,new Packet{Operation=request.Operation,Sequence=request.Sequence,IsResponse=true,IsError=error,Body=body});}
 }
}

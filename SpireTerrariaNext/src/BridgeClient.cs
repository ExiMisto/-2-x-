using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Threading;

namespace SpireTerraria.Next {
 internal sealed class BridgeClient : IDisposable {
  internal readonly NamedPipeServerStream Pipe;
  internal readonly Process Process;
  internal readonly string Secret;
  readonly object calls=new object();
  int sequence,disposed;
  internal BridgeClient(string worker,string game){
   string name="SpireNext-"+Guid.NewGuid().ToString("N");
   byte[] random=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(random);Secret=BitConverter.ToString(random).Replace("-","");
   var security=new PipeSecurity();security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
   Pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
   try{
    string owner;
    using(var parent=System.Diagnostics.Process.GetCurrentProcess())owner=parent.Id.ToString(CultureInfo.InvariantCulture)+" "+parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    var start=new ProcessStartInfo{FileName=worker,Arguments=Quote(name)+" "+Quote(game)+" "+owner,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true};
    Process=System.Diagnostics.Process.Start(start);if(Process==null)throw new IOException("Worker did not start.");
    Process.OutputDataReceived+=delegate{};Process.ErrorDataReceived+=delegate{};Process.BeginOutputReadLine();Process.BeginErrorReadLine();
    Process.StandardInput.WriteLine(Secret);Process.StandardInput.Close();
    IAsyncResult wait=Pipe.BeginWaitForConnection(null,null);
    using(wait.AsyncWaitHandle){if(!wait.AsyncWaitHandle.WaitOne(5000))throw new TimeoutException("Worker did not connect.");Pipe.EndWaitForConnection(wait);}
   }catch{Dispose();throw;}
  }
  static string Quote(string value){
   if(value.IndexOf('"')>=0)throw new ArgumentException("Unexpected quote in path.");
   int trailing=0;for(int i=value.Length-1;i>=0&&value[i]=='\\';i--)trailing++;
   return "\""+value+new string('\\',trailing)+"\"";
  }
  internal Packet Call(Operation op,byte[] body){
   lock(calls){return CallCore(op,body);}
  }
  Packet CallCore(Operation op,byte[] body){
   if(Thread.VolatileRead(ref disposed)!=0)throw new ObjectDisposedException("BridgeClient");
   int current=++sequence;
   Task<Packet> pending=Task.Factory.StartNew(delegate{Protocol.Write(Pipe,new Packet{Operation=op,Sequence=current,Body=body});return Protocol.Read(Pipe);});
   using(var ready=((IAsyncResult)pending).AsyncWaitHandle){if(!ready.WaitOne(5000)){Dispose();pending.ContinueWith(delegate(Task<Packet> task){var ignored=task.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);throw new TimeoutException("Worker response timed out.");}}
   Packet reply=pending.GetAwaiter().GetResult();
   if(!reply.IsResponse||reply.Sequence!=current||reply.Operation!=op)throw new InvalidDataException("Unmatched response.");
   return reply;
  }
  internal Packet Hello(string token){return Call(Operation.Hello,Protocol.Body(delegate(BinaryWriter w){w.Write(token);}));}
  public void Dispose(){
   if(Interlocked.Exchange(ref disposed,1)!=0)return;
   Pipe.Dispose();
   if(Process!=null){try{if(!Process.WaitForExit(250)){Process.Kill();Process.WaitForExit(500);}}catch(InvalidOperationException){}finally{Process.Dispose();}}
  }
 }
}

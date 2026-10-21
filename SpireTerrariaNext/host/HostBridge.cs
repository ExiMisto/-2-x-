using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SpireTerraria.Next;

namespace SpireTerraria.Next.Host {
 // Native .NET 9 host endpoint. The x86 worker keeps the installed game's .NET
 // Framework/XNA runtime entirely outside Godot's process and assembly resolver.
 internal sealed class HostBridge : IDisposable {
  readonly NamedPipeServerStream pipe;
  readonly SemaphoreSlim calls=new SemaphoreSlim(1,1);
  readonly Process process;
  readonly CancellationTokenSource lifetime=new CancellationTokenSource();
  static readonly ConcurrentDictionary<HostBridge,byte> live=new ConcurrentDictionary<HostBridge,byte>();
  int sequence;
  int disposed;
  static HostBridge(){AppDomain.CurrentDomain.ProcessExit+=(_,__)=>{foreach(var bridge in live.Keys)bridge.Dispose();};}
  internal int WorkerId {get{return process.Id;}}
  internal static async Task<HostBridge> Open(string executable,string game,CancellationToken cancellationToken=default) {
   var bridge=new HostBridge(executable,game);
   using var cancellation=cancellationToken.Register(()=>{bridge.lifetime.Cancel();bridge.pipe.Dispose();});
   try {
    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,bridge.lifetime.Token)){timeout.CancelAfter(6000);await bridge.pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);}
    cancellationToken.ThrowIfCancellationRequested();
    Packet hello=await bridge.Call(Operation.Hello,Protocol.Body(w=>w.Write(bridge.secret))).ConfigureAwait(false);
    using(var reader=Protocol.Reader(hello)) {
     if(reader.ReadString()!="SpireTerraria.Next.Worker"||reader.ReadUInt16()!=Protocol.Version||reader.ReadInt32()!=32)throw new InvalidDataException("原生战斗核心版本不匹配。");
     Protocol.RequireEnd(reader);
    }
    return bridge;
   } catch {bridge.Dispose();throw;}
  }
  readonly string secret;
  HostBridge(string executable,string game) {
   secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));string channel="SpireNext-"+Guid.NewGuid().ToString("N");
   pipe=new NamedPipeServerStream(channel,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,65536,65536);
   try {
    var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
    start.ArgumentList.Add(channel);start.ArgumentList.Add(game);
    using(var parent=Process.GetCurrentProcess()){start.ArgumentList.Add(parent.Id.ToString(CultureInfo.InvariantCulture));start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));}
    process=Process.Start(start)??throw new IOException("未能启动原生战斗核心。");
    live.TryAdd(this,0);
    process.OutputDataReceived+=(_,__)=>{};process.ErrorDataReceived+=(_,__)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();
    process.StandardInput.WriteLine(secret);process.StandardInput.Close();
   } catch {Dispose();throw;}
  }
  internal async Task<Packet> Call(Operation operation,byte[] body=null,int timeoutMilliseconds=8000) {
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(timeoutMilliseconds);bool entered=false;
   try {
    await calls.WaitAsync(timeout.Token).ConfigureAwait(false);entered=true;
    if(Volatile.Read(ref disposed)!=0)throw new ObjectDisposedException("HostBridge");int id=++sequence;
    // Keep all pipe IO off Godot's main thread. A request timeout closes the pipe
    // and causes the worker to exit; no concurrent read survives for reuse.
    Task<Packet> io=Task.Run(()=> {
     Protocol.Write(pipe,new Packet{Operation=operation,Sequence=id,Body=body});Packet reply=Protocol.Read(pipe);
     if(!reply.IsResponse||reply.Sequence!=id||reply.Operation!=operation)throw new InvalidDataException("战斗核心响应不匹配。");
     if(reply.IsError)using(var reader=Protocol.Reader(reply)){string type=reader.ReadString(),message=reader.ReadString();throw new InvalidOperationException(type+": "+message);}
     return reply;
    });
    if(await Task.WhenAny(io,Task.Delay(Timeout.Infinite,timeout.Token)).ConfigureAwait(false)!=io){
     pipe.Dispose();_=io.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);
     if(lifetime.IsCancellationRequested)throw new ObjectDisposedException("HostBridge");
     throw new TimeoutException("战斗核心响应超时，请重新进入测试。");
    }
    return await io.ConfigureAwait(false);
   } catch(OperationCanceledException){pipe.Dispose();if(lifetime.IsCancellationRequested)throw new ObjectDisposedException("HostBridge");throw new TimeoutException("战斗核心响应超时，请重新进入测试。");}
   finally {timeout.Cancel();if(entered)calls.Release();}
  }
  public void Dispose() {
   if(Interlocked.Exchange(ref disposed,1)!=0)return;live.TryRemove(this,out _);lifetime.Cancel();pipe.Dispose();
   if(process==null)return;
   try{if(!process.WaitForExit(250)){process.Kill();process.WaitForExit(500);}}catch(InvalidOperationException){}finally{process.Dispose();}
  }
 }
}

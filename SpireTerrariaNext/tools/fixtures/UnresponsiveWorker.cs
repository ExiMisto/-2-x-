// Test helper only: connects without replying, to verify bounded host recovery.
using System;
using System.IO.Pipes;
using System.Threading;
class UnresponsiveWorker {
 static void Main(string[] args){
  Console.ReadLine();
  using(var pipe=new NamedPipeClientStream(".",args[0],PipeDirection.InOut,PipeOptions.Asynchronous)){
   pipe.Connect(5000);Thread.Sleep(Timeout.Infinite);
  }
 }
}

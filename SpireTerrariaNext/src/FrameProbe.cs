using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SpireTerraria.Next {
 internal static class FrameProbe {
  private const long TimestampBase=638000000000000000L;
  private static readonly List<object> Checks=new List<object>();
  private static void Check(string name,bool condition){Checks.Add(new{name=name,passed=condition});if(!condition)throw new InvalidOperationException("FAILED: "+name);}
  private static void Reject<T>(string name,Action action) where T:Exception{bool rejected=false;try{action();}catch(T){rejected=true;}Check(name,rejected);}
  private static byte[] Pattern(int width,int height,int seed){byte[] pixels=new byte[checked(width*height*4)];for(int i=0;i<pixels.Length;i++)pixels[i]=(byte)((i*37+seed*13+(i/4)*11)&255);return pixels;}
  private static bool Correct(SharedFrame frame,int seed){byte[] expected=Pattern(frame.Width,frame.Height,seed);if(frame.TimestampUtcTicks!=TimestampBase+seed||frame.Stride!=frame.Width*4||frame.Pixels.Length!=expected.Length)return false;for(int i=0;i<expected.Length;i++)if(frame.Pixels[i]!=expected[i])return false;return true;}
  private static string ReadLine(Process process){Task<string> read=Task.Factory.StartNew(delegate{return process.StandardOutput.ReadLine();});if(!read.Wait(10000))throw new TimeoutException("Synthetic frame writer did not reply.");string line=read.Result;if(line==null)throw new EndOfStreamException("Synthetic frame writer exited: "+process.StandardError.ReadToEnd());return line;}
  private static string Send(Process process,string command){process.StandardInput.WriteLine(command);process.StandardInput.Flush();return ReadLine(process);}
  private static string Name(){return "Probe_"+Guid.NewGuid().ToString("N");}
  private static int Writer(string name){
   using(FrameExchange channel=FrameExchange.CreateWriter(name,4096)){
    Console.WriteLine("READY "+(IntPtr.Size*8));Console.Out.Flush();string command;
    while((command=Console.ReadLine())!=null){
     string[] parts=command.Split(' ');
     if(parts[0]=="WRITE"){
      int width=Int32.Parse(parts[1]),height=Int32.Parse(parts[2]),seed=Int32.Parse(parts[3]);
      try{long id=channel.WriteBgra32(width,height,Pattern(width,height,seed),TimestampBase+seed,5000);Console.WriteLine("FRAME "+id);}
      catch(ArgumentException ex){Console.WriteLine("REJECTED "+ex.GetType().Name);}
     }else if(parts[0]=="BURST"){
      int count=Int32.Parse(parts[1]);for(int i=0;i<count;i++){int seed=100+i;channel.WriteBgra32(8+(i%19),8+(i%11),Pattern(8+(i%19),8+(i%11),seed),TimestampBase+seed,5000);if(i%10==0)Thread.Sleep(1);}Console.WriteLine("BURST_DONE");
     }else if(parts[0]=="HOLD"){
      using(Mutex held=Mutex.OpenExisting(FrameExchange.MapObjectName(name)+".Gate")){
       held.WaitOne();try{Console.WriteLine("HELD");Console.Out.Flush();if(Console.ReadLine()!="RELEASE")throw new IOException("Synthetic lock holder was not released.");}finally{held.ReleaseMutex();}
      }Console.WriteLine("RELEASED");
     }else if(parts[0]=="CLOSE"){channel.Dispose();Console.WriteLine("CLOSED");Console.Out.Flush();string ending;while((ending=Console.ReadLine())!=null&&ending!="EXIT"){}return 0;}
     else if(parts[0]=="EXIT")return 0;
     else throw new ArgumentException("Unknown synthetic frame command.");
     Console.Out.Flush();
    }
   }return 0;
  }
  private static void HeaderRejection(string description,long offset,int corruptValue){
   string name=Name();using(FrameExchange owner=FrameExchange.CreateWriter(name,1024))using(FrameExchange reader=FrameExchange.OpenReader(name)){
    owner.WriteBgra32(4,4,Pattern(4,4,1),TimestampBase+1,1000);
    using(MemoryMappedFile map=MemoryMappedFile.OpenExisting(FrameExchange.MapObjectName(name)))using(MemoryMappedViewAccessor view=map.CreateViewAccessor()){view.Write(offset,corruptValue);view.Flush();}
    Reject<InvalidDataException>(description,delegate{SharedFrame frame;reader.ReadLatest(0,out frame);});
   }
  }
  private static void Test(string writerExecutable){
   Check("Reader process is x64",IntPtr.Size==8);
   Reject<ArgumentException>("Reject unsafe channel names",delegate{FrameExchange.CreateWriter("../bad",64);});
   Reject<ArgumentOutOfRangeException>("Reject zero capacity",delegate{FrameExchange.CreateWriter(Name(),0);});
   Reject<ArgumentOutOfRangeException>("Reject over-limit capacity",delegate{FrameExchange.CreateWriter(Name(),FrameExchange.MaximumFrameBytes+1);});
   string name=Name();
   ProcessStartInfo start=new ProcessStartInfo(writerExecutable,"writer "+name){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(Process child=Process.Start(start)){
    try{
     Check("Producer process is x86",ReadLine(child)=="READY 32");
     using(FrameExchange reader=FrameExchange.OpenReader(name)){
      SharedFrame frame;
      Check("New channel has no frame",reader.ReadLatest(0,out frame)==FrameReadStatus.Empty&&frame==null);
      Reject<IOException>("Reject duplicate producer mapping",delegate{FrameExchange.CreateWriter(name,4096);});
      Check("First producer sequence",Send(child,"WRITE 4 4 1")=="FRAME 1");
      Check("Cross-process first frame",reader.ReadLatest(0,out frame)==FrameReadStatus.Available&&frame.FrameId==1&&frame.Width==4&&frame.Height==4&&Correct(frame,1));
      Check("No duplicate delivery",reader.ReadLatest(1,out frame)==FrameReadStatus.Unchanged&&frame==null);
      Check("Producer holds shared mutex",Send(child,"HOLD")=="HELD");
      Reject<TimeoutException>("Reader obeys mutex timeout",delegate{SharedFrame waiting;reader.ReadLatest(0,out waiting,25);});
      Check("Producer releases shared mutex",Send(child,"RELEASE")=="RELEASED");
      Check("Timeout leaves channel usable",reader.ReadLatest(0,out frame)==FrameReadStatus.Available&&frame.FrameId==1&&Correct(frame,1));
      Check("Second producer sequence",Send(child,"WRITE 8 2 2")=="FRAME 2");
      Check("Width and height can change",reader.ReadLatest(1,out frame)==FrameReadStatus.Available&&frame.FrameId==2&&frame.Width==8&&frame.Height==2&&Correct(frame,2));
      Check("Third producer sequence",Send(child,"WRITE 32 32 3")=="FRAME 3");
      Check("Exact-capacity frame",reader.ReadLatest(2,out frame)==FrameReadStatus.Available&&frame.FrameId==3&&frame.Pixels.Length==4096&&Correct(frame,3));
      Check("Producer rejects oversized dimensions",Send(child,"WRITE 33 32 4").StartsWith("REJECTED "));
      child.StandardInput.WriteLine("BURST 2000");child.StandardInput.Flush();
      Task<string> burst=Task.Factory.StartNew(delegate{return child.StandardOutput.ReadLine();});
      long last=3;int observed=0;Stopwatch timer=Stopwatch.StartNew();
      while(!burst.IsCompleted){
       if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("Synthetic frame burst exceeded deadline.");
       FrameReadStatus status=reader.ReadLatest(last,out frame,5000);
       if(status==FrameReadStatus.Available){if(frame.FrameId<=last||!Correct(frame,checked((int)(frame.TimestampUtcTicks-TimestampBase))))throw new InvalidDataException("Torn or unordered synthetic frame.");last=frame.FrameId;observed++;}
       else Thread.Sleep(1);
      }
      Check("Concurrent producer completed",burst.Result=="BURST_DONE");
      FrameReadStatus finalStatus=reader.ReadLatest(0,out frame);
      Check("Burst sequence preserved",finalStatus==FrameReadStatus.Available&&frame.FrameId==2003&&Correct(frame,2099));
      Check("Concurrent synthetic reads have intact pixels",observed>0);
      Console.WriteLine("Concurrent snapshots verified: "+observed);
      Reject<ArgumentOutOfRangeException>("Reject negative reader sequence",delegate{SharedFrame invalid;reader.ReadLatest(-1,out invalid);});
      Reject<InvalidOperationException>("Reader cannot publish",delegate{reader.WriteBgra32(1,1,new byte[4]);});
      Check("Producer publishes close",Send(child,"CLOSE")=="CLOSED");
      Check("Reader observes close",reader.ReadLatest(0,out frame)==FrameReadStatus.Closed&&frame==null);
      reader.Dispose();reader.Dispose();
      Reject<ObjectDisposedException>("Disposed reader rejects reads",delegate{SharedFrame invalid;reader.ReadLatest(0,out invalid);});
     }
     child.StandardInput.WriteLine("EXIT");child.StandardInput.Flush();
     Check("Producer exits cleanly",child.WaitForExit(5000)&&child.ExitCode==0);
     Reject<FileNotFoundException>("Mapping released after both endpoints close",delegate{using(MemoryMappedFile.OpenExisting(FrameExchange.MapObjectName(name))){} });
    }finally{if(!child.HasExited){child.Kill();child.WaitForExit(5000);}}
   }
   string abandonedName=Name();ProcessStartInfo abandonedStart=new ProcessStartInfo(writerExecutable,"writer "+abandonedName){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(Process child=Process.Start(abandonedStart)){
    try{
     Check("Crash fixture starts as x86",ReadLine(child)=="READY 32");
     using(FrameExchange reader=FrameExchange.OpenReader(abandonedName)){
      Check("Crash fixture publishes initial frame",Send(child,"WRITE 4 4 7")=="FRAME 1");
      Check("Crash fixture owns mutex",Send(child,"HOLD")=="HELD");
      child.Kill();Check("Crash fixture exits",child.WaitForExit(5000));
      Reject<IOException>("Abandoned producer mutex rejects frame",delegate{SharedFrame invalid;reader.ReadLatest(0,out invalid);});
      Reject<InvalidDataException>("Abandoned channel remains faulted",delegate{SharedFrame invalid;reader.ReadLatest(0,out invalid);});
     }
     Reject<FileNotFoundException>("Crashed producer mapping is reclaimed",delegate{using(MemoryMappedFile.OpenExisting(FrameExchange.MapObjectName(abandonedName))){} });
    }finally{if(!child.HasExited){child.Kill();child.WaitForExit(5000);}}
   }
   using(FrameExchange writer=FrameExchange.CreateWriter(Name(),1024)){
    Reject<ArgumentOutOfRangeException>("Reject zero width",delegate{writer.WriteBgra32(0,1,new byte[0]);});
    Reject<ArgumentOutOfRangeException>("Reject negative height",delegate{writer.WriteBgra32(1,-1,new byte[0]);});
    Reject<ArgumentOutOfRangeException>("Reject oversized multiplication",delegate{writer.WriteBgra32(Int32.MaxValue,Int32.MaxValue,new byte[0]);});
    Reject<ArgumentException>("Reject wrong pixel count",delegate{writer.WriteBgra32(2,2,new byte[15]);});
    Reject<ArgumentNullException>("Reject null pixel buffer",delegate{writer.WriteBgra32(2,2,null);});
    Reject<ArgumentOutOfRangeException>("Reject invalid timestamp",delegate{writer.WriteBgra32(1,1,new byte[4],0,1000);});
    writer.Dispose();writer.Dispose();Reject<ObjectDisposedException>("Disposed writer rejects writes",delegate{writer.WriteBgra32(1,1,new byte[4]);});
   }
   string readerLifetime=Name();using(FrameExchange owner=FrameExchange.CreateWriter(readerLifetime,64)){
    using(FrameExchange temporary=FrameExchange.OpenReader(readerLifetime)){}
    Check("Closing reader keeps producer alive",owner.WriteBgra32(1,1,new byte[4])==1);
   }
   HeaderRejection("Reject corrupt capacity",12,Int32.MaxValue);
   HeaderRejection("Reject corrupt mapping length",16,Int32.MaxValue);
   HeaderRejection("Reject incompatible header magic",0,0);
   HeaderRejection("Reject incompatible header version",4,99);
   HeaderRejection("Reject corrupt active slot",28,9);
   HeaderRejection("Reject corrupt dimensions",48,Int32.MaxValue);
   HeaderRejection("Reject corrupt stride",56,3);
   HeaderRejection("Reject corrupt pixel length",60,Int32.MaxValue);
   HeaderRejection("Reject incomplete frame publication",24,2);
   HeaderRejection("Reject faulted frame channel",24,4);
  }
  public static int Main(string[] args){
   if(args.Length==2&&args[0]=="writer")return Writer(args[1]);
   if(args.Length!=3||args[0]!="test"){Console.Error.WriteLine("Usage: FrameProbe test <x86-writer.exe> <report.json> | writer <name>");return 2;}
   bool passed=false;string error=null;
   try{Test(args[1]);passed=true;}catch(Exception ex){error=ex.ToString();Console.Error.WriteLine(error);}
   object report=new{passed=passed,checkCount=Checks.Count,checks=Checks,readerBits=IntPtr.Size*8,producerBits=32,frameFormat="BGRA32",syntheticFramesOnly=true,nativeGameRenderingVerified=false,error=error};
   File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(report));Console.WriteLine((passed?"PASS ":"FAIL ")+Checks.Count+" synthetic frame transport checks.");return passed?0:1;
  }
 }
}

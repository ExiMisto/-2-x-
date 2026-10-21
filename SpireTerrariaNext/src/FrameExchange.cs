using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace SpireTerraria.Next {
 public enum FrameReadStatus { Empty, Unchanged, Available, Closed }

 // A copy owned by the receiving process; no pointer into the shared mapping escapes.
 public sealed class SharedFrame {
  public long FrameId { get; private set; }
  public long TimestampUtcTicks { get; private set; }
  public int Width { get; private set; }
  public int Height { get; private set; }
  public int Stride { get; private set; }
  public byte[] Pixels { get; private set; }
  internal SharedFrame(long id,long timestamp,int width,int height,int stride,byte[] pixels){FrameId=id;TimestampUtcTicks=timestamp;Width=width;Height=height;Stride=stride;Pixels=pixels;}
 }

 // Single producer, one or more consumers. Windows named objects are local to the session.
 // All metadata and pixel copies occur under the same named mutex. The producer writes
 // the inactive slot and publishes metadata last, so consumers cannot observe half a frame.
 public sealed class FrameExchange : IDisposable {
  public const int MaximumFrameBytes=256*1024*1024;
  public const int MaximumDimension=16384;
  internal const int HeaderBytes=128;
  internal const int Magic=0x32465053;
  private const int Version=1, PixelFormatBgra32=0x41524742;
  private const int StateEmpty=0,StateReady=1,StateWriting=2,StateClosed=3,StateFaulted=4;
  private readonly object sync=new object();
  private readonly bool writer;
  private readonly int capacity;
  private MemoryMappedFile mapping;
  private MemoryMappedViewAccessor view;
  private Mutex gate;
  private bool disposed;
  public string ChannelName { get; private set; }
  public int CapacityBytes { get { return capacity; } }
  public bool IsWriter { get { return writer; } }

  private FrameExchange(string name,int capacityBytes,bool isWriter,MemoryMappedFile map,MemoryMappedViewAccessor accessor,Mutex mutex){ChannelName=name;capacity=capacityBytes;writer=isWriter;mapping=map;view=accessor;gate=mutex;}
  internal static string MapObjectName(string name){ValidateName(name);return "Local\\SpireNext.Frames."+name;}
  private static string GateObjectName(string name){return MapObjectName(name)+".Gate";}
  private static void ValidateName(string name){
   if(String.IsNullOrEmpty(name)||name.Length>160)throw new ArgumentException("Channel name must contain 1 to 160 ASCII letters, digits, dots, hyphens or underscores.","name");
   foreach(char c in name)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='.'||c=='-'||c=='_'))throw new ArgumentException("Invalid channel name character.","name");
  }
  private static void ValidateCapacity(int value){if(value<4||value>MaximumFrameBytes)throw new ArgumentOutOfRangeException("capacityBytes","Per-slot capacity must be between 4 bytes and 256 MiB.");}
  private static int FrameLength(int width,int height,int limit){
   if(width<1||height<1||width>MaximumDimension||height>MaximumDimension)throw new ArgumentOutOfRangeException("width/height","Frame dimensions must be between 1 and 16384.");
   long bytes=(long)width*height*4;
   if(bytes>limit)throw new ArgumentOutOfRangeException("width/height","Frame exceeds channel capacity.");
   return checked((int)bytes);
  }
  public static FrameExchange CreateWriter(string name,int capacityBytes){
   ValidateName(name);ValidateCapacity(capacityBytes);
   Mutex mutex=null;MemoryMappedFile map=null;MemoryMappedViewAccessor accessor=null;bool entered=false;
   try{
    mutex=new Mutex(false,GateObjectName(name));
    try{entered=mutex.WaitOne(5000);}catch(AbandonedMutexException){entered=true;}
    if(!entered)throw new TimeoutException("Timed out creating frame channel.");
    long total=HeaderBytes+(long)capacityBytes*2;
    map=MemoryMappedFile.CreateNew(MapObjectName(name),total,MemoryMappedFileAccess.ReadWrite);
    accessor=map.CreateViewAccessor(0,total,MemoryMappedFileAccess.ReadWrite);
    accessor.Write(0,Magic);accessor.Write(4,Version);accessor.Write(8,HeaderBytes);accessor.Write(12,capacityBytes);accessor.Write(16,total);
    accessor.Write(24,StateEmpty);accessor.Write(28,-1);accessor.Write(32,0L);accessor.Write(40,0L);
    accessor.Write(48,0);accessor.Write(52,0);accessor.Write(56,0);accessor.Write(60,0);
    accessor.Write(64,Process.GetCurrentProcess().Id);accessor.Write(68,PixelFormatBgra32);accessor.Write(72,DateTime.UtcNow.Ticks);
    accessor.Flush();
    FrameExchange result=new FrameExchange(name,capacityBytes,true,map,accessor,mutex);
    mutex.ReleaseMutex();entered=false;return result;
   }catch{
    if(accessor!=null)accessor.Dispose();if(map!=null)map.Dispose();
    if(entered)mutex.ReleaseMutex();if(mutex!=null)mutex.Dispose();throw;
   }
  }
  public static FrameExchange OpenReader(string name){
   ValidateName(name);
   Mutex mutex=null;MemoryMappedFile map=null;MemoryMappedViewAccessor accessor=null;bool entered=false;
   try{
    mutex=Mutex.OpenExisting(GateObjectName(name));
    map=MemoryMappedFile.OpenExisting(MapObjectName(name),MemoryMappedFileRights.ReadWrite);
    accessor=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.ReadWrite);
    try{entered=mutex.WaitOne(5000);}catch(AbandonedMutexException){entered=true;MarkFaulted(accessor);throw new IOException("Frame producer abandoned its mutex; channel rejected.");}
    if(!entered)throw new TimeoutException("Timed out opening frame channel.");
    int slotCapacity=ValidateHeader(accessor,0);
    FrameExchange result=new FrameExchange(name,slotCapacity,false,map,accessor,mutex);
    mutex.ReleaseMutex();entered=false;return result;
   }catch{
    if(accessor!=null)accessor.Dispose();if(map!=null)map.Dispose();
    if(entered)mutex.ReleaseMutex();if(mutex!=null)mutex.Dispose();throw;
   }
  }
  private static int ValidateHeader(MemoryMappedViewAccessor accessor,int expectedCapacity){
   if(accessor.Capacity<HeaderBytes||accessor.ReadInt32(0)!=Magic||accessor.ReadInt32(4)!=Version||accessor.ReadInt32(8)!=HeaderBytes||accessor.ReadInt32(68)!=PixelFormatBgra32)throw new InvalidDataException("Invalid frame mapping header or format.");
   int slotCapacity=accessor.ReadInt32(12);
   if(slotCapacity<4||slotCapacity>MaximumFrameBytes||(expectedCapacity!=0&&slotCapacity!=expectedCapacity))throw new InvalidDataException("Invalid or changed frame capacity.");
   long required=HeaderBytes+(long)slotCapacity*2;
   if(accessor.ReadInt64(16)!=required||required>accessor.Capacity)throw new InvalidDataException("Frame mapping is shorter than its declared capacity.");
   int state=accessor.ReadInt32(24);
   if(state<0||state>StateFaulted)throw new InvalidDataException("Invalid frame channel state.");
   return slotCapacity;
  }
  private static void MarkFaulted(MemoryMappedViewAccessor accessor){if(accessor!=null&&accessor.Capacity>=HeaderBytes&&accessor.ReadInt32(0)==Magic){accessor.Write(24,StateFaulted);accessor.Flush();}}
  private void Enter(int timeoutMilliseconds){
   if(timeoutMilliseconds<0&&timeoutMilliseconds!=Timeout.Infinite)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
   try{if(!gate.WaitOne(timeoutMilliseconds))throw new TimeoutException("Timed out waiting for frame channel mutex.");}
   catch(AbandonedMutexException){try{MarkFaulted(view);}finally{gate.ReleaseMutex();}throw new IOException("A frame channel participant exited while holding the mutex; channel rejected.");}
  }
  private void RequireOpen(){if(disposed)throw new ObjectDisposedException("FrameExchange");}

  public long WriteBgra32(int width,int height,byte[] pixels){return WriteBgra32(width,height,pixels,DateTime.UtcNow.Ticks,1000);}
  public long WriteBgra32(int width,int height,byte[] pixels,long timestampUtcTicks,int timeoutMilliseconds){
   lock(sync){
    RequireOpen();if(!writer)throw new InvalidOperationException("Only the channel owner may publish frames.");
    int length=FrameLength(width,height,capacity);
    if(pixels==null)throw new ArgumentNullException("pixels");if(pixels.Length!=length)throw new ArgumentException("BGRA32 data must contain exactly width * height * 4 bytes.","pixels");
    if(timestampUtcTicks<1||timestampUtcTicks>DateTime.MaxValue.Ticks)throw new ArgumentOutOfRangeException("timestampUtcTicks");
    Enter(timeoutMilliseconds);
    try{
     ValidateHeader(view,capacity);int state=view.ReadInt32(24);
     if(state==StateClosed)throw new InvalidOperationException("Frame channel is closed.");if(state==StateWriting||state==StateFaulted)throw new InvalidDataException("Frame channel is incomplete or faulted.");
     long previous=view.ReadInt64(32);if(previous<0||previous==Int64.MaxValue)throw new InvalidDataException("Frame sequence is invalid or exhausted.");
     int active=view.ReadInt32(28);if(active<-1||active>1)throw new InvalidDataException("Invalid active frame slot.");int next=active==0?1:0;
     view.Write(24,StateWriting);
     try{
      view.WriteArray(HeaderBytes+(long)next*capacity,pixels,0,length);
      view.Write(28,next);view.Write(32,previous+1);view.Write(40,timestampUtcTicks);view.Write(48,width);view.Write(52,height);view.Write(56,checked(width*4));view.Write(60,length);
      view.Write(24,StateReady);
     }catch{view.Write(24,StateFaulted);throw;}
     return previous+1;
    }finally{gate.ReleaseMutex();}
   }
  }
  public FrameReadStatus ReadLatest(long afterFrameId,out SharedFrame frame){return ReadLatest(afterFrameId,out frame,1000);}
  public FrameReadStatus ReadLatest(long afterFrameId,out SharedFrame frame,int timeoutMilliseconds){
   lock(sync){
    RequireOpen();if(afterFrameId<0)throw new ArgumentOutOfRangeException("afterFrameId");frame=null;Enter(timeoutMilliseconds);
    try{
     ValidateHeader(view,capacity);int state=view.ReadInt32(24);
     if(state==StateClosed)return FrameReadStatus.Closed;
     if(state==StateWriting||state==StateFaulted)throw new InvalidDataException("Frame channel is incomplete or faulted.");
     if(state==StateEmpty){if(view.ReadInt64(32)!=0||view.ReadInt32(28)!=-1)throw new InvalidDataException("Invalid empty channel metadata.");return FrameReadStatus.Empty;}
     long id=view.ReadInt64(32),timestamp=view.ReadInt64(40);int slot=view.ReadInt32(28),width=view.ReadInt32(48),height=view.ReadInt32(52),stride=view.ReadInt32(56),length=view.ReadInt32(60);
     int expected;
     try{expected=FrameLength(width,height,capacity);}catch(ArgumentOutOfRangeException ex){throw new InvalidDataException("Invalid shared frame dimensions.",ex);}
     if(id<1||slot<0||slot>1||timestamp<1||timestamp>DateTime.MaxValue.Ticks||stride!=width*4||length!=expected)throw new InvalidDataException("Invalid shared frame metadata.");
     if(id<=afterFrameId)return FrameReadStatus.Unchanged;
     byte[] pixels=new byte[length];int read=view.ReadArray(HeaderBytes+(long)slot*capacity,pixels,0,length);
     if(read!=length)throw new EndOfStreamException("Incomplete shared frame.");
     frame=new SharedFrame(id,timestamp,width,height,stride,pixels);return FrameReadStatus.Available;
    }finally{gate.ReleaseMutex();}
   }
  }
  public void Dispose(){
   lock(sync){
    if(disposed)return;
    try{
     if(writer){
      bool entered=false;
      try{try{entered=gate.WaitOne(5000);}catch(AbandonedMutexException){entered=true;}
       if(!entered)throw new TimeoutException("Could not publish channel closure before disposing handles.");
       view.Write(24,StateClosed);view.Flush();
      }finally{if(entered)gate.ReleaseMutex();}
     }
    }finally{disposed=true;view.Dispose();mapping.Dispose();gate.Dispose();view=null;mapping=null;gate=null;}
   }
  }
 }
}

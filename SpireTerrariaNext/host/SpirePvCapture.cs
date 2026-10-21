using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace SpireTerraria.Next.Host {
 // Captures only the game's rendered viewport. No desktop or monitor capture API
 // is used. Battle steps wait for the previous native frame to be copied, giving
 // exactly two Terraria ticks per 30 fps output frame even on a slow renderer.
 internal sealed class SpirePvCapture : IAsyncDisposable {
  internal static SpirePvCapture Active;
  internal static readonly List<object> Shots=new();
  internal const int Fps=30;
  readonly string path,name,description;
  readonly bool fixture;
  readonly bool battleOnly;
  readonly List<object> markers=new();
  readonly List<object> bookmarks=new();
  Process encoder;
  Stream pipe;
  Task captureTask,errorTask;
  string encoderErrors="";
  bool running=true;
  int frames,width,height;
  BattleOverlay capturedBattle;
  long copiedBattleFrame=-1;
  readonly int startTick;
  readonly DateTime started=DateTime.UtcNow;
  Exception failure;
  int priorEnemyLife=-1,priorLife=-1,priorProjectiles=-1,lastNativeTick=-1;
  BattleOverlay initialBattle;
  SpirePvCapture(string shotName,string detail,bool staged) {
   if(Active!=null)throw new InvalidOperationException("A PV shot is already recording.");
   name=shotName;description=detail;fixture=staged;
   initialBattle=HostIntegration.Active?.Ready==true?HostIntegration.Active:null;battleOnly=initialBattle!=null;
   SpireSelfTest.PVSetViewportCursorAway();
   path=Path.Combine(SpireSelfTest.PVDirectory,"footage",shotName+".mp4");
   if(File.Exists(path))throw new IOException("PV footage is immutable; choose a new take name: "+path);
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   startTick=HostIntegration.Active?.State?.Tick??-1;Active=this;
   captureTask=CaptureLoop();
  }
  internal static SpirePvCapture Start(string name,string description,bool fixture=false)=>new(name,description,fixture);
  internal int Frames=>frames;
  internal bool IsBattleShot=>battleOnly;
  internal void Bookmark(string label)=>bookmarks.Add(new{frame=frames,time=frames/(double)Fps,label});
  internal static bool WaitingForBattleFrame(BattleOverlay battle)=>Active!=null&&Active.running&&battle?.Ready==true&&battle.State!=null&&
   (!ReferenceEquals(Active.capturedBattle,battle)||Active.copiedBattleFrame!=battle.State.Frame);
  async Task CaptureLoop() {
   try {
    while(running) {
     await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
     if(!running)break;
     var battle=HostIntegration.Active;
     // A combat shot ends on its final native frame, before the host resumes
     // the vanilla room underneath it or opens the reward screen.
     if(battleOnly&&(!ReferenceEquals(battle,initialBattle)||battle?.Ready!=true)){running=false;break;}
     if(battle?.Ready==true&&battle.State!=null&&ReferenceEquals(capturedBattle,battle)&&copiedBattleFrame==battle.State.Frame)continue;
     using var image=NGame.Instance.GetViewport().GetTexture().GetImage();
     if(image==null||image.IsEmpty())throw new IOException("Game viewport is empty.");
     image.Convert(Image.Format.Rgba8);
     if(encoder==null)OpenEncoder(image.GetWidth(),image.GetHeight());
     if(image.GetWidth()!=width||image.GetHeight()!=height)image.Resize(width,height,Image.Interpolation.Nearest);
     byte[] data=image.GetData();
     if(battle?.Ready==true&&battle.State!=null) {
      var state=battle.State;
      lastNativeTick=state.Tick;
      markers.Add(new {frame=frames,time=frames/(double)Fps,tick=state.Tick,nativeFrame=state.Frame,
       life=state.Life,lifeMax=state.LifeMax,enemyLife=state.EnemyLife,enemyLifeMax=state.EnemyLifeMax,
       projectiles=state.Projectiles,selectedWeapon=state.SelectedWeapon,remaining=state.Remaining,alive=state.Alive,
       playerX=state.PlayerX,playerY=state.PlayerY,enemyX=state.EnemyX,enemyY=state.EnemyY,
       playerDamage=priorLife<0?0:Math.Max(0,priorLife-state.Life),
       enemyLifeDecrease=priorEnemyLife<0?0:Math.Max(0,priorEnemyLife-state.EnemyLife),
       projectileCountIncrease=priorProjectiles<0?0:Math.Max(0,state.Projectiles-priorProjectiles),
       input=SpireSelfTest.PVLastInput,dead=state.Dead});
      priorLife=state.Life;priorEnemyLife=state.EnemyLife;priorProjectiles=state.Projectiles;
     }
     // Copy completion releases the next native battle step. The managed byte
     // buffer is owned by this writer until its async pipe write completes.
     if(battle?.Ready==true&&battle.State!=null){capturedBattle=battle;copiedBattleFrame=battle.State.Frame;}
     await pipe.WriteAsync(data.AsMemory());
     frames++;
    }
   }catch(Exception error){failure=error;running=false;}
  }
  void OpenEncoder(int w,int h) {
   width=w;height=h;
   string exe=System.Environment.GetEnvironmentVariable("SPIRE_PV_FFMPEG")??@"C:\Users\ExiMisto\AppData\Local\oopz\ffmpeg.exe";
   if(!File.Exists(exe))throw new FileNotFoundException("PV recording requires the configured local FFmpeg.",exe);
   var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true};
   foreach(string arg in new[]{"-hide_banner","-loglevel","warning","-nostats","-f","rawvideo","-pixel_format","rgba","-video_size",width+"x"+height,"-framerate",Fps.ToString(),"-i","pipe:0","-an","-c:v","libx264","-preset","ultrafast","-crf","15","-pix_fmt","yuv420p","-movflags","+faststart","-n",path})info.ArgumentList.Add(arg);
   encoder=Process.Start(info)??throw new IOException("FFmpeg did not start.");
   pipe=encoder.StandardInput.BaseStream;
   errorTask=ReadEncoderErrors();
  }
  async Task ReadEncoderErrors(){encoderErrors=await encoder.StandardError.ReadToEndAsync();}
  internal bool Running=>running;
  public async ValueTask DisposeAsync() {
   running=false;
   try {
    await captureTask.WaitAsync(TimeSpan.FromSeconds(15));
    if(pipe!=null){await pipe.FlushAsync();pipe.Dispose();pipe=null;}
    if(encoder!=null){await encoder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));await errorTask;}
    if(failure!=null)throw new IOException("PV viewport capture failed: "+name,failure);
    if(encoder==null||encoder.ExitCode!=0||frames<2)throw new IOException("PV encoder failed: "+name+" "+encoderErrors);
    string markersPath=Path.ChangeExtension(path,"markers.json");
    File.WriteAllText(markersPath,JsonSerializer.Serialize(new{shot=name,fps=Fps,description="Observed native combat state at each encoded frame. Enemy life decreases may include target changes; confirm against picture before sound design.",bookmarks,frames=markers},new JsonSerializerOptions{WriteIndented=true}));
    var shot=new {name,file=path,markersFile=markersPath,description,source="Godot NGame viewport only",fixture,frames,fps=Fps,width,height,duration=frames/(double)Fps,
     nativeStartTick=startTick,nativeEndTick=lastNativeTick,startedUtc=started,finishedUtc=DateTime.UtcNow,
     capture="raw RGBA pipe, no desktop API",audio="silent source; sound design and original score supplied in edit"};
    Shots.Add(shot);
    string manifest=Path.Combine(SpireSelfTest.PVDirectory,"footage","shots-"+SpireSelfTest.PVTake+".json");
    File.WriteAllText(manifest,JsonSerializer.Serialize(Shots,new JsonSerializerOptions{WriteIndented=true}));
    GD.Print("[SpireNext PV] "+name+" "+frames+" frames "+width+"x"+height);
   }finally{if(ReferenceEquals(Active,this))Active=null;encoder?.Dispose();}
  }
 }
}

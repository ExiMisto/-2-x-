using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SpireTerraria.Next {
 internal static class BattlePreview {
  [STAThread] static int Main(string[] arguments) {
   string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));
   string worker=Path.Combine(root,".local","bin","SpireNext.Worker.exe"),game=Path.GetFullPath(Path.Combine(root,"..")),weapon="EnchantedSword";bool smoke=false;
   for(int i=0;i<arguments.Length;i++) {
    string argument=arguments[i];if(argument=="--smoke"){smoke=true;continue;}
    if(i+1>=arguments.Length)throw new ArgumentException("Missing argument value: "+argument);
    string value=arguments[++i];if(argument=="--root")root=Path.GetFullPath(value);else if(argument=="--worker")worker=Path.GetFullPath(value);else if(argument=="--game")game=Path.GetFullPath(value);else if(argument=="--weapon")weapon=value;else throw new ArgumentException("Unknown argument: "+argument);
   }
   string output=Path.Combine(root,".local","preview-test");
   var report=new Dictionary<string,object>{{"passed",false},{"hostBits",IntPtr.Size*8},{"smokeTest",smoke},{"desktopCaptured",false}};
   try {
    if(IntPtr.Size!=8)throw new InvalidOperationException("Preview must run as a 64-bit process.");
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    using(var window=new PreviewWindow(worker,game,weapon,smoke,output,report))Application.Run(window);
   } catch(Exception error) {report["error"]=error.ToString();if(!smoke)MessageBox.Show("预览未能启动。请先运行 tools/Build.ps1，并确认本机泰拉瑞亚安装完整。\r\n\r\n"+error.Message,"泰拉尖塔",MessageBoxButtons.OK,MessageBoxIcon.Error);}
   if(smoke){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"result.json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));}
   return smoke&&!(bool)report["passed"]?1:0;
  }
 }

 internal sealed class PreviewWindow : Form {
  readonly string workerPath,gameDirectory,smokeOutput;
  readonly bool smoke;
  readonly Dictionary<string,object> report;
  readonly HashSet<Keys> held=new HashSet<Keys>();
  readonly HashSet<Keys> commands=new HashSet<Keys>();
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
  readonly Stopwatch idleWatch=new Stopwatch(),smokeWatch=new Stopwatch(),smokeCombatWatch=new Stopwatch(),simulationClock=new Stopwatch();
  readonly SceneCanvas scene=new SceneCanvas();
  readonly ComboBox weapons=new ComboBox();
  readonly Label health=new Label(),status=new Label();
  readonly Button pauseButton=new Button(),restartButton=new Button();
  BridgeClient bridge;
  FrameExchange frames;
  BattleViewState state;
  bool running,busy,paused,attacking,closing,ready,smokeSaved;
  bool smokePausePhase,smokePauseVerified,smokeResumeVerified;
  int smokePauseTick,smokePauseHeartbeats;
  double simulationAccumulator;
  long shownFrame;
  int workerId;
  byte[] lastPixels;
  string currentWeapon;

  internal PreviewWindow(string worker,string game,string weapon,bool smoke,string output,Dictionary<string,object> report) {
   workerPath=worker;gameDirectory=game;this.smoke=smoke;smokeOutput=output;this.report=report;
   Text="泰拉尖塔 · 实机战斗预览";StartPosition=FormStartPosition.CenterScreen;KeyPreview=true;BackColor=Color.FromArgb(23,29,38);ForeColor=Color.White;Font=new Font("Microsoft YaHei UI",9f);
   Rectangle area=Screen.PrimaryScreen.WorkingArea;ClientSize=area.Width>=1350&&area.Height>=950?new Size(1280,860):new Size(960,650);MinimumSize=new Size(700,540);
   if(smoke){ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);Opacity=0;}
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,80));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));Controls.Add(layout);
   var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};header.RowStyles.Add(new RowStyle(SizeType.Absolute,30));header.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
   header.Controls.Add(new Label{Text="泰拉尖塔  /  蓝史莱姆试炼",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,13f,FontStyle.Bold),AutoSize=false},0,0);
   var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,3,0,0)};
   toolbar.Controls.Add(new Label{Text="武器",AutoSize=true,Margin=new Padding(0,7,8,0)});weapons.DropDownStyle=ComboBoxStyle.DropDownList;weapons.Width=180;
   weapons.Items.Add(new WeaponChoice("EnchantedSword","附魔剑"));weapons.Items.Add(new WeaponChoice("WoodenSword","木剑"));weapons.Items.Add(new WeaponChoice("WaterBolt","水矢"));
   weapons.SelectedIndex=0;for(int i=0;i<weapons.Items.Count;i++)if(((WeaponChoice)weapons.Items[i]).Symbol==weapon)weapons.SelectedIndex=i;
   toolbar.Controls.Add(weapons);pauseButton.Text="暂停 [P]";pauseButton.Width=105;restartButton.Text="重新开始 [R]";restartButton.Width=135;toolbar.Controls.Add(pauseButton);toolbar.Controls.Add(restartButton);
   toolbar.Controls.Add(new Label{Text="开发预览 · 独立窗口",AutoSize=true,ForeColor=Color.Silver,Margin=new Padding(15,7,0,0)});header.Controls.Add(toolbar,0,1);layout.Controls.Add(header,0,0);
   scene.Dock=DockStyle.Fill;scene.Margin=new Padding(0);layout.Controls.Add(scene,0,1);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(0,6,0,0)};footer.RowStyles.Add(new RowStyle(SizeType.Absolute,22));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,22));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,22));health.Dock=DockStyle.Fill;status.Dock=DockStyle.Fill;status.AutoEllipsis=true;
   footer.Controls.Add(health,0,0);footer.Controls.Add(status,0,1);footer.Controls.Add(new Label{Text="A/D 或 ←/→ 移动    空格/W/↑ 跳跃    鼠标瞄准 · 左键攻击    P 暂停    R 重开    Esc 退出",Dock=DockStyle.Fill,ForeColor=Color.Silver},0,2);layout.Controls.Add(footer,0,2);
   pauseButton.Click+=delegate{TogglePause();};restartButton.Click+=delegate{Restart();};weapons.SelectedIndexChanged+=delegate{if(ready)Restart();};
   scene.MouseMove+=delegate(object sender,MouseEventArgs e){scene.Aim=e.Location;scene.Invalidate();};scene.MouseDown+=delegate(object sender,MouseEventArgs e){scene.Focus();if(e.Button==MouseButtons.Left&&!paused){attacking=true;scene.Capture=true;}};scene.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){attacking=false;scene.Capture=false;}};scene.MouseCaptureChanged+=delegate{if(!scene.Capture)attacking=false;};
   foreach(Control inputControl in new Control[]{scene,weapons,pauseButton,restartButton})inputControl.PreviewKeyDown+=delegate(object sender,PreviewKeyDownEventArgs e){if(IsBattleKey(e.KeyCode))e.IsInputKey=true;};
   KeyDown+=HandleKeyDown;KeyUp+=delegate(object sender,KeyEventArgs e){held.Remove(e.KeyCode);commands.Remove(e.KeyCode);};Deactivate+=delegate{ClearInput();commands.Clear();ResetSimulationClock();if(running){paused=true;RefreshStatus();}};
   timer.Interval=16;timer.Tick+=Tick;Shown+=delegate{ready=true;Restart();timer.Start();};FormClosing+=delegate{closing=true;timer.Stop();ClearInput();Cleanup();};
  }
  static bool IsBattleKey(Keys key){return key==Keys.Space||key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.A||key==Keys.D||key==Keys.W||key==Keys.S;}
  protected override bool ProcessCmdKey(ref Message message,Keys keyData){if(IsBattleKey(keyData&Keys.KeyCode)){HandleKeyDown(this,new KeyEventArgs(keyData));return true;}return base.ProcessCmdKey(ref message,keyData);}
  void HandleKeyDown(object sender,KeyEventArgs e) {
   if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;Close();return;}
   held.Add(e.KeyCode);bool first=commands.Add(e.KeyCode);if(e.KeyCode==Keys.P&&first){TogglePause();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.R&&first){Restart();e.SuppressKeyPress=true;}
   if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Left||e.KeyCode==Keys.Right||e.KeyCode==Keys.Up||e.KeyCode==Keys.A||e.KeyCode==Keys.D||e.KeyCode==Keys.W)e.SuppressKeyPress=true;
  }
  void TogglePause(){if(!running)return;paused=!paused;ClearInput();ResetSimulationClock();RefreshStatus();scene.Focus();}
  void ResetSimulationClock(){simulationAccumulator=0;simulationClock.Restart();}
  void ClearInput(){held.Clear();attacking=false;scene.Capture=false;}
  void EnsureBridge() {
   if(bridge!=null&&!bridge.Process.HasExited)return;
   if(bridge!=null){bridge.Dispose();bridge=null;}
   if(!File.Exists(workerPath))throw new FileNotFoundException("缺少战斗程序，请先运行 tools/Build.ps1。",workerPath);
   bridge=new BridgeClient(workerPath,gameDirectory);workerId=bridge.Process.Id;report["workerProcessId"]=workerId;
   using(var reader=Protocol.Reader(Call(Operation.Hello,Protocol.Body(delegate(BinaryWriter writer){writer.Write(bridge.Secret);})))){reader.ReadString();if(reader.ReadUInt16()!=Protocol.Version||reader.ReadInt32()!=32)throw new InvalidDataException("战斗程序版本不匹配。请重新构建。 ");Protocol.RequireEnd(reader);}
   idleWatch.Restart();
  }
  Packet Call(Operation operation,byte[] body) {
   Packet packet=bridge.Call(operation,body);idleWatch.Restart();
   if(packet.IsError)using(var reader=Protocol.Reader(packet)){string kind=reader.ReadString(),message=reader.ReadString();Protocol.RequireEnd(reader);throw new InvalidOperationException(message+" ("+kind+")");}
   return packet;
  }
  void Restart() {
   if(busy||closing)return;busy=true;ClearInput();SetButtons(false);status.Text="正在准备场地……";
   try {
    EndBattle();EnsureBridge();currentWeapon=((WeaponChoice)weapons.SelectedItem).Symbol;
    using(var reader=Protocol.Reader(Call(Operation.BattleStart,Protocol.Body(delegate(BinaryWriter writer){writer.Write(20261021);writer.Write(currentWeapon);writer.Write("BlueSlime");})))) {
     string session=reader.ReadString();Guid parsed;if(!Guid.TryParseExact(session,"N",out parsed))throw new InvalidDataException("场地会话无效，请重新开始。");
     int width=reader.ReadInt32(),height=reader.ReadInt32();if(width!=640||height!=360)throw new InvalidDataException("场地画面尺寸不匹配，请重新构建。");
     state=ReadState(reader);Protocol.RequireEnd(reader);running=true;frames=FrameExchange.OpenReader(session);
    }
    paused=false;shownFrame=0;CopyFrame();smokeWatch.Restart();ResetSimulationClock();RefreshStatus();scene.Focus();
    if(smoke){paused=true;smokePausePhase=true;smokePauseTick=state.Tick;smokePauseHeartbeats=0;smokePauseVerified=false;smokeResumeVerified=false;smokeCombatWatch.Reset();status.Text="自动检查：短暂停留后继续战斗……";}
   } catch(Exception error){ShowError(error);}finally{SetButtons(true);busy=false;}
  }
  void EndBattle() {
   bool hadBattle=running;running=false;
   if(frames!=null){frames.Dispose();frames=null;}
   if(hadBattle&&bridge!=null&&!bridge.Process.HasExited)using(var reader=Protocol.Reader(Call(Operation.BattleEnd,null))){if(reader.ReadString()!="ended")throw new InvalidDataException("场地未能正确结束。");Protocol.RequireEnd(reader);}
   state=null;
  }
  void Tick(object sender,EventArgs args) {
   if(busy||closing)return;busy=true;
   try {
    double elapsed=simulationClock.IsRunning?Math.Min(0.25,simulationClock.Elapsed.TotalSeconds):0;simulationClock.Restart();
    if(smoke&&smokePausePhase&&smokeWatch.Elapsed.TotalSeconds>=17) {
     smokePauseVerified=state!=null&&state.Tick==smokePauseTick&&smokePauseHeartbeats>=3&&bridge!=null&&!bridge.Process.HasExited;
     if(!smokePauseVerified)throw new InvalidOperationException("暂停后的战斗连接未通过检查。");
     report["pauseSeconds"]=smokeWatch.Elapsed.TotalSeconds;report["pauseHeartbeats"]=smokePauseHeartbeats;report["pausedTickUnchanged"]=true;
     smokePausePhase=false;paused=false;ClearInput();ResetSimulationClock();smokeCombatWatch.Restart();elapsed=0;RefreshStatus();
    }
    if(running&&!paused&&state!=null&&!state.Finished) {
     simulationAccumulator+=elapsed;int due=(int)Math.Floor(simulationAccumulator*60);int count=Math.Min(4,due);
     // Discard excess catch-up after a long stall; pause never accumulates work.
     if(due>0)simulationAccumulator-=due/60.0;
     if(count>0) {
     byte input=Input();float cameraX=Math.Max(0,Math.Min(512*16-640,state.PlayerX-320));PointF aim=scene.ImagePoint();float aimX=cameraX+aim.X,aimY=168*16-260+aim.Y;
     if(smoke){double seconds=smokeCombatWatch.Elapsed.TotalSeconds;input=(byte)(seconds<0.7?2:0);if(seconds>0.2&&seconds<0.42)input|=4;if(seconds>0.9)input|=8;aimX=state.EnemyX+15;aimY=state.EnemyY+15;}
     using(var reader=Protocol.Reader(Call(Operation.BattleStep,Protocol.Body(delegate(BinaryWriter writer){writer.Write(count);writer.Write(input);writer.Write(aimX);writer.Write(aimY);})))){state=ReadState(reader);Protocol.RequireEnd(reader);}
     if(smoke&&smokePauseVerified&&state.Tick>smokePauseTick)smokeResumeVerified=true;
     CopyFrame();RefreshStatus();
     }
    } else {simulationAccumulator=0;if(bridge!=null&&idleWatch.ElapsedMilliseconds>=5000)using(var reader=Protocol.Reader(Call(Operation.Ping,null))){if(reader.ReadString()!="pong")throw new InvalidDataException("战斗连接已中断，请重新开始。");Protocol.RequireEnd(reader);if(smokePausePhase)smokePauseHeartbeats++;}}
    if(smoke&&!smokeSaved&&smokeCombatWatch.IsRunning&&smokeCombatWatch.Elapsed.TotalSeconds>=3)SaveSmoke();
   } catch(Exception error){ShowError(error);if(smoke)SaveSmoke();}finally{busy=false;}
  }
  byte Input(){byte value=0;if(held.Contains(Keys.A)||held.Contains(Keys.Left))value|=1;if(held.Contains(Keys.D)||held.Contains(Keys.Right))value|=2;if(held.Contains(Keys.Space)||held.Contains(Keys.W)||held.Contains(Keys.Up))value|=4;if(attacking)value|=8;return value;}
  static BattleViewState ReadState(BinaryReader reader) {
   return new BattleViewState{Tick=reader.ReadInt32(),Life=reader.ReadInt32(),LifeMax=reader.ReadInt32(),PlayerX=reader.ReadSingle(),PlayerY=reader.ReadSingle(),Dead=reader.ReadBoolean(),EnemyLife=reader.ReadInt32(),EnemyLifeMax=reader.ReadInt32(),EnemyX=reader.ReadSingle(),EnemyY=reader.ReadSingle(),EnemyActive=reader.ReadBoolean(),Projectiles=reader.ReadInt32(),Frame=reader.ReadInt64()};
  }
  void CopyFrame() {
   SharedFrame frame;FrameReadStatus read=frames.ReadLatest(shownFrame,out frame);
   if(read==FrameReadStatus.Available){if(frame.FrameId!=state.Frame||frame.Width!=640||frame.Height!=360)throw new InvalidDataException("战斗画面与场地状态不一致，请重新开始。");scene.SetFrame(frame);shownFrame=frame.FrameId;lastPixels=frame.Pixels;}
   else if(read==FrameReadStatus.Closed||read==FrameReadStatus.Empty||shownFrame!=state.Frame)throw new InvalidDataException("战斗画面暂时不可用，请重新开始。");
  }
  void RefreshStatus() {
   if(state==null)return;health.Text="生命 "+Math.Max(0,state.Life)+" / "+state.LifeMax+"     蓝史莱姆 "+Math.Max(0,state.EnemyLife)+" / "+state.EnemyLifeMax;
   if(state.Dead)status.Text="这次倒下了。按 R 再试一次！";else if(!state.EnemyActive||state.EnemyLife<=0)status.Text="胜利！蓝史莱姆已击败。按 R 再来一场。";else status.Text=paused?"已暂停。按 P 继续；切回窗口后也需按 P。":"移动、跳跃，瞄准史莱姆后按住鼠标左键攻击。";
   pauseButton.Text=paused?"继续 [P]":"暂停 [P]";
  }
  void SetButtons(bool enabled){weapons.Enabled=enabled;restartButton.Enabled=enabled;pauseButton.Enabled=enabled;}
  void ShowError(Exception error){paused=true;ClearInput();ResetSimulationClock();Cleanup();status.Text="战斗未能继续。按 R 重试；若仍失败，请确认泰拉瑞亚安装完整。";health.Text=error.Message;report["error"]=error.ToString();if(smoke&&!smokeWatch.IsRunning)smokeWatch.Start();}
  void SaveSmoke() {
   if(smokeSaved)return;smokeSaved=true;
   try {
    Directory.CreateDirectory(smokeOutput);
    using(var bitmap=new Bitmap(scene.ClientSize.Width,scene.ClientSize.Height)){scene.DrawToBitmap(bitmap,new Rectangle(Point.Empty,scene.ClientSize));bitmap.Save(Path.Combine(smokeOutput,"host-preview.png"),ImageFormat.Png);report["previewWidth"]=bitmap.Width;report["previewHeight"]=bitmap.Height;}
    report["sourceWidth"]=640;report["sourceHeight"]=360;report["frameId"]=shownFrame;report["ticks"]=state==null?0:state.Tick;report["weapon"]=currentWeapon;report["elapsedSeconds"]=smokeWatch.Elapsed.TotalSeconds;
    report["pauseKeepaliveVerified"]=smokePauseVerified;report["resumeVerified"]=smokeResumeVerified;report["combatElapsedSeconds"]=smokeCombatWatch.Elapsed.TotalSeconds;
    if(state!=null){report["life"]=state.Life;report["enemyLife"]=state.EnemyLife;report["playerDead"]=state.Dead;report["enemyActive"]=state.EnemyActive;report["activeProjectiles"]=state.Projectiles;}
    if(lastPixels!=null)using(var sha=SHA256.Create())report["sourceFrameSha256"]=BitConverter.ToString(sha.ComputeHash(lastPixels)).Replace("-","").ToLowerInvariant();
    report["passed"]=!report.ContainsKey("error")&&state!=null&&(state.Tick>=60||(!state.Dead&&(!state.EnemyActive||state.EnemyLife<=0)))&&shownFrame==state.Frame&&lastPixels!=null&&smokePauseVerified&&smokeResumeVerified;
   } catch(Exception error){report["passed"]=false;report["error"]=error.ToString();}
   BeginInvoke(new Action(Close));
  }
  void Cleanup() {
   try{EndBattle();}catch(Exception error){report["endError"]=error.Message;}
   if(bridge!=null){try{if(!bridge.Process.HasExited)Call(Operation.Shutdown,null);}catch{}finally{try{bridge.Dispose();}catch(Exception error){report["cleanupError"]=error.Message;}bridge=null;}}
   bool alive=false;if(workerId!=0)try{using(Process process=Process.GetProcessById(workerId))alive=!process.HasExited;}catch(ArgumentException){}
   report["workerReclaimed"]=!alive;if(alive)report["passed"]=false;
  }
  protected override void Dispose(bool disposing){if(disposing){if(!closing){closing=true;timer.Stop();Cleanup();}timer.Dispose();scene.Dispose();}base.Dispose(disposing);}
  sealed class WeaponChoice {internal readonly string Symbol;readonly string name;internal WeaponChoice(string symbol,string name){Symbol=symbol;this.name=name;}public override string ToString(){return name;}}
  sealed class BattleViewState {internal int Tick,Life,LifeMax,EnemyLife,EnemyLifeMax,Projectiles;internal float PlayerX,PlayerY,EnemyX,EnemyY;internal bool Dead,EnemyActive;internal long Frame;internal bool Finished{get{return Dead||!EnemyActive||EnemyLife<=0;}}}
 }

 internal sealed class SceneCanvas : Control {
  Bitmap image;
  internal Point Aim;
  internal SceneCanvas(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);TabStop=true;BackColor=Color.FromArgb(12,17,23);Aim=new Point(Width/2,Height/2);}
  protected override bool IsInputKey(Keys keyData){Keys key=keyData&Keys.KeyCode;return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Space||base.IsInputKey(keyData);}
  Rectangle Viewport(){int scale=Math.Max(1,Math.Min(ClientSize.Width/640,ClientSize.Height/360));return new Rectangle((ClientSize.Width-640*scale)/2,(ClientSize.Height-360*scale)/2,640*scale,360*scale);}
  internal PointF ImagePoint(){Rectangle viewport=Viewport();return new PointF(Math.Max(0,Math.Min(639,(Aim.X-viewport.X)*640f/viewport.Width)),Math.Max(0,Math.Min(359,(Aim.Y-viewport.Y)*360f/viewport.Height)));}
  internal void SetFrame(SharedFrame frame) {
   var next=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb);
   try{BitmapData data=next.LockBits(new Rectangle(0,0,next.Width,next.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{for(int row=0;row<frame.Height;row++)Marshal.Copy(frame.Pixels,row*frame.Stride,IntPtr.Add(data.Scan0,row*data.Stride),frame.Stride);}finally{next.UnlockBits(data);}}
   catch{next.Dispose();throw;}
   Bitmap previous=image;image=next;if(previous!=null)previous.Dispose();Invalidate();
  }
  protected override void OnPaint(PaintEventArgs e) {
   base.OnPaint(e);if(image==null)return;e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;e.Graphics.DrawImage(image,Viewport());
   if(ClientRectangle.Contains(Aim))using(var pen=new Pen(Color.FromArgb(200,Color.White),1)){e.Graphics.DrawLine(pen,Aim.X-5,Aim.Y,Aim.X+5,Aim.Y);e.Graphics.DrawLine(pen,Aim.X,Aim.Y-5,Aim.X,Aim.Y+5);}
  }
  protected override void Dispose(bool disposing){if(disposing&&image!=null){image.Dispose();image=null;}base.Dispose(disposing);}
 }
}

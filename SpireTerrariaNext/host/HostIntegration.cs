using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireTerraria.Next;

namespace SpireTerraria.Next.Host {
 internal static class HostIntegration {
  internal static BattleOverlay Active;
  internal static readonly string ModDirectory=Path.GetDirectoryName(typeof(EntryProbe).Assembly.Location);
  internal static bool Enabled=true;
  static SceneTree tree;static bool started,starting;static int generation;
  static CombatState pendingCombat,awaitingHostVictory;static RunState pendingRun;static NRun pendingScene;
  internal static void Install() {
   SpireSelfTest.InstallSaveIsolation();var harmony=new Harmony("ExiMisto.SpireTerrariaNextProbe");int inputPatches=0;
   foreach(string name in new[]{"MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager","MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi"}) {
    Type type=typeof(NGame).Assembly.GetType(name);if(type==null)continue;
    foreach(string method in new[]{"_Input","_UnhandledInput","_UnhandledKeyInput"}) {
     MethodInfo target=type.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
     if(target!=null){harmony.Patch(target,prefix:new HarmonyMethod(typeof(HostIntegration).GetMethod("AllowOriginalInput",BindingFlags.NonPublic|BindingFlags.Static)));inputPatches++;}
    }
   }
   AdventureSession.Install(harmony);AdventureCharacters.Install(harmony);AdventureInventoryModels.Install(harmony);
   AdventureInventoryModels.CanUsePotion=CanUseInventoryPotion;AdventureInventoryModels.UsePotion=UseInventoryPotion;
   harmony.Patch(AccessTools.Method(typeof(NGame),"StartNewSingleplayerRun"),prefix:new HarmonyMethod(typeof(HostIntegration),nameof(IsolateTestRun)));
   AdventureHostUi.Install(harmony);CombatManager.Instance.CombatBegan+=OnCombatBegan;
   CombatManager.Instance.CombatWon+=OnCombatWon;CombatManager.Instance.CombatEnded+=OnCombatEnded;
   RunManager.Instance.RoomExited+=OnRoomExited;
   foreach(string method in new[]{"CleanUp","Abandon"})harmony.Patch(AccessTools.Method(typeof(RunManager),method),prefix:new HarmonyMethod(typeof(HostIntegration),nameof(OnRunLeaving)));
   harmony.Patch(AccessTools.Method(typeof(CombatManager),"Reset"),prefix:new HarmonyMethod(typeof(HostIntegration),nameof(OnRunLeaving)));
   Type winCommand=typeof(NGame).Assembly.GetType("MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.WinConsoleCmd");
   if(winCommand!=null)harmony.Patch(AccessTools.Method(winCommand,"Process"),prefix:new HarmonyMethod(typeof(HostIntegration),nameof(OnConsoleWin)));
   foreach(string method in new[]{"ReturnToMainMenu","ReturnToMainMenuAfterRun","ReturnToMainMenuWithInternalError","Quit","_ExitTree"})harmony.Patch(AccessTools.Method(typeof(NGame),method),prefix:new HarmonyMethod(typeof(HostIntegration),nameof(OnRunLeaving)));
   if(SpireSelfTest.Enabled)SpireSelfTest.Report["hostInputMethodsPatched"]=inputPatches;
   tree=(SceneTree)Engine.GetMainLoop();tree.ProcessFrame+=Bootstrap;tree.ProcessFrame+=CheckOwner;tree.Root.TreeExiting+=OnRunLeaving;GD.Print("[SpireTerrariaNext] independent adventure integration installed");
  }
  static bool AllowOriginalInput(){return Active==null||Active.NativeScreenPaused;}
  static void IsolateTestRun(ref bool shouldSave,ref string seed){if(SpireSelfTest.Enabled){shouldSave=false;seed="SPIRENEXTM2";}}
  internal static bool OriginalInputAllowed=>AllowOriginalInput();
  internal static bool CanUseInventoryPotion(string instanceId)=>Active?.CanUsePotion(instanceId)==true;
  internal static Task<bool> UseInventoryPotion(string instanceId)=>Active?.UsePotion(instanceId)??Task.FromResult(false);
  static void Bootstrap(){if(started||NGame.Instance==null||!NGame.Instance.IsInsideTree())return;started=true;tree.ProcessFrame-=Bootstrap;_=PrepareInterface();}
  static async Task PrepareInterface(){try{await AdventureHostUi.PreloadItemIcons();if(SpireSelfTest.Enabled)await SpireSelfTest.Run();}catch(Exception error){GD.PrintErr("[SpireNext] interface preparation failed: "+error);if(SpireSelfTest.Enabled){SpireSelfTest.Report["interfacePreparationError"]=error.ToString();await SpireSelfTest.Run();}}}
  internal static bool OwnsCombat(CombatState combat,RunState run,NRun scene)=>run!=null&&ReferenceEquals(AdventureSession.HostRun,run)&&GodotObject.IsInstanceValid(scene)&&scene.IsInsideTree()&&ReferenceEquals(NRun.Instance,scene)&&ReferenceEquals(CombatManager.Instance.DebugOnlyGetState(),combat)&&!RunManager.Instance.IsCleaningUp;
  static void CheckOwner(){if(Active!=null&&!Active.HasBattleOwner)_=EndExternally("combat owner removed");else if(starting&&Active==null&&!OwnsCombat(pendingCombat,pendingRun,pendingScene))_=EndExternally("pending combat owner removed");}
  static void OnCombatWon(CombatRoom room){
   if(Active!=null&&ReferenceEquals(Active.Combat,room.CombatState)||starting&&ReferenceEquals(pendingCombat,room.CombatState))_=EndExternally("host combat won",true);
   if(ReferenceEquals(awaitingHostVictory,room.CombatState)){awaitingHostVictory=null;if(SpireSelfTest.Enabled){SpireSelfTest.Report["externalHostVictory"]=true;SpireSelfTest.CombatCompleted.TrySetResult(true);}}
  }
  static void OnCombatEnded(CombatRoom room){if(Active!=null&&ReferenceEquals(Active.Combat,room.CombatState)||starting&&ReferenceEquals(pendingCombat,room.CombatState))_=EndExternally("host combat ended");if(ReferenceEquals(awaitingHostVictory,room.CombatState))awaitingHostVictory=null;}
  static void OnConsoleWin(){if((Active!=null||starting)&&CombatManager.Instance.IsInProgress){awaitingHostVictory=Active?.Combat??pendingCombat;_=EndExternally("host console win");}}
  static void OnRoomExited(){awaitingHostVictory=null;_=EndExternally("room exited");}
  static void OnRunLeaving(){awaitingHostVictory=null;_=EndExternally("run or game closing");}
  internal static void OwnerLeaving(BattleOverlay overlay){if(ReferenceEquals(Active,overlay))_=EndExternally("run scene removed");}
  static void OnCombatBegan(CombatState state){
   if(!Enabled||Active!=null||starting||state.Players.Count()!=1)return;
   if(!AdventureSession.IsLegacyTest&&state.Players.First().Character is not AdventureCharacter)return;
   int ticket=++generation;starting=true;awaitingHostVictory=null;pendingCombat=state;pendingRun=AdventureSession.HostRun;pendingScene=NRun.Instance;CombatManager.Instance.Pause();_=StartBattle(state,ticket);
  }
  static async Task StartBattle(CombatState state,int ticket) {
   BattleOverlay overlay=null;
   try {
    if(!AdventureSession.IsLegacyTest){AdventureSession.EnsureRun();await AdventureHostUi.EnsureClassChosen();}
    if(ticket!=generation)return;if(!OwnsCombat(state,pendingRun,pendingScene)){CombatManager.Instance.Unpause();return;}
    overlay=new BattleOverlay(tree,state);Active=overlay;await overlay.Start();
   }catch(Exception error){
    bool current=ticket==generation;if(overlay!=null&&ReferenceEquals(Active,overlay)){Active=null;CombatManager.Instance.Unpause();}
    if(overlay!=null)await overlay.Close();
    if(current){GD.PrintErr("[SpireTerrariaNext] attach: "+error);CombatManager.Instance.Unpause();if(SpireSelfTest.Enabled)SpireSelfTest.CombatCompleted.TrySetException(error);}
   }finally{if(ticket==generation){starting=false;pendingCombat=null;pendingRun=null;pendingScene=null;}}
  }
  internal static async Task Complete(BattleOverlay overlay,bool won) {
   if(Active!=overlay)return;
   Active=null;starting=false;++generation;Task cleanup=null;Exception failure=null;
   try {
    try{overlay.FinishProgress(won);}catch(Exception error){failure=error;GD.PrintErr("[SpireTerrariaNext] battle progress: "+error);}
    cleanup=overlay.Close();CombatManager.Instance.Unpause();
    if(overlay.HasBattleOwner){
     if(won) {
      // Native win removes enemy powers before killing them. Killing only the
      // current enemy snapshot leaves death-spawn/replacement encounters alive.
      // FinishProgress has already saved the real Terraria victory and drops.
      var result=new WinConsoleCmd().Process(overlay.Combat.Players.Single(),Array.Empty<string>());
      if(!result.success)throw new InvalidOperationException("尖塔胜利结算失败："+result.msg);
      if(result.task!=null)await result.task;
      if(SpireSelfTest.Enabled){SpireSelfTest.Report["nativeVictorySettlement"]="WinConsoleCmd.Process";SpireSelfTest.Report["nativeVictorySettlementTaskCompleted"]=true;}
     }else {
      CombatManager.Instance.LoseCombat();
      await CombatManager.Instance.CheckWinCondition();
     }
    }
   }catch(Exception error){failure=error;GD.PrintErr("[SpireTerrariaNext] completion: "+error);}
   finally{if(cleanup==null)cleanup=overlay.Close();await cleanup;}
   if(SpireSelfTest.Enabled){if(failure!=null)SpireSelfTest.CombatCompleted.TrySetException(failure);else SpireSelfTest.CombatCompleted.TrySetResult(won);}
  }
  internal static async Task Abort(BattleOverlay overlay){if(Active!=overlay)return;await EndExternally("original card combat resumed");}
  static async Task EndExternally(string reason,bool hostWon=false){
   if(Active==null&&!starting)return;++generation;starting=false;pendingCombat=null;pendingRun=null;pendingScene=null;
   var overlay=Active;Active=null;Task cleanup=null;
   try{overlay?.SaveInterruptedProgress();}
   catch(Exception error){GD.PrintErr("[SpireTerrariaNext] interrupted progress: "+error);}
   finally{if(overlay!=null)cleanup=overlay.Close();CombatManager.Instance.Unpause();if(cleanup!=null)await cleanup;}
   GD.Print("[SpireTerrariaNext] battle detached: "+reason);
   if(SpireSelfTest.Enabled){SpireSelfTest.Report["externalBattleCleanup"]=reason;if(hostWon)SpireSelfTest.CombatCompleted.TrySetResult(true);}
  }
 }

 internal sealed class BattleOverlay {
  internal readonly CombatState Combat;
  internal AdvancedBattleState State {get;private set;}
  internal BattleConfiguration Configuration {get;private set;}
  readonly SceneTree tree;
  readonly RunState ownerRun;readonly NRun ownerScene;readonly AdventureRun ownerAdventure;
  readonly CancellationTokenSource lifetime=new CancellationTokenSource();Task closeTask;bool progressSaved;
  internal bool HasBattleOwner=>HostIntegration.OwnsCombat(Combat,ownerRun,ownerScene);
  bool SameRun=>ReferenceEquals(ownerRun,AdventureSession.HostRun)&&ReferenceEquals(ownerAdventure,AdventureSession.Current);
  readonly CanvasLayer layer=new CanvasLayer{Layer=AdventureBattleHud.BattleCanvasLayer,Name="SpireTerrariaBattle"};
  readonly Control root=new Control();readonly TextureRect view=new TextureRect();
  readonly Label status=new Label(),health=new Label(),encounter=new Label(),mana=new Label();
  readonly HBoxContainer slots=new HBoxContainer(),utility=new HBoxContainer();
  readonly Button[] slotButtons=new Button[6];readonly TextureRect[] slotIcons=new TextureRect[6];readonly Label[] slotCounts=new Label[6];readonly string[] slotSymbols=new string[6];
  readonly ProgressBar lifeBar=new ProgressBar{CustomMinimumSize=new Vector2(300,34),ShowPercentage=false,MouseFilter=Control.MouseFilterEnum.Ignore};
  AdventureBattleHud inventoryHud;int requestedConsumable=-1;bool nativeScreenOpen;
  string[] consumableInstanceIds=Array.Empty<string>();
  internal AdventureBattleHud InventoryHud=>inventoryHud;
  internal bool NativeScreenPaused=>nativeScreenOpen;
  internal bool Ready {get;private set;}
  internal bool NativeHudUncovered=>NRun.Instance?.GlobalUi.TopBar.IsVisibleInTree()==true&&root.GetGlobalRect().Position.Y>=AdventureBattleHud.TopBarBottom;
  internal bool HealthBarReadable=>lifeBar.GetGlobalRect().Size.X>=300&&lifeBar.GetGlobalRect().Size.Y>=30;
  internal string[] ConsumedInstanceIds=>State==null?Array.Empty<string>():State.ConsumedSlots.Where(i=>i>=0&&i<consumableInstanceIds.Length).Select(i=>consumableInstanceIds[i]).ToArray();
  sealed class PotionRequest {internal int Slot,Started;internal bool Sent;internal readonly TaskCompletionSource<bool> Completion=new TaskCompletionSource<bool>();}
  PotionRequest potionRequest;
  readonly Stopwatch clock=new Stopwatch(),heartbeat=Stopwatch.StartNew();
  readonly List<Tuple<Node,Node.ProcessModeEnum>> suspended=new List<Tuple<Node,Node.ProcessModeEnum>>();
  HostBridge bridge;FrameExchange frames;ImageTexture texture;bool busy,closed,paused,lastPause,finishing,selftestDamageCaptured;double accumulator;long shown;int workerId,selectedSlot,selectedEquipment,frameWidth=640,frameHeight=360;
  internal BattleOverlay(SceneTree tree,CombatState combat){this.tree=tree;Combat=combat;ownerRun=AdventureSession.HostRun;ownerScene=NRun.Instance;ownerAdventure=AdventureSession.Current;if(GodotObject.IsInstanceValid(ownerScene))ownerScene.TreeExiting+=OwnerExiting;}
  void OwnerExiting(){HostIntegration.OwnerLeaving(this);}
  internal void FinishProgress(bool won){if(progressSaved||Configuration==null||State==null||!SameRun)return;progressSaved=true;AdventureSession.FinishBattle(Configuration,State,won,ConsumedInstanceIds);}
  internal void SaveInterruptedProgress(){
   if(progressSaved||Configuration==null||State==null||!SameRun)return;progressSaved=true;
   // External console victory owns the host's reward/room transition. Preserve
   // only HP and used supplies; it is not a native Terraria boss kill.
   ownerAdventure.Life=Math.Clamp(State.Life,0,ownerAdventure.LifeMax);var consumed=new HashSet<string>(ConsumedInstanceIds,StringComparer.Ordinal);ownerAdventure.Consumables.RemoveAll(item=>consumed.Contains(item.InstanceId));AdventureSession.PushHostHealth();AdventureSession.Save();
  }
  internal async Task Start() {
   try {
    bool legacy=AdventureSession.IsLegacyTest;
    if(!legacy){Configuration=AdventureSession.BuildConfiguration();consumableInstanceIds=AdventureSession.Current.Consumables.Take(Configuration.Consumables.Length).Select(c=>c.InstanceId).ToArray();}
    NMapScreen.Instance?.Close(false);
    tree.Root.AddChild(layer);layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);root.MouseFilter=Control.MouseFilterEnum.Stop;
    var background=new ColorRect{Color=new Color(.055f,.065f,.08f,1),MouseFilter=Control.MouseFilterEnum.Ignore};root.AddChild(background);background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    view.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;view.StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered;view.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;view.MouseFilter=Control.MouseFilterEnum.Ignore;root.AddChild(view);view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    BuildBattleControls(legacy);LayoutBelowNativeHud();SuspendRun(tree.Root);
    if(NRun.Instance?.GlobalUi!=null){var ui=NRun.Instance.GlobalUi;suspended.Add(Tuple.Create((Node)ui,ui.ProcessMode));ui.ProcessMode=Node.ProcessModeEnum.Always;}
    string worker=Path.Combine(HostIntegration.ModDirectory,"worker","SpireNext.Worker.exe"),game=Path.GetFullPath(Path.Combine(HostIntegration.ModDirectory,"..","..","..","Terraria"));
    var opened=await HostBridge.Open(worker,game,lifetime.Token);if(closed){await Task.Run(()=>opened.Dispose());return;}bridge=opened;workerId=bridge.WorkerId;
    Packet start=await bridge.Call(legacy?Operation.BattleStart:Operation.AdventureStart,legacy?Protocol.Body(w=>{w.Write(20261021);w.Write("EnchantedSword");w.Write("BlueSlime");}):Protocol.Body(Configuration.Write));
    if(closed)return;
    using(var reader=Protocol.Reader(start)){string name=reader.ReadString();int width=reader.ReadInt32(),height=reader.ReadInt32();if((width!=640||height!=360)&&(width!=1280||height!=720))throw new InvalidDataException("战斗画面尺寸不匹配。");frameWidth=width;frameHeight=height;State=legacy?ReadLegacy(reader):AdvancedBattleState.Read(reader);Protocol.RequireEnd(reader);frames=FrameExchange.OpenReader(name);}
    if(!legacy){await AdventureHostUi.LoadBattleIcons(bridge,Configuration);if(closed)return;inventoryHud=new AdventureBattleHud(Configuration,index=>requestedConsumable=index);for(int i=0;i<slotSymbols.Length;i++)if(!string.IsNullOrEmpty(slotSymbols[i]))slotIcons[i].Texture=AdventureHostUi.ItemIcon(slotSymbols[i]);}
    UpdateFrame();ShowStatus("");LayoutBelowNativeHud();
    if(SpireSelfTest.Enabled){SpireSelfTest.Report["hostInputGateClosed"]=!HostIntegration.OriginalInputAllowed;SpireSelfTest.Report["hostRunNodesSuspended"]=suspended.Count;await SpireSelfTest.Capture("02-native-combat.png");}
    if(closed)return;clock.Restart();tree.ProcessFrame+=Tick;Ready=true;GD.Print("[SpireTerrariaNext] battle attached worker="+workerId+" host="+IntPtr.Size*8);
    if(SpireSelfTest.Enabled){SpireSelfTest.Report["nativeWorkerBits"]=32;SpireSelfTest.Report["hostBits"]=IntPtr.Size*8;SpireSelfTest.Report["workerId"]=workerId;SpireSelfTest.Report["battleAttached"]=true;if(!legacy)SpireSelfTest.Report["adventureEncounterSize"]=State.Remaining;}
   }catch{await Close();throw;}
  }
  void BuildBattleControls(bool legacy) {
   root.Theme=NGame.Instance.Theme;
   lifeBar.AddThemeStyleboxOverride("background",PanelStyle(new Color(.12f,.045f,.055f,.94f),new Color(.69f,.53f,.32f)));
   lifeBar.AddThemeStyleboxOverride("fill",PanelStyle(new Color(.73f,.12f,.16f),new Color(.94f,.42f,.32f)));
   root.AddChild(lifeBar);lifeBar.Position=new Vector2(20,18);lifeBar.Size=new Vector2(300,34);lifeBar.AddChild(health);health.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);health.HorizontalAlignment=HorizontalAlignment.Center;health.VerticalAlignment=VerticalAlignment.Center;DecorateText(health,24);
   root.AddChild(encounter);encounter.Position=new Vector2(20,59);DecorateText(encounter,18);encounter.MouseFilter=Control.MouseFilterEnum.Ignore;
   root.AddChild(mana);mana.Position=new Vector2(20,84);DecorateText(mana,17);mana.AddThemeColorOverride("font_color",new Color(.55f,.77f,1));mana.MouseFilter=Control.MouseFilterEnum.Ignore;
   root.AddChild(slots);slots.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);slots.OffsetLeft=-458;slots.OffsetRight=-108;slots.OffsetTop=16;slots.OffsetBottom=82;slots.AddThemeConstantOverride("separation",5);
   string[] thrown=legacy?Array.Empty<string>():Configuration.Consumables.Where(s=>!s.Contains("Potion")).Distinct().Take(3).ToArray();
   for(int i=0;i<6;i++) {
    int index=i;string symbol=i<3?(legacy?(i==0?"EnchantedSword":""):i<Configuration.Weapons.Length?Configuration.Weapons[i]:""):i-3<thrown.Length?thrown[i-3]:"";slotSymbols[i]=symbol;
    var button=new Button{CustomMinimumSize=new Vector2(54,62),ToggleMode=true,ButtonPressed=i==0,FocusMode=Control.FocusModeEnum.None,Disabled=string.IsNullOrEmpty(symbol)};
    button.AddThemeStyleboxOverride("normal",PanelStyle(new Color(.065f,.075f,.08f,.9f),new Color(.38f,.32f,.24f)));
    button.AddThemeStyleboxOverride("hover",PanelStyle(new Color(.14f,.15f,.15f,.96f),new Color(.76f,.61f,.37f)));
    button.AddThemeStyleboxOverride("pressed",PanelStyle(new Color(.19f,.16f,.105f,.96f),new Color(.98f,.78f,.4f)));
    button.AddThemeStyleboxOverride("disabled",PanelStyle(new Color(.055f,.06f,.065f,.64f),new Color(.23f,.22f,.20f)));
    button.Pressed+=()=>SelectEquipment(index);slots.AddChild(button);slotButtons[i]=button;
    var icon=new TextureRect{Position=new Vector2(10,8),Size=new Vector2(34,34),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};button.AddChild(icon);slotIcons[i]=icon;
    var key=new Label{Text=(i+1).ToString(),Position=new Vector2(5,40),MouseFilter=Control.MouseFilterEnum.Ignore};DecorateText(key,15);button.AddChild(key);
    var count=new Label{Position=new Vector2(20,40),Size=new Vector2(29,20),HorizontalAlignment=HorizontalAlignment.Right,MouseFilter=Control.MouseFilterEnum.Ignore};DecorateText(count,14);button.AddChild(count);slotCounts[i]=count;
    if(!string.IsNullOrEmpty(symbol)) {
     if(i<3&&!legacy)button.TooltipText=AdventureHostUi.GearName(AdventureSession.Current.Weapons[i])+"\n"+AdventureHostUi.GearDescription(AdventureSession.Current.Weapons[i]);
     else if(i<3)button.TooltipText="附魔剑 [1]";
     else button.TooltipText=GearCatalog.All.First(g=>g.Kind==GearKind.Consumable&&g.NativeSymbols.Contains(symbol)).Name+"\n选择后左键投掷，不占武器槽。";
    }
   }
   root.AddChild(utility);utility.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);utility.OffsetLeft=-98;utility.OffsetRight=-18;utility.OffsetTop=25;utility.OffsetBottom=64;
   var pause=new Button{Text="Ⅱ",TooltipText="暂停 / 继续 [P]",FocusMode=Control.FocusModeEnum.None};pause.Pressed+=TogglePause;utility.AddChild(pause);
   var cancel=new Button{Text="↩",TooltipText="退出泰拉战斗，恢复本房间卡牌战斗",FocusMode=Control.FocusModeEnum.None};cancel.Pressed+=()=>{if(!finishing){finishing=true;_=HostIntegration.Abort(this);}};utility.AddChild(cancel);
   root.AddChild(status);status.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);status.OffsetLeft=-320;status.OffsetRight=320;status.OffsetTop=-38;status.OffsetBottom=38;status.HorizontalAlignment=HorizontalAlignment.Center;status.VerticalAlignment=VerticalAlignment.Center;DecorateText(status,26);status.MouseFilter=Control.MouseFilterEnum.Ignore;ShowStatus("正在进入战场……");
  }
  static void DecorateText(Label label,int size){label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",new Color(.96f,.91f,.80f));label.AddThemeColorOverride("font_outline_color",new Color(.03f,.035f,.04f));label.AddThemeConstantOverride("outline_size",4);}
  static StyleBoxFlat PanelStyle(Color fill,Color edge)=>new StyleBoxFlat{BgColor=fill,BorderColor=edge,BorderWidthLeft=2,BorderWidthTop=2,BorderWidthRight=2,BorderWidthBottom=2,CornerRadiusTopLeft=4,CornerRadiusTopRight=4,CornerRadiusBottomLeft=4,CornerRadiusBottomRight=4};
  static AdvancedBattleState ReadLegacy(BinaryReader reader) {
   var s=new AdvancedBattleState{Tick=reader.ReadInt32(),Life=reader.ReadInt32(),LifeMax=reader.ReadInt32(),PlayerX=reader.ReadSingle(),PlayerY=reader.ReadSingle(),Dead=reader.ReadBoolean(),EnemyLife=reader.ReadInt32(),EnemyLifeMax=reader.ReadInt32(),EnemyX=reader.ReadSingle(),EnemyY=reader.ReadSingle(),EnemyActive=reader.ReadBoolean(),Projectiles=reader.ReadInt32(),Frame=reader.ReadInt64(),Title="史莱姆林地"};
   s.Alive=s.EnemyLife>0?1:0;s.Remaining=s.Alive;s.CameraX=Math.Max(0,Math.Min(512*16-640,s.PlayerX-320));s.CameraY=168*16-260;return s;
  }
  void LayoutBelowNativeHud(){if(NRun.Instance?.GlobalUi==null)return;float bottom=inventoryHud?.Bottom??AdventureBattleHud.TopBarBottom+8;root.OffsetTop=Math.Clamp(bottom,72,tree.Root.GetVisibleRect().Size.Y*.25f);}
  void SuspendRun(Node node){if(node.GetType().FullName=="MegaCrit.Sts2.Core.Nodes.NRun"){suspended.Add(Tuple.Create(node,node.ProcessMode));node.ProcessMode=Node.ProcessModeEnum.Disabled;return;}foreach(Node child in node.GetChildren())if(child!=layer)SuspendRun(child);}
  void SelectWeapon(int index){if(Configuration!=null&&index<Configuration.Weapons.Length)SelectEquipment(index);}
  void SelectEquipment(int index){if(index<0||index>=6||slotButtons[index]==null||slotButtons[index].Disabled)return;selectedEquipment=index;if(index<3)selectedSlot=index;for(int i=0;i<6;i++)slotButtons[i]?.SetPressedNoSignal(i==index);}
  void ShowStatus(string message){status.Text=message;status.Visible=message.Length>0;}
  void TogglePause(){paused=!paused;accumulator=0;clock.Restart();ShowStatus(paused?"已暂停 · P 继续":"");}
  int RemainingSlot(string symbol){if(Configuration==null||State==null)return -1;for(int i=0;i<Configuration.Consumables.Length;i++)if(Configuration.Consumables[i]==symbol&&!State.ConsumedSlots.Contains(i))return i;return -1;}
  int PotionSlot(bool magic){if(Configuration==null||State==null)return -1;for(int i=0;i<Configuration.Consumables.Length;i++){string symbol=Configuration.Consumables[i];if(symbol.Contains("Potion")&&symbol.Contains("Mana")==magic&&!State.ConsumedSlots.Contains(i)&&PotionUsable(i))return i;}return -1;}
  bool PotionUsable(int slot){if(State==null||Configuration==null||slot<0||slot>=Configuration.Consumables.Length||State.ConsumedSlots.Contains(slot)||State.ConsumingSlot>=0)return false;string symbol=Configuration.Consumables[slot];if(!symbol.Contains("Potion"))return false;return symbol.Contains("Mana")?State.Mana<State.MaxMana:State.PotionDelay<=0&&State.Life<State.LifeMax;}
  internal bool CanUsePotion(string instanceId){if(!Ready||closed||finishing||paused||nativeScreenOpen||potionRequest!=null)return false;int slot=Array.IndexOf(consumableInstanceIds,instanceId);return PotionUsable(slot);}
  internal Task<bool> UsePotion(string instanceId){if(!CanUsePotion(instanceId))return Task.FromResult(false);potionRequest=new PotionRequest{Slot=Array.IndexOf(consumableInstanceIds,instanceId),Started=State.Tick};return potionRequest.Completion.Task;}
  bool PointerOverHud(Vector2 pointer)=>slots.GetGlobalRect().HasPoint(pointer)||utility.GetGlobalRect().HasPoint(pointer)||lifeBar.GetGlobalRect().HasPoint(pointer);
  async void Tick() {
   if(SpireSelfTest.PVEnabled&&SpirePvCapture.WaitingForBattleFrame(this))return;
   if(busy||closed||finishing||State==null)return;busy=true;
   try {
    double elapsed=Math.Min(.2,clock.Elapsed.TotalSeconds);clock.Restart();bool key=Input.IsPhysicalKeyPressed(Key.P);if(key&&!lastPause)TogglePause();lastPause=key;
    LayoutBelowNativeHud();var globalUi=NRun.Instance?.GlobalUi;bool screen=MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole.IsConsoleVisible||NModalContainer.Instance?.OpenModal!=null||globalUi!=null&&(globalUi.MapScreen.IsOpen||globalUi.CapstoneContainer.InUse||globalUi.Overlays.ScreenCount>0);
    if(screen){nativeScreenOpen=true;root.Visible=false;accumulator=0;if(heartbeat.ElapsedMilliseconds>4000){await bridge.Call(Operation.Ping);heartbeat.Restart();}return;}
    if(nativeScreenOpen){nativeScreenOpen=false;root.Visible=true;paused=true;ShowStatus("已暂停 · P 继续");}
    if(!SpireSelfTest.Enabled&&!DisplayServer.WindowIsFocused()){paused=true;accumulator=0;ShowStatus("已暂停 · P 继续");}
    if(paused){accumulator=0;if(heartbeat.ElapsedMilliseconds>4000){await bridge.Call(Operation.Ping);heartbeat.Restart();}return;}
    accumulator+=elapsed;int due=(int)(accumulator*60);if(due<1&&!SpireSelfTest.PVEnabled)return;accumulator-=due/60d;int ticks=SpireSelfTest.PVEnabled?2:SpireSelfTest.AdventureEnabled?12:Math.Min(4,due);byte input=0;
    if(Input.IsPhysicalKeyPressed(Key.A)||Input.IsPhysicalKeyPressed(Key.Left))input|=1;if(Input.IsPhysicalKeyPressed(Key.D)||Input.IsPhysicalKeyPressed(Key.Right))input|=2;
    if(Input.IsPhysicalKeyPressed(Key.Space)||Input.IsPhysicalKeyPressed(Key.W)||Input.IsPhysicalKeyPressed(Key.Up))input|=4;
    if(Configuration!=null){for(int i=0;i<6;i++)if(Input.IsPhysicalKeyPressed((Key)((int)Key.Key1+i)))SelectEquipment(i);if(Input.IsPhysicalKeyPressed(Key.S)||Input.IsPhysicalKeyPressed(Key.Down))input|=64;if(Input.IsPhysicalKeyPressed(Key.Q))input|=16;}
    bool attack=Input.IsMouseButtonPressed(MouseButton.Left)&&view.GetGlobalRect().HasPoint(view.GetGlobalMousePosition())&&!PointerOverHud(view.GetGlobalMousePosition());
    if(attack&&selectedEquipment<3)input|=8;
    Vector2 local=view.GetLocalMousePosition(),size=view.Size;float scale=Math.Max(.01f,Math.Min(size.X/frameWidth,size.Y/frameHeight));Vector2 pixel=(local-(size-new Vector2(frameWidth,frameHeight)*scale)/2)/scale;
    float aimX=State.CameraX+Math.Clamp(pixel.X,0,frameWidth-1),aimY=State.CameraY+Math.Clamp(pixel.Y,0,frameHeight-1);
    int consumable=requestedConsumable;requestedConsumable=-1;
    if(Configuration!=null){if(attack&&selectedEquipment>=3&&State.ConsumingSlot<0)consumable=RemainingSlot(slotSymbols[selectedEquipment]);if(Input.IsPhysicalKeyPressed(Key.H))consumable=PotionSlot(false);else if(Input.IsPhysicalKeyPressed(Key.J))consumable=PotionSlot(true);}
    if(potionRequest!=null&&!potionRequest.Sent){consumable=potionRequest.Slot;potionRequest.Sent=true;potionRequest.Started=State.Tick;}
    if(SpireSelfTest.Enabled){aimX=State.EnemyX+16;aimY=State.EnemyY+12;input=(byte)(State.Tick%36<24?8:0);if(SpireSelfTest.AdventureEnabled){float distance=State.EnemyX-State.PlayerX;if(Math.Abs(distance)>38)input|=(byte)(distance>0?2:1);if(State.Tick%90<15||State.EnemyY<State.PlayerY-45)input|=4;}else{if(State.Tick<35)input|=2;if(State.Tick>=10&&State.Tick<20)input|=4;}}
    if(SpireSelfTest.PVEnabled)SpireSelfTest.PVControls(this,ref input,ref aimX,ref aimY,ref selectedSlot,ref consumable);
    Packet packet=await bridge.Call(Configuration==null?Operation.BattleStep:Operation.AdventureStep,Protocol.Body(w=>{w.Write(ticks);w.Write(input);w.Write(aimX);w.Write(aimY);if(Configuration!=null){w.Write(selectedSlot);w.Write(consumable);}}));heartbeat.Restart();
    if(closed)return;using(var reader=Protocol.Reader(packet)){State=Configuration==null?ReadLegacy(reader):AdvancedBattleState.Read(reader);Protocol.RequireEnd(reader);}UpdateFrame();
    if(potionRequest!=null){bool consumed=State.ConsumedSlots.Contains(potionRequest.Slot);if(consumed||State.Dead||State.Tick-potionRequest.Started>120){var done=potionRequest;potionRequest=null;done.Completion.TrySetResult(consumed);}}
    if(SpireSelfTest.Enabled&&!selftestDamageCaptured) {
     var diagnostics=new Dictionary<string,string>();Packet feedback=await bridge.Call(Operation.ArenaDiagnostics);
     using(var reader=Protocol.Reader(feedback)){int count=reader.ReadInt32();for(int i=0;i<count;i++)diagnostics.Add(reader.ReadString(),reader.ReadString());Protocol.RequireEnd(reader);}
     if(closed)return;
     if(int.Parse(diagnostics["combatText.visible"],System.Globalization.CultureInfo.InvariantCulture)>0){
      selftestDamageCaptured=true;SpireSelfTest.Report["nativeCombatTextDiagnostics"]=diagnostics;
      SpireSelfTest.Report["nativeCombatTextVisibleInGame"]=true;await SpireSelfTest.Capture("combat-damage.png");
     }
    }
    bool won=Configuration==null?State.EnemyLife<=0:State.Remaining==0&&State.Alive==0;
    if(SpireSelfTest.Enabled&&won&&!selftestDamageCaptured)throw new InvalidOperationException("No native damage glyph reached the visible battle viewport before victory.");
    if(State.Dead||won){finishing=true;ShowStatus(State.Dead?"远征结束":"胜利");won=won&&!State.Dead;if(SpireSelfTest.Enabled){SpireSelfTest.Report["nativeVictory"]=won;SpireSelfTest.Report["nativeTicks"]=State.Tick;SpireSelfTest.Report["enemyLife"]=State.EnemyLife;SpireSelfTest.Report["adventureRemaining"]=State.Remaining;SpireSelfTest.Report["adventureRecovered"]=State.Recovered;await SpireSelfTest.Capture("combat.png");}await HostIntegration.Complete(this,won);}
   }catch(Exception error){if(closed)return;ShowStatus("战斗连接中断，可用右上角返回。");paused=true;potionRequest?.Completion.TrySetResult(false);potionRequest=null;GD.PrintErr("[SpireTerrariaNext] step: "+error);if(SpireSelfTest.Enabled)SpireSelfTest.CombatCompleted.TrySetException(error);}finally{busy=false;}
  }
  void UpdateFrame() {
   SharedFrame frame;var result=frames.ReadLatest(shown,out frame);if(result==FrameReadStatus.Unchanged)return;if(result!=FrameReadStatus.Available||frame.FrameId!=State.Frame)throw new InvalidDataException("原生战斗画面与状态不匹配。");
   byte[] rgba=frame.Pixels;for(int i=0;i<rgba.Length;i+=4){byte b=rgba[i];rgba[i]=rgba[i+2];rgba[i+2]=b;}
   using(var image=Image.CreateFromData(frame.Width,frame.Height,false,Image.Format.Rgba8,rgba)){if(texture==null){texture=ImageTexture.CreateFromImage(image);view.Texture=texture;}else texture.Update(image);}shown=frame.FrameId;
   health.Text=Math.Max(0,State.Life)+" / "+State.LifeMax;lifeBar.MaxValue=State.LifeMax;lifeBar.Value=Math.Max(0,State.Life);encounter.Text=State.Title+" · 第 "+Math.Max(1,State.Wave)+" / "+Math.Max(1,State.Waves)+" 波 · 剩余 "+State.Remaining;mana.Text=Configuration==null?"":"魔力 "+State.Mana+" / "+State.MaxMana;
   if(Configuration!=null){AdventureInventoryModels.SyncConsumed(ConsumedInstanceIds);var player=AdventureSession.HostRun?.Players.FirstOrDefault()?.Creature;if(player!=null&&player.CurrentHp!=State.Life){AdventureSession.Current.Life=Math.Clamp(State.Life,0,AdventureSession.Current.LifeMax);AdventureSession.PushHostHealth();}inventoryHud?.Update(State);
    for(int i=0;i<6;i++){if(i<Configuration.Weapons.Length)slotCounts[i].Text=Configuration.WeaponLevels[i]>0?"+"+Configuration.WeaponLevels[i]:"";else if(i>=3&&!string.IsNullOrEmpty(slotSymbols[i])){int count=Configuration.Consumables.Where((s,n)=>s==slotSymbols[i]&&!State.ConsumedSlots.Contains(n)).Count();slotCounts[i].Text="×"+count;slotButtons[i].Disabled=count==0;slotIcons[i].Modulate=count>0?Colors.White:new Color(.4f,.4f,.4f);}}
   }
  }
  internal Task Close() {
   if(closeTask!=null)return closeTask;if(closed)return Task.CompletedTask;
   closed=true;Ready=false;finishing=true;tree.ProcessFrame-=Tick;lifetime.Cancel();potionRequest?.Completion.TrySetResult(false);potionRequest=null;bool restored=true;
   // Release the Godot overlay synchronously. Worker shutdown may be slow or
   // broken, and must never keep a window/input gate alive after room exit.
   try{if(GodotObject.IsInstanceValid(ownerScene))ownerScene.TreeExiting-=OwnerExiting;if(GodotObject.IsInstanceValid(layer)){layer.Visible=false;if(!layer.IsQueuedForDeletion())layer.QueueFree();}}catch(Exception error){GD.PrintErr("[SpireTerrariaNext] layer cleanup: "+error.Message);}
   try{inventoryHud?.Dispose();}catch(Exception error){GD.PrintErr("[SpireTerrariaNext] HUD cleanup: "+error.Message);}finally{inventoryHud=null;}
   foreach(var item in suspended)if(GodotObject.IsInstanceValid(item.Item1))try{item.Item1.ProcessMode=item.Item2;restored&=item.Item1.ProcessMode==item.Item2;}catch(Exception error){restored=false;GD.PrintErr("[SpireTerrariaNext] process restore: "+error.Message);}suspended.Clear();if(SpireSelfTest.Enabled)SpireSelfTest.Report["hostRunProcessingRestored"]=restored;
   try{frames?.Dispose();}catch(Exception error){GD.PrintErr("[SpireTerrariaNext] frame cleanup: "+error.Message);}finally{frames=null;}
   try{if(texture!=null){if(GodotObject.IsInstanceValid(view))view.Texture=null;texture.Dispose();}}catch(Exception error){GD.PrintErr("[SpireTerrariaNext] texture cleanup: "+error.Message);}finally{texture=null;}
   var released=bridge;bridge=null;closeTask=ReleaseWorker(released);return closeTask;
  }
  async Task ReleaseWorker(HostBridge released){
   if(released!=null){try{await released.Call(Operation.Shutdown,timeoutMilliseconds:600);}catch(Exception error){GD.Print("[SpireTerrariaNext] worker shutdown: "+error.Message);}finally{try{await Task.Run(()=>released.Dispose());}catch(Exception error){GD.PrintErr("[SpireTerrariaNext] worker reclaim: "+error.Message);}}}
   bool alive=false;if(workerId>0)try{using(var process=Process.GetProcessById(workerId))alive=!process.HasExited;}catch(ArgumentException){}if(SpireSelfTest.Enabled)SpireSelfTest.Report["workerReclaimed"]=!alive;
  }
 }
}

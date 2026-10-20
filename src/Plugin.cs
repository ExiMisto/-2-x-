using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Validation;
using TerrariaICEY.Bridge;

namespace SpireTerraria {
 [ModInitializer("Initialize")]
 public static partial class Plugin {
  internal static readonly string Root=Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
  static readonly string TerrariaPath=Path.GetFullPath(Path.Combine(Root,"..","..","..","Terraria"));
  static readonly object LogLock=new object();
  static readonly ConcurrentQueue<BattleSetup> Rooms=new ConcurrentQueue<BattleSetup>();
  sealed class PotionRequest {public int Kind;public TaskCompletionSource<bool> Result=new TaskCompletionSource<bool>();}
  static readonly ConcurrentQueue<PotionRequest> Drinks=new ConcurrentQueue<PotionRequest>();
  static SceneTree tree;static Control root,arena,world;static CanvasLayer layer;
  static Label status,objective;static TextureRect artwork;static ImageTexture texture;
  static readonly List<TextureRect> slotIcons=new List<TextureRect>();static readonly List<Label> slotLabels=new List<Label>();static readonly int[] iconIds=new int[6];
  static readonly List<ColorRect> healthBacks=new List<ColorRect>(),healthFills=new List<ColorRect>();
  static BattleSetup setup;static EngineInput latestInput;static NativePacket latest;static FramePacket latestFrame;
  static CancellationTokenSource cancellation;static Task worker;static string workerError="";
  public static bool Demo {get;private set;}
  public static bool Realtime {get;private set;}
  static bool starting,stopping,won,lost,completing,pHeld,paused,potionBusy;static volatile bool clearNative;
  static float clock,elapsed,healingAt=-100;static int selected,session,appliedFrames,totalDamage,roomsWon,keysBeforeDemo,baseDefense=25,configuredDefense;
  static Creature player;static CombatRoom boundRoom;static ProgressState originalProgress;static Vector2 renderScale;
  sealed class NativePacket {public EngineOutput Output;public BattleState Battle;public RenderRequest Camera;}
  sealed class FramePacket {public int RoundId;public RenderFrame Frame;}
  public static void Initialize(){
   try{
    var h=new Harmony("local.spireterraria.character");
    foreach(string name in new[]{"SaveProgressFile","SavePrefsFile","DeleteCurrentRun","DeleteCurrentMultiplayerRun","UpdateProgressAfterCombatWon","UpdateProgressWithRunData"})foreach(var m in typeof(SaveManager).GetMethods().Where(m=>m.Name==name))h.Patch(m,prefix:new HarmonyMethod(typeof(Plugin),nameof(AllowPersistentWrite)));
    h.Patch(AccessTools.Method(typeof(SaveManager),"SaveRun"),prefix:new HarmonyMethod(typeof(Plugin),nameof(SkipDemoSave)));
    h.Patch(AccessTools.Method(typeof(CombatManager),"SetReadyToEndTurn"),prefix:new HarmonyMethod(typeof(Plugin),nameof(AllowTurn)));
    Log("KEYBOARD ISOLATION handlers="+KeyboardIsolation.Install(h));
    var achievements=typeof(NGame).Assembly.GetType("MegaCrit.Sts2.Core.Achievements.AchievementsHelper");
    if(achievements!=null)foreach(var m in achievements.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Where(m=>m.ReturnType==typeof(void)&&!m.IsGenericMethod))h.Patch(m,prefix:new HarmonyMethod(typeof(Plugin),nameof(AllowPersistentWrite)));
    BaseLib.Utils.CustomCharacterUtils.TryOrderCustomCharacters<TerraWarrior,TerraRanger,TerraMage,TerraSummoner>();TerraIntegration.Install(h);tree=(SceneTree)Engine.GetMainLoop();tree.ProcessFrame+=Frame;
    AppDomain.CurrentDomain.ProcessExit+=(s,e)=>cancellation?.Cancel();CombatManager.Instance.CombatWon+=OnWon;
    Log("INITIALIZED v0.4.0; fixed equipment/loadout; Terraria="+TerrariaPath);
   }catch(Exception e){Log("INIT FAILED "+e);}
  }
  public static bool AllowPersistentWrite()=>!Demo;
  public static bool AllowTurn()=>!Realtime;
  public static bool SkipDemoSave(ref Task __result){if(!Demo)return true;__result=Task.CompletedTask;return false;}
  internal static void Log(string value){lock(LogLock)File.AppendAllText(Path.Combine(Root,"prototype.log"),DateTime.Now.ToString("O")+" "+value+System.Environment.NewLine);GD.Print("[SpireTerraria] "+value);}
  internal static void Begin(Terrarian c){
   if(Demo)return;Demo=true;starting=true;session++;keysBeforeDemo=KeyboardIsolation.RemainingKeyBindings();originalProgress=SaveManager.Instance.Progress;
   var json=JsonSerializer.Serialize(originalProgress.ToSerializable());SaveManager.Instance.Progress=ProgressState.FromSerializable(JsonSerializer.Deserialize<SerializableProgress>(json),new DeserializationContext());SaveManager.Instance.Progress.EnableFtues=false;
   setup=CampaignRules.Starter(c.Hero,BattleKind.Normal,1);setup.RoundId=0;roomsWon=totalDamage=appliedFrames=0;elapsed=0;TerraEconomy.Reset();boundRoom=null;player=null;workerError="";clearNative=false;
   while(Rooms.TryDequeue(out _)){}Volatile.Write(ref latest,null);Interlocked.Exchange(ref latestFrame,null);
   cancellation=new CancellationTokenSource();int id=session;worker=Task.Run(()=>Worker(cancellation.Token,id));Log("CHARACTER RUN START class="+c.Hero+" shouldSave=false");
  }
  static ColorRect Box(Control p,float x,float y,float w,float h,Color c){var n=new ColorRect{Position=new Vector2(x,y),Size=new Vector2(w,h),Color=c,MouseFilter=Control.MouseFilterEnum.Ignore};p.AddChild(n);return n;}
  static Label Text(Control p,string v,float x,float y,int size,Color c){var n=new Label{Text=v,Position=new Vector2(x,y),MouseFilter=Control.MouseFilterEnum.Ignore};n.AddThemeFontSizeOverride("font_size",size);n.AddThemeColorOverride("font_color",c);p.AddChild(n);return n;}
  static void CreateUi(){
   layer=new CanvasLayer{Layer=10,Name="SpireTerrariaBattle"};tree.Root.AddChild(layer);root=new Control{Size=new Vector2(960,540),MouseFilter=Control.MouseFilterEnum.Ignore};layer.AddChild(root);
   arena=new Control{Size=new Vector2(960,540),MouseFilter=Control.MouseFilterEnum.Ignore,Visible=false};root.AddChild(arena);
   world=new Control{Position=new Vector2(0,92),Size=new Vector2(960,448),MouseFilter=Control.MouseFilterEnum.Ignore,ClipContents=true};arena.AddChild(world);Box(world,0,0,960,448,new Color("18243a"));
   artwork=new TextureRect{MouseFilter=Control.MouseFilterEnum.Ignore,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,Material=new CanvasItemMaterial{BlendMode=CanvasItemMaterial.BlendModeEnum.PremultAlpha}};world.AddChild(artwork);
   for(int n=0;n<32;n++){healthBacks.Add(Box(world,0,0,48,3,new Color(.03f,.02f,.02f,.9f)));healthFills.Add(Box(world,0,0,48,3,new Color("bd5c48")));}
   status=Text(arena,"",24,122,13,new Color("a7cdec"));objective=Text(arena,"",24,100,15,new Color("e4e2d4"));
   for(int n=0;n<6;n++){float x=666+n*47;Box(arena,x,100,42,42,new Color(.03f,.04f,.045f,.65f));var icon=new TextureRect{Position=new Vector2(x+8,103),Size=new Vector2(26,26),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};arena.AddChild(icon);slotIcons.Add(icon);slotLabels.Add(Text(arena,"",x+3,130,10,new Color("d9d4c3")));}
  }
  static void Worker(CancellationToken token,int id){
   try{using(var bridge=new NativeBridge(Root,TerrariaPath,JourneyTest.Enabled)){
    BattleSetup current=null;BattleState previous=null;int ticks=0;double next=0;var timer=System.Diagnostics.Stopwatch.StartNew();Log("NATIVE CONNECTED "+bridge.Version);
    while(!token.IsCancellationRequested&&session==id){
     if(timer.Elapsed.TotalMilliseconds<next){Thread.Sleep(1);continue;}EngineInput input;
     if(clearNative){clearNative=false;bridge.ClearRoom();}
     if(Rooms.TryDequeue(out var r)){current=r;bridge.Configure(current);input=Arena.CampaignInput(current,true);input.Paused=true;previous=null;Log("NATIVE ROOM round="+r.RoundId+" kind="+r.Kind+" stage="+r.Stage+" bounds="+r.Width+"x"+r.Height);}
     else if(current==null){Thread.Sleep(5);continue;}
     else{input=Volatile.Read(ref latestInput)??Arena.CampaignInput(current,false);if(previous!=null&&(previous.Won||previous.Lost))input.Paused=true;}
     while(Drinks.TryDequeue(out var drink)){try{drink.Result.SetResult(bridge.UsePotion(drink.Kind));}catch(Exception e){drink.Result.SetException(e);}}
     if(JourneyTest.Strike){JourneyTest.Strike=false;JourneyTest.FixtureStrikes++;bridge.TestStrikeEnemies();}
     var output=bridge.Step(input);var battle=bridge.Battle();previous=battle;var camera=Arena.CampaignRender(current,output);Volatile.Write(ref latest,new NativePacket{Output=output,Battle=battle,Camera=camera});
     if(ticks++%2==0)Interlocked.Exchange(ref latestFrame,new FramePacket{RoundId=current.RoundId,Frame=bridge.Render(camera)});
     next+=1000.0/60;if(timer.Elapsed.TotalMilliseconds-next>100)next=timer.Elapsed.TotalMilliseconds;
    }
    File.WriteAllText(Path.Combine(Root,"native-worker.log"),bridge.Log);
   }}catch(Exception e){if(!token.IsCancellationRequested){workerError=e.Message;Log("NATIVE FAILED "+e);}}finally{while(Drinks.TryDequeue(out var d))d.Result.TrySetCanceled();}
  }
  static void SetRealtime(bool v){if(v==Realtime)return;Realtime=v;if(v)KeyboardIsolation.Enter();else Log("SPIRE INPUT RESTORED="+KeyboardIsolation.Restore()+" bindings="+KeyboardIsolation.RemainingKeyBindings());}
  static Player CurrentPlayer=>RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault();
  static void BeginRoom(CombatRoom room){
   SyncEquipment();var owner=CurrentPlayer;player=owner.Creature;boundRoom=room;won=lost=completing=paused=pHeld=false;clock=0;selected=0;healingAt=-100;
   setup.Kind=room.RoomType==RoomType.Boss?BattleKind.Boss:room.RoomType==RoomType.Elite?BattleKind.Elite:BattleKind.Normal;
   setup.Stage=Math.Clamp(owner.RunState.CurrentActIndex+1,1,3);setup.RoundId++;setup.NewRun=setup.RoundId==1;setup.CurrentLife=(int)player.CurrentHp;setup.Seed=unchecked((int)owner.RunState.Rng.Seed+setup.RoundId*7919);setup.Accessories=owner.Relics.OfType<TerraAccessory>().Select(r=>r.ItemId).Where(id=>CampaignRules.IsAccessoryAllowed(id,setup.Class)).ToArray();
   configuredDefense=setup.DefenseBonus;Volatile.Write(ref latestInput,Arena.CampaignInput(setup,false));Interlocked.Exchange(ref latestFrame,null);artwork.Texture=null;texture=null;SetRealtime(true);Rooms.Enqueue(setup.Copy());Log("MAP ROOM BOUND floor="+owner.RunState.TotalFloor+" round="+setup.RoundId+" type="+room.RoomType);
  }
  static void SyncEquipment(){
   var owner=CurrentPlayer;if(owner==null)return;
   int stage=Math.Clamp(owner.RunState.CurrentActIndex+1,1,3),life=CampaignRules.BaseLife(setup.Class)*stage;
   if(setup.MaxLife!=life){int before=setup.MaxLife;setup.MaxLife=life;owner.Creature.SetMaxHpInternal(life);owner.Creature.SetCurrentHpInternal(Math.Clamp(owner.Creature.CurrentHp+Math.Max(0,life-before),1,life));Log("STAGE LIFE "+before+" -> "+life);}
   var cards=owner.Deck.Cards.OfType<TerraEquipment>().ToArray();var weapons=cards.Where(c=>c.Stat==0).Take(3).ToArray();var armor=cards.LastOrDefault(c=>c.Stat==2);
   setup.WeaponIds=weapons.Select(c=>c.ItemId).ToArray();setup.WeaponLevels=weapons.Select(c=>c.CurrentUpgradeLevel).ToArray();setup.ArmorIds=armor==null?Array.Empty<int>():(int[])armor.Definition.Items.Clone();setup.ArmorLevel=armor?.CurrentUpgradeLevel??0;setup.Throwables=(int[])TerraEconomy.Stock.Clone();setup.DefenseBonus=0;setup.DamageBonus=.5f;
  }
  static void Frame(){
   try{
    if(NGame.Instance==null)return;if(root==null)CreateUi();JourneyTest.Tick();ShowcaseCapture.Tick();
    if(File.Exists(Path.Combine(Root,"diagnostics.request"))){File.Delete(Path.Combine(Root,"diagnostics.request"));Capture("validation-current.png");File.WriteAllText(Path.Combine(Root,"diagnostics.json"),JsonSerializer.Serialize(new{Demo,Realtime,setup,battle=latest?.Battle,output=latest?.Output,workerError,focused=NGame.IsGameFocusedWindow()},new JsonSerializerOptions{IncludeFields=true}));}
    if(File.Exists(Path.Combine(Root,"exit.request"))){File.Delete(Path.Combine(Root,"exit.request"));_ = Exit();}
    var size=NGame.Instance.GetViewportRect().Size;float scale=Math.Min(size.X/960,size.Y/540);renderScale=Vector2.One*scale;root.Scale=renderScale;root.Position=(size-new Vector2(960,540)*scale)/2;
    if(!Demo){arena.Visible=false;return;}if(stopping||starting)return;if(NGame.Instance.MainMenu!=null){_ = Cleanup();return;}
    var room=RunManager.Instance.DebugOnlyGetState()?.CurrentRoom as CombatRoom;
    if(room!=null&&NCombatRoom.Instance!=null&&CombatManager.Instance.IsInProgress&&!ReferenceEquals(boundRoom,room))BeginRoom(room);
    bool live=room!=null&&ReferenceEquals(room,boundRoom)&&!won&&!lost;if(!live){SetRealtime(false);arena.Visible=false;SyncEquipment();return;}
    bool nativeUi=(NMapScreen.Instance?.IsOpen??false)||(NOverlayStack.Instance?.ScreenCount??0)>0||(NCapstoneContainer.Instance?.InUse??false)||NModalContainer.Instance?.OpenModal!=null;
    arena.Visible=!nativeUi;SetRealtime(!nativeUi);
    var visual=NCombatRoom.Instance;if(visual!=null){visual.Ui.Visible=false;foreach(var c in boundRoom.CombatState.Creatures){var cn=visual.GetCreatureNode(c);if(cn!=null)cn.Visible=false;}}
    float dt=Math.Min(.05f,(float)NGame.Instance.GetProcessDeltaTime());elapsed+=dt;bool pause=Input.IsPhysicalKeyPressed(Key.P);if(pause&&!pHeld)paused=!paused;pHeld=pause;
    var packet=Volatile.Read(ref latest);bool ready=packet!=null&&packet.Battle.RoundId==setup.RoundId;bool blocked=NModalContainer.Instance?.OpenModal!=null||(!NGame.IsGameFocusedWindow()&&!ShowcaseCapture.Enabled);
    var input=Arena.CampaignInput(setup,false);input.Paused=paused||blocked||nativeUi||!ready||starting||completing;if(!input.Paused)clock+=dt;
    input.Left=Input.IsPhysicalKeyPressed(Key.A)||Input.IsPhysicalKeyPressed(Key.Left);input.Right=Input.IsPhysicalKeyPressed(Key.D)||Input.IsPhysicalKeyPressed(Key.Right);input.Jump=Input.IsPhysicalKeyPressed(Key.Space)||Input.IsPhysicalKeyPressed(Key.W);input.Down=Input.IsPhysicalKeyPressed(Key.S);
    if(Input.IsPhysicalKeyPressed(Key.Key1))selected=0;if(Input.IsPhysicalKeyPressed(Key.Key2))selected=1;if(Input.IsPhysicalKeyPressed(Key.Key3))selected=2;if(Input.IsPhysicalKeyPressed(Key.Key4))selected=3;if(Input.IsPhysicalKeyPressed(Key.Key5))selected=4;if(Input.IsPhysicalKeyPressed(Key.Key6))selected=5;input.Selected=selected;
    var mouse=(root.GetGlobalMousePosition()-root.GlobalPosition)/renderScale;input.MouseX=(ready?packet.Camera.X:setup.Left)+mouse.X;input.MouseY=(ready?packet.Camera.Y:setup.Top)+mouse.Y;input.Use=Input.IsMouseButtonPressed(MouseButton.Left)&&mouse.Y>142&&mouse.Y<540;input.Alt=Input.IsMouseButtonPressed(MouseButton.Right);
    if(ready){var output=packet.Output;var battle=packet.Battle;baseDefense=Math.Max(1,output.Defense-configuredDefense);player.SetMaxHpInternal(output.MaxLife);player.SetCurrentHpInternal(Math.Max(1,output.Life));
     if(!input.Paused&&!potionBusy){if(Input.IsPhysicalKeyPressed(Key.H))_ = UseOwnedPotion(0);else if(Input.IsPhysicalKeyPressed(Key.J))_ = UseOwnedPotion(1);}
     JourneyTest.Control(input,battle,output,clock);
     ShowcaseCapture.Control(input,battle,output,clock);
     if(ShowcaseCapture.Enabled)selected=input.Selected;
     if(battle.Lost&&!lost){lost=true;SetRealtime(false);Log("NATIVE DEFEAT round="+setup.RoundId);_ = CreatureCmd.Kill(player,true);}
     if(battle.Won&&!won&&!completing){completing=true;_ = CompleteCombat(session,setup.RoundId);}
     for(int n=0;n<3;n++)TerraEconomy.Stock[n]=output.InventoryIds[n+3]==new[]{168,166,167}[n]?output.InventoryStacks[n+3]:0;
     UpdateHealthBars(battle,packet.Camera);status.Text=(paused?"暂停 · ":"")+"魔力 "+output.Mana+" / "+output.MaxMana;
     objective.Text=setup.Kind==BattleKind.Normal?"":CampaignRules.BattleName(setup.Kind);UpdateSlots(output);
    }else status.Text="正在准备泰拉瑞亚战场…";
    if(workerError.Length>0)status.Text="引擎错误："+workerError;Volatile.Write(ref latestInput,input);ApplyFrame();
   }catch(Exception e){if(workerError!=e.Message){workerError=e.Message;Log("FRAME FAILED "+e);}}
  }
  static void UpdateSlots(EngineOutput o){for(int n=0;n<6;n++){int id=o.InventoryIds[n];if(id!=iconIds[n]){iconIds[n]=id;slotIcons[n].Texture=id>0?TerraArt.Item(id):null;}slotIcons[n].Modulate=n==selected?Colors.White:new Color(.65f,.65f,.65f);slotLabels[n].Text=(n+1)+(n>=3?" ×"+o.InventoryStacks[n]:n<setup.WeaponLevels.Length&&setup.WeaponLevels[n]>0?" +"+setup.WeaponLevels[n]:"");}}
  static void UpdateHealthBars(BattleState b,RenderRequest camera){foreach(var x in healthBacks)x.Visible=false;foreach(var x in healthFills)x.Visible=false;int at=0;foreach(var e in b.Enemies){if(at>=healthBacks.Count)break;float x=e.X+e.Width*.5f-camera.X-24,y=e.Y-camera.Y-102;if(x< -48||x>960||y<0||y>380)continue;var back=healthBacks[at];var fill=healthFills[at++];back.Visible=fill.Visible=true;back.Position=fill.Position=new Vector2(x,y);fill.Size=new Vector2(48*Math.Clamp(e.Life/(float)Math.Max(1,e.MaxLife),0,1),3);}}
  static void ApplyFrame(){var p=Interlocked.Exchange(ref latestFrame,null);if(p==null||p.RoundId!=setup.RoundId)return;var f=p.Frame;if(f.EffectWidth<=0||f.EffectHeight<=0)return;using(var img=Image.CreateFromData(f.EffectWidth,f.EffectHeight,false,Image.Format.Rgba8,f.Effects)){if(texture==null||texture.GetWidth()!=f.EffectWidth||texture.GetHeight()!=f.EffectHeight){texture=ImageTexture.CreateFromImage(img);artwork.Texture=texture;}else texture.Update(img);}artwork.Position=new Vector2(f.EffectX,f.EffectY-92);artwork.Size=new Vector2(f.EffectWidth,f.EffectHeight);appliedFrames++;}
  static async Task CompleteCombat(int id,int round){try{if(!Demo||session!=id||setup.RoundId!=round||latest?.Battle.RoundId!=round||latest.Battle.Lost||!latest.Battle.Won)return;Log("NATIVE VICTORY round="+round+" kills="+latest.Battle.Kills+" damage="+latest.Battle.Damage);foreach(var e in boundRoom.Enemies.ToArray())if(e.IsAlive)await CreatureCmd.Kill(e,true);await CombatManager.Instance.CheckWinCondition();}catch(Exception e){Log("SETTLEMENT FAILED "+e);workerError=e.Message;}finally{completing=false;}}
  static void OnWon(CombatRoom room){if(!Demo)return;won=true;roomsWon++;totalDamage+=latest?.Battle.Damage??0;clearNative=true;SetRealtime(false);arena.Visible=false;Log("MAP COMBAT WON rooms="+roomsWon+" damage="+totalDamage+" frames="+appliedFrames);}
  public static bool PotionReady(int kind)=>!potionBusy&&latest!=null&&(kind==0?clock-healingAt>=60&&latest.Output.Life<latest.Output.MaxLife:latest.Output.Mana<latest.Output.MaxMana);
  public static async Task Drink(int kind){var r=new PotionRequest{Kind=kind};Drinks.Enqueue(r);bool used=await r.Result.Task;if(used&&kind==0)healingAt=clock;Log("POTION kind="+kind+" applied="+used);}
  static async Task UseOwnedPotion(int kind){var p=CurrentPlayer?.Potions.OfType<TerraPotion>().FirstOrDefault(x=>x.Kind==kind);if(p==null||!PotionReady(kind))return;potionBusy=true;try{await p.OnUseWrapper(new ThrowingPlayerChoiceContext(),player);}catch(Exception e){Log("POTION FAILED "+e);}finally{potionBusy=false;}}
  internal static IEnumerable<Node> Descendants(Node p){foreach(Node c in p.GetChildren()){yield return c;foreach(var n in Descendants(c))yield return n;}}
  internal static void Capture(string f){using(var img=tree.Root.GetTexture().GetImage())Log("CAPTURE "+f+" result="+img.SavePng(Path.Combine(Root,f)));}
  internal static async Task NextFrames(int n){for(int i=0;i<n;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
  internal static async Task Exit(){if(stopping||!Demo)return;stopping=true;try{await NGame.Instance.ReturnToMainMenu();}catch(Exception e){Log("EXIT FAILED "+e);}await Cleanup();}
  internal static async Task<RunState> FinishStart(Task<RunState> task){try{return await task;}finally{starting=false;}}
  internal static object JourneySnapshot()=>new{roomsWon,totalDamage,appliedFrames,damageMultiplier=1+setup.DamageBonus,setup.DefenseBonus,setup.MaxLife,baseDefense,native=latest?.Output.HeldDamage};
  internal static int NativeTick=>latest?.Battle.Tick??0;
  static async Task Cleanup(){stopping=true;session++;cancellation?.Cancel();if(worker!=null)await worker;if(originalProgress!=null)SaveManager.Instance.Progress=originalProgress;originalProgress=null;SetRealtime(false);Demo=false;starting=stopping=completing=false;player=null;boundRoom=null;cancellation?.Dispose();cancellation=null;Log("RUN EXIT progress restored; keyboard="+(KeyboardIsolation.RemainingKeyBindings()==keysBeforeDemo));}
 }
}

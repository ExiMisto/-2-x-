using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using TerrariaICEY.Bridge;

namespace SpireTerraria {
 // Opt-in production recorder. Captures the actual Godot viewport after drawing;
 // combat uses normal native input, owned equipment and normal victory handling.
 internal static class ShowcaseCapture {
  sealed class Options {
   public string Ffmpeg {get;set;}="C:/Users/ExiMisto/AppData/Local/oopz/ffmpeg.exe";
   public string Output {get;set;}="";
   public string Hero {get;set;}="Summoner";
   public bool PreviewCharacters {get;set;}=true;
   public bool Economy {get;set;}=true;
   public bool Boss {get;set;}=true;
   public int BossSeconds {get;set;}=24;
   public int CombatLimitSeconds {get;set;}=150;
   public bool ShowcaseOnly {get;set;}=false;
   public int Stage {get;set;}=1;
   public string Battle {get;set;}="Normal";
   public int Seconds {get;set;}=20;
   public string Explosive {get;set;}="None";
   public string[] EquipmentKeys {get;set;}
  }
  internal static bool Enabled {get;private set;}
  static bool running,recording,abort,potionBusy,runShouldSave;
  static int idleFrames,traceRound=-1,traceTick=-60;
  static int explosiveTick=-1,explosiveStack;static bool explosiveConsumed,retreatLeft;
  static long captured,written,dropped;
  static double nextSample;
  static Options options;static HeroClass hero;
  static string folder,failure,encoderFailure;
  static Process encoder;static Task writer;static Task<string> encoderLog;
  static BlockingCollection<byte[]> queue;
  static Stopwatch timer;
  static readonly List<object> markers=new List<object>(),trace=new List<object>();
  static readonly JsonSerializerOptions jsonOptions=new JsonSerializerOptions{WriteIndented=true,PropertyNameCaseInsensitive=true};

  internal static void Tick(){
   if(running){string stop=Path.Combine(Plugin.Root,"showcase.stop");if(File.Exists(stop)){File.Delete(stop);abort=true;}return;}
   if(NGame.Instance.MainMenu==null||++idleFrames<90)return;
   string request=Path.Combine(Plugin.Root,"showcase.request");if(!File.Exists(request))return;
   try{
    string json=File.ReadAllText(request);File.Delete(request);
    options=string.IsNullOrWhiteSpace(json)?new Options():JsonSerializer.Deserialize<Options>(json,jsonOptions)??new Options();
    if(!Enum.TryParse(options.Hero,true,out hero))throw new Exception("Unknown showcase hero "+options.Hero);
    if(!File.Exists(options.Ffmpeg))throw new FileNotFoundException("Showcase requires ffmpeg.exe",options.Ffmpeg);
    folder=string.IsNullOrWhiteSpace(options.Output)?Path.Combine(Plugin.Root,"showcase","capture",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+hero):Path.GetFullPath(options.Output);
    Directory.CreateDirectory(folder);running=Enabled=true;abort=false;failure=encoderFailure=null;potionBusy=false;
    captured=written=dropped=0;traceRound=-1;traceTick=-60;nextSample=0;explosiveTick=-1;explosiveConsumed=false;markers.Clear();trace.Clear();
    StartRecorder();_ = Run();
   }catch(Exception e){running=Enabled=false;Plugin.Log("SHOWCASE START FAILED "+e);File.WriteAllText(Path.Combine(Plugin.Root,"showcase-error.txt"),e.ToString());}
  }
  static void StartRecorder(){
   var start=new ProcessStartInfo{FileName=options.Ffmpeg,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
   foreach(string arg in new[]{"-hide_banner","-loglevel","warning","-y","-f","rawvideo","-pixel_format","rgba","-video_size","1920x1080","-framerate","30","-i","pipe:0","-an","-c:v","libx264","-preset","ultrafast","-crf","16","-pix_fmt","yuv420p","-movflags","+faststart",Path.Combine(folder,"gameplay.mp4")})start.ArgumentList.Add(arg);
   encoder=Process.Start(start);encoderLog=encoder.StandardError.ReadToEndAsync();queue=new BlockingCollection<byte[]>(5);
   writer=Task.Run(()=>{try{foreach(byte[] bytes in queue.GetConsumingEnumerable()){encoder.StandardInput.BaseStream.Write(bytes,0,bytes.Length);Interlocked.Increment(ref written);}}catch(Exception e){encoderFailure=e.ToString();}finally{try{encoder.StandardInput.Close();}catch{}}});
   timer=Stopwatch.StartNew();recording=true;RenderingServer.FramePostDraw+=CaptureFrame;
   Plugin.Log("SHOWCASE RECORDING "+folder);
  }
  static void CaptureFrame(){
   if(!recording||encoderFailure!=null)return;double seconds=timer.Elapsed.TotalSeconds;if(seconds<nextSample)return;nextSample+=1d/30;if(seconds-nextSample>.1)nextSample=seconds+1d/30;
   try{
    using(var img=((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage()){
     if(img.GetWidth()!=1920||img.GetHeight()!=1080)img.Resize(1920,1080,Image.Interpolation.Nearest);
     if(img.GetFormat()!=Image.Format.Rgba8)img.Convert(Image.Format.Rgba8);
     if(queue.TryAdd(img.GetData()))Interlocked.Increment(ref captured);else Interlocked.Increment(ref dropped);
    }
   }catch(Exception e){encoderFailure=e.ToString();Plugin.Log("SHOWCASE FRAME FAILED "+e);}
  }
  static void SaveMarkers(){File.WriteAllText(Path.Combine(folder,"markers.json"),JsonSerializer.Serialize(markers,jsonOptions));}
  static void Mark(string name,object detail=null){markers.Add(new{name,frame=Interlocked.Read(ref captured),videoSeconds=captured/30d,wallSeconds=timer.Elapsed.TotalSeconds,hero=hero.ToString(),detail});SaveMarkers();Plugin.Log("SHOWCASE MARK "+name+" frame="+captured);}
  static async Task Hold(double seconds){var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){Check();await Plugin.NextFrames(1);}}
  static void Check(){if(abort)throw new OperationCanceledException("Showcase stopped by request");if(encoderFailure!=null)throw new Exception("Showcase encoder: "+encoderFailure);}
  static async Task Until(Func<bool> check,string step,double seconds=60){var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){Check();if(check())return;await Plugin.NextFrames(1);}throw new TimeoutException("Showcase: "+step);}
  static async Task Await(Task task,string step,double seconds=60){await Until(()=>task.IsCompleted,step,seconds);await task;}
  static T Visible<T>() where T:Control=>Plugin.Descendants(((SceneTree)Engine.GetMainLoop()).Root).OfType<T>().FirstOrDefault(x=>x.IsVisibleInTree());
  static NCharacterSelectButton Button(NCharacterSelectButton[] buttons,Terrarian t)=>buttons.FirstOrDefault(x=>Equals(AccessTools.Property(typeof(NCharacterSelectButton),"Character")?.GetValue(x),t)||Equals(AccessTools.Field(typeof(NCharacterSelectButton),"_character")?.GetValue(x),t));

  static async Task Run(){
   try{
    Mark("main-menu");await Hold(options.ShowcaseOnly?1:2);
    var menu=NGame.Instance.MainMenu;var select=menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();select.InitializeSingleplayer();menu.SubmenuStack.Push(select);await Hold(2);
    var buttons=Plugin.Descendants(select).OfType<NCharacterSelectButton>().ToArray();
    if(options.PreviewCharacters)foreach(var t in ModelDb.AllCharacters.OfType<Terrarian>()){Button(buttons,t).Select();Mark("character-"+t.Hero);await Hold(4);}
    Button(buttons,ModelDb.AllCharacters.OfType<Terrarian>().First(t=>t.Hero==hero)).Select();await Hold(2);
    AccessTools.Method(typeof(NCharacterSelectScreen),"OnEmbarkPressed").Invoke(select,new object[]{null});
    await Until(()=>Plugin.Demo&&RunManager.Instance.DebugOnlyGetState()?.Map!=null&&NGame.Instance.MainMenu==null,"run startup",90);runShouldSave=RunManager.Instance.ShouldSave;Mark("run-start",new{shouldSave=runShouldSave});await Hold(options.ShowcaseOnly?1:3);
    var initial=RunManager.Instance.EventSynchronizer.GetLocalEvent();await Until(()=>initial.CurrentOptions.Count>=3,"ancient choices");Mark("ancient-choices");await Hold(options.ShowcaseOnly?1:6);
    RunManager.Instance.EventSynchronizer.ChooseLocalOption(2);await Until(()=>initial.IsFinished,"ancient supplies");await Hold(options.ShowcaseOnly?.5:2);await Await(NEventRoom.Proceed(),"leave ancient");
    await Until(()=>NMapScreen.Instance.IsOpen,"map opens");Mark("map-before-combat");await Hold(options.ShowcaseOnly?2:7);
    if(options.ShowcaseOnly){await DemoCombat();return;}
    var state=RunManager.Instance.DebugOnlyGetState();var choices=state.CurrentMapPoint?.Children??state.Map.StartingMapPoint.Children;
    var point=choices.FirstOrDefault(x=>x.PointType==MapPointType.Monster)??choices.FirstOrDefault(x=>x.PointType==MapPointType.Elite);
    if(point==null)throw new Exception("No natural combat successor in current map");
    Mark("enter-combat");await Await(RunManager.Instance.EnterMapCoord(point.coord),"enter map combat");await Until(()=>Plugin.Realtime,"native combat starts");Mark("combat-"+hero);
    await Until(()=>Visible<NRewardsScreen>()!=null||NGame.Instance.MainMenu!=null,"normal combat victory",Math.Clamp(options.CombatLimitSeconds,30,240));
    var rewards=Visible<NRewardsScreen>();if(rewards==null)throw new Exception("Combat ended without a rewards screen");Mark("combat-victory");await Hold(6);
    var set=(RewardsSet)AccessTools.Field(typeof(NRewardsScreen),"_rewardsSet").GetValue(rewards);var gear=set.Rewards.OfType<CardReward>().First();
    var pick=RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(gear);await Until(()=>Visible<NCardRewardSelectionScreen>()!=null,"equipment rewards");Mark("equipment-choice");await Hold(8);
    var cards=Visible<NCardRewardSelectionScreen>();var holder=Plugin.Descendants(cards).OfType<NCardHolder>().First(x=>x.CardModel is TerraEquipment e&&e.Stat==0);
    AccessTools.Method(typeof(NCardRewardSelectionScreen),"SelectCard").Invoke(cards,new object[]{holder});await DeckSelect(pick,"equipment-replacement");await Hold(2);
    foreach(var reward in set.Rewards.Where(x=>!(x is CardReward)).ToArray())await Await(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward),"take other reward");
    AccessTools.Method(typeof(NRewardsScreen),"OnProceedButtonPressed").Invoke(rewards,new object[]{null});await Until(()=>NMapScreen.Instance.IsOpen,"map after victory");Mark("map-after-combat");await Hold(6);
    if(options.Economy)await Economy();
    if(options.Boss){
     // A direct debug room is explicitly identified in the recording manifest.
     // It keeps the owned loadout and normal stats; no strike/clear API is used.
     Mark("boss-demonstration-entry",new{debugRoom=true,stage=1,ownedLoadout=true,modifiedStats=false});
     await Await(RunManager.Instance.EnterRoomDebug(RoomType.Boss,MapPointType.Boss,ModelDb.Encounter<TerraBoss>(),false),"boss demo room");
     await Until(()=>Plugin.Realtime,"boss starts");Mark("boss-live");await Hold(Math.Clamp(options.BossSeconds,5,60));
    }
    Mark("recording-end");
   }catch(Exception e){failure=e.ToString();Plugin.Log("SHOWCASE FAILED "+e);Mark("recording-failure",new{message=e.Message});}
   finally{
    Enabled=false;try{if(Plugin.Demo)await Plugin.Exit();}catch(Exception e){failure=(failure??"")+"\nCleanup: "+e;}
    await FinishRecorder();running=false;idleFrames=0;
   }
  }
  static string[] DemoLoadout(HeroClass h,int stage){
   if(h==HeroClass.Warrior)return stage==1?new[]{"EnchantedBlade","EnchantedBoomerang","WarriorMolten"}:stage==2?new[]{"ShadowKnife","FetidGear","WarriorTitanium"}:new[]{"TerraBladeGear","PaladinHammerGear","WarriorBeetle"};
   if(h==HeroClass.Ranger)return stage==1?new[]{"BeesKneesGear","PhoenixGear","RangerNecro"}:stage==2?new[]{"OnyxGear","DaedalusGear","RangerTitanium"}:new[]{"TsunamiGear","MegaSharkGear","RangerShroomiteBow"};
   if(h==HeroClass.Mage)return stage==1?new[]{"WaterBoltGear","DemonScytheGear","MageMeteor"}:stage==2?new[]{"CrystalSerpentGear","SkyFractureGear","MageTitanium"}:new[]{"TyphoonGear","MagnetSphereGear","MageSpectre"};
   return stage==1?new[]{"SpinalTapGear","ImpStaffGear","SummonerObsidian"}:stage==2?new[]{"CoolWhipGear","SanguineStaffGear","SummonerForbidden"}:new[]{"MorningStarGear","TempestStaffGear","SummonerSpooky"};
  }
  static async Task DemoCombat(){
   int stage=Math.Clamp(options.Stage,1,3);if(!Enum.TryParse(options.Battle,true,out BattleKind kind)||(int)kind>2)throw new Exception("Demo battle must be Normal, Elite or Boss");
   var state=RunManager.Instance.DebugOnlyGetState();var p=state.Players.First();var keys=options.EquipmentKeys??DemoLoadout(hero,stage);
   var defs=keys.Select(EquipmentCatalog.Get).ToArray();if(defs.Length==0||defs.Count(x=>!x.IsArmor)>3||defs.Count(x=>x.IsArmor)>1||defs.Any(x=>x.Hero!=hero||x.Stage!=stage))throw new Exception("Demo loadout must contain 1–3 weapons and at most one armor, all from selected hero and stage");
   foreach(var old in p.Deck.Cards.OfType<TerraEquipment>().ToArray())await CardPileCmd.RemoveFromDeck(old,false);
   foreach(var d in defs)await CardPileCmd.Add(TerraEconomy.Create(p,d),PileType.Deck,skipVisuals:true);
   state.CurrentActIndex=stage-1;
   if(options.Explosive.Equals("Dynamite",StringComparison.OrdinalIgnoreCase)){await CardPileCmd.Add(state.CreateCard(ModelDb.Card<TerraDynamite>(),p),PileType.Deck,skipVisuals:true);Mark("demo-explosive-supply",new{item=167,count=3,source="normal TerraDynamite card acquisition"});}
   else if(!options.Explosive.Equals("None",StringComparison.OrdinalIgnoreCase)&&!options.Explosive.Equals("Grenade",StringComparison.OrdinalIgnoreCase))throw new Exception("Demo explosive must be None, Grenade or Dynamite");
   Mark("demo-loadout",new{stage,battle=kind.ToString(),equipment=defs.Select(d=>new{d.Key,d.Name,d.Items}).ToArray(),forgeLevel=0,modifiedStats=false,demonstrationEquipment=true,explosive=options.Explosive});await Hold(1);
   RoomType room=kind==BattleKind.Normal?RoomType.Monster:kind==BattleKind.Elite?RoomType.Elite:RoomType.Boss;
   MapPointType map=kind==BattleKind.Normal?MapPointType.Monster:kind==BattleKind.Elite?MapPointType.Elite:MapPointType.Boss;
   EncounterModel encounter=kind==BattleKind.Normal?ModelDb.Encounter<TerraNormal>():kind==BattleKind.Elite?ModelDb.Encounter<TerraElite>():ModelDb.Encounter<TerraBoss>();
   await Await(RunManager.Instance.EnterRoomDebug(room,map,encounter,false),"demonstration combat room");await Until(()=>Plugin.Realtime,"demonstration battle starts");
   Mark("demo-combat-live",new{stage,battle=kind.ToString(),seconds=options.Seconds});await Hold(Math.Clamp(options.Seconds,5,90));Mark("demo-combat-end");
  }
  static async Task DeckSelect(Task pending,string name){
   await Until(()=>pending.IsCompleted||Visible<NDeckCardSelectScreen>()!=null,"replacement/forge selection");if(pending.IsCompleted){await pending;return;}
   await Hold(1);var screen=Visible<NDeckCardSelectScreen>();Mark(name);await Hold(6);
   var card=Plugin.Descendants(screen).OfType<NCardHolder>().Select(x=>x.CardModel).First(x=>x is TerraEquipment);AccessTools.Method(typeof(NDeckCardSelectScreen),"OnCardClicked").Invoke(screen,new object[]{card});await Hold(1);
   AccessTools.Method(typeof(NDeckCardSelectScreen),"ConfirmSelection").Invoke(screen,new object[]{null});await Await(pending,"selected equipment applies");
  }
  static async Task Economy(){
   var p=RunManager.Instance.DebugOnlyGetState().Players.First();await PlayerCmd.GainGold(1500,p);
   Mark("economy-demo-funding",new{debugGold=1500});
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Shop,MapPointType.Shop,null,false),"shop");await Until(()=>NMerchantRoom.Instance?.Inventory?.Inventory!=null,"shop inventory");NMerchantRoom.Instance.OpenInventory();await Hold(2);Mark("shop-inventory");await Hold(8);
   var inv=NMerchantRoom.Instance.Inventory.Inventory;var weapon=inv.CharacterCardEntries.First(e=>e.CreationResult?.Card is TerraEquipment g&&g.Stat==0);
   await DeckSelect(weapon.OnTryPurchaseWrapper(inv),"shop-replace-weapon");Mark("shop-weapon-bought");await Hold(3);
   await inv.ColorlessCardEntries.First().OnTryPurchaseWrapper(inv);await inv.RelicEntries.First().OnTryPurchaseWrapper(inv);Mark("shop-accessory-explosives");await Hold(5);
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Treasure,MapPointType.Treasure,null,false),"treasure");await Until(()=>Visible<NTreasureRoom>()!=null,"treasure room opens");Mark("treasure-before-open");await Hold(4);
   var chest=Visible<NTreasureRoom>();var open=(Task)AccessTools.Method(typeof(NTreasureRoom),"OpenChest").Invoke(chest,null);await Until(()=>(bool)AccessTools.Field(typeof(NTreasureRoom),"_isRelicCollectionOpen").GetValue(chest),"accessory picker");Mark("treasure-accessory-choice");await Hold(7);
   RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(0);await Await(open,"chest completes");Mark("treasure-accessory-taken");await Hold(4);
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Event,MapPointType.Unknown,ModelDb.Event<TerraCacheEvent>(),false),"cache event");var cache=RunManager.Instance.EventSynchronizer.GetLocalEvent();await Until(()=>cache.CurrentOptions.Count>=2,"event options");Mark("event-cache-choices");await Hold(8);
   RunManager.Instance.EventSynchronizer.ChooseLocalOption(1);await Until(()=>cache.IsFinished,"event supplies");Mark("event-cache-supplies");await Hold(4);
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Event,MapPointType.Unknown,ModelDb.Event<TerraWorkshopEvent>(),false),"workshop");var workshop=RunManager.Instance.EventSynchronizer.GetLocalEvent();await Until(()=>workshop.CurrentOptions.Count>0,"workshop options");Mark("workshop-options");await Hold(6);RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
   await Until(()=>Visible<NDeckCardSelectScreen>()!=null,"forge selector");await DeckSelect(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks(),"workshop-forge-equipment");await Until(()=>workshop.IsFinished,"forge completion");Mark("workshop-forge-applied");await Hold(4);
  }
  static async Task AutoPotion(int kind){potionBusy=true;try{var p=RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault();var owned=p?.Potions.OfType<TerraPotion>().FirstOrDefault(x=>x.Kind==kind);if(owned!=null&&Plugin.PotionReady(kind))await owned.OnUseWrapper(new ThrowingPlayerChoiceContext(),p.Creature);}catch(Exception e){Plugin.Log("SHOWCASE owned potion failed "+e.Message);}finally{potionBusy=false;}}
  internal static void Control(EngineInput input,BattleState battle,EngineOutput output,float clock){
   if(!Enabled)return;
   if(traceRound!=battle.RoundId){traceRound=battle.RoundId;traceTick=-60;explosiveTick=-1;explosiveConsumed=false;Mark("native-round-"+battle.RoundId,new{inventory=output.InventoryIds.Take(3).ToArray(),enemies=battle.Enemies.Select(n=>new{n.Type,n.Life}).ToArray()});}
   if(battle.Tick-traceTick>=60||battle.Won||battle.Lost){traceTick=battle.Tick;if(trace.Count<600)trace.Add(new{round=battle.RoundId,tick=battle.Tick,life=output.Life,maxLife=output.MaxLife,damage=battle.Damage,minions=battle.MinionCount,won=battle.Won,lost=battle.Lost,enemies=battle.Enemies.Select(n=>new{n.Type,n.Life}).ToArray()});}
   if(input.Paused||battle.Won||battle.Lost)return;
   int slot=0;int[] whips={4672,4913,5074,4912,4911,4678,4679,4914};if(hero==HeroClass.Summoner){slot=Array.FindIndex(output.InventoryIds,0,3,id=>whips.Contains(id));if(slot<0)slot=0;}
   input.Selected=slot;var target=battle.Enemies.OrderBy(n=>Math.Abs(n.X+n.Width*.5f-output.X)).FirstOrDefault();
   if(target!=null){float distance=target.X+target.Width*.5f-output.X,range=hero==HeroClass.Warrior?60:hero==HeroClass.Summoner?125:210;bool near=Math.Abs(distance)<range-30,far=Math.Abs(distance)>range+20;
    input.Right=far&&distance>0||near&&distance<0;input.Left=far&&distance<0||near&&distance>0;
    if(output.X<input.MinX+75&&input.Left){input.Left=false;input.Right=true;}if(output.X>input.MaxX-75&&input.Right){input.Right=false;input.Left=true;}
    input.MouseX=target.X+target.Width*.5f;input.MouseY=target.Y+target.Height*.5f;int cycle=Math.Max(18,output.HeldUseTime+4);input.Use=battle.Tick%cycle<cycle-4;input.Jump=battle.Tick%90<28;
    if(options.ShowcaseOnly&&!options.Explosive.Equals("None",StringComparison.OrdinalIgnoreCase)){
     int explosiveSlot=options.Explosive.Equals("Dynamite",StringComparison.OrdinalIgnoreCase)?5:3;
     // Release the current weapon before switching, then hold less than one
     // explosive use cycle. The native engine creates and consumes the item.
     if(explosiveTick<0&&battle.Tick>=330&&battle.Tick<360)input.Use=false;
     if(explosiveTick<0&&battle.Tick>=360&&output.InventoryStacks[explosiveSlot]>0){explosiveTick=battle.Tick;explosiveStack=output.InventoryStacks[explosiveSlot];retreatLeft=distance>0;Mark("explosive-throw-input",new{slot=explosiveSlot+1,item=output.InventoryIds[explosiveSlot],stock=explosiveStack,tick=battle.Tick});}
     if(explosiveTick>=0){int since=battle.Tick-explosiveTick;
      if(!explosiveConsumed&&output.InventoryStacks[explosiveSlot]<explosiveStack){explosiveConsumed=true;Mark("explosive-consumed",new{tick=battle.Tick,remaining=output.InventoryStacks[explosiveSlot]});}
      if(since<22){input.Selected=explosiveSlot;input.Use=since<18;input.Jump=false;}
      if(since<200){input.Left=retreatLeft;input.Right=!retreatLeft;if(since>=22)input.Jump=since%90<28;}
     }
    }
   }
   if(!potionBusy&&output.MaxLife-output.Life>=100&&Plugin.PotionReady(0))_ = AutoPotion(0);else if(!potionBusy&&hero==HeroClass.Mage&&output.Mana<Math.Max(20,output.HeldMana*2)&&Plugin.PotionReady(1))_ = AutoPotion(1);
  }
  static async Task FinishRecorder(){
   recording=false;RenderingServer.FramePostDraw-=CaptureFrame;double wall=timer.Elapsed.TotalSeconds;queue.CompleteAdding();
   try{await writer;await encoder.WaitForExitAsync();File.WriteAllText(Path.Combine(folder,"ffmpeg.log"),await encoderLog);}catch(Exception e){encoderFailure=(encoderFailure??"")+e;}
   var result=new{success=failure==null&&encoderFailure==null&&encoder.ExitCode==0,hero=hero.ToString(),showcaseOnly=options.ShowcaseOnly,stage=options.ShowcaseOnly?options.Stage:1,battle=options.Battle,explosiveConsumed,width=1920,height=1080,fps=30,capturedFrames=captured,writtenFrames=written,queueDroppedFrames=dropped,wallSeconds=wall,encodedSeconds=written/30d,actualCaptureFps=captured/Math.Max(1,wall),failure,encoderFailure,testFixtureStrikes=0,shouldSaveDuringRun=runShouldSave,returnedToMainMenu=NGame.Instance.MainMenu!=null,notes="Actual Godot viewport; normal native attacks, normal stats, no test strike or enemy clear. ShowcaseOnly prepares stage-matched catalog equipment (forge 0), sets act index and directly enters the chosen encounter. Main tour has direct shop/event/boss rooms and 1500 demonstration shop gold. Explosives come from normal supply/card acquisition and native consumption.",combatTrace=trace.ToArray()};
   File.WriteAllText(Path.Combine(folder,"capture-result.json"),JsonSerializer.Serialize(result,jsonOptions));SaveMarkers();Plugin.Log("SHOWCASE FINISHED "+folder+" success="+result.success+" frames="+written);queue.Dispose();encoder.Dispose();
  }
 }
}

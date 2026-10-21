using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;

namespace SpireTerraria.Next.Host {
 // This harness runs in the actual game, on Godot's main thread. All save calls
 // use the game's own in-memory implementation. No player save is loaded here.
 internal static partial class SpireSelfTest {
  public static bool Enabled {
   get {
    return System.Environment.GetEnvironmentVariable("SPIRE_NEXT_SELFTEST")=="1" ||
     OS.GetCmdlineUserArgs().Contains("--spire-next-selftest") || AdventureEnabled;
   }
  }
  public static bool ConsoleRegressionEnabled=>System.Environment.GetEnvironmentVariable("SPIRE_NEXT_CONSOLE_REGRESSION")=="1"||OS.GetCmdlineUserArgs().Contains("--spire-next-console-regression");
  public static bool VictoryRegressionEnabled=>OS.GetCmdlineUserArgs().Contains("--spire-next-victory-regression");
  public static bool AdventureEnabled => System.Environment.GetEnvironmentVariable("SPIRE_NEXT_ADVENTURETEST")=="1" || OS.GetCmdlineUserArgs().Contains("--spire-next-adventuretest")||ConsoleRegressionEnabled||VictoryRegressionEnabled||PVEnabled;
  public static readonly Dictionary<string,object> Report=new Dictionary<string,object>();
  public static TaskCompletionSource<bool> CombatCompleted=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  internal static void ResetCombat(){CombatCompleted=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);}
  public static readonly string OutputDirectory=Path.GetFullPath(
   System.Environment.GetEnvironmentVariable("SPIRE_NEXT_TEST_OUTPUT") ??
   @"D:\steam\steamapps\common\Terraria\SpireTerrariaNext\.local\spire-test");
  static MockGodotFileIo memoryStore;
  static bool started;
  static EncounterModel nextVictoryEncounter;
  internal static int ChoiceCaptureCount;

  public static void InstallSaveIsolation() {
   if(!Enabled || memoryStore!=null)return;
   memoryStore=new MockGodotFileIo("spire-next-selftest");
   if(VictoryRegressionEnabled)new Harmony("ExiMisto.SpireNext.VictoryRegression").Patch(
    AccessTools.Constructor(typeof(CombatRoom),new[]{typeof(EncounterModel),typeof(IRunState)}),
    prefix:new HarmonyMethod(typeof(SpireSelfTest),nameof(UseVictoryEncounter)));
   var isolated=new SaveManager(memoryStore,true);
   SaveManager.MockInstanceForTesting(isolated);
   // Mod initializers run before ModelDb.Init. Only create model-independent
   // settings here; the normal NGame.GameStartup sequence initializes progress
   // after OneTimeInitialization.ExecuteEssential has built the model database.
   isolated.InitSettingsDataForTest();
   // The user has already enabled local Mod testing. Mirror that choice only in
   // this in-memory profile, using the field written by the native Yes button.
   isolated.SettingsSave.ModSettings=new ModSettings{PlayerAgreedToModLoading=true};
   isolated.SettingsSave.SkipIntroLogo=true;
   // This is an established player's local feasibility test, not a new-player
   // session. The native menu consults this flag before showing its EA notice.
   isolated.SettingsSave.SeenEaDisclaimer=true;
   Report["saveBackend"]="Native MockGodotFileIo (memory only)";
   Report["testModeEnabled"]=false;
   Directory.CreateDirectory(OutputDirectory);
   WriteProgress("save-isolation-installed");
  }

  public static async Task Run() {
   if(!Enabled || started)return;
   started=true;
   var watch=Stopwatch.StartNew();
   int exit=1;
   try {
    InstallSaveIsolation();
    await WaitUntil(()=>NGame.Instance!=null && NGame.Instance.IsInsideTree(),"NGame scene",90);
    await WaitUntil(()=>NGame.Instance.GameStartupComplete!=null,"startup task",30);
    await NGame.Instance.GameStartupComplete.WaitAsync(TimeSpan.FromSeconds(120));
    await WaitUntil(()=>NGame.Instance.MainMenu!=null && NGame.Instance.MainMenu.IsInsideTree(),"main menu",30);
    Report["gameStartupCompleted"]=true;
    if(SaveManager.Instance.Progress==null)SaveManager.Instance.InitProgressData();
    SaveManager.Instance.SetFtuesEnabled(false);
    await WaitUntil(()=>NoModal() && TransitionFinished(),"unobstructed main menu",20);
    Report["memoryModLoadingConfirmed"]=SaveManager.Instance.SettingsSave.ModSettings.PlayerAgreedToModLoading;
    Report["memoryEaDisclaimerSeen"]=SaveManager.Instance.SettingsSave.SeenEaDisclaimer;
    WriteProgress("starting-native-run");
    if(PVEnabled)PreparePV();

    var run=AdventureEnabled?await StartAdventureFromNativeMenu():await NGame.Instance.StartNewSingleplayerRun(
     ModelDb.Character<Ironclad>(),false,ActModel.GetDefaultList(),
     Array.Empty<ModifierModel>(),"SPIRENEXTM2",GameMode.Standard);
    Report["nativeRunCreated"]=run!=null && NRun.Instance!=null;
    Report["runSavingDisabled"]=!RunManager.Instance.ShouldSave;
    Report["character"]=run.Players[0].Character.Id.ToString();
    Report["mapPointCount"]=run.Map.GetAllMapPoints().Count();
    if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Self-test must not create a persistent run.");
    await WaitUntil(()=>NMapScreen.Instance!=null,"native map",20);
    if(AdventureEnabled) {
     AdventureSession.EnsureRun();
     AssertNativeInventory(run,"starter");
     if(run.CurrentRoom is EventRoom opening) {
      var model=opening.LocalMutableEvent;
      await WaitUntil(()=>model.Node!=null&&model.Node.IsInsideTree()&&TransitionFinished(),"opening ancient event",20);
      await WaitForEventButtons();
      var leave=model.CurrentOptions.SingleOrDefault(x=>x.TextKey=="SPIRE_NEXT_LEAVE")??throw new InvalidOperationException("The opening ancient did not offer Terraria expedition choices.");
      NEventRoom.Instance.OptionButtonClicked(leave,model.CurrentOptions.ToList().IndexOf(leave));
      await WaitUntil(()=>model.IsFinished,"opening ancient choice",20);
      await NEventRoom.Proceed().WaitAsync(TimeSpan.FromSeconds(20));
      Report["nativeOpeningAncientCompleted"]=true;
     }
    }
    NMapScreen.Instance.Open(false);
    await WaitForMap("initial map");
    if(AdventureEnabled) {
     var testRun=AdventureSession.Current;
     Report["firstBattleStartingWeapon"]=testRun.Weapons[0].Definition.NativeSymbols[0];
     AdventureSession.PushHostHealth();
    }
    Report["initialVisibleMapPoints"]=VisibleMapPoints();
    await Capture("01-native-map.png");
    if(PVEnabled) {
     await RunPVAdventure(run);Report["memorySaveCalls"]=memoryStore.Calls.Count;Report["passed"]=true;exit=0;return;
    }
    if(ConsoleRegressionEnabled) {
     await RunConsoleRegression(run);Report["memorySaveCalls"]=memoryStore.Calls.Count;Report["passed"]=true;exit=0;return;
    }
    if(VictoryRegressionEnabled) {
     await RunVictoryRegression(run);Report["memorySaveCalls"]=memoryStore.Calls.Count;Report["passed"]=true;exit=0;return;
    }

    MapPoint first=run.Map.GetAllMapPoints()
     .Where(p=>p.PointType==MapPointType.Monster)
     .OrderBy(p=>p.coord.row).ThenBy(p=>p.coord.col).First();
    Report["selectedMapCoord"]=first.coord.ToString();
    WriteProgress("entering-native-combat");
    await RunManager.Instance.EnterMapCoord(first.coord);
    await WaitUntil(()=>run.CurrentRoom is CombatRoom,"native combat room",30);
    Report["nativeCombatRoomEntered"]=true;
    WriteProgress("waiting-for-native-core-battle");
    bool victory=await CombatCompleted.Task.WaitAsync(TimeSpan.FromSeconds(90));
    if(!victory)throw new InvalidOperationException("Native Terraria battle did not finish in victory.");
    Report["nativeCoreVictory"]=true;

    NRewardsScreen rewards=null;
    await WaitUntil(()=>{ rewards=FindVisibleRewards(GetTree().Root);return rewards!=null; },"native reward screen",30);
    await WaitUntil(()=>NoModal() && TransitionFinished() && rewards.IsVisibleInTree() && rewards.Modulate.A>0.95f,"unobstructed native rewards",20);
    await Settle(0.35);
    Report["nativeRewardsVisible"]=true;
    Report["nativeRewardsUnobstructed"]=true;
    Report["nativeRewardScreenType"]=rewards.GetType().FullName;
    await Capture("03-native-rewards.png");
    if(AdventureEnabled) {
     WriteProgress("claiming-native-equipment-reward");
     var adventure=AdventureSession.Current??throw new InvalidOperationException("Adventure model was not attached.");
     if(!AdventureSession.ClassChosen)throw new InvalidOperationException("Native class choice was not completed.");
     Report["adventureClass"]=adventure.Hero.ToString();
     Report["catalogEquipmentCount"]=GearCatalog.All.Count;
     int before=adventure.Weapons.Count+(adventure.Armor==null?0:1);
     var reward=AdventureHostUi.LastEquipmentReward??throw new InvalidOperationException("Native reward list did not contain an equipment reward.");
     var serialized=reward.ToSerializable();
     string cachedOffers=JsonSerializer.Serialize(adventure.RewardNodes[AdventureSession.NodeKey]);
     var restoredReward=Reward.FromSerializable(serialized,run.Players[0]);
     if(restoredReward.GetType()!=reward.GetType()||JsonSerializer.Serialize(adventure.RewardNodes[AdventureSession.NodeKey])!=cachedOffers)throw new InvalidOperationException("Equipment reward restore changed its cached options or became a native card reward.");
     Report["equipmentRewardSerializationStable"]=true;
     AdventureHostUi.TestChoices.Enqueue(0);
     bool claimed=await restoredReward.SelectUnsynchronized().WaitAsync(TimeSpan.FromSeconds(20));
     if(!claimed||!adventure.ClaimedNodes.Contains(AdventureSession.NodeKey))throw new InvalidOperationException("Native equipment choice did not persist its claim.");
     Report["nativeEquipmentRewardClaimed"]=claimed;
     AssertNativeInventory(run,"reward");
     Report["equipmentAfterReward"]=adventure.Weapons.Select(AdventureHostUi.GearName).Concat(adventure.Armor==null?Array.Empty<string>():new[]{AdventureHostUi.GearName(adventure.Armor)}).ToArray();
     if(adventure.Weapons.Count+(adventure.Armor==null?0:1)<=before&&adventure.Armor==null)throw new InvalidOperationException("Equipment reward did not alter the loadout.");
    }
    WriteProgress("proceeding-from-native-rewards");
    // Invoke the exact callback wired to the native Proceed control. This also
    // tests the game's normal return-to-map path, including reward skipping.
    var proceed=typeof(NRewardsScreen).GetMethod("OnProceedButtonPressed",BindingFlags.Instance|BindingFlags.NonPublic);
    if(proceed==null)throw new MissingMethodException("Native rewards Proceed callback changed.");
    proceed.Invoke(rewards,new object[]{null});
    await WaitForMap("return to map");
    Report["returnedToNativeMap"]=true;
    Report["returnedVisibleMapPoints"]=VisibleMapPoints();
    await Capture("04-returned-map.png");
    if(AdventureEnabled) {
     await RunAdventureRooms(run);
     NMapScreen.Instance.Open(false);
     await WaitForMap("map after adventure systems");
    }

    // Continue along an actual child edge. A second native battle is deliberately
    // cancelled after attachment so its automatic victory cannot reuse the first
    // battle's completion source or turn this finite test into a full run.
    var next=first.Children.OrderBy(p=>p.PointType==MapPointType.Monster?0:1)
     .ThenBy(p=>p.coord.col).FirstOrDefault();
    if(next==null)throw new InvalidOperationException("First map room has no legal child edge.");
    Report["nextLegalMapCoord"]=next.coord.ToString();
    Report["nextMapPointType"]=next.PointType.ToString();
    WriteProgress("continuing-to-next-legal-map-room");
    await NMapScreen.Instance.TravelToMapCoord(next.coord);
    await WaitUntil(()=>run.CurrentMapCoord.HasValue && run.CurrentMapCoord.Value==next.coord && TransitionFinished(),"next map room",30);
    Report["nextLegalRoomEntered"]=true;
    if(run.CurrentRoom is CombatRoom) {
     await WaitUntil(()=>HostIntegration.Active?.Ready==true,"next native battle attachment",30);
     var nextBattle=HostIntegration.Active;
     Report["nextNativeBattleAttached"]=true;
     if(AdventureEnabled) {
      var expected=AdventureSession.BuildConfiguration();
      if(nextBattle.Configuration==null||!nextBattle.Configuration.Weapons.SequenceEqual(expected.Weapons)||!nextBattle.Configuration.Armor.SequenceEqual(expected.Armor)||!nextBattle.Configuration.WeaponAffixes.SequenceEqual(expected.WeaponAffixes))throw new InvalidOperationException("Forged equipment was not passed into the following native battle.");
      Report["nextBattleUsesForgedLoadout"]=true;
      Report["nextNativeEncounterSize"]=nextBattle.State.Remaining;
      Report["nativeTopBarVisibleDuringBattle"]=nextBattle.NativeHudUncovered;Report["readableHeroHealthBar"]=nextBattle.HealthBarReadable;Report["nativeInventoryAccessoryCount"]=nextBattle.InventoryHud?.AccessoryCount;Report["nativeInventoryPotionCount"]=nextBattle.InventoryHud?.PotionCount;
      if(!nextBattle.NativeHudUncovered||!nextBattle.HealthBarReadable||nextBattle.InventoryHud?.AccessoryCount!=AdventureSession.Current.Accessories.Count||nextBattle.InventoryHud?.PotionCount<1)throw new InvalidOperationException("Native top bar, readable HP or actual Terraria inventories were missing from combat.");
      await Settle(.25);
      AssertNativeItemArt(NRun.Instance.GlobalUi,"combat");
      await Capture("12-combat-native-hud.png");
      var accessoryHolder=Descendants(NRun.Instance.GlobalUi).OfType<NRelicInventoryHolder>().First(x=>x.Relic?.Model is AdventureAccessory&&IsRendered(x));
      await AssertAccessoryHover(accessoryHolder,(AdventureAccessory)accessoryHolder.Relic.Model,"combat","12b-combat-accessory-tooltip.png");
      var potion=FindVisibleHealingPotion(GetTree().Root)??throw new InvalidOperationException("Actual Terraria healing potion holder was not visible.");
      var model=(AdventurePotion)potion.Potion.Model;string potionId=model.InstanceId;
      int bottlesBefore=run.Players[0].Potions.Count();int consumedBefore=nextBattle.State.ConsumedSlots.Length;
      typeof(NPotionHolder).GetMethod("OnRelease",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(potion,null);
      NPotionPopup popup=null;
      await WaitUntil(()=>{popup=FindNode<NPotionPopup>(GetTree().Root);return popup!=null&&IsRendered(popup);},"native potion use menu",10);
      if(run.Players[0].Potions.Count()!=bottlesBefore||!run.Players[0].Potions.OfType<AdventurePotion>().Any(p=>p.InstanceId==potionId))throw new InvalidOperationException("Opening a potion menu consumed its bottle before the worker accepted use.");
      if(!popup.IsUsable)throw new InvalidOperationException("Native healing potion use menu was disabled during Terraria combat.");
      Report["nativePotionMenuDoesNotPreConsume"]=true;
      typeof(NPotionPopup).GetMethod("OnUseButtonPressed",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(popup,new object[]{null});
      await WaitUntil(()=>nextBattle.State.ConsumedSlots.Length>consumedBefore&&!run.Players[0].Potions.OfType<AdventurePotion>().Any(p=>p.InstanceId==potionId),"worker-confirmed native potion consumption",15);
      Report["nativePotionClickConsumesTerrariaItem"]=true;
      Report["consumedNativePotionInstance"]=potionId;
      NMapScreen.Instance.Open(false);
      await WaitUntil(()=>nextBattle.NativeScreenPaused,"native map pauses battle",10);
      await Settle(.25);int pausedTick=nextBattle.State.Tick;await Settle(.25);
      if(nextBattle.State.Tick!=pausedTick)throw new InvalidOperationException("Native simulation advanced behind the map.");
      Report["nativeMapPausesSimulation"]=true;NMapScreen.Instance.Close(false);
      await WaitUntil(()=>!nextBattle.NativeScreenPaused,"native map returns to paused battle",10);
     }
     await Capture("05-next-native-battle.png");
     await HostIntegration.Abort(nextBattle);
     await WaitUntil(()=>HostIntegration.Active==null,"cancel second battle",10);
     if(AdventureEnabled&&(!Report.TryGetValue("nativeTooltipLayerRestored",out var restoredTooltips)||restoredTooltips is not true))throw new InvalidOperationException("Native tooltip presentation was not restored after leaving Terraria combat.");
     Report["nextBattleCancelledToCardCombat"]=true;
    } else {
     Report["nextNativeRoomType"]=run.CurrentRoom.GetType().FullName;
     await Settle(0.35);
     await Capture("05-next-native-room.png");
    }
    WriteProgress("cleaning-up-native-test-run");
    // Public menu navigation performs RunManager.CleanUp(true) and releases the
    // native room scene before the process quits, matching normal game teardown.
    await NGame.Instance.ReturnToMainMenu().WaitAsync(TimeSpan.FromSeconds(30));
    await WaitUntil(()=>!RunManager.Instance.IsInProgress && NGame.Instance.MainMenu!=null &&
     NGame.Instance.MainMenu.IsVisibleInTree() && TransitionFinished(),"native test run cleanup",20);
    Report["nativeRunCleanedUp"]=true;
    await Settle(0.35);
    Report["memorySaveCalls"]=memoryStore.Calls.Count;
    Report["passed"]=true;
    exit=0;
   } catch(Exception error) {
    Report["passed"]=false;
    Report["error"]=error.ToString();
    GD.PrintErr("[SpireNext SelfTest] "+error);
    try { await Capture("failure.png"); } catch(Exception captureError) { Report["captureError"]=captureError.Message; }
   } finally {
    if(HostIntegration.Active!=null)try{await HostIntegration.Abort(HostIntegration.Active);}catch(Exception cleanup){Report["battleCleanupError"]=cleanup.ToString();}
    Report["durationSeconds"]=Math.Round(watch.Elapsed.TotalSeconds,3);
    Report["completedUtc"]=DateTime.UtcNow.ToString("O");
    File.WriteAllText(Path.Combine(OutputDirectory,"report.json"),JsonSerializer.Serialize(Report,new JsonSerializerOptions{WriteIndented=true}));
    if(ConsoleRegressionEnabled)File.WriteAllText(Path.Combine(OutputDirectory,"console-regression-report.json"),JsonSerializer.Serialize(Report,new JsonSerializerOptions{WriteIndented=true}));
    if(VictoryRegressionEnabled)File.WriteAllText(Path.Combine(OutputDirectory,"victory-settlement-report.json"),JsonSerializer.Serialize(Report,new JsonSerializerOptions{WriteIndented=true}));
    GD.Print("[SpireNext SelfTest] "+(exit==0?"PASS":"FAIL")+" "+OutputDirectory);
    GetTree().Quit(exit);
   }
  }

  static void UseVictoryEncounter(ref EncounterModel encounter) {
   if(nextVictoryEncounter==null)return;
   encounter=nextVictoryEncounter.ToMutable();nextVictoryEncounter=null;
  }
  static async Task RunVictoryRegression(RunState run) {
   Report["regressionMode"]="Terraria victory through native win on death-spawn encounters";
   var first=run.Map.GetAllMapPoints().Where(p=>p.PointType==MapPointType.Monster&&p.Children.Any(c=>c.PointType==MapPointType.Monster&&c.Children.Any(next=>next.PointType==MapPointType.Monster))).OrderBy(p=>p.coord.row).ThenBy(p=>p.coord.col).FirstOrDefault()
    ??throw new InvalidOperationException("Victory regression needs three connected battles.");
   var second=first.Children.Where(p=>p.PointType==MapPointType.Monster&&p.Children.Any(c=>c.PointType==MapPointType.Monster)).OrderBy(p=>p.coord.col).First();
   var third=second.Children.Where(p=>p.PointType==MapPointType.Monster).OrderBy(p=>p.coord.col).First();
   var points=new[]{first,second,third};
   EncounterModel[] encounters={ModelDb.Encounter<PhrogParasiteElite>(),ModelDb.Encounter<AxebotsNormal>(),ModelDb.Encounter<GremlinMercNormal>()};
   string[] powers={"InfestedPower","StockPower","SurprisePower"};
   var checks=new List<object>();Report["victorySettlementCases"]=checks;
   // Only this in-memory regression fixture raises a weapon level and restores
   // HP between cases. Native combat still has to win through its normal tick.
   Report["victoryRegressionFixture"]="starter weapon +20; full HP before each encounter; real map travel and native Terraria victories";
   for(int i=0;i<points.Length;i++) {
    WriteProgress("victory-regression-"+encounters[i].GetType().Name);
    ResetCombat();Report.Remove("nativeVictorySettlement");Report.Remove("nativeVictorySettlementTaskCompleted");
    var adventure=AdventureSession.Current;adventure.Weapons[0].Level=20;adventure.Life=adventure.LifeMax;AdventureSession.PushHostHealth();
    nextVictoryEncounter=encounters[i];
    if(i==0)await RunManager.Instance.EnterMapCoord(points[i].coord);else await NMapScreen.Instance.TravelToMapCoord(points[i].coord);
    await WaitUntil(()=>HostIntegration.Active?.Ready==true,"native battle for "+encounters[i].GetType().Name,30);
    var battle=HostIntegration.Active;var combat=battle.Combat;int worker=ReadActiveWorkerId();
    if(combat.Encounter.GetType()!=encounters[i].GetType())throw new InvalidOperationException("The regression encounter was not installed.");
    var deathPowers=combat.Enemies.SelectMany(enemy=>enemy.Powers).Where(power=>power.GetType().Name==powers[i]).Select(power=>new{type=power.GetType().Name,amount=power.Amount}).ToArray();
    if(deathPowers.Length==0)throw new InvalidOperationException("Regression is missing its actual death-spawn power: "+powers[i]);
    string boss=battle.Configuration?.BossSymbol??"";
    await Capture("victory-"+i+"-death-spawn-combat.png");
    if(!await CombatCompleted.Task.WaitAsync(TimeSpan.FromSeconds(90)))throw new InvalidOperationException("Terraria battle did not win in "+encounters[i].GetType().Name);
    if(!Report.TryGetValue("nativeVictorySettlement",out var settlement)||!Equals(settlement,"WinConsoleCmd.Process")||!Report.TryGetValue("nativeVictorySettlementTaskCompleted",out var completed)||!Equals(completed,true))throw new InvalidOperationException("Victory did not await the native win command.");
    NRewardsScreen rewards=null;
    await WaitUntil(()=>{rewards=FindVisibleRewards(GetTree().Root);return rewards!=null&&IsRendered(rewards)&&TransitionFinished();},"death-spawn encounter rewards",30);
    await WaitUntil(()=>HostIntegration.Active==null&&BattleLayers()==0&&!WorkerAlive(worker),"death-spawn worker cleanup",15);
    if(combat.Enemies.Any(enemy=>enemy.IsAlive)||!HostIntegration.OriginalInputAllowed)throw new InvalidOperationException("Host enemies or input remained after native win.");
    if(boss.Length>0&&AdventureSession.VerifiedBossSymbol!=boss)throw new InvalidOperationException("Native win lost the verified Terraria boss drop source.");
    var reward=AdventureHostUi.LastEquipmentReward??throw new InvalidOperationException("Native win lost the equipment reward.");
    AdventureHostUi.TestChoices.Enqueue(0);
    if(!await reward.SelectUnsynchronized().WaitAsync(TimeSpan.FromSeconds(20)))throw new InvalidOperationException("Death-spawn equipment reward could not be claimed.");
    AssertNativeInventory(run,"victorySettlement"+i);
    await Capture("victory-"+i+"-rewards.png");
    typeof(NRewardsScreen).GetMethod("OnProceedButtonPressed",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(rewards,new object[]{null});
    await WaitForMap("map after "+encounters[i].GetType().Name);
    checks.Add(new{encounter=encounters[i].GetType().Name,deathPowers,settlement,workerExited=!WorkerAlive(worker),remainingHostEnemies=combat.Enemies.Count(enemy=>enemy.IsAlive),inputRestored=HostIntegration.OriginalInputAllowed,equipmentClaimed=true,mapReturned=true,verifiedBoss=boss});
   }
   WriteProgress("victory-regression-returning-to-menu");
   await NGame.Instance.ReturnToMainMenu().WaitAsync(TimeSpan.FromSeconds(30));
   await WaitUntil(()=>!RunManager.Instance.IsInProgress&&NGame.Instance.MainMenu?.IsVisibleInTree()==true&&TransitionFinished()&&BattleLayers()==0,"victory regression clean menu",20);
   Report["nativeRunCleanedUp"]=true;
  }
  static async Task RunConsoleRegression(RunState run) {
   Report["regressionMode"]="native console win lifecycle";
   var first=run.Map.GetAllMapPoints().Where(p=>p.PointType==MapPointType.Monster&&p.Children.Any(c=>c.PointType==MapPointType.Monster&&c.Children.Any(next=>next.PointType==MapPointType.Monster))).OrderBy(p=>p.coord.row).ThenBy(p=>p.coord.col).FirstOrDefault()
    ??throw new InvalidOperationException("Regression map needs three connected ordinary battles; seed: "+run.Rng.StringSeed);
   var second=first.Children.Where(p=>p.PointType==MapPointType.Monster&&p.Children.Any(c=>c.PointType==MapPointType.Monster)).OrderBy(p=>p.coord.col).First();
   var third=second.Children.Where(p=>p.PointType==MapPointType.Monster).OrderBy(p=>p.coord.col).First();
   Report["consoleRegressionMapChain"]=new[]{first.coord.ToString(),second.coord.ToString(),third.coord.ToString()};
   await RunManager.Instance.EnterMapCoord(first.coord);
   await CheckConsoleWin(run,"first");
   NRewardsScreen rewards=null;
   await WaitUntil(()=>{rewards=FindVisibleRewards(GetTree().Root);return rewards!=null&&IsRendered(rewards)&&TransitionFinished();},"console victory native rewards",30);
   AssertNativeItemArt(NRun.Instance.GlobalUi,"consoleFirstRewards");
   await Capture("regression-02-console-win-rewards.png");
   var reward=AdventureHostUi.LastEquipmentReward??throw new InvalidOperationException("External console victory did not offer Terraria equipment.");
   AdventureHostUi.TestChoices.Enqueue(0);
   var claim=reward.SelectUnsynchronized();
   await WaitUntil(()=>AdventureHostUi.CurrentSelection!=null&&AdventureHostUi.CurrentCards?.Count>=3,"visible equipment selection after console victory",20);
   Report["consoleChoiceBeforeFadeDiagnostics"]=ChoiceColorDiagnostics();
   await WaitUntil(()=>VisibleChoiceCards()==AdventureHostUi.CurrentCards?.Count,"all actual equipment choices rendered",10);
   await WaitUntil(()=>AdventureHostUi.ChoicePresentationReady(AdventureHostUi.CurrentSelection,AdventureHostUi.CurrentCards),"native choice holder color fade completed",10);
   var cards=Descendants(AdventureHostUi.CurrentSelection).OfType<NCard>().Where(card=>AdventureHostUi.CurrentCards.Contains(card.Model)).ToArray();
   Report["consoleChoiceSettledDiagnostics"]=ChoiceColorDiagnostics();
   Report["consoleRewardVisibleCards"]=cards.Select(card=>new{title=card.Model.Title,model=card.Model.GetType().FullName,owner=card.Model.Owner?.Character.Id.ToString(),portraitWidth=CardPortraitNode(card).Texture.GetWidth(),portraitHeight=CardPortraitNode(card).Texture.GetHeight()}).ToArray();
   if(cards.Any(card=>card.Model is not AdventureGearCard&&card.Model is not AdventureChoiceCard))throw new InvalidOperationException("A Terraria reward reused an unrelated vanilla card model.");
   await Capture("regression-03-console-equipment-choice.png");
   if(!await claim.WaitAsync(TimeSpan.FromSeconds(20)))throw new InvalidOperationException("Rendered equipment choice could not be claimed.");
   AssertNativeInventory(run,"consoleReward");
   Report["consoleRewardClaimedAfterVisibleSelection"]=true;
   typeof(NRewardsScreen).GetMethod("OnProceedButtonPressed",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(rewards,new object[]{null});
   await WaitForMap("console regression map after first battle");
   await NMapScreen.Instance.TravelToMapCoord(second.coord);
   await WaitUntil(()=>run.CurrentMapCoord==second.coord&&run.CurrentRoom is CombatRoom,"second connected console regression combat",30);
   await CheckConsoleWin(run,"second");
   await WaitUntil(()=>{rewards=FindVisibleRewards(GetTree().Root);return rewards!=null&&IsRendered(rewards)&&TransitionFinished();},"second external victory rewards",30);
   AssertNativeItemArt(NRun.Instance.GlobalUi,"consoleSecondRewards");
   await Capture("regression-05-second-console-rewards.png");
   // Follow the next real map edge so native travel establishes the encounter's
   // replay recorder before testing a return to the menu with a live worker.
   typeof(NRewardsScreen).GetMethod("OnProceedButtonPressed",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(rewards,new object[]{null});
   await WaitForMap("console regression map after second battle");
   await NMapScreen.Instance.TravelToMapCoord(third.coord);
   await WaitUntil(()=>run.CurrentMapCoord==third.coord&&run.CurrentRoom is CombatRoom&&TransitionFinished(),"third connected console regression combat",30);
   await WaitUntil(()=>HostIntegration.Active?.Ready==true,"live third battle for menu cleanup",30);
   int worker=ReadActiveWorkerId();
   if(BattleLayers()!=1)throw new InvalidOperationException("Combat layers accumulated before menu cleanup.");
   WriteProgress("regression-returning-to-main-menu-during-battle");
   await NGame.Instance.ReturnToMainMenu().WaitAsync(TimeSpan.FromSeconds(30));
   await WaitUntil(()=>!RunManager.Instance.IsInProgress&&NGame.Instance.MainMenu!=null&&NGame.Instance.MainMenu.IsVisibleInTree()&&TransitionFinished()&&HostIntegration.Active==null&&BattleLayers()==0&&!WorkerAlive(worker),"console regression menu and worker cleanup",20);
   if(!HostIntegration.OriginalInputAllowed)throw new InvalidOperationException("Original input stayed blocked after returning to the menu.");
   Report["consoleRegressionMenuCleanup"]=new{worker,workerExited=!WorkerAlive(worker),battleLayers=BattleLayers(),inputRestored=HostIntegration.OriginalInputAllowed};
   Report["nativeRunCleanedUp"]=true;await Capture("regression-06-clean-menu.png");
  }
  static async Task CheckConsoleWin(RunState run,string step) {
   WriteProgress("regression-"+step+"-native-console-win");
   await WaitUntil(()=>HostIntegration.Active?.Ready==true,"native battle ready before console win "+step,30);
   var battle=HostIntegration.Active;int worker=ReadActiveWorkerId();
   if(worker<=0||!WorkerAlive(worker)||BattleLayers()!=1)throw new InvalidOperationException("Battle worker/layer was not alive before console win "+step+".");
   await Settle(.15);
   AssertNativeItemArt(NRun.Instance.GlobalUi,"console"+step+"Combat");
   if(battle.InventoryHud?.VisibleAccessoryCount!=AdventureSession.Current.Accessories.Count||battle.InventoryHud?.VisiblePotionCount<1)throw new InvalidOperationException("Native inventory models existed but their actual icons were not rendered before console win "+step+".");
   await Capture("regression-"+step+"-live-combat.png");
   // Execute the game's actual command and its asynchronous kill task. This
   // deliberately bypasses the Mod's native victory/Complete path.
   var result=new WinConsoleCmd().Process(run.Players[0],Array.Empty<string>());
   if(!result.success)throw new InvalidOperationException("Native win command rejected regression: "+result.msg);
   if(result.task!=null)await result.task.WaitAsync(TimeSpan.FromSeconds(30));
   await WaitUntil(()=>HostIntegration.Active==null&&BattleLayers()==0&&!WorkerAlive(worker),"external win releases overlay and worker "+step,20);
   if(!HostIntegration.OriginalInputAllowed||NRun.Instance.ProcessMode==Node.ProcessModeEnum.Disabled)throw new InvalidOperationException("External win did not restore native input/run processing "+step+".");
   Report[step+"NativeConsoleWin"]=new{command="WinConsoleCmd.Process",message=result.msg,worker,workerExited=!WorkerAlive(worker),battleLayers=BattleLayers(),inputRestored=HostIntegration.OriginalInputAllowed};
  }
  static int ReadActiveWorkerId() {var field=typeof(BattleOverlay).GetField("workerId",BindingFlags.NonPublic|BindingFlags.Instance);return field?.GetValue(HostIntegration.Active) is int id?id:0;}
  static bool WorkerAlive(int id) {if(id<=0)return false;try{using var process=Process.GetProcessById(id);return !process.HasExited;}catch(ArgumentException){return false;}}
  static int BattleLayers()=>Descendants(GetTree().Root).OfType<CanvasLayer>().Count(layer=>layer.Name.ToString()=="SpireTerrariaBattle");
  static TextureRect CardPortraitNode(NCard card)=>typeof(NCard).GetField("_portrait",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(card) as TextureRect;
  static int VisibleChoiceCards() {
   if(AdventureHostUi.CurrentSelection==null||AdventureHostUi.CurrentCards==null)return 0;
   return Descendants(AdventureHostUi.CurrentSelection).OfType<NCard>().Count(card=>AdventureHostUi.CurrentCards.Contains(card.Model)&&CardPortraitNode(card) is TextureRect portrait&&portrait.Texture!=null&&IsRendered(portrait));
  }
  static object ChoiceColorDiagnostics() {
   var screen=AdventureHostUi.CurrentSelection;if(screen==null)return Array.Empty<object>();
   return Descendants(screen).OfType<NCard>().Where(card=>AdventureHostUi.CurrentCards?.Contains(card.Model)==true).Select(card=>new {
    title=card.Model.Title,
    fields=new[]{"_portrait","_portraitCanvasGroup","_frame","_titleLabel","_descriptionLabel","_banner"}.Select(field=>new{field,node=typeof(NCard).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(card) as CanvasItem}).Where(entry=>entry.node!=null).Select(entry=>new{entry.field,path=entry.node.GetPath().ToString(),modulate=Rgba(entry.node.Modulate),selfModulate=Rgba(entry.node.SelfModulate),material=entry.node.Material?.ResourcePath,shader=(entry.node.Material as ShaderMaterial)?.Shader?.ResourcePath}).ToArray(),
    ancestors=CardColorAncestors(CardPortraitNode(card))
   }).ToArray();
  }
  static object[] CardColorAncestors(CanvasItem node) {
   var result=new List<object>();Color combined=Colors.White;
   for(Node current=node;current!=null;current=current.GetParent())if(current is CanvasItem item) {
    combined*=item.Modulate;result.Add(new{path=item.GetPath().ToString(),modulate=Rgba(item.Modulate),selfModulate=Rgba(item.SelfModulate),combined=Rgba(combined),material=item.Material?.ResourcePath});
   }
   return result.ToArray();
  }
  static float[] Rgba(Color color)=>new[]{color.R,color.G,color.B,color.A};

  static async Task<RunState> StartAdventureFromNativeMenu() {
   await AdventureHostUi.PreloadItemIcons().WaitAsync(TimeSpan.FromSeconds(45));
   var accessoryDescriptions=GearCatalog.All.Where(g=>g.Kind==GearKind.Accessory).Select(g=>AssertAccessoryTooltip((AdventureAccessory)AdventureInventoryModels.CanonicalAccessory(g.Key),"catalog")).ToArray();
   Report["nativeAccessoryTooltipCatalog"]=accessoryDescriptions;
   var canonical=ModelDb.AllCharacters.OfType<AdventureCharacter>().OrderBy(x=>x.Hero).ToArray();
   if(canonical.Length!=4||canonical.Select(x=>x.Hero).Distinct().Count()!=4)throw new InvalidOperationException("BaseLib did not register all four Terraria characters.");
   var original=ModelDb.Character<Ironclad>();
   if(AdventureCharacters.IsTerrariaCharacter(original)||AdventureCharacters.TryGetHero(original,out _)||original.StartingDeck.Any(x=>x is AdventureGearCard)||original.StartingRelics.Any(x=>x is AdventureAccessory))throw new InvalidOperationException("Vanilla Ironclad was included in the Terraria character predicate or its starting models were replaced.");
   Report["vanillaCharacterExcludedFromTakeover"]=true;
   WriteProgress("checking-baselib-native-character-select");
   var menu=NGame.Instance.MainMenu;
   typeof(NMainMenu).GetMethod("SingleplayerButtonPressed",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(menu,new object[]{null});
   NSingleplayerSubmenu submenu=null;
   NCharacterSelectScreen screen=null;
   // A profile with only Standard unlocked goes directly from Singleplayer to
   // character selection. More progressed profiles first show the mode submenu.
   await WaitUntil(()=> {
    screen=Descendants(menu).OfType<NCharacterSelectScreen>().FirstOrDefault(IsRendered);
    submenu=Descendants(menu).OfType<NSingleplayerSubmenu>().FirstOrDefault(IsRendered);
    return screen!=null||submenu!=null;
   },"native singleplayer menu or character select",15);
   if(screen==null) {
    await Settle(.35);
    typeof(NSingleplayerSubmenu).GetMethod("OpenCharacterSelect",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(submenu,new object[]{null});
   }
   await WaitUntil(()=>{screen=Descendants(menu).OfType<NCharacterSelectScreen>().FirstOrDefault(IsRendered);return screen!=null&&TransitionFinished();},"native character select",20);
   await Settle(.65);
   var buttons=Descendants(screen).OfType<NCharacterSelectButton>().Where(x=>x.Character is AdventureCharacter).ToArray();
   if(buttons.Length!=4||buttons.Any(x=>!IsRendered(x)||x.IsLocked))throw new InvalidOperationException("The native selection screen did not show four unlocked Terraria character buttons.");
   var checkedCharacters=new List<object>();
   foreach(var character in canonical) {
    var button=buttons.Single(x=>x.Character.Id==character.Id);button.Select();await Settle(.6);
    var icon=typeof(NCharacterSelectButton).GetField("_icon",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(button) as TextureRect;
    var hp=ReadTextField(screen,"_hp");var title=ReadTextField(screen,"_name");var relic=ReadTextField(screen,"_relicTitle");
    var expected=AdventureRun.Create(0,character.Hero);
    string[] expectedCards=expected.Weapons.Append(expected.Armor).Select(g=>g.DefinitionKey).OrderBy(x=>x).ToArray();
    var actualCards=character.StartingDeck.ToArray();
    var nativeIcon=AdventureHostUi.ItemIcon(expected.Weapons[0].Definition.NativeSymbols[0]);
    if(nativeIcon==null||icon?.Texture==null||icon.Texture.GetInstanceId()!=nativeIcon.GetInstanceId()||!button.IsSelected||hp!="300/300"||!title.Contains(GearCatalog.HeroName(character.Hero))||!relic.Contains("赫尔墨斯靴")||actualCards.Any(x=>x is not AdventureGearCard)||!actualCards.Cast<AdventureGearCard>().Select(x=>x.GearKey).OrderBy(x=>x).SequenceEqual(expectedCards)||character.StartingRelics.Count!=3||character.StartingPotions.Count!=2)throw new InvalidOperationException("Native character artwork, HP or Terraria starting equipment was incorrect for "+character.Hero+".");
    checkedCharacters.Add(new{hero=character.Hero.ToString(),title,hp,startingCards=actualCards.Select(x=>x.Title).ToArray(),startingRelics=character.StartingRelics.Select(x=>x.Title.GetFormattedText()).ToArray(),startingPotions=character.StartingPotions.Select(x=>x.Title.GetFormattedText()).ToArray(),iconWidth=icon.Texture.GetWidth(),iconHeight=icon.Texture.GetHeight()});
    AssertNativeItemArt(screen,"character"+character.Hero);
    await Capture("00-character-"+character.Hero+".png");
   }
   var warrior=buttons.Single(x=>x.Character is AdventureWarrior);warrior.Select();await Settle(.6);
   await Capture("00-character-select.png");
   Report["nativeCharacterSelectValidated"]=checkedCharacters;Report["nativeTerrariaCharacterButtons"]=buttons.Length;
   typeof(NCharacterSelectScreen).GetMethod("OnEmbarkPressed",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(screen,new object[]{null});
   await WaitUntil(()=>AdventureSession.HostRun?.Map!=null&&AdventureSession.HostRun.CurrentRoom!=null&&NRun.Instance!=null&&NGame.Instance.MainMenu==null&&TransitionFinished(),"native character Embark run",60);
   var run=AdventureSession.HostRun;
   if(run.Players[0].Character is not AdventureWarrior)throw new InvalidOperationException("Native Embark did not start the selected Terraria warrior.");
   Report["nativeCharacterEmbarkCallbackStartedRun"]=true;return run;
  }
  static string ReadTextField(object parent,string field) {
   object value=parent.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(parent);
   return value is Label label?label.Text:value is RichTextLabel rich?rich.Text:value?.GetType().GetProperty("Text")?.GetValue(value)?.ToString()??"";
  }
  static IEnumerable<Node> Descendants(Node node){yield return node;foreach(Node child in node.GetChildren())foreach(var descendant in Descendants(child))yield return descendant;}
  static void AssertNativeItemArt(Node root,string step) {
   var relics=Descendants(root).OfType<NRelic>().Where(x=>x.Model is AdventureAccessory&&IsRendered(x)).ToArray();
   var potions=Descendants(root).OfType<NPotion>().Where(x=>x.Model is AdventurePotion&&IsRendered(x)).ToArray();
   foreach(var relic in relics){if(relic.Icon?.Texture?.GetInstanceId()!=AdventureHostUi.ItemIcon(((AdventureAccessory)relic.Model).Definition.NativeSymbols[0])?.GetInstanceId()||!IsRendered(relic.Icon))throw new InvalidOperationException("Native accessory has invisible or placeholder artwork at "+step+".");AssertAccessoryTooltip((AdventureAccessory)relic.Model,step);}
   foreach(var potion in potions)if(potion.Image?.Texture?.GetInstanceId()!=AdventureHostUi.ItemIcon(((AdventurePotion)potion.Model).Definition.NativeSymbols[0])?.GetInstanceId()||!IsRendered(potion.Image))throw new InvalidOperationException("Native potion has invisible or placeholder artwork at "+step+".");
   if(relics.Length<3||(step=="combat"&&potions.Length<1))throw new InvalidOperationException("Actual inventory images are missing at "+step+".");
   Report[step+"NativeItemArtwork"]=new{accessories=relics.Length,potions=potions.Length};
  }
  static object AssertAccessoryTooltip(AdventureAccessory accessory,string step) {
   var tip=accessory.HoverTip;string description=accessory.DynamicDescription.GetFormattedText();
   var allTips=accessory.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().ToArray();
   var checks=new{key=accessory.GearKey,step,expectedTitle=accessory.Definition.Name,actualTitle=tip.Title,expectedDescription=accessory.EquipmentDescription,actualDescription=description,tipDescription=tip.Description,canonicalType=tip.CanonicalModel?.GetType().FullName,canonicalKey=(tip.CanonicalModel as AdventureAccessory)?.GearKey,tipCount=allTips.Length,collectionTips=allTips.Select(t=>new{title=t.Title,description=t.Description,canonicalKey=(t.CanonicalModel as AdventureAccessory)?.GearKey}).ToArray(),descriptionMatches=description==accessory.EquipmentDescription,titleMatches=tip.Title==accessory.Definition.Name,tipMatches=AccessoryDescriptionMatches(tip.Description,description),canonicalMatches=tip.CanonicalModel is AdventureAccessory canonical&&canonical.GearKey==accessory.GearKey,collectionMatches=allTips.Any(t=>t.Title==tip.Title&&AccessoryDescriptionMatches(t.Description,description)&&(t.CanonicalModel as AdventureAccessory)?.GearKey==accessory.GearKey)};
   if(!checks.descriptionMatches||!checks.titleMatches||!checks.tipMatches||!checks.canonicalMatches||!checks.collectionMatches){Report["nativeAccessoryTooltipFailure"]=checks;throw new InvalidOperationException("Native accessory tooltip lost its precise description or canonical model: "+JsonSerializer.Serialize(checks));}
   return new{key=accessory.GearKey,title=tip.Title,description};
  }
  // BaseLib adds its standard source footer to displayed mod hover tips.
  static bool AccessoryDescriptionMatches(string displayed,string expected)=>displayed==expected||displayed==expected+"\n[color=#8a8a8a]来自模组: SpireTerraria[/color]";
  static async Task AssertAccessoryHover(Control holder,AdventureAccessory accessory,string step,string filename,string callback="OnFocus") {
   AssertAccessoryTooltip(accessory,step);NHoverTipSet.Clear();
   var battle=step=="combat"?HostIntegration.Active:null;
   bool resumeBattle=battle!=null&&!(bool)AccessTools.Field(typeof(BattleOverlay),"paused").GetValue(battle);
   if(resumeBattle)AccessTools.Method(typeof(BattleOverlay),"TogglePause").Invoke(battle,null);
   await Settle(.2);
   using var before=battle==null?null:await ViewportImage();
   var focus=AccessTools.Method(holder.GetType(),callback)??throw new MissingMethodException(holder.GetType().FullName,callback);
   focus.Invoke(holder,null);
   NHoverTipSet tips=null;
   try {
    await WaitUntil(()=>{tips=Descendants(GetTree().Root).OfType<NHoverTipSet>().FirstOrDefault(IsRendered);return tips!=null;},step+" native accessory hover",5);
    await Settle(.2);
    var description=Descendants(tips).OfType<RichTextLabel>().FirstOrDefault(label=>AccessoryDescriptionMatches(label.Text,accessory.EquipmentDescription)&&IsRendered(label));
    if(description==null||!GetTree().Root.GetVisibleRect().Encloses(description.GetGlobalRect()))throw new InvalidOperationException("Native accessory tooltip text is missing or outside the viewport at "+step+".");
    if(battle!=null) {
     int canvasLayer=0;for(Node parent=description;parent!=null;parent=parent.GetParent())if(parent is CanvasLayer canvas){canvasLayer=canvas.Layer;break;}
     if(canvasLayer<=AdventureBattleHud.BattleCanvasLayer)throw new InvalidOperationException("Native accessory tooltip is covered by the Terraria battle canvas.");
     using var after=await ViewportImage();var pixels=CompareTooltipPixels(before,after,description.GetGlobalRect());
     if(pixels.changed<Math.Max(100,pixels.total/100)||pixels.brightText<100)throw new InvalidOperationException("Native accessory tooltip did not produce visible text in the paused battle viewport.");
     Report["combatAccessoryTooltipPixels"]=new{canvasLayer,battleLayer=AdventureBattleHud.BattleCanvasLayer,pixels.changed,pixels.brightText,pixels.total,pausedTick=battle.State.Tick};
     if(before.SavePng(Path.Combine(OutputDirectory,Path.GetFileNameWithoutExtension(filename)+"-before.png"))!=Error.Ok)throw new IOException("Tooltip comparison viewport export failed.");
    }
    Report[step+"NativeAccessoryHover"]=new{key=accessory.GearKey,description=description.Text,rect=description.GetGlobalRect().ToString(),nativeScene=tips.GetType().FullName};
    await Capture(filename);
   } finally {
    if(callback=="OnFocus")AccessTools.Method(holder.GetType(),"OnUnfocus")?.Invoke(holder,null);
    NHoverTipSet.Remove(holder);
    if(resumeBattle&&HostIntegration.Active==battle)AccessTools.Method(typeof(BattleOverlay),"TogglePause").Invoke(battle,null);
   }
  }
  static (int changed,int brightText,int total) CompareTooltipPixels(Image before,Image after,Rect2 screenRect) {
   before.Convert(Image.Format.Rgba8);after.Convert(Image.Format.Rgba8);
   if(before.GetWidth()!=after.GetWidth()||before.GetHeight()!=after.GetHeight())throw new InvalidOperationException("Tooltip viewport dimensions changed while paused.");
   var viewport=GetTree().Root.GetVisibleRect();float scaleX=after.GetWidth()/viewport.Size.X,scaleY=after.GetHeight()/viewport.Size.Y;
   int x0=Math.Clamp((int)(screenRect.Position.X*scaleX),0,after.GetWidth()),y0=Math.Clamp((int)(screenRect.Position.Y*scaleY),0,after.GetHeight());
   int x1=Math.Clamp((int)Math.Ceiling(screenRect.End.X*scaleX),0,after.GetWidth()),y1=Math.Clamp((int)Math.Ceiling(screenRect.End.Y*scaleY),0,after.GetHeight());
   byte[] first=before.GetData(),second=after.GetData();int changed=0,brightText=0;
   for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){int i=(y*after.GetWidth()+x)*4;if(Math.Abs(first[i]-second[i])+Math.Abs(first[i+1]-second[i+1])+Math.Abs(first[i+2]-second[i+2])<=40)continue;changed++;if(second[i]+second[i+1]+second[i+2]>450)brightText++;}
   return(changed,brightText,(x1-x0)*(y1-y0));
  }
  static void AssertNativeInventory(RunState run,string step) {
   var adventure=AdventureSession.Current??throw new InvalidOperationException("No adventure equipment model at "+step);
   var player=run.Players[0];var cards=player.Deck.Cards.ToArray();
   var expectedCards=adventure.Weapons.Append(adventure.Armor).Where(x=>x!=null).Select(x=>x.DefinitionKey).OrderBy(x=>x).ToArray();
   var expectedRelics=adventure.Accessories.Select(x=>x.DefinitionKey).OrderBy(x=>x).ToArray();
   var expectedPotions=adventure.Consumables.Where(x=>AdventureInventoryModels.IsPotion(x.Definition)).Take(player.MaxPotionCount).Select(x=>x.DefinitionKey).OrderBy(x=>x).ToArray();
   if(cards.Any(c=>c is not AdventureGearCard)||!cards.Cast<AdventureGearCard>().Select(c=>c.GearKey).OrderBy(x=>x).SequenceEqual(expectedCards)||player.Relics.Any(r=>r is not AdventureAccessory)||!player.Relics.Cast<AdventureAccessory>().Select(r=>r.GearKey).OrderBy(x=>x).SequenceEqual(expectedRelics)||player.Potions.Any(p=>p is not AdventurePotion)||!player.Potions.Cast<AdventurePotion>().Select(p=>p.GearKey).OrderBy(x=>x).SequenceEqual(expectedPotions))throw new InvalidOperationException("The native deck, relics or potions did not match the Terraria expedition inventory at "+step+".");
   Report[step+"NativeInventory"]=new{cards=cards.Select(c=>c.Title).ToArray(),accessories=player.Relics.Count,potions=player.Potions.Count(),supplyPotions=adventure.Consumables.Count(x=>AdventureInventoryModels.IsPotion(x.Definition))};
  }

  static async Task RunAdventureRooms(RunState run) {
   var adventure=AdventureSession.Current;
   // EnterRoom is a direct test fixture; normal map travel clears overlays.
   NMapScreen.Instance.Close(false);
   MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Clear();
   WriteProgress("entering-native-camp");
   await RunManager.Instance.EnterRoom(new RestSiteRoom());
   await WaitUntil(()=>NRestSiteRoom.Instance!=null&&NRestSiteRoom.Instance.IsInsideTree()&&TransitionFinished(),"native camp",20);
   await Settle(.5);
   string campKey=AdventureSession.NodeKey;
   var camp=adventure.GetCamp(campKey);
   var options=NRestSiteRoom.Instance.Options;
   if(options.Count!=4||!options.Any(x=>x.OptionId=="SPIRE_NEXT_FORGE")||!options.Any(x=>x.OptionId=="SPIRE_NEXT_REFORGE"))throw new InvalidOperationException("Native camp does not expose all adventure actions.");
   Report["nativeCampOptions"]=options.Select(x=>x.Title.GetFormattedText()).ToArray();
   await Capture("06-native-camp.png");
   // Material stock is an explicit test fixture. Actual random reward bundles
   // remain unchanged; the fixture ensures all recipe rarity paths are usable.
   adventure.GiveMaterials(new Dictionary<string,int>{{"1:ore",12},{"1:weave",12},{"1:bone",12},{"1:crystal",12},{"1:essence",12}});
   Report["forgeMaterialFixture"]=true;
   for(int i=0;i<2;i++) {
    AdventureHostUi.TestChoices.Enqueue(0);AdventureHostUi.TestChoices.Enqueue(i==1?1:0);
    await options.Single(x=>x.OptionId=="SPIRE_NEXT_REFORGE").OnSelect().WaitAsync(TimeSpan.FromSeconds(25));
    if(camp.ReforgeAttempts!=i+1||camp.MainActionUsed)throw new InvalidOperationException("Free reforge consumed the main camp action or did not consume exactly one attempt.");
   }
   int materialsBefore=adventure.Materials.Values.Sum();
   var weaponsBeforeForge=adventure.Weapons.Select(item=>item.InstanceId).ToArray();
   AdventureHostUi.TestChoices.Enqueue(0);AdventureHostUi.TestChoices.Enqueue(PVEnabled?1:0);
   await options.Single(x=>x.OptionId=="SPIRE_NEXT_FORGE").OnSelect().WaitAsync(TimeSpan.FromSeconds(25));
   if(!camp.MainActionUsed||camp.PendingForge?.Count>0||adventure.Materials.Values.Sum()>=materialsBefore)throw new InvalidOperationException("Native forge did not consume materials and resolve its random output.");
   if(adventure.Weapons.Count>3)throw new InvalidOperationException("Forge exceeded three weapon slots.");
   Report["nativeForgeResolved"]=true;
   if(PVEnabled)PVConfirmForgedWeapon(adventure,weaponsBeforeForge);
   Report["forgeMaterialsConsumed"]=materialsBefore-adventure.Materials.Values.Sum();
   Report["forgedLoadout"]=adventure.Weapons.Select(AdventureHostUi.GearName).Concat(adventure.Armor==null?Array.Empty<string>():new[]{AdventureHostUi.GearName(adventure.Armor)}).ToArray();
   if(options.Single(x=>x.OptionId=="SPIRE_NEXT_UPGRADE").IsEnabled)throw new InvalidOperationException("Main camp action remained available after forging.");
   var reforgeOption=options.Single(x=>x.OptionId=="SPIRE_NEXT_REFORGE");
   if(!reforgeOption.IsEnabled)throw new InvalidOperationException("Forging disabled an unused free reforge.");
   // Interrupt the final attempt after its roll, as a save/return would do.
   var pending=adventure.RollReforge(campKey,adventure.Weapons[0].InstanceId);
   if(pending==null||camp.ReforgeRemaining!=0||!reforgeOption.IsEnabled)throw new InvalidOperationException("Final pending reforge could not be resumed after using the main camp action.");
   AdventureHostUi.TestChoices.Enqueue(0);
   await reforgeOption.OnSelect().WaitAsync(TimeSpan.FromSeconds(25));
   if(camp.PendingReforge!=null||camp.ReforgeAttempts!=3||reforgeOption.IsEnabled||await AdventureHostUi.Reforge())throw new InvalidOperationException("Camp did not resolve exactly three free reforges.");
   Report["threeFreeReforgesShared"]=true;
   Report["pendingReforgeAfterForgeResolved"]=true;
   Report["reforgeAffix"]=adventure.Weapons[0].Affix;
   await Capture("07-camp-after-forge.png");

   WriteProgress("entering-native-adventure-shop");
   await RunManager.Instance.EnterRoom(new MerchantRoom());
   await WaitUntil(()=>run.CurrentRoom is MerchantRoom&&TransitionFinished(),"native shop room",20);
   await Settle(.5);
   var merchant=(MerchantRoom)run.CurrentRoom;
   var inventory=merchant.Inventories.First();
   NMerchantRoom.Instance.OpenInventory();
   await WaitUntil(()=>NMerchantRoom.Instance.Inventory.IsOpen,"native merchant inventory opened",10);await Settle(.7);
   await Capture("08a-native-shop-stock.png");
   foreach(var item in inventory.RelicEntries.Where(x=>x.IsStocked))AssertAccessoryTooltip((AdventureAccessory)item.Model,"shop");
   var shopAccessory=Descendants(NMerchantRoom.Instance).OfType<NMerchantRelic>().First(x=>x.Entry is MerchantRelicEntry entry&&entry.Model is AdventureAccessory&&IsRendered(x));
   await AssertAccessoryHover(shopAccessory,(AdventureAccessory)((MerchantRelicEntry)shopAccessory.Entry).Model,"shop","08b-native-shop-accessory-tooltip.png","CreateHoverTip");
   await PlayerCmd.SetGold(1000,run.Players[0]);
   var entry=inventory.CharacterCardEntries.First(x=>x.IsStocked);int goldBefore=run.Players[0].Gold;
   if(adventure.Weapons.Count>=3)AdventureHostUi.TestChoices.Enqueue(0);
   if(!await entry.OnTryPurchaseWrapper(inventory).WaitAsync(TimeSpan.FromSeconds(20)))throw new InvalidOperationException("Native equipment shop purchase failed.");
   if(run.Players[0].Gold>=goldBefore)throw new InvalidOperationException("Shop purchase did not spend gold.");
   AssertNativeInventory(run,"shopEquipment");
   Report["nativeShopEquipmentPurchase"]=true;
   Report["shopDeckContainsOnlyTerrariaEquipment"]=true;
   int consumables=adventure.Consumables.Count;
   var explosive=inventory.ColorlessCardEntries.First(x=>x.IsStocked);
   int weaponCount=adventure.Weapons.Count;
   if(!await explosive.OnTryPurchaseWrapper(inventory).WaitAsync(TimeSpan.FromSeconds(20))||adventure.Consumables.Count!=consumables+1||adventure.Weapons.Count!=weaponCount)throw new InvalidOperationException("Universal throwable shop item used a weapon slot.");
   Report["shopExplosiveOutsideWeaponSlots"]=true;
   var potionEntry=inventory.PotionEntries.First(x=>x.IsStocked);
   if(!await potionEntry.OnTryPurchaseWrapper(inventory).WaitAsync(TimeSpan.FromSeconds(20)))throw new InvalidOperationException("Native Terraria potion purchase failed.");
   // Leave one chest-exclusive accessory unowned so this journey exercises an
   // actual new chest pickup, rather than the valid duplicate-to-material path.
   var relicEntry=inventory.RelicEntries.First(x=>x.IsStocked&&x.Model is AdventureAccessory accessory&&accessory.Definition.NativeSymbols[0]!="CobaltShield");
   if(!await relicEntry.OnTryPurchaseWrapper(inventory).WaitAsync(TimeSpan.FromSeconds(20)))throw new InvalidOperationException("Native Terraria accessory purchase failed.");
   Report["nativeShopPotionAndAccessoryPurchase"]=true;
   AssertNativeInventory(run,"shop");
   await Capture("08-native-shop.png");

   WriteProgress("opening-native-adventure-treasure");
   await RunManager.Instance.EnterRoom(new TreasureRoom(run.CurrentActIndex));
   NTreasureRoom treasure=null;
   await WaitUntil(()=>{treasure=FindNode<NTreasureRoom>(GetTree().Root);return treasure!=null&&treasure.IsVisibleInTree()&&TransitionFinished();},"native treasure",20);
   await Settle(.5);
   int relicsBefore=run.Players[0].Relics.Count;
   int originalRelicsBefore=run.Players[0].Relics.Count(r=>r is not AdventureAccessory);
   int accessoriesBefore=adventure.Accessories.Count;
   var open=typeof(NTreasureRoom).GetMethod("OpenChest",BindingFlags.NonPublic|BindingFlags.Instance)??throw new MissingMethodException("Native chest callback changed.");
   var openTask=(Task)open.Invoke(treasure,null);
   var collection=(NTreasureRoomRelicCollection)typeof(NTreasureRoom).GetField("_relicCollection",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(treasure);
   await WaitUntil(()=>collection.SingleplayerRelicHolder?.Relic?.Model is AdventureAccessory&&IsRendered(collection.SingleplayerRelicHolder)&&collection.SingleplayerRelicHolder.IsEnabled&&collection.SingleplayerRelicHolder.MouseFilter!=Control.MouseFilterEnum.Ignore&&Godot.Time.GetTicksMsec()>(ulong)typeof(NTreasureRoomRelicCollection).GetField("_openedTicks",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(collection)+200,"native treasure accessory picker",15);
   if(AdventureHostUi.CurrentSelection!=null||FindVisibleRewards(GetTree().Root)!=null||collection.SingleplayerRelicHolder.Relic.Icon?.Texture==null)throw new InvalidOperationException("Treasure did not use its visible native relic picker.");
   await Capture("09-native-treasure-picker.png");
   var treasureHolder=collection.SingleplayerRelicHolder;bool treasureReleased=false;
   await AssertAccessoryHover(treasureHolder,(AdventureAccessory)treasureHolder.Relic.Model,"treasure","09b-native-treasure-accessory-tooltip.png");
   treasureHolder.Released+=_=>treasureReleased=true;treasureHolder.GrabFocus();
   // OnRelease alone only animates the icon. Feed the actual native selection
   // action so press state, Released, voting and relic acquisition all execute.
   using(var press=new InputEventAction{Action=MegaCrit.Sts2.Core.ControllerInput.MegaInput.select,Pressed=true})treasureHolder._GuiInput(press);
   await NextFrame();
   using(var release=new InputEventAction{Action=MegaCrit.Sts2.Core.ControllerInput.MegaInput.select,Pressed=false})treasureHolder._GuiInput(release);
   Report["treasureNativePickerReleased"]=treasureReleased;
   if(!treasureReleased)throw new InvalidOperationException("Native treasure selection action did not emit Released.");
   await openTask.WaitAsync(TimeSpan.FromSeconds(20));
   Report["treasureAccessoryDelta"]=adventure.Accessories.Count-accessoriesBefore;
   Report["treasureOriginalRelicDelta"]=run.Players[0].Relics.Count(r=>r is not AdventureAccessory)-originalRelicsBefore;
   Report["treasureVisibleRewardScreen"]=FindVisibleRewards(GetTree().Root)?.GetPath().ToString()??"none";
   if(adventure.Accessories.Count!=accessoriesBefore+1||run.Players[0].Relics.Count!=relicsBefore+1||run.Players[0].Relics.Count(r=>r is not AdventureAccessory)!=originalRelicsBefore||FindVisibleRewards(GetTree().Root)!=null)throw new InvalidOperationException("Treasure did not grant exactly one native Terraria accessory or opened a combat reward page.");
   AssertNativeInventory(run,"treasure");
   Report["nativeTreasureAccessoryOnly"]=true;
   Report["nativeTreasureRelicPicker"]=true;
   Report["treasureNoOriginalRelic"]=true;
   await Capture("09-native-treasure.png");
   int materialCount=adventure.Materials.Values.Sum();
   relicsBefore=run.Players[0].Relics.Count;accessoriesBefore=adventure.Accessories.Count;
   // This memory-only room fixture reuses its map coordinate. Reset its cached
   // claim to exercise a fresh, exhausted chest pool at that same coordinate.
   adventure.ClaimedNodes.Remove(AdventureSession.NodeKey);adventure.RewardNodes.Remove(AdventureSession.NodeKey);
   await RunManager.Instance.EnterRoom(new TreasureRoom(run.CurrentActIndex));
   treasure=null;
   await WaitUntil(()=>{treasure=FindNode<NTreasureRoom>(GetTree().Root);return treasure!=null&&treasure.IsVisibleInTree()&&TransitionFinished();},"native duplicate treasure",20);
   await ((Task)open.Invoke(treasure,null)).WaitAsync(TimeSpan.FromSeconds(20));
   if(adventure.Materials.Values.Sum()!=materialCount+2||adventure.Accessories.Count!=accessoriesBefore||run.Players[0].Relics.Count!=relicsBefore||AdventureHostUi.CurrentSelection!=null||FindVisibleRewards(GetTree().Root)!=null)throw new InvalidOperationException("An exhausted treasure pool did not convert once to materials through the native chest.");
   AssertNativeInventory(run,"duplicate-treasure");
   Report["nativeTreasureDuplicateMaterialsOnly"]=true;
   Report["adventureConfigurationWeapons"]=AdventureSession.BuildConfiguration().Weapons;
   Report["adventureConfigurationArmor"]=AdventureSession.BuildConfiguration().Armor;
   await RunAdventureEvents(run);
  }

  static async Task RunAdventureEvents(RunState run) {
   var adventure=AdventureSession.Current;
   var canonical=ModelDb.AllSharedEvents.First(x=>x is not AncientEventModel&&x.IsAllowed(run)&&AdventureHostUi.EventThemeFor(x.GetType().Name,false)=="clinic");
   WriteProgress("checking-native-terraria-event");
   await RunManager.Instance.EnterRoom(new EventRoom(canonical));
   var model=((EventRoom)run.CurrentRoom).LocalMutableEvent;
   await WaitUntil(()=>model.Node!=null&&model.Node.IsInsideTree()&&TransitionFinished(),"native event",20);
   await WaitForEventButtons();
   if(model.CurrentOptions.Count!=4||model.CurrentOptions.Any(x=>!x.TextKey.StartsWith("SPIRE_NEXT_")))throw new InvalidOperationException("Native event retained Spire rewards.");
   int bottles=adventure.Consumables.Count,gold=run.Players[0].Gold;
   var gather=model.CurrentOptions.Single(x=>x.TextKey=="SPIRE_NEXT_EVENT_CLINIC_HEALING");
   await Capture("10-native-event.png");
   NEventRoom.Instance.OptionButtonClicked(gather,model.CurrentOptions.ToList().IndexOf(gather));
   await WaitUntil(()=>model.IsFinished,"native event option callback",20);
   if(!model.IsFinished||adventure.Consumables.Count!=bottles+2||run.Players[0].Gold!=gold-20)throw new InvalidOperationException("Clinic did not exchange 20 gold for two actual Terraria healing potions.");
   await gather.Chosen().WaitAsync(TimeSpan.FromSeconds(20));
   if(adventure.Consumables.Count!=bottles+2||run.Players[0].Gold!=gold-20)throw new InvalidOperationException("Native event supplies could be claimed or charged twice.");
   Report["nativeEventModel"]=canonical.Id.ToString();
   Report["nativeClinicPurchasesTwoActualPotionsOnce"]=true;
   AssertNativeInventory(run,"clinicEvent");
   await Settle(.5);await Capture("10b-native-event-result.png");

   WriteProgress("checking-native-terraria-ancient");
   await RunManager.Instance.EnterRoom(new EventRoom(ModelDb.AncientEvent<MegaCrit.Sts2.Core.Models.Events.Neow>()));
   model=((EventRoom)run.CurrentRoom).LocalMutableEvent;
   await WaitUntil(()=>model.Node!=null&&model.Node.IsInsideTree()&&TransitionFinished(),"native ancient",20);
   await WaitForEventButtons();
   if(model.CurrentOptions.Count!=4||model.CurrentOptions.Any(x=>!x.TextKey.StartsWith("SPIRE_NEXT_")))throw new InvalidOperationException("Ancient retained Spire rewards.");
   var levels=adventure.Weapons.Append(adventure.Armor).ToDictionary(g=>g.InstanceId,g=>g.Level);
   int relics=run.Players[0].Relics.Count;
   await Capture("11-native-ancient.png");
   AdventureHostUi.TestChoices.Enqueue(0);
   var forge=model.CurrentOptions.Single(x=>x.TextKey=="SPIRE_NEXT_EVENT_NEOW_FORGE");NEventRoom.Instance.OptionButtonClicked(forge,model.CurrentOptions.ToList().IndexOf(forge));
   await WaitUntil(()=>model.IsFinished,"native ancient option callback",20);
   var changed=adventure.Weapons.Append(adventure.Armor).Where(g=>g.Level!=levels[g.InstanceId]).ToArray();
   if(!model.IsFinished||changed.Length!=1||changed[0].Level!=levels[changed[0].InstanceId]+1||run.Players[0].Relics.Count!=relics)throw new InvalidOperationException("Neow's forge did not upgrade exactly one selected Terraria item or retained a native relic.");
   Report["nativeAncientUpgradesOneTerrariaItem"]=true;
   Report["nativeAncientUpgradedInstance"]=changed[0].InstanceId;
   AssertNativeInventory(run,"neowForgeEvent");
   await Settle(.5);await Capture("11b-native-ancient-result.png");
  }
  static async Task WaitForEventButtons() {
   await WaitUntil(()=>NEventRoom.Instance?.Layout?.OptionButtons.Count()>=3,"native event layout",20);await Settle(.8);
   if(NEventRoom.Instance.Layout is NAncientEventLayout ancient)for(int i=0;i<16&&ancient.OptionButtons.Count(IsRendered)<3;i++){typeof(NAncientEventLayout).GetMethod("OnDialogueHitboxClicked",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ancient,new object[]{null});await Settle(.35);}
   await WaitUntil(()=>NEventRoom.Instance.Layout.OptionButtons.Count(IsRendered)>=3,"visible native event choices",20);await Settle(.3);
  }
  static bool IsRendered(Control node){if(!node.IsVisibleInTree()||!node.GetGlobalRect().Intersects(GetTree().Root.GetVisibleRect()))return false;for(Node parent=node;parent!=null;parent=parent.GetParent())if(parent is CanvasItem item&&item.Modulate.A<.9f)return false;return true;}
  static NPotionHolder FindVisibleHealingPotion(Node node){if(node is NPotionHolder holder&&holder.Potion?.Model is AdventurePotion potion&&potion.Definition.NativeSymbols[0].Contains("HealingPotion")&&IsRendered(holder))return holder;foreach(Node child in node.GetChildren()){var result=FindVisibleHealingPotion(child);if(result!=null)return result;}return null;}

  static T FindNode<T>(Node node) where T:Node {if(node is T found)return found;foreach(Node child in node.GetChildren()){var result=FindNode<T>(child);if(result!=null)return result;}return null;}

  public static async Task Capture(string filename) {
   using var image=await ViewportImage();
   var result=image.SavePng(Path.Combine(OutputDirectory,filename));
   if(result!=Error.Ok)throw new IOException("Viewport export failed: "+result);
   if(PVEnabled)await CapturePVSnapshotClip(filename);
  }
  static async Task<Image> ViewportImage() {
   await NextFrame();
   await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
   var image=NGame.Instance.GetViewport().GetTexture().GetImage();
   if(image==null || image.IsEmpty())throw new InvalidOperationException("Actual Godot viewport produced no image.");
   return image;
  }

  static NRewardsScreen FindVisibleRewards(Node node) {
   if(node is NRewardsScreen rewards && rewards.IsVisibleInTree())return rewards;
   foreach(Node child in node.GetChildren()) {
    var found=FindVisibleRewards(child);
    if(found!=null)return found;
   }
   return null;
  }
  static SceneTree GetTree() {return (SceneTree)Engine.GetMainLoop();}
  static bool NoModal() {return NModalContainer.Instance==null || NModalContainer.Instance.OpenModal==null;}
  static bool TransitionFinished() {return NGame.Instance.Transition==null || !NGame.Instance.Transition.InTransition;}
  static async Task WaitForMap(string step) {
   await WaitUntil(()=>NMapScreen.Instance!=null && NMapScreen.Instance.IsOpen && NMapScreen.Instance.IsTravelEnabled &&
    NoModal() && TransitionFinished() && MapAnimationFinished() && VisibleMapPoints()>=2,step,30);
   await Settle(0.35);
  }
  static bool MapAnimationFinished() {
   // Observe native tween completion; do not alter animation state or layout.
   foreach(string name in new[]{"_actAnimTween","_tween"}) {
    var field=typeof(NMapScreen).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);
    var tween=field==null?null:field.GetValue(NMapScreen.Instance) as Tween;
    if(tween!=null && GodotObject.IsInstanceValid(tween) && tween.IsRunning())return false;
   }
   return true;
  }
  static int VisibleMapPoints() {
   if(NMapScreen.Instance==null)return 0;
   return CountVisibleMapPoints(NMapScreen.Instance,NGame.Instance.GetViewport().GetVisibleRect());
  }
  static int CountVisibleMapPoints(Node node,Rect2 viewport) {
   int count=0;
   if(node is NMapPoint point && point.IsVisibleInTree() && point.Modulate.A>0.95f && point.GetGlobalRect().Intersects(viewport))count++;
   foreach(Node child in node.GetChildren())count+=CountVisibleMapPoints(child,viewport);
   return count;
  }
  static async Task Settle(double seconds) {
   var watch=Stopwatch.StartNew();
   while(watch.Elapsed.TotalSeconds<seconds)await NextFrame();
  }
  static async Task NextFrame() {
   var tree=GetTree();
   await tree.Root.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
  }
  static async Task WaitUntil(Func<bool> condition,string step,int timeoutSeconds) {
   var watch=Stopwatch.StartNew();
   while(!condition()) {
    if(watch.Elapsed.TotalSeconds>timeoutSeconds)throw new TimeoutException("Timed out waiting for "+step+".");
    await NextFrame();
   }
  }
  static void WriteProgress(string state) {
   Report["stage"]=state;
   Directory.CreateDirectory(OutputDirectory);
   File.WriteAllText(Path.Combine(OutputDirectory,"progress.json"),JsonSerializer.Serialize(Report,new JsonSerializerOptions{WriteIndented=true}));
   GD.Print("[SpireNext SelfTest] "+state);
  }
 }
}

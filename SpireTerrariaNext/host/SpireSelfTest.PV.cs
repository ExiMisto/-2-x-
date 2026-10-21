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
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireTerraria.Next;

namespace SpireTerraria.Next.Host {
 internal static partial class SpireSelfTest {
  internal static bool PVEnabled=>System.Environment.GetEnvironmentVariable("SPIRE_NEXT_PV")=="1";
  internal static string PVDirectory=>System.Environment.GetEnvironmentVariable("SPIRE_PV_DIRECTORY")??@"D:\steam\steamapps\common\Terraria\SpireTerrariaNext\pv_new_era";
  internal static string PVTake=>System.Environment.GetEnvironmentVariable("SPIRE_PV_TAKE")??"take01";
  static string PVScope=>System.Environment.GetEnvironmentVariable("SPIRE_PV_SCOPE")??"smoke";
  static BattleConfiguration pvConfiguration;
  static HeroClass pvHero=HeroClass.Warrior;
  static bool pvPrepared;
  static int pvFixtureStage;
  internal static byte PVLastInput;
  static void PreparePV() {
   if(pvPrepared)return;pvPrepared=true;
   Directory.CreateDirectory(Path.Combine(PVDirectory,"footage"));
   Engine.MaxFps=SpirePvCapture.Fps;
   DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
   DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
   bool smoke=PVScope=="smoke";
   DisplayServer.WindowSetSize(new Vector2I(smoke?1280:1920,smoke?720:1080));
   var harmony=new Harmony("ExiMisto.SpireNext.PVMemoryFixture");
   harmony.Patch(AccessTools.Method(typeof(AdventureSession),"BuildConfiguration"),postfix:new HarmonyMethod(typeof(SpireSelfTest),nameof(PVConfiguration)));
   harmony.Patch(AccessTools.Method(typeof(AdventureSession),"UpdateStageAndHealth"),prefix:new HarmonyMethod(typeof(SpireSelfTest),nameof(PVKeepFixtureStage)));
   harmony.Patch(AccessTools.Method(typeof(AdventureHostUi),"Choose"),prefix:new HarmonyMethod(typeof(SpireSelfTest),nameof(PVBeforeNativeChoice)));
   Report["pvMode"]=new{scope=PVScope,take=PVTake,viewportOnly=true,simulationTicksPerFrame=2,videoFps=30,persistentSaveWrites=false};
  }
  static void PVConfiguration(ref BattleConfiguration __result) {
   if(!PVEnabled||pvConfiguration==null)return;
   __result=pvConfiguration;__result.Validate();
  }
  static bool PVKeepFixtureStage() {
   if(!PVEnabled||pvFixtureStage==0||AdventureSession.Current==null)return true;
   // These late-game shots reuse an isolated first-act native room fixture.
   // Keep its expedition stage and real host Creature health aligned with the
   // selected combat stage; never fake the health-bar label or alter real saves.
   var adventure=AdventureSession.Current;
   adventure.Stage=pvFixtureStage;adventure.LifeMax=300*pvFixtureStage;
   var creature=AdventureSession.HostRun?.Players.FirstOrDefault()?.Creature;
   if(HostIntegration.Active==null&&creature!=null)adventure.Life=Math.Clamp(creature.CurrentHp,0,adventure.LifeMax);
   return false;
  }
  static Dictionary<string,int> pvForgeMaterialsBefore,pvForgeMaterialsAfter;
  static string pvSelectedForgeKey;
  static int pvForgedWeaponSlot=-1;
  static void PVBeforeNativeChoice(string purpose,IReadOnlyList<AdventureHostUi.Choice> choices) {
   if(!PVEnabled||PVScope!="systems")return;
   var adventure=AdventureSession.Current;
   if(purpose.StartsWith("选择锻造配方")) {
    pvForgeMaterialsBefore=new Dictionary<string,int>(adventure.Materials);
    foreach(var choice in choices)if(choice.Value is ForgeRecipe recipe) {
     if(!recipe.Costs.All(cost=>adventure.Materials.TryGetValue(cost.Key,out int count)&&count>=cost.Value))throw new InvalidOperationException("PV forge recipe is not affordable.");
     var held=recipe.Costs.ToDictionary(cost=>cost.Key,cost=>adventure.Materials[cost.Key]);
     // This is a shorter presentation of the actual recipe and inventory values,
     // kept inside the native choice card for the filming fixture only.
     choice.Description="消耗："+AdventureRun.DescribeMaterials(recipe.Costs)+"\n持有："+AdventureRun.DescribeMaterials(held)+"\n随机锻造三件，选择一件带走。";
    }
    Report["pvForgeMaterialsBefore"]=pvForgeMaterialsBefore;
   }
   if(purpose.StartsWith("锻造完成")) {
    pvForgeMaterialsAfter=new Dictionary<string,int>(adventure.Materials);
    int selected=AdventureHostUi.TestChoices.Count>0?AdventureHostUi.TestChoices.Peek():0;
    var offer=choices[Math.Clamp(selected,0,choices.Count-1)].Value as RewardOffer;
    pvSelectedForgeKey=offer?.GearKey;
    if(pvSelectedForgeKey==null||GearCatalog.Get(pvSelectedForgeKey).Kind!=GearKind.Weapon)throw new InvalidOperationException("PV forge output is not a weapon.");
    Report["pvForgeMaterialsAfterConsumption"]=pvForgeMaterialsAfter;
    Report["pvForgeSelectedOutput"]=new{key=pvSelectedForgeKey,title=offer.Title,symbol=GearCatalog.Get(pvSelectedForgeKey).NativeSymbols[0]};
   }
  }
  static async Task CapturePVSnapshotClip(string filename) {
   if(SpirePvCapture.Active!=null) {
    var active=SpirePvCapture.Active;active.Bookmark(filename);
    if(active.IsBattleShot)return;
    string purpose=AdventureHostUi.SelectionPurpose??"";
    string forgePhase=purpose.StartsWith("选择锻造配方")?"forge-materials-and-recipe-ready":purpose.StartsWith("锻造完成")?"forge-consumed-and-new-weapons-ready":null;
    int start=active.Frames;
    if(forgePhase!=null)active.Bookmark(forgePhase);
    int until=active.Frames+66;
    while(active.Running&&active.Frames<until)await NextFrame();
    if(forgePhase!=null){active.Bookmark(forgePhase+"-hold-end");Report[forgePhase]=new{frame=start,heldFrames=active.Frames-start,seconds=(active.Frames-start)/30d,choicePurpose=purpose};}
    return;
   }
   bool character=filename.StartsWith("00-character-"),map=filename=="01-native-map.png",choice=filename.StartsWith("choice-");
   if(!character&&!map&&!choice)return;
   if(character&&PVScope!="menu"&&PVScope!="smoke")return;
   if(map&&(PVScope=="effects"||PVScope=="late"||PVScope=="moon"||PVScope.StartsWith("showcase-")))return;
   await PVClip(Path.GetFileNameWithoutExtension(filename),character?2.5:choice?2.2:3,
    character?"Native BaseLib character selection; actual animated game screen":map?"Native Slay the Spire route selection":"Native equipment/forge/affix choice",false);
  }
  static async Task PVClip(string name,double seconds,string description,bool fixture) {
   await using var clip=SpirePvCapture.Start(PVTake+"-"+name,description,fixture);
   while(clip.Running&&clip.Frames<(int)Math.Round(seconds*SpirePvCapture.Fps))await NextFrame();
  }
  internal static void PVSetViewportCursorAway() {
   if(!PVEnabled||NGame.Instance==null)return;
   var viewport=NGame.Instance.GetViewport();
   // A synthetic, viewport-local event changes Godot's hover location without
   // moving the OS cursor, touching another monitor or capturing the desktop.
   using var motion=new InputEventMouseMotion{Position=new Vector2(8,viewport.GetVisibleRect().Size.Y-8),GlobalPosition=new Vector2(8,viewport.GetVisibleRect().Size.Y-8)};
   viewport.PushInput(motion,true);
   NHoverTipSet.Clear();
  }
  static void ExportPVAssets() {
   string items=Path.Combine(PVDirectory,"assets","items"),characters=Path.Combine(PVDirectory,"assets","characters"),catalog=Path.Combine(PVDirectory,"assets","catalog"),icons=Path.Combine(catalog,"icons");
   Directory.CreateDirectory(items);Directory.CreateDirectory(characters);Directory.CreateDirectory(icons);
   var symbols=new[]{"EnchantedSword","GoldBow","WaterBolt","BabyBirdStaff","TerraBlade","LastPrism","IronAnvil","IronOre","CrystalShard","SoulofNight","FishronWings","HermesBoots","CloudinaBottle","BandofRegeneration","HealingPotion","ManaPotion","Grenade","GoldCoin","LunarFlareBook","VampireKnives","Megashark","RainbowCrystalStaff","Bone","Silk","StarWrath","TrueNightsEdge"};
   foreach(string symbol in symbols)PVExportIcon(symbol,Path.Combine(items,symbol+".png"));
   foreach(HeroClass hero in Enum.GetValues<HeroClass>()){var texture=AdventureHostUi.CharacterScene(hero);if(texture==null)continue;using var image=texture.GetImage();if(image!=null&&!image.IsEmpty())image.SavePng(Path.Combine(characters,hero+".png"));}
   // Keep the original RGBA textures and dimensions. All equipment symbols
   // have already been loaded for the native inventory and choice screens.
   var iconPaths=new Dictionary<string,string>();var missing=new List<string>();
   foreach(string symbol in GearCatalog.All.SelectMany(gear=>gear.NativeSymbols).Distinct()) {
    string path=Path.GetFullPath(Path.Combine(icons,symbol+".png"));
    if(PVExportIcon(symbol,path))iconPaths[symbol]=path;else missing.Add(symbol);
   }
   var entries=GearCatalog.All.Select(gear=>new{key=gear.Key,name=gear.Name,kind=gear.Kind.ToString(),hero=gear.Hero?.ToString(),stage=gear.Stage,rarity=gear.Rarity.ToString(),nativeSymbols=gear.NativeSymbols,iconPath=iconPaths.TryGetValue(gear.NativeSymbols[0],out string path)?path:null}).ToArray();
   var jsonOptions=new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
   File.WriteAllText(Path.Combine(catalog,"catalog.json"),JsonSerializer.Serialize(entries,jsonOptions));
   // The model has five material families in each of three acts. These are
   // the actual fifteen resource keys, sharing the five existing UI textures.
   var materialFamilies=new[]{(key:"ore",symbol:"IronOre"),(key:"weave",symbol:"Silk"),(key:"bone",symbol:"Bone"),(key:"crystal",symbol:"CrystalShard"),(key:"essence",symbol:"SoulofNight")};
   var materialIcons=new Dictionary<string,string>();
   foreach(var material in materialFamilies){string path=Path.GetFullPath(Path.Combine(icons,material.symbol+".png"));if(PVExportIcon(material.symbol,path))materialIcons[material.symbol]=path;else missing.Add(material.symbol);}
   var materials=Enumerable.Range(1,3).SelectMany(stage=>materialFamilies.Select(material=>new{key=stage+":"+material.key,name=GearCatalog.MaterialName(stage+":"+material.key),stage,nativeSymbol=material.symbol,iconPath=materialIcons.TryGetValue(material.symbol,out string path)?path:null})).ToArray();
   File.WriteAllText(Path.Combine(catalog,"materials.json"),JsonSerializer.Serialize(materials,jsonOptions));
   Report["pvLocalArtExported"]=new{items=Directory.GetFiles(items,"*.png").Length,characters=Directory.GetFiles(characters,"*.png").Length,catalogEntries=entries.Length,catalogIcons=iconPaths.Count,materials=materials.Length,missingCatalogSymbols=missing.Distinct().ToArray(),catalogPath=Path.GetFullPath(Path.Combine(catalog,"catalog.json"))};
  }
  static bool PVExportIcon(string symbol,string path) {
   var texture=AdventureHostUi.ItemIcon(symbol);if(texture==null)return false;
   using var image=texture.GetImage();return image!=null&&!image.IsEmpty()&&image.SavePng(path)==Error.Ok;
  }
  static async Task RunPVAdventure(RunState run) {
   ExportPVAssets();
   if(PVScope=="menu"){await PVCleanRun();return;}
   if(PVScope=="smoke") {
    await PVEnterMapBattle(run);
    await PVClip("starter-battle-smoke",8,"Real starter warrior battle; capture timing validation, no staged victory",false);
    await PVCleanRun();return;
   }
   if(PVScope=="systems") {
    await PVEnterMapBattle(run);
    var battle=HostIntegration.Active;
    await using(var clip=SpirePvCapture.Start(PVTake+"-starter-battle","Starter warrior with native equipment and full HUD; natural victory only",false)) {
     var timeout=Stopwatch.StartNew();
     while(clip.Running&&!CombatCompleted.Task.IsCompleted&&timeout.Elapsed.TotalSeconds<150)await NextFrame();
     if(!await CombatCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30)))throw new InvalidOperationException("PV starter encounter did not naturally win.");
    }
    NRewardsScreen rewards=null;
    await WaitUntil(()=>{rewards=FindVisibleRewards(GetTree().Root);return rewards!=null&&IsRendered(rewards)&&TransitionFinished();},"PV actual rewards",30);
    await PVClip("battle-rewards",3,"Actual reward screen produced by natural Terraria victory",false);
    AdventureHostUi.TestChoices.Enqueue(0);
    await (AdventureHostUi.LastEquipmentReward??throw new InvalidOperationException("No equipment reward")).SelectUnsynchronized().WaitAsync(TimeSpan.FromSeconds(25));
    typeof(NRewardsScreen).GetMethod("OnProceedButtonPressed",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(rewards,new object[]{null});
    await WaitForMap("PV return map");
    await using(var systems=SpirePvCapture.Start(PVTake+"-growth-systems","Native camp forge/affix, equipment shop, accessories, potions, chest and events. Test fixture supplies materials and shop gold; real UI and item transactions.",true))await RunAdventureRooms(run);
    // Keep the exact item identities and levels from the preceding actual UI
    // transactions, then select the newly acquired weapon for the next fight.
    NMapScreen.Instance.Close(false);MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Clear();
    pvConfiguration=null;ResetCombat();
    var followup=AdventureSession.BuildConfiguration();followup.EnemyCount=32;followup.MaxConcurrent=6;pvConfiguration=followup;
    PVUseNewestWeapon=true;
    pvForgedWeaponSlot=AdventureSession.Current.Weapons.FindIndex(x=>x.InstanceId==PVForgedWeaponId);
    if(pvForgedWeaponSlot<0||AdventureSession.Current.Weapons[pvForgedWeaponSlot].DefinitionKey!=pvSelectedForgeKey)throw new InvalidOperationException("The forged weapon instance was lost before its follow-up battle.");
    if(followup.Weapons[pvForgedWeaponSlot]!=GearCatalog.Get(pvSelectedForgeKey).NativeSymbols[0])throw new InvalidOperationException("The follow-up worker loadout differs from the selected forged weapon.");
    await PVEnterFixtureCombat(run,"forged-weapon-followup");
    await WaitUntil(()=>HostIntegration.Active?.Ready==true,"PV newly forged weapon ready",30);
    Report["pvGrowthFollowupWeapons"]=HostIntegration.Active.Configuration.Weapons;
    Report["pvGrowthFollowupLevels"]=HostIntegration.Active.Configuration.WeaponLevels;
    Report["pvGrowthFollowupForgedInstance"]=PVForgedWeaponId??"";
    Report["pvGrowthFollowupSelectedSlot"]=pvForgedWeaponSlot;
    await PVClip("forged-weapon-in-use",12,"The exact equipment obtained and forged in the preceding native UI, carried into native combat; memory fixture raises wave count to 32 so the newly acquired weapon can be observed",true);
    PVUseNewestWeapon=false;
    pvConfiguration=null;
    await PVCleanRun();return;
   }
   if(PVScope=="classes") {
    foreach(HeroClass hero in Enum.GetValues<HeroClass>()) {
     pvHero=hero;
     string weapon=hero==HeroClass.Warrior?"EnchantedSword":hero==HeroClass.Ranger?"Minishark":hero==HeroClass.Mage?"WaterBolt":"ImpStaff";
     await PVFixtureBattle(run,hero,1,"normal","",weapon,10,40,false);
    }
    await PVCleanRun();return;
   }
   if(PVScope=="late") {
    await PVFixtureBattle(run,HeroClass.Warrior,2,"boss","SkeletronPrime","BeamSword",15,1,false);
    await PVFixtureBattle(run,HeroClass.Mage,3,"elite","Plantera","RazorbladeTyphoon",15,1,false);
    await PVFixtureBattle(run,HeroClass.Ranger,3,"boss","DukeFishron","Megashark",15,1,false);
    await PVCleanRun();return;
   }
   if(PVScope=="effects") {
    await PVFixtureBattle(run,HeroClass.Warrior,3,"boss","SkeletronPrime","TerraBlade",12,1,false);
    await PVFixtureBattle(run,HeroClass.Mage,3,"elite","Plantera","RazorbladeTyphoon",12,1,false);
    await PVFixtureBattle(run,HeroClass.Ranger,3,"boss","DukeFishron","Megashark",12,1,false);
    await PVFixtureBattle(run,HeroClass.Summoner,3,"elite","QueenSlimeBoss","TempestStaff",15,1,false);
    await PVCleanRun();return;
   }
   if(PVScope=="moon") {
    await PVFixtureBattle(run,HeroClass.Mage,3,"boss","MoonLordCore","RazorbladeTyphoon",60,1,false);
    await PVCleanRun();return;
   }
   if(PVScope=="showcase-a"||PVScope=="showcase-b"||PVScope=="showcase-c") {
    await PVShowcase(run);await PVCleanRun();return;
   }
   throw new InvalidOperationException("Unknown PV capture scope: "+PVScope);
  }
  static async Task PVShowcase(RunState run) {
   // The action library is divided into independent five-shot sessions so
   // worker startup and lossless frame transport fit each launcher's timeout.
   (string group,string id,HeroClass hero,int stage,string kind,string boss,string symbol,string scene,double seconds,string secondary,string action)[] shots={
    ("a","a01-meteor-outpost",HeroClass.Mage,2,"normal","","MeteorStaff","forest-outpost",8,null,"Overhead meteor rain through a dense forest-outpost encounter"),
    ("a","a02-stormbow-mine",HeroClass.Ranger,2,"normal","","DaedalusStormbow","mine",8,null,"Arrow rain across a dense mining encounter"),
    ("a","a03-ufo-altar",HeroClass.Summoner,3,"normal","","XenoStaff","corruption-altar",9,"RainbowWhip","UFO minion attacks followed by whip direction at a corruption altar"),
    ("a","a04-sentry-outpost",HeroClass.Summoner,3,"normal","","DD2FlameburstTowerT3Popper","forest-outpost",10,"SwordWhip","Grounded tier-three sentry placement, fireballs and whip attacks without tower-defense mode"),
    ("a","a05-shadowbeam-mine",HeroClass.Mage,3,"normal","","ShadowbeamStaff","mine",8,null,"Purple beams within the mine architecture"),
    ("b","b01-starfury-eye",HeroClass.Warrior,1,"boss","EyeofCthulhu","Starfury",null,9,null,"Starfury sword swings and falling stars at the Eye arena"),
    ("b","b02-sunfury-king",HeroClass.Warrior,1,"boss","KingSlime","Sunfury",null,9,null,"Flail swings and releases in the King Slime arena"),
    ("b","b03-nightedge-prime",HeroClass.Warrior,3,"boss","SkeletronPrime","TrueNightsEdge",null,9,null,"Purple melee arcs against the mechanical boss architecture"),
    ("b","b04-inferno-plantera",HeroClass.Mage,3,"elite","Plantera","InfernoFork",null,9,null,"Inferno bursts within Plantera's jungle temple"),
    ("b","b05-electrosphere-duke",HeroClass.Ranger,3,"boss","DukeFishron","ElectrosphereLauncher",null,9,null,"Electric projectile fields during a harbor boss chase"),
    ("c","c01-blizzard-altar",HeroClass.Mage,3,"normal","","BlizzardStaff","corruption-altar",8,null,"Falling ice shards over a dense corruption-altar encounter"),
    ("c","c02-chaingun-prime",HeroClass.Ranger,3,"boss","SkeletronPrime","ChainGun",null,9,null,"Continuous bullet fire against Prime's moving limbs"),
    ("c","c03-bubbles-mine",HeroClass.Mage,3,"normal","","BubbleGun","mine",8,null,"Close-range bubble clusters across the mine encounter"),
    ("c","c04-tiger-outpost",HeroClass.Summoner,3,"normal","","StormTigerStaff","forest-outpost",10,"ScytheWhip","Desert tiger leaps followed by whip attacks in the timber outpost"),
    ("c","c05-scourge-plantera",HeroClass.Warrior,3,"elite","Plantera","ScourgeoftheCorruptor",null,9,null,"Thrown corruptor javelins and seekers within the jungle temple")
   };
   string group=PVScope.Substring(PVScope.Length-1);
   var selected=shots.Where(shot=>shot.group==group).ToArray();
   var plan=selected.Select(shot=>new{shot.id,hero=shot.hero.ToString(),shot.stage,shot.kind,shot.boss,shot.symbol,scene=shot.scene??("boss-"+shot.boss),plannedSeconds=shot.seconds,secondaryWeapon=shot.secondary,expectedVisibleAction=shot.action,enemyCount=shot.kind=="normal"?40:0,maxConcurrent=shot.kind=="normal"?10:0,stagedLoadout=true,naturalAi=true,injectedDamage=false}).ToArray();
   Report["pvShowcasePlan"]=plan;
   File.WriteAllText(Path.Combine(PVDirectory,"footage","plan-"+PVTake+".json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
   foreach(var shot in selected)await PVFixtureBattle(run,shot.hero,shot.stage,shot.kind,shot.boss,shot.symbol,shot.seconds,40,false,shot.scene,shot.id,10,shot.secondary,shot.action);
  }
  static async Task PVEnterMapBattle(RunState run) {
   ResetCombat();NMapScreen.Instance.Close(false);
   var first=run.Map.GetAllMapPoints().Where(p=>p.PointType==MapPointType.Monster).OrderBy(p=>p.coord.row).ThenBy(p=>p.coord.col).First();
   await RunManager.Instance.EnterMapCoord(first.coord);
   await WaitUntil(()=>HostIntegration.Active?.Ready==true,"PV native battle ready",30);
  }
  static GearInstance PVItem(AdventureRun adventure,GearDefinition definition)=>new(){InstanceId="pv-gear-"+(++adventure.InstanceCounter),DefinitionKey=definition.Key};
  static int pvNativeReplayInitializations;
  static async Task PVEnterFixtureCombat(RunState run,string shotId) {
   var manager=RunManager.Instance;
   if(!PVEnabled||manager.ShouldSave||!ReferenceEquals(manager.DebugOnlyGetState(),run))throw new InvalidOperationException("PV combat entry requires its isolated, non-persistent native run.");
   // EnterRoom only attaches a room. It omits the replay snapshot normally
   // created by map travel, so native victory later cannot write its replay.
   // The game's own debug-room entry performs synchronization, history and
   // RecordInitialState(ToSave(null)) before starting the real combat room.
   await manager.EnterRoomDebug(RoomType.Monster,MapPointType.Monster,ModelDb.Encounter<AxebotsNormal>().ToMutable(),false);
   if(manager.CombatReplayWriter.IsEnabled&&!manager.CombatReplayWriter.IsRecordingReplay)throw new InvalidOperationException("Native PV room entry did not initialize its combat replay: "+shotId);
   Report["pvNativeReplayInitializations"]=++pvNativeReplayInitializations;
   Report["pv"+shotId+"Replay"]=new{entry="RunManager.EnterRoomDebug",nativeInitialStateRecorded=manager.CombatReplayWriter.IsRecordingReplay,replayEnabled=manager.CombatReplayWriter.IsEnabled,persistentRunWrites=manager.ShouldSave};
  }
  static async Task PVFixtureBattle(RunState run,HeroClass hero,int stage,string kind,string boss,string symbol,double seconds,int enemyCount,bool requireVictory,string sceneOverride=null,string shotId=null,int maxConcurrent=6,string secondaryWeapon=null,string expectedAction=null) {
   if(HostIntegration.Active!=null)await HostIntegration.Abort(HostIntegration.Active);
   NMapScreen.Instance.Close(false);MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Clear();
   var adventure=AdventureSession.Current;
   var weapon=GearCatalog.All.FirstOrDefault(x=>x.Kind==GearKind.Weapon&&x.NativeSymbols[0]==symbol)??throw new InvalidOperationException("PV weapon is absent from catalog: "+symbol);
   var armor=GearCatalog.All.Where(x=>x.Kind==GearKind.Armor&&x.Stage==stage&&GearCatalog.IsForHero(x,hero)).OrderByDescending(x=>x.Rarity).First();
   pvFixtureStage=stage;
   adventure.Stage=stage;adventure.LifeMax=stage*300;adventure.Life=adventure.LifeMax;
   adventure.Hero=hero;adventure.Weapons=new(){PVItem(adventure,weapon)};adventure.Armor=PVItem(adventure,armor);
   if(hero==HeroClass.Summoner){string whipSymbol=secondaryWeapon??(stage==3?"RainbowWhip":stage==2?"FireWhip":"BlandWhip");var whip=GearCatalog.All.First(x=>x.Kind==GearKind.Weapon&&x.NativeSymbols[0]==whipSymbol);adventure.Weapons.Add(PVItem(adventure,whip));}
   string[] extra=stage==3?new[]{"FishronWings","AvengerEmblem","StarVeil","WormScarf"}:Array.Empty<string>();
   foreach(string accessory in extra){var gear=GearCatalog.All.FirstOrDefault(x=>x.Kind==GearKind.Accessory&&x.NativeSymbols[0]==accessory);if(gear!=null&&!adventure.Accessories.Any(x=>x.DefinitionKey==gear.Key))adventure.Accessories.Add(PVItem(adventure,gear));}
   adventure.Life=adventure.LifeMax;AdventureSession.PushHostHealth();AdventureSession.Save();
   pvHero=hero;pvConfiguration=null;
   var config=AdventureSession.BuildConfiguration();
   config.Stage=stage;config.RoomKind=kind;config.BossSymbol=boss;config.SceneKey=kind=="normal"?(sceneOverride??"forest-outpost"):"boss-"+boss;
   var creature=run.Players[0].Creature;
   if(creature.MaxHp!=config.LifeMax||creature.CurrentHp!=config.Life||config.LifeMax!=stage*300)throw new InvalidOperationException("PV host health does not match its native battle configuration.");
   config.EnemyCount=kind=="normal"?enemyCount:0;config.MaxConcurrent=kind=="normal"?maxConcurrent:0;
   pvConfiguration=config;ResetCombat();var completion=CombatCompleted.Task;
   // Only this fixture redirects encounter configuration. Native NPC AI, damage,
   // projectile rendering and victory settlement are the production code paths.
   await PVEnterFixtureCombat(run,shotId??symbol);
   await WaitUntil(()=>HostIntegration.Active?.Ready==true,"PV fixture battle "+symbol,30);
   var live=HostIntegration.Active;int firstTick=live.State.Tick;
   if(live.State.LifeMax!=creature.MaxHp||!live.NativeHudUncovered||!live.HealthBarReadable)throw new InvalidOperationException("PV battle host/native health or visible HUD is inconsistent.");
   string reportId=shotId??(boss==""?symbol:boss);
   Report["pv"+reportId+"Hud"]=new{nativeTopBarVisible=live.NativeHudUncovered,healthReadable=live.HealthBarReadable,hostMaxHp=creature.MaxHp,nativeMaxHp=live.State.LifeMax,visibleAccessories=live.InventoryHud?.VisibleAccessoryCount,visiblePotions=live.InventoryHud?.VisiblePotionCount,plannedSeconds=seconds};
   await using(var clip=SpirePvCapture.Start(PVTake+"-"+(shotId??(hero+"-"+(boss==""?symbol:boss))),"Actual "+hero+" "+symbol+" combat against "+(boss==""?"stage "+stage+" waves":boss)+" in "+config.SceneKey+"; explicit memory-only loadout/encounter fixture, natural AI and damage"+(expectedAction==null?"":". Expected action to verify in the footage: "+expectedAction),true)) {
    while(clip.Running&&clip.Frames<seconds*SpirePvCapture.Fps&&HostIntegration.Active==live&&!live.State.Dead&&!completion.IsCompleted)await NextFrame();
   }
   // Complete clears Active before asynchronously executing native Win and
   // closing its worker. Starting the next fixture during that interval lets
   // the old completion signal land in the next fight's reset source.
   bool endedNatively=live.State.Dead||(live.State.Remaining==0&&live.State.Alive==0);
   bool naturallyWon=false;
   if(endedNatively||completion.IsCompleted) {
    naturallyWon=await completion.WaitAsync(TimeSpan.FromSeconds(45));
    await WaitUntil(()=>HostIntegration.Active==null&&BattleLayers()==0,"PV completed battle cleanup",20);
    if(naturallyWon)await WaitUntil(()=>FindVisibleRewards(GetTree().Root)!=null&&TransitionFinished(),"PV native victory rewards settled",20);
   }
   Report["pv"+reportId]=new{hero=hero.ToString(),symbol,boss,stage,scene=config.SceneKey,secondaryWeapon,expectedAction,firstTick,lastTick=live.State.Tick,naturallyWon,dead=live.State.Dead,hostMaxHp=creature.MaxHp,battleMaxHp=live.State.LifeMax,configurationMaxHp=config.LifeMax};
   if(requireVictory&&!naturallyWon)throw new InvalidOperationException("PV finale has no natural victory: "+boss+" tick "+live.State.Tick);
   if(HostIntegration.Active==live)await HostIntegration.Abort(live);
   await WaitUntil(()=>HostIntegration.Active==null&&BattleLayers()==0,"PV fixture worker fully detached",20);
   await NextFrame();
   pvConfiguration=null;
  }
  static async Task PVCleanRun() {
   pvConfiguration=null;
   if(HostIntegration.Active!=null)await HostIntegration.Abort(HostIntegration.Active);
   await NGame.Instance.ReturnToMainMenu().WaitAsync(TimeSpan.FromSeconds(30));
   await WaitUntil(()=>HostIntegration.Active==null&&NGame.Instance.MainMenu!=null&&TransitionFinished(),"PV cleanup",20);
   pvFixtureStage=0;
   Report["nativeRunCleanedUp"]=true;
  }
  internal static void PVControls(BattleOverlay battle,ref byte input,ref float aimX,ref float aimY,ref int weaponSlot,ref int consumable) {
   var state=battle.State;var config=battle.Configuration;if(state==null||config==null)return;
   float delta=state.EnemyX-state.PlayerX;
   input=(byte)(state.Tick%42<34?8:0);
   float desired=pvHero==HeroClass.Warrior?40:170;
   if(Math.Abs(delta)>desired+35)input|=(byte)(delta>0?2:1);
   else if(pvHero!=HeroClass.Warrior&&Math.Abs(delta)<desired-45)input|=(byte)(delta>0?1:2);
   if(state.Tick%150<24||state.EnemyY<state.PlayerY-80)input|=4;
   if(state.PlayerX<state.ArenaLeft+110)input=(byte)((input&~1)|2);
   if(state.PlayerX>state.ArenaRight-110)input=(byte)((input&~2)|1);
   aimX=state.EnemyX+20;aimY=state.EnemyY+20;
   if(PVScope.StartsWith("showcase-")&&pvHero==HeroClass.Summoner&&config.Weapons[0].StartsWith("DD2")&&state.Tick<=180) {
    // This is only filming input: place the real sentry on the arena floor,
    // then let its native targeting run while the player uses the whip.
    aimX=state.PlayerX+(delta>=0?80:-80);aimY=state.FloorY-16;input=(byte)(input&~4);
   }
   weaponSlot=pvHero==HeroClass.Summoner&&config.Weapons.Length>1&&state.Tick>180?1:0;
   if(PVUseNewestWeapon)weaponSlot=pvForgedWeaponSlot;
   if(state.Mana<25&&state.Tick%60==0)for(int i=0;i<config.Consumables.Length;i++)if(config.Consumables[i].Contains("ManaPotion")&&!state.ConsumedSlots.Contains(i)){consumable=i;break;}
   if(state.Life<state.LifeMax*.4&&state.PotionDelay==0)for(int i=0;i<config.Consumables.Length;i++)if(config.Consumables[i].Contains("HealingPotion")&&!state.ConsumedSlots.Contains(i)){consumable=i;break;}
   PVLastInput=input;
  }
  static bool PVUseNewestWeapon;
  internal static string PVForgedWeaponId;
  internal static void PVConfirmForgedWeapon(AdventureRun adventure,IEnumerable<string> beforeIds) {
   var prior=beforeIds.ToHashSet();var added=adventure.Weapons.Where(item=>!prior.Contains(item.InstanceId)).ToArray();
   if(added.Length!=1||added[0].DefinitionKey!=pvSelectedForgeKey)throw new InvalidOperationException("The native forge did not grant the selected exact new weapon instance.");
   PVForgedWeaponId=added[0].InstanceId;
   Report["pvForgedWeaponInstance"]=new{instanceId=PVForgedWeaponId,key=added[0].DefinitionKey,title=added[0].Definition.Name,symbol=added[0].Definition.NativeSymbols[0],level=added[0].Level};
   SpirePvCapture.Active?.Bookmark("forge-new-weapon-instance-obtained-"+PVForgedWeaponId);
  }
 }
}

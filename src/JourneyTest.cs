using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using TerrariaICEY.Bridge;
namespace SpireTerraria {
 internal static class JourneyTest {
  internal static bool Enabled;internal static volatile bool Strike;internal static int FixtureStrikes;
  static bool running,economyOnly;static int frames;static bool captured;
  static readonly List<string> checks=new List<string>();
  static readonly List<object> combatTrace=new List<object>();
  static string combatFailure;static bool autoPotionBusy;static int traceRound=-1,traceTick=-60;
  internal static void Tick(){
   if(running||NGame.Instance.MainMenu==null)return;if(++frames<120)return;
   string path=Path.Combine(Plugin.Root,"journey.request"),quick=Path.Combine(Plugin.Root,"economy.request");if(!File.Exists(path)&&!File.Exists(quick))return;economyOnly=File.Exists(quick);File.Delete(economyOnly?quick:path);running=true;Enabled=true;FixtureStrikes=0;Strike=false;checks.Clear();combatTrace.Clear();combatFailure=null;autoPotionBusy=false;traceRound=-1;traceTick=-60;_ = Run();
  }
  static async Task Until(Func<bool> check,string step,int limit=1800){var timer=System.Diagnostics.Stopwatch.StartNew();double seconds=Math.Clamp(limit/60d,15,90);for(int i=0;i<limit;i++){if(combatFailure!=null)throw new Exception(combatFailure);if(check())return;if(timer.Elapsed.TotalSeconds>seconds)break;await Plugin.NextFrames(1);}throw new TimeoutException("Journey: "+step+" after "+timer.Elapsed.TotalSeconds.ToString("F1")+"s");}
  static async Task Await(Task task,string step,int limit=1800){await Until(()=>task.IsCompleted,step,limit);await task;}
  static void Require(bool ok,string message){if(!ok)throw new Exception("Journey: "+message);checks.Add(message);Plugin.Log("JOURNEY PASS "+message);}
  static T Visible<T>() where T:Control=>Plugin.Descendants(((SceneTree)Engine.GetMainLoop()).Root).OfType<T>().FirstOrDefault(x=>x.IsVisibleInTree());
  static async Task<CardModel> DeckSelection(Task pending,bool cancel,string capture){
   await Until(()=>pending.IsCompleted||Visible<NDeckCardSelectScreen>()!=null,"deck replacement/forge selector");
   if(pending.IsCompleted){await pending;return null;}await Plugin.NextFrames(45);var screen=Visible<NDeckCardSelectScreen>();Plugin.Capture(capture);
   if(cancel){AccessTools.Method(typeof(NDeckCardSelectScreen),"CloseSelection").Invoke(screen,new object[]{null});await Until(()=>pending.IsCompleted,"cancel completes");await pending;return null;}
   var card=Plugin.Descendants(screen).OfType<NCardHolder>().Select(x=>x.CardModel).First(x=>x is TerraEquipment);
   AccessTools.Method(typeof(NDeckCardSelectScreen),"OnCardClicked").Invoke(screen,new object[]{card});await Plugin.NextFrames(25);
   AccessTools.Method(typeof(NDeckCardSelectScreen),"ConfirmSelection").Invoke(screen,new object[]{null});await Until(()=>pending.IsCompleted,"confirmed selection completes");await pending;await Plugin.NextFrames(30);return card;
  }
  static async Task Run(){
   int visited=0,rewards=0;bool keyboardCombat=false,keyboardMap=false,overlayPause=false,ancient=false,economy=false;int characters=0;string failure=null;
   try{
    var menu=NGame.Instance.MainMenu;var screen=menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();screen.InitializeSingleplayer();menu.SubmenuStack.Push(screen);await Plugin.NextFrames(90);
    var tree=(SceneTree)Engine.GetMainLoop();var buttons=Plugin.Descendants(screen).OfType<NCharacterSelectButton>().ToArray();
    characters=ModelDb.AllCharacters.OfType<Terrarian>().Count();
    foreach(var t in ModelDb.AllCharacters.OfType<Terrarian>()){
     var b=buttons.FirstOrDefault(x=>Equals(AccessTools.Property(typeof(NCharacterSelectButton),"Character")?.GetValue(x),t));
     if(b==null){b=buttons.FirstOrDefault(x=>Equals(AccessTools.Field(typeof(NCharacterSelectButton),"_character")?.GetValue(x),t));}
     if(b==null){Plugin.Log("BUTTON FIELDS "+string.Join(",",typeof(NCharacterSelectButton).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Select(x=>x.Name)));throw new Exception("Custom character button missing "+t.Hero);}
     b.Select();await Plugin.NextFrames(40);Plugin.Capture("validation-character-"+t.Hero+".png");
    }
    AccessTools.Method(typeof(NCharacterSelectScreen),"OnEmbarkPressed").Invoke(screen,new object[]{null});
    await Until(()=>Plugin.Demo&&RunManager.Instance.DebugOnlyGetState()?.Map!=null&&NGame.Instance.MainMenu==null,"run startup",3000);await Plugin.NextFrames(180);
    var initial=RunManager.Instance.EventSynchronizer.GetLocalEvent();await Until(()=>initial.CurrentOptions.Count>=3,"Terraria ancient options");
    int grenades=TerraEconomy.Stock[0],relicCount=initial.Owner.Relics.Count();Plugin.Capture("validation-ancient-options.png");RunManager.Instance.EventSynchronizer.ChooseLocalOption(2);await Until(()=>initial.IsFinished,"ancient supply applied");
    Require(TerraEconomy.Stock[0]==grenades+8&&initial.Owner.Relics.Count()==relicCount,"ancient choice grants actual Terraria supplies without vanilla relic");ancient=true;
    await Await(NEventRoom.Proceed(),"leave ancient event");await Plugin.NextFrames(100);Plugin.Capture("validation-native-map.png");
    keyboardMap=KeyboardIsolation.RemainingKeyBindings()>0&&!Plugin.Realtime;
    for(int n=0;n<(economyOnly?0:2);n++){
     var state=RunManager.Instance.DebugOnlyGetState();var choices=state.CurrentMapPoint?.Children??state.Map.StartingMapPoint.Children;
     var point=choices.OrderBy(x=>x.PointType==MapPointType.Monster?(x.Children.Any(y=>y.PointType==MapPointType.Monster)?-1:0):x.PointType==MapPointType.Elite?1:2).First();
     if(point.PointType!=MapPointType.Monster&&point.PointType!=MapPointType.Elite)throw new Exception("Fixture seed has no combat successor: "+point.PointType);
     captured=false;await Await(RunManager.Instance.EnterMapCoord(point.coord),"enter map combat");visited++;await Until(()=>Plugin.Realtime,"native room bind");
     keyboardCombat|=KeyboardIsolation.RemainingKeyBindings()==0;
     if(n==0){await Until(()=>Plugin.NativeTick>30,"native initialized");NMapScreen.Instance.Open(true);await Plugin.NextFrames(45);int before=Plugin.NativeTick;await Plugin.NextFrames(60);overlayPause=!Plugin.Realtime&&KeyboardIsolation.RemainingKeyBindings()>0&&Plugin.NativeTick==before;Plugin.Capture("validation-battle-map-overlay.png");NMapScreen.Instance.Close();await Until(()=>Plugin.Realtime,"combat resumes after map overlay");}
     await Until(()=>Plugin.Descendants(tree.Root).OfType<NRewardsScreen>().Any(x=>x.IsVisibleInTree()),"combat victory",5400);await Plugin.NextFrames(120);
     var rewardScreen=Plugin.Descendants(tree.Root).OfType<NRewardsScreen>().First(x=>x.IsVisibleInTree());Plugin.Capture("validation-native-rewards-"+n+".png");
     var set=(RewardsSet)AccessTools.Field(typeof(NRewardsScreen),"_rewardsSet").GetValue(rewardScreen);
     var gear=set.Rewards.OfType<CardReward>().First();var offered=gear.Cards.OfType<TerraEquipment>().ToArray();Require(offered.Length==3&&offered.Count(c=>c.Stat==0)==2&&offered.Count(c=>c.Stat==2)==1&&offered.Select(c=>c.GearKey).Distinct().Count()==3,"battle reward offers distinct two weapons and one armor");var select=RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(gear);
     await Until(()=>Plugin.Descendants(tree.Root).OfType<NCardRewardSelectionScreen>().Any(x=>x.IsVisibleInTree()),"gear selection");await Plugin.NextFrames(75);Plugin.Capture("validation-native-equipment-"+n+".png");
     var cardScreen=Plugin.Descendants(tree.Root).OfType<NCardRewardSelectionScreen>().First(x=>x.IsVisibleInTree());
     var holder=Plugin.Descendants(cardScreen).OfType<NCardHolder>().First(x=>x.CardModel is TerraEquipment e&&e.Stat==0);var beforeReward=state.Players.First().Deck.Cards.ToArray();AccessTools.Method(typeof(NCardRewardSelectionScreen),"SelectCard").Invoke(cardScreen,new object[]{holder});
     if(n==1){await DeckSelection(select,true,"validation-reward-replacement-cancel.png");Require(!gear.SuccessfullySelected&&set.Rewards.Contains(gear)&&state.Players.First().Deck.Cards.SequenceEqual(beforeReward),"cancel fourth reward weapon retains reward and old loadout");await Plugin.NextFrames(45);select=RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(gear);await Until(()=>Visible<NCardRewardSelectionScreen>()!=null,"cancelled equipment reward can reopen");await Plugin.NextFrames(45);cardScreen=Visible<NCardRewardSelectionScreen>();holder=Plugin.Descendants(cardScreen).OfType<NCardHolder>().First(x=>x.CardModel is TerraEquipment e&&e.Stat==0);AccessTools.Method(typeof(NCardRewardSelectionScreen),"SelectCard").Invoke(cardScreen,new object[]{holder});}
     await DeckSelection(select,false,"validation-reward-replacement-"+n+".png");Require(gear.SuccessfullySelected,"confirmed equipment reward is consumed once");rewards++;await Plugin.NextFrames(90);
     foreach(var reward in set.Rewards.Where(x=>!(x is CardReward)))await Await(RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward),"take room reward");
     AccessTools.Method(typeof(NRewardsScreen),"OnProceedButtonPressed").Invoke(rewardScreen,new object[]{null});await Until(()=>NMapScreen.Instance.IsOpen,"return to map");await Plugin.NextFrames(100);Plugin.Capture("validation-native-map-after-"+n+".png");
     keyboardMap&=KeyboardIsolation.RemainingKeyBindings()>0&&!Plugin.Realtime;
    }
    await Economy();economy=true;
   }catch(Exception e){failure=e.ToString();Plugin.Log("JOURNEY FAILED "+e);Plugin.Capture("validation-journey-error.png");}
   finally{
    var state=RunManager.Instance.DebugOnlyGetState();var result=new{passed=failure==null&&FixtureStrikes==0&&characters==4&&ancient&&economy&&(economyOnly||(visited==2&&rewards==2&&keyboardMap&&keyboardCombat&&overlayPause)),version="0.4",economyOnly,characters,visited,rewards,keyboardMap,keyboardCombat,overlayPause,ancient,economy,checks=checks.ToArray(),combatTrace=combatTrace.ToArray(),fixtureStrikes=FixtureStrikes,native=Plugin.Demo?Plugin.JourneySnapshot():null,shouldSave=RunManager.Instance.ShouldSave,deck=state?.Players.FirstOrDefault()?.Deck.Cards.Select(c=>c.Title).ToArray(),failure};
    File.WriteAllText(Path.Combine(Plugin.Root,economyOnly?"economy-result.json":"journey-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Plugin.Log("JOURNEY RESULT "+JsonSerializer.Serialize(result));
    combatFailure=null;Strike=false;Enabled=false;
    try{if(Plugin.Demo){var exit=Plugin.Exit();if(await Task.WhenAny(exit,Task.Delay(45000))==exit)await exit;else Plugin.Log("JOURNEY CLEANUP timeout waiting for main menu/native worker");}}catch(Exception cleanup){Plugin.Log("JOURNEY CLEANUP FAILED "+cleanup);}finally{running=false;}
   }
  }
  static async Task Economy(){
   var p=RunManager.Instance.DebugOnlyGetState().Players.First();await PlayerCmd.GainGold(1500,p);
   // A quick economy-only run still exercises the real fourth-weapon selector.
   while(p.Deck.Cards.OfType<TerraEquipment>().Count(x=>x.Stat==0)<3){var extra=TerraEconomy.Roll(p,1,false).First();await CardPileCmd.Add(TerraEconomy.Create(p,extra),PileType.Deck);}
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Shop,MapPointType.Shop,null,false),"enter merchant room");await Until(()=>NMerchantRoom.Instance?.Inventory?.Inventory!=null,"merchant inventory initializes");
   var room=NMerchantRoom.Instance;room.OpenInventory();await Plugin.NextFrames(90);var inv=room.Inventory.Inventory;
   Require(inv.CharacterCardEntries.Count==5&&inv.CharacterCardEntries.All(e=>e.CreationResult?.Card is TerraEquipment),"native shop has five weapon/armor goods");
   Require(inv.ColorlessCardEntries.Count==2&&inv.ColorlessCardEntries.All(e=>e.CreationResult?.Card is TerraThrowable),"native white-card goods are universal explosives");
   Require(inv.PotionEntries.Count==3&&inv.PotionEntries.All(e=>e.Model is TerraPotion)&&inv.RelicEntries.Count>0&&inv.RelicEntries.All(e=>e.Model is TerraAccessory),"native shop sells Terraria potions and accessories");
   Require(inv.CardRemovalEntry==null,"shop has no vanilla card-removal service");Plugin.Capture("validation-terraria-shop.png");
   var weapon=inv.CharacterCardEntries.First(e=>e.CreationResult?.Card is TerraEquipment gear&&gear.Stat==0);var incoming=weapon.CreationResult.Card;var before=p.Deck.Cards.ToArray();int gold=p.Gold;
   var canceled=weapon.OnTryPurchaseWrapper(inv);await DeckSelection(canceled,true,"validation-shop-replace-cancel.png");Require(!await canceled&&weapon.IsStocked&&p.Gold==gold&&p.Deck.Cards.SequenceEqual(before),"cancel fourth weapon retains gold stock and old loadout");
   int cost=weapon.Cost;var bought=weapon.OnTryPurchaseWrapper(inv);var replaced=await DeckSelection(bought,false,"validation-shop-replace-confirm.png");
   Require(await bought&&replaced!=null&&!p.Deck.Cards.Contains(replaced)&&p.Deck.Cards.Contains(incoming)&&p.Deck.Cards.OfType<TerraEquipment>().Count(x=>x.Stat==0)==3&&p.Gold==gold-cost,"buy fourth weapon replaces exactly one and charges once");
   var armor=inv.CharacterCardEntries.First(e=>e.CreationResult?.Card is TerraEquipment gear&&gear.Stat==2);var armorIncoming=armor.CreationResult.Card;var armorBuy=armor.OnTryPurchaseWrapper(inv);await DeckSelection(armorBuy,false,"validation-shop-armor-replace.png");
   Require(await armorBuy&&p.Deck.Cards.Contains(armorIncoming)&&p.Deck.Cards.OfType<TerraEquipment>().Count(x=>x.Stat==2)==1,"buy armor replaces whole set");
   var explosives=inv.ColorlessCardEntries.First(e=>e.CreationResult?.Card is TerraThrowable);var explosive=(TerraThrowable)explosives.CreationResult.Card;int index=explosive.ItemId==168?0:explosive.ItemId==166?1:2;int quantity=TerraEconomy.Stock[index],deckCount=p.Deck.Cards.Count;
   Require(await explosives.OnTryPurchaseWrapper(inv)&&TerraEconomy.Stock[index]==quantity+explosive.Quantity&&p.Deck.Cards.Count==deckCount,"buy universal explosive increases stock without using a weapon slot");
   if(!p.HasOpenPotionSlots)await PotionCmd.Discard(p.Potions.First());int potions=p.Potions.Count();Require(await inv.PotionEntries.First().OnTryPurchaseWrapper(inv)&&p.Potions.Count()==potions+1&&p.Potions.All(x=>x is TerraPotion),"buy potion uses original potion bar");
   int relics=p.Relics.Count();Require(await inv.RelicEntries.First().OnTryPurchaseWrapper(inv)&&p.Relics.Count()==relics+1&&p.Relics.All(x=>x is TerraAccessory),"buy accessory uses original relic bar");Plugin.Capture("validation-terraria-shop-after.png");
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Treasure,MapPointType.Treasure,null,false),"enter treasure room");await Until(()=>Visible<NTreasureRoom>()!=null,"treasure room opens");await Plugin.NextFrames(60);
   var sync=RunManager.Instance.TreasureRoomRelicSynchronizer;Require(sync.CurrentRelics!=null&&sync.CurrentRelics.Count>0&&sync.CurrentRelics.All(x=>x is TerraAccessory),"chest contains Terraria accessories");
   var chest=Visible<NTreasureRoom>();relics=p.Relics.Count();bool extraRewards=false;var open=(Task)AccessTools.Method(typeof(NTreasureRoom),"OpenChest").Invoke(chest,null);
   await Until(()=>{extraRewards|=Visible<NRewardsScreen>()!=null;return (bool)AccessTools.Field(typeof(NTreasureRoom),"_isRelicCollectionOpen").GetValue(chest);},"chest uses native accessory picker");await Plugin.NextFrames(120);Plugin.Capture("validation-terraria-chest.png");sync.PickRelicLocally(0);
   await Until(()=>{extraRewards|=Visible<NRewardsScreen>()!=null;return open.IsCompleted;},"chest collection completes");await open;
   Require(!extraRewards&&p.Relics.Count()==relics+1&&p.Relics.All(x=>x is TerraAccessory),"opening chest awards one accessory without combat rewards page");
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Event,MapPointType.Unknown,ModelDb.Event<TerraCacheEvent>(),false),"enter cache event");var cache=RunManager.Instance.EventSynchronizer.GetLocalEvent();await Until(()=>cache.CurrentOptions.Count>=2,"Terraria cache event choices");await Plugin.NextFrames(120);quantity=TerraEconomy.Stock[0];int bombs=TerraEconomy.Stock[1];relics=p.Relics.Count();Plugin.Capture("validation-terraria-event.png");RunManager.Instance.EventSynchronizer.ChooseLocalOption(1);await Until(()=>cache.IsFinished,"cache supplies applied");Require(TerraEconomy.Stock[0]==quantity+8&&TerraEconomy.Stock[1]==bombs+5&&p.Relics.Count()==relics,"event choices grant actual native explosive stock");
   await Await(RunManager.Instance.EnterRoomDebug(RoomType.Event,MapPointType.Unknown,ModelDb.Event<TerraWorkshopEvent>(),false),"enter workshop event");var workshop=RunManager.Instance.EventSynchronizer.GetLocalEvent();await Until(()=>workshop.CurrentOptions.Count>0,"workshop options");var levels=p.Deck.Cards.OfType<TerraEquipment>().ToDictionary(x=>x,x=>x.CurrentUpgradeLevel);RunManager.Instance.EventSynchronizer.ChooseLocalOption(0);
   await Until(()=>Visible<NDeckCardSelectScreen>()!=null,"workshop equipment selector");var forged=await DeckSelection(RunManager.Instance.EventSynchronizer.AwaitPendingOptionTasks(),false,"validation-terraria-forge.png");await Until(()=>workshop.IsFinished,"forge applies to selected equipment");Require(forged is TerraEquipment&&levels.All(pair=>pair.Key.CurrentUpgradeLevel==pair.Value+(ReferenceEquals(pair.Key,forged)?1:0)),"workshop forges only chosen equipment by one level");
   Require(!Plugin.Realtime&&KeyboardIsolation.RemainingKeyBindings()>0,"shop chest and event restore original UI input");
  }
  static async Task AutoPotion(int kind){
   autoPotionBusy=true;try{var p=RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault();var potion=p?.Potions.OfType<TerraPotion>().FirstOrDefault(x=>x.Kind==kind);if(potion!=null&&Plugin.PotionReady(kind)){Plugin.Log("JOURNEY actual owned potion kind="+kind);await potion.OnUseWrapper(new ThrowingPlayerChoiceContext(),p.Creature);}}catch(Exception e){combatFailure="Journey owned potion failed: "+e.Message;}finally{autoPotionBusy=false;}
  }
  internal static void Control(EngineInput input,BattleState battle,EngineOutput output,float clock){
   if(!Enabled)return;
   if(traceRound!=battle.RoundId){traceRound=battle.RoundId;traceTick=-60;Plugin.Log("JOURNEY combat loadout round="+battle.RoundId+" ids="+string.Join(",",output.InventoryIds.Take(3))+" minions="+battle.MinionCount+" enemies="+string.Join(",",battle.Enemies.Select(n=>n.Type)));}
   if(battle.Tick-traceTick>=30||battle.Lost||battle.Won){traceTick=battle.Tick;if(combatTrace.Count<400)combatTrace.Add(new{round=battle.RoundId,tick=battle.Tick,life=output.Life,maxLife=output.MaxLife,damage=battle.Damage,minions=battle.MinionCount,ids=output.InventoryIds.Take(3).ToArray(),enemies=battle.Enemies.Select(n=>new{n.Type,n.Life}).ToArray()});}
   if(battle.Lost||output.Dead||output.Life<=0){combatFailure="Journey: native defeat round="+battle.RoundId+" tick="+battle.Tick+" life="+output.Life+" damage="+battle.Damage;return;}
   if(input.Paused||battle.Won)return;
   var p=RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault();var hero=(p?.Character as Terrarian)?.Hero??HeroClass.Warrior;int slot=0;
   if(hero==HeroClass.Summoner){int[] whips={4672,4913,5074,4912,4911,4678,4679,4914};slot=Array.FindIndex(output.InventoryIds,0,3,id=>whips.Contains(id));if(slot<0)slot=0;}
   input.Selected=slot;
   var target=battle.Enemies.OrderBy(n=>Math.Abs(n.X+n.Width*.5f-output.X)).FirstOrDefault();
   if(target!=null){
    float distance=target.X+target.Width*.5f-output.X,range=hero==HeroClass.Warrior?60:hero==HeroClass.Summoner?125:210;bool near=Math.Abs(distance)<range-30,far=Math.Abs(distance)>range+20;
    input.Right=far&&distance>0||near&&distance<0;input.Left=far&&distance<0||near&&distance>0;
    if(output.X<input.MinX+75&&input.Left){input.Left=false;input.Right=true;}if(output.X>input.MaxX-75&&input.Right){input.Right=false;input.Left=true;}
    input.MouseX=target.X+target.Width*.5f;input.MouseY=target.Y+target.Height*.5f;
    // Hold long enough to finish the animation, then release for several native
    // ticks so non-auto-reuse whips and swords receive a fresh click at 30 FPS.
    int cycle=Math.Max(18,output.HeldUseTime+4);input.Use=battle.Tick%cycle<cycle-4;
    if(hero==HeroClass.Summoner&&!new[]{4672,4913,5074,4912,4911,4678,4679,4914}.Contains(output.InventoryIds[slot]))input.Use=battle.MinionCount==0&&battle.Tick%90<4;
    input.Jump=battle.Tick%90<28;
   }
   if(!autoPotionBusy&&output.MaxLife-output.Life>=100&&Plugin.PotionReady(0))_ = AutoPotion(0);
   else if(!autoPotionBusy&&hero==HeroClass.Mage&&output.Mana<Math.Max(20,output.HeldMana*2)&&Plugin.PotionReady(1))_ = AutoPotion(1);
   if(clock>3&&!captured){captured=true;Plugin.Capture("validation-native-battle-"+battle.RoundId+".png");}
  }
 }
}

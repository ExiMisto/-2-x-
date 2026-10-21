using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpireTerraria.Next.Host {
 public static class AdventureModelTests {
  static int assertions;
  static void Check(bool condition,string message){assertions++;if(!condition)throw new InvalidOperationException("Adventure check failed: "+message);}
  static AdventureRun RoundTrip(AdventureRun run)=>AdventureRun.Deserialize(run.Serialize());
  static void Fund(AdventureRun run){for(int stage=1;stage<=3;stage++)foreach(string name in new[]{"ore","weave","bone","crystal","essence"})run.Materials[stage+":"+name]=100;}
  public static string Run(string nativeSymbolsPath) {
   assertions=0;
   var native=new HashSet<string>(File.ReadAllLines(nativeSymbolsPath),StringComparer.Ordinal);
   var missing=GearCatalog.All.SelectMany(x=>x.NativeSymbols).Distinct().Where(x=>!native.Contains(x)).ToArray();
   Check(missing.Length==0,"Native item symbols exist: "+string.Join(", ",missing));
   Check(GearCatalog.All.Count>200,"Expanded equipment pool");
   Check(GearCatalog.All.Select(x=>x.Key).Distinct().Count()==GearCatalog.All.Count,"Unique definition keys");
   Check(GearCatalog.All.Where(x=>x.Kind==GearKind.Weapon).GroupBy(x=>x.NativeSymbols[0]).All(x=>x.Count()==1),"Weapon pool contains no renamed duplicate native weapon");
   Check(GearCatalog.For(HeroClass.Summoner,1,GearKind.Weapon).Any(x=>x.NativeSymbols[0]=="DD2BallistraTowerT1Popper")&&!GearCatalog.For(HeroClass.Summoner,2,GearKind.Weapon).Any(x=>x.NativeSymbols[0]=="DD2BallistraTowerT1Popper"),"Tier-one Old One sentries are pre-hardmode");
   foreach(string symbol in new[]{"DD2BallistraTowerT3Popper","DD2FlameburstTowerT3Popper","DD2ExplosiveTrapT3Popper","DD2LightningAuraT3Popper"}) {
    var gear=GearCatalog.Get("weapon:Summoner:3:"+symbol);
    Check(gear.Kind==GearKind.Weapon&&gear.Rarity==GearRarity.Rare&&!gear.IsStageEntry&&gear.NativeSymbols.SequenceEqual(new[]{symbol}),"Recovered tier-three sentry is act-three rare equipment: "+symbol);
    Check(GearCatalog.For(HeroClass.Summoner,3,GearKind.Weapon).Contains(gear)&&!GearCatalog.For(HeroClass.Summoner,2,GearKind.Weapon).Contains(gear)&&!GearCatalog.For(HeroClass.Summoner,1,GearKind.Weapon).Contains(gear),"Tier-three sentry stays in its native progression stage: "+symbol);
   }
   Check(GearCatalog.All.Where(x=>x.Kind==GearKind.Armor).All(x=>x.NativeSymbols.Length==3),"Armor sets have three native pieces");
   var recoveredArmor=new[]{
    (HeroClass.Warrior,"squire","SquireGreatHelm SquirePlating SquireGreaves",GearRarity.Common),
    (HeroClass.Warrior,"valhalla","SquireAltHead SquireAltShirt SquireAltPants",GearRarity.Rare),
    (HeroClass.Ranger,"huntress","HuntressWig HuntressJerkin HuntressPants",GearRarity.Common),
    (HeroClass.Ranger,"red-riding","HuntressAltHead HuntressAltShirt HuntressAltPants",GearRarity.Rare),
    (HeroClass.Mage,"apprentice","ApprenticeHat ApprenticeRobe ApprenticeTrousers",GearRarity.Common),
    (HeroClass.Mage,"dark-artist","ApprenticeAltHead ApprenticeAltShirt ApprenticeAltPants",GearRarity.Rare),
    (HeroClass.Summoner,"monk","MonkBrows MonkShirt MonkPants",GearRarity.Common),
    (HeroClass.Summoner,"shinobi","MonkAltHead MonkAltShirt MonkAltPants",GearRarity.Rare)
   };
   foreach(var (owner,key,pieces,rarity) in recoveredArmor) {
    var gear=GearCatalog.Get("armor:"+owner+":3:"+key);
    Check(gear.NativeSymbols.SequenceEqual(pieces.Split(' '))&&gear.Rarity==rarity,"Recovered Tavernkeeper set uses its three correct native pieces: "+key);
    foreach(HeroClass hero in Enum.GetValues<HeroClass>()) {
     Check(GearCatalog.For(hero,3,GearKind.Armor).Contains(gear)==(hero==owner),"Tavernkeeper armor stays in its class pool: "+key+"/"+hero);
     Check(!GearCatalog.For(hero,1,GearKind.Armor).Contains(gear)&&!GearCatalog.For(hero,2,GearKind.Armor).Contains(gear),"Tavernkeeper armor cannot drop before act three: "+key);
    }
   }
   var oldAccessories="HermesBoots CloudinaBottle BandofRegeneration WarriorEmblem RangerEmblem SorcererEmblem SummonerEmblem Shackle Aglet AnkletoftheWind SharkToothNecklace AvengerEmblem ApprenticeScarf SquireShield HuntressBuckler MonkBelt CobaltShield ObsidianShield SpectreBoots FeralClaws TitanGlove ManaRegenerationBand MagicCuffs ManaFlower PygmyNecklace CrossNecklace StarVeil MoonCharm".Split(' ');
   Check(oldAccessories.All(symbol=>GearCatalog.TryGet("accessory:"+symbol,out var gear)&&gear.Kind==GearKind.Accessory&&gear.NativeSymbols.SequenceEqual(new[]{symbol})),"All 28 Classic accessories have real native definitions");
   var accessories=GearCatalog.All.Where(x=>x.Kind==GearKind.Accessory).ToArray();
   Check(accessories.Length==48,"Description recovery preserves the existing accessory catalog");
   Check(accessories.All(x=>!string.IsNullOrWhiteSpace(x.Description)&&x.Description.Contains("每种只保留一件")&&!x.Description.Contains("来自泰拉瑞亚对应敌人或宝箱的饰品")),"Every accessory has a concrete reusable effect description rather than the old source-only placeholder");
   Check(accessories.Select(x=>x.Description.Split(" 来源：")[0]).Distinct().Count()==45,"Only the four mechanically equivalent Tavernkeeper accessories share an effect description");
   Check(GearCatalog.Get("accessory:ManaFlower").Description.Contains("暂未接入自动喝药")&&GearCatalog.Get("accessory:RoyalGel").Description.Contains("仍须击败房内目标"),"Descriptions disclose custom potion-slot and mandatory encounter-clear limits");
   foreach(HeroClass hero in Enum.GetValues<HeroClass>()) {
    var emblem=GearCatalog.Get("accessory:"+new[]{"WarriorEmblem","RangerEmblem","SorcererEmblem","SummonerEmblem"}[(int)hero]);
    Check(emblem.Stage==2&&GearCatalog.IsForHero(emblem,hero),"Class emblem starts in hardmode: "+hero);
    Check(Enum.GetValues<HeroClass>().Where(other=>other!=hero).All(other=>!GearCatalog.IsForHero(emblem,other)),"Class emblem excluded from other classes: "+hero);
    foreach(string symbol in new[]{"FeralClaws","TitanGlove"})Check(GearCatalog.IsForHero(GearCatalog.Get("accessory:"+symbol),hero)==(hero==HeroClass.Warrior||hero==HeroClass.Summoner),"Melee and whip accessories serve both relevant classes: "+symbol+"/"+hero);
    foreach(string symbol in new[]{"ManaRegenerationBand","MagicCuffs","ManaFlower"})Check(GearCatalog.IsForHero(GearCatalog.Get("accessory:"+symbol),hero)==(hero==HeroClass.Mage),"Mana accessory stays with mage: "+symbol+"/"+hero);
    foreach(string symbol in new[]{"ApprenticeScarf","SquireShield","HuntressBuckler","MonkBelt","PygmyNecklace"})Check(GearCatalog.IsForHero(GearCatalog.Get("accessory:"+symbol),hero)==(hero==HeroClass.Summoner),"Native summon accessory stays with summoner: "+symbol+"/"+hero);
    var acquisition=AdventureRun.Create(93,hero);const string accessoryNode="class-accessory";var foreign=new RewardOffer{NodeKey=accessoryNode,OfferId="foreign",GearKey="accessory:"+(hero==HeroClass.Mage?"WarriorEmblem":"SorcererEmblem")};acquisition.RewardNodes[accessoryNode]=new List<RewardOffer>{foreign};
    int count=acquisition.Accessories.Count;Check(!acquisition.Accept(foreign)&&acquisition.Accessories.Count==count&&!acquisition.ClaimedNodes.Contains(accessoryNode),"Wrong-class reward cannot bypass acquisition guard: "+hero);
    foreign.GearKey="accessory:FeralClaws";bool allowed=hero==HeroClass.Warrior||hero==HeroClass.Summoner;Check(acquisition.Accept(foreign)==allowed&&acquisition.Accessories.Count==count+(allowed?1:0),"Shared melee accessory acquisition honors both classes: "+hero);
    var resumedAccessory=RoundTrip(acquisition);Check(resumedAccessory.Accessories.Select(g=>g.DefinitionKey).SequenceEqual(acquisition.Accessories.Select(g=>g.DefinitionKey)),"Recovered accessory definition keys remain save-compatible: "+hero);
   }
   foreach(HeroClass hero in Enum.GetValues<HeroClass>())for(int stage=1;stage<=3;stage++) {
    Check(GearCatalog.For(hero,stage,GearKind.Weapon).Count()>=(hero==HeroClass.Summoner&&stage==2?7:9),hero+" stage "+stage+" weapon variety");
    Check(GearCatalog.For(hero,stage,GearKind.Armor).Count()>=2,hero+" stage "+stage+" armor variety");
    var run=AdventureRun.Create(12345,hero);run.EnterStage(stage);
    foreach(var scope in run.ForgeCandidates().Where(x=>x.Unlocked))Check(scope.PossibleGearKeys.Count>=3,"Each unlocked forge scope has three distinct possible products");
    var seen=new HashSet<string>();
    for(int room=0;room<30;room++) {
     var offers=run.Rewards("room-"+room,RewardKind.Normal);
     Check(offers.Count==3,"Equipment and material choice");
     Check(offers.Count(x=>x.GearKey!="")==2&&offers.Count(x=>x.Materials.Count>0)==1,"Two equipment alternatives and one material pack");
     foreach(var offer in offers.Where(x=>x.GearKey!="")){var gear=GearCatalog.Get(offer.GearKey);Check(GearCatalog.IsForHero(gear,hero)&&gear.Stage==stage,"No wrong class or stage rewards");seen.Add(gear.Key);}
     Check(run.Accept(offers[2]),"Material option claims");
     Check(!run.Accept(offers[0]),"Claiming a second option is rejected");
    }
    Check(seen.Count>=10,"Thirty rooms show diverse rewards");
   }
   var a=AdventureRun.Create(2718,HeroClass.Warrior);var b=AdventureRun.Create(2718,HeroClass.Warrior);
   Check(JsonSerializer.Serialize(a.Rewards("fixed",RewardKind.Elite))==JsonSerializer.Serialize(b.Rewards("fixed",RewardKind.Elite)),"Same seed and route produce identical choices");
   var snapshot=a.Serialize();var loaded=AdventureRun.Deserialize(snapshot);Check(JsonSerializer.Serialize(a.Rewards("fixed",RewardKind.Elite))==JsonSerializer.Serialize(loaded.Rewards("fixed",RewardKind.Elite)),"Re-entering reward retains choices");
   Check(a.Accept(a.Rewards("fixed",RewardKind.Elite)[2]),"Accept after reopen");Check(!RoundTrip(a).Accept(a.Rewards("fixed",RewardKind.Elite)[2]),"Claim persists after save/load");
   a.Life=210;a.EnterStage(2);Check(a.LifeMax==600&&a.Life==510,"Act two adds initial 300 HP rather than multiplying current HP");a.EnterStage(2);Check(a.Life==510,"Re-entering act does not heal again");a.EnterStage(3);Check(a.LifeMax==900&&a.Life==810,"Act three baseline is 900");
   var camp=AdventureRun.Create(42,HeroClass.Mage);Fund(camp);var original=camp.Weapons[0];
   Check(camp.Upgrade("camp-a",original.InstanceId)&&original.Level==1&&Math.Abs(original.PowerMultiplier-1.2)<.0001,"First upgrade adds 20% base");
   Check(!camp.Upgrade("camp-a",original.InstanceId),"One camp main action");
   for(int i=0;i<4;i++)Check(camp.Upgrade("upgrade-"+i,original.InstanceId),"Independent camps permit repeat upgrade");
   Check(original.Level==5&&Math.Abs(original.PowerMultiplier-2)<.0001,"Five upgrades double base without compounding");
   string beforeAffix=original.Affix;var pending=camp.RollReforge("camp-a",original.InstanceId);Check(pending!=null,"Reforge allowed after main action");
   Check(camp.RollReforge("camp-a",original.InstanceId).NewAffix==pending.NewAffix&&camp.GetCamp("camp-a").ReforgeAttempts==1,"Pending reforge cannot be rerolled by reopening");
   camp=RoundTrip(camp);original=camp.Weapons[0];Check(camp.GetCamp("camp-a").PendingReforge.NewAffix==pending.NewAffix,"Pending result persists");
   Check(camp.ResolveReforge("camp-a",false)&&original.Affix==beforeAffix,"Reject result retains old affix");
   for(int i=0;i<2;i++){Check(camp.RollReforge("camp-a",original.InstanceId)!=null,"Remaining free reforge");Check(camp.ResolveReforge("camp-a",true),"Accept reforge");}
   Check(camp.GetCamp("camp-a").ReforgeAttempts==3&&camp.RollReforge("camp-a",original.InstanceId)==null,"Three free attempts shared per camp");
   Check(RoundTrip(camp).RollReforge("camp-a",original.InstanceId)==null,"Save/load does not replenish attempts");
   Check(camp.RollReforge("camp-b",camp.Armor.InstanceId)!=null&&!camp.GetCamp("camp-b").MainActionUsed,"Another camp has three attempts and main action");
   var forge=AdventureRun.Create(555,HeroClass.Ranger);Fund(forge);
   var recipes=forge.ForgeCandidates("forge");
   Check(recipes.Any(x=>x.Unlocked&&GearCatalog.Get(x.GearKey).Stage==2),"Next stage entry gear craftable early");
   Check(recipes.Where(x=>GearCatalog.Get(x.GearKey).Stage==2&&!GearCatalog.Get(x.GearKey).IsStageEntry).All(x=>!x.Unlocked),"Next stage non-entry gear locked");
   foreach(var recipe in recipes) {
    var gear=GearCatalog.Get(recipe.GearKey);int kinds=gear.Rarity==GearRarity.Common?1:gear.Rarity==GearRarity.Uncommon?2:3;
    Check(recipe.Costs.Count==kinds&&recipe.Costs.Values.Sum()==(gear.Rarity==GearRarity.Common?2:3),"Rarity controls recipe diversity");
   }
   var target=recipes.First(x=>x.Unlocked&&GearCatalog.Get(x.GearKey).Stage==2);
   var costCopy=new Dictionary<string,int>(target.Costs);var forged=forge.BeginForge("forge",target.GearKey);Check(forged!=null&&forged.Count==3&&forged.Select(x=>x.GearKey).Distinct().Count()==3,"Forge caches three distinct choices");Check(forged.Count(x=>GearCatalog.Get(x.GearKey).Kind==GearKind.Armor)==1&&forged.Count(x=>GearCatalog.Get(x.GearKey).Kind==GearKind.Weapon)==2,"Crafted choices include armor and weapon alternatives");Check(forge.Weapons.Count==1,"Starting forge does not directly grant an item");Check(target.Costs.All(x=>forge.Materials[x.Key]==100-x.Value)&&forge.GetCamp("forge").MainActionUsed,"Exact cost and main action paid once");
   int paidTotal=forge.Materials.Values.Sum();var serializedOffers=JsonSerializer.Serialize(forged);forge=RoundTrip(forge);var resumed=forge.BeginForge("forge",target.GearKey);Check(JsonSerializer.Serialize(resumed)==serializedOffers&&forge.Materials.Values.Sum()==paidTotal,"Reloading keeps all three outcomes and does not charge again");
   var weaponOffer=resumed.First(x=>GearCatalog.Get(x.GearKey).Kind==GearKind.Weapon);Check(forge.ResolveForge("forge",weaponOffer.OfferId)&&forge.Weapons.Count==2,"Choose crafted weapon after viewing outcomes");Check(!forge.ResolveForge("forge",weaponOffer.OfferId)&&forge.BeginForge("forge",target.GearKey)==null,"Completed forge cannot be claimed or bought again");
   var secondForge=forge.BeginForge("forge-2",target.GearKey);var secondWeapon=secondForge.First(x=>GearCatalog.Get(x.GearKey).Kind==GearKind.Weapon);Check(forge.ResolveForge("forge-2",secondWeapon.OfferId)&&forge.Weapons.Count==3,"Third weapon slot fills");
   var thirdForge=forge.BeginForge("forge-3",target.GearKey);var thirdWeapon=thirdForge.First(x=>GearCatalog.Get(x.GearKey).Kind==GearKind.Weapon);int materialTotal=forge.Materials.Values.Sum();Check(!forge.ResolveForge("forge-3",thirdWeapon.OfferId)&&forge.GetCamp("forge-3").PendingForge!=null&&forge.Materials.Values.Sum()==materialTotal,"Full weapon slots retain pending choice until replacement");
   string oldId=forge.Weapons[1].InstanceId;Check(forge.ResolveForge("forge-3",thirdWeapon.OfferId,1)&&forge.Weapons.Count==3&&forge.Weapons[1].InstanceId!=oldId,"Replacement preserves slot limit");
   forge.EnterStage(2);Check(forge.ForgeCandidates().First(x=>x.GearKey==target.GearKey).Costs.OrderBy(x=>x.Key).SequenceEqual(costCopy.OrderBy(x=>x.Key)),"Recipes remain stable across act transitions");
   var poor=AdventureRun.Create(1,HeroClass.Warrior);var poorRecipe=poor.ForgeCandidates().First(x=>x.Unlocked);Check(!poor.Forge("poor",poorRecipe.GearKey)&&!poor.GetCamp("poor").MainActionUsed,"Insufficient materials do not consume main action");
   var drops=AdventureRun.Create(888,HeroClass.Summoner);
   var chest=AdventureRun.Create(31,HeroClass.Warrior);var newAccessory=chest.Rewards("chest-new",RewardKind.Treasure).Single();
   Check(newAccessory.GearKey=="accessory:CobaltShield"&&chest.Accept(newAccessory)&&chest.Accessories.Count==4,"Starter accessories leave one new chest accessory to obtain");
   int chestMaterials=chest.Materials.Values.Sum();var duplicateChest=chest.Rewards("chest-duplicate",RewardKind.Treasure).Single();
   Check(chest.Accept(duplicateChest)&&chest.Accessories.Count==4&&chest.Materials.Values.Sum()==chestMaterials+2,"Exhausted chest pool converts duplicate accessory to two materials");
   Check(!RoundTrip(chest).Accept(duplicateChest),"Duplicate chest conversion cannot be claimed again after restore");
   int initialAccessories=drops.Accessories.Count;var eye=drops.ResolveBossDrop("eye","EyeofCthulhu");Check(eye.GearKey=="accessory:EoCShield"&&drops.Accessories.Count==initialAccessories+1,"Eye drops its actual expert shield");
   Check(drops.ResolveBossDrop("eye","EyeofCthulhu").AlreadyClaimed&&drops.Accessories.Count==initialAccessories+1,"Boss rewards cannot be claimed twice");
   Check(RoundTrip(drops).ResolveBossDrop("eye","EyeofCthulhu").AlreadyClaimed,"Boss claim survives save/load");
   var duplicate=drops.ResolveBossDrop("eye-again","EyeofCthulhu");Check(duplicate.DuplicateConverted&&duplicate.Materials.Count>0&&drops.Accessories.Count==initialAccessories+1,"Repeated accessory converts to materials");
   var mech=drops.ResolveBossDrop("destroyer","TheDestroyer");Check(mech.GearKey==""&&mech.Materials.Values.Sum()==3,"Boss without accessory grants materials");
   Check(AdventureRun.BossAccessorySymbols("KingSlime").SequenceEqual(new[]{"RoyalGel"}),"King Slime expert bag source");
   Check(AdventureRun.BossAccessorySymbols("QueenSlimeBoss").SequenceEqual(new[]{"VolatileGelatin"}),"Queen Slime expert bag source");
   Check(AdventureRun.BossAccessorySymbols("SkeletronPrime").Count==0,"Prime has no dedicated accessory");
   Check(AdventureRun.BossAccessorySymbols("Plantera").SequenceEqual(new[]{"SporeSac"}),"Plantera expert bag source");
   Check(AdventureRun.BossAccessorySymbols("DukeFishron").SequenceEqual(new[]{"FishronWings"}),"Duke Fishron normal and expert source");
   var queen=drops.ResolveBossDrop("bee","QueenBee");Check(new[]{"accessory:HiveBackpack","accessory:HoneyComb"}.Contains(queen.GearKey),"Queen Bee draws only actual drop accessories");
   var queen2=drops.ResolveBossDrop("bee2","QueenBee");Check(queen2.GearKey!=queen.GearKey,"Prefer unowned accessory within enemy pool");
   bool invalid=false;try{AdventureRun.Deserialize(poor.Serialize().Replace("\"LifeMax\":300","\"LifeMax\":301"));}catch(JsonException){invalid=true;}Check(invalid,"Corrupt baseline rejected");
   return JsonSerializer.Serialize(new{status="passed",assertions,catalog=GearCatalog.All.Count,weapons=GearCatalog.All.Count(x=>x.Kind==GearKind.Weapon),armor=GearCatalog.All.Count(x=>x.Kind==GearKind.Armor),accessories=GearCatalog.All.Count(x=>x.Kind==GearKind.Accessory),consumables=GearCatalog.All.Count(x=>x.Kind==GearKind.Consumable),nativeSymbolsChecked=GearCatalog.All.SelectMany(x=>x.NativeSymbols).Distinct().Count()},new JsonSerializerOptions{WriteIndented=true});
  }
 }
}

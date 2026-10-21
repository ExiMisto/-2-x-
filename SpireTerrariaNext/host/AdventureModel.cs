using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireTerraria.Next.Host {
 public sealed class GearInstance {
  public string InstanceId {get;set;}="";
  public string DefinitionKey {get;set;}="";
  public int Level {get;set;}
  public string Affix {get;set;}="";
  [JsonIgnore] public double PowerMultiplier=>1+Level*.2;
  [JsonIgnore] public GearDefinition Definition=>GearCatalog.Get(DefinitionKey);
 }
 public sealed class RewardOffer {
  public string OfferId {get;set;}="";
  public string NodeKey {get;set;}="";
  public string GearKey {get;set;}="";
  public Dictionary<string,int> Materials {get;set;}=new Dictionary<string,int>();
  public string Title {get;set;}="";
  public string Description {get;set;}="";
 }
 public sealed class PendingReforge {
  public string InstanceId {get;set;}="";
  public string OldAffix {get;set;}="";
  public string NewAffix {get;set;}="";
  public string Title {get;set;}="";
  public string Description {get;set;}="";
 }
 public sealed class CampState {
  public int ReforgeAttempts {get;set;}
  public bool MainActionUsed {get;set;}
  public PendingReforge PendingReforge {get;set;}
  public List<RewardOffer> PendingForge {get;set;}
  [JsonIgnore] public int ReforgeRemaining=>Math.Max(0,3-ReforgeAttempts);
 }
 public sealed class ForgeRecipe {
  public string GearKey {get;set;}="";
  public Dictionary<string,int> Costs {get;set;}=new Dictionary<string,int>();
  public bool Affordable {get;set;}
  public bool Unlocked {get;set;}
  public int TargetStage {get;set;}
  public GearRarity Rarity {get;set;}
  public List<string> PossibleGearKeys {get;set;}=new List<string>();
  [JsonIgnore] public string Title=>"第 "+TargetStage+" 幕 · "+GearCatalog.RarityName(Rarity)+"锻造";
  [JsonIgnore] public string Description=>"消耗 "+AdventureRun.DescribeMaterials(Costs)+"；从以下范围随机出现三件，选择一件："+string.Join("、",PossibleGearKeys.Select(x=>GearCatalog.Get(x).Name));
 }
 public sealed class BossDropResult {
  public string GearKey {get;set;}="";
  public Dictionary<string,int> Materials {get;set;}=new Dictionary<string,int>();
  public bool AlreadyClaimed {get;set;}
  public bool DuplicateConverted {get;set;}
 }
 public sealed class AdventureRun {
  const int SaveVersion=1;
  static readonly string[] materialTypes={"ore","weave","bone","crystal","essence"};
  public int Version {get;set;}=SaveVersion;
  public long Seed {get;set;}
  public HeroClass Hero {get;set;}
  public int Stage {get;set;}=1;
  public int Life {get;set;}=300;
  public int LifeMax {get;set;}=300;
  public List<GearInstance> Weapons {get;set;}=new List<GearInstance>();
  public GearInstance Armor {get;set;}
  public List<GearInstance> Accessories {get;set;}=new List<GearInstance>();
  public List<GearInstance> Consumables {get;set;}=new List<GearInstance>();
  public Dictionary<string,int> Materials {get;set;}=new Dictionary<string,int>();
  public Dictionary<string,List<RewardOffer>> RewardNodes {get;set;}=new Dictionary<string,List<RewardOffer>>();
  public HashSet<string> ClaimedNodes {get;set;}=new HashSet<string>();
  public Dictionary<string,CampState> Camps {get;set;}=new Dictionary<string,CampState>();
  public Dictionary<string,BossDropResult> BossDrops {get;set;}=new Dictionary<string,BossDropResult>();
  public List<string> RecentEquipment {get;set;}=new List<string>();
  public int InstanceCounter {get;set;}
  public static AdventureRun Create(long seed,HeroClass hero) {
   var run=new AdventureRun{Seed=seed,Hero=hero};
   string weapon=hero==HeroClass.Warrior?"EnchantedSword":hero==HeroClass.Ranger?"GoldBow":hero==HeroClass.Mage?"WaterBolt":"BabyBirdStaff";
   run.Weapons.Add(run.NewInstance("weapon:"+hero+":1:"+weapon));
   if(hero==HeroClass.Summoner)run.Weapons.Add(run.NewInstance("weapon:Summoner:1:BlandWhip"));
   run.Armor=run.NewInstance("armor:Any:1:wood");
   foreach(string symbol in new[]{"HermesBoots","CloudinaBottle","BandofRegeneration"})run.Accessories.Add(run.NewInstance("accessory:"+symbol));
   foreach(string symbol in new[]{"Grenade","HealingPotion","ManaPotion"})run.Consumables.Add(run.NewInstance("consumable:"+symbol));return run;
  }
  GearInstance NewInstance(string key)=>new GearInstance{InstanceId="gear-"+(++InstanceCounter),DefinitionKey=key};
  public void EnterStage(int stage) {
   if(stage<1||stage>3)throw new ArgumentOutOfRangeException(nameof(stage));
   if(stage<Stage)throw new InvalidOperationException("Adventure stages cannot move backwards.");
   if(stage==Stage)return;
   int increase=300*stage-LifeMax;Stage=stage;LifeMax=300*stage;Life=Math.Min(LifeMax,Math.Max(0,Life+increase));
  }
  public List<RewardOffer> Rewards(string node,RewardKind kind,string bossSymbol="") {
   RequireNode(node);if(RewardNodes.TryGetValue(node,out var cached))return cached;
   var rng=RandomFor("rewards:"+node+":"+Stage+":"+kind+":"+bossSymbol);
   var offers=new List<RewardOffer>();
   if(kind==RewardKind.Treasure) {
    var pool=GearCatalog.All.Where(x=>x.Kind==GearKind.Accessory&&x.Stage<=Stage&&new[]{"HermesBoots","CloudinaBottle","BandofRegeneration","CobaltShield"}.Contains(x.NativeSymbols[0])).ToList();
    var unowned=pool.Where(x=>!Owns(x.Key)).ToList();var selected=WeightedPick(unowned.Count>0?unowned:pool,rng);offers.Add(EquipmentOffer(node,selected));
   } else {
    var pool=GearCatalog.For(Hero,Stage).Where(x=>(x.Kind==GearKind.Weapon||x.Kind==GearKind.Armor)&&!IsPrimitive(x)).ToList();
    // Two distinct visible equipment choices compete with a visible material package.
    for(int i=0;i<2&&pool.Count>0;i++){var selected=WeightedPick(pool,rng);pool.Remove(selected);offers.Add(EquipmentOffer(node,selected));}
    var material=MaterialBundle(Stage,rng,kind==RewardKind.Boss?4:kind==RewardKind.Elite?3:2);
    offers.Add(new RewardOffer{OfferId=node+":materials",NodeKey=node,Materials=material,Title="锻造材料",Description=DescribeMaterials(material)});
   }
   foreach(var offer in offers.Where(x=>x.GearKey!="")){RecentEquipment.Add(offer.GearKey);while(RecentEquipment.Count>12)RecentEquipment.RemoveAt(0);}
   RewardNodes.Add(node,offers);return offers;
  }
  RewardOffer EquipmentOffer(string node,GearDefinition gear)=>new RewardOffer{OfferId=node+":"+gear.Key,NodeKey=node,GearKey=gear.Key,Title=gear.Name,Description=GearCatalog.RarityName(gear.Rarity)+" · "+gear.Description};
  GearDefinition WeightedPick(List<GearDefinition> pool,StableRandom rng) {
   double Weight(GearDefinition item){double rarity=item.Rarity==GearRarity.Common?5:item.Rarity==GearRarity.Uncommon?3:1;int recent=RecentEquipment.Count(x=>x==item.Key);return rarity*(Owns(item.Key) ? .12 : 1)*Math.Pow(.35,recent);}
   double target=rng.NextDouble()*pool.Sum(Weight);foreach(var item in pool){target-=Weight(item);if(target<=0)return item;}return pool[pool.Count-1];
  }
  bool Owns(string key)=>Weapons.Any(x=>x.DefinitionKey==key)||(Armor?.DefinitionKey==key)||Accessories.Any(x=>x.DefinitionKey==key);
  internal static bool IsPrimitive(GearDefinition gear)=>gear.Kind==GearKind.Weapon&&new[]{"WoodenSword","CopperBroadsword","SilverBroadsword","WoodenBow","CopperBow","SilverBow","WandofSparking","AmethystStaff","TopazStaff"}.Contains(gear.NativeSymbols[0]);
  public bool Accept(RewardOffer offer,int replaceIndex=-1) {
   if(offer==null||ClaimedNodes.Contains(offer.NodeKey)||!RewardNodes.TryGetValue(offer.NodeKey,out var offers))return false;
   var actual=offers.FirstOrDefault(x=>x.OfferId==offer.OfferId);if(actual==null)return false;
   if(actual.GearKey!=""&&!GiveGear(actual.GearKey,replaceIndex))return false;
   GiveMaterials(actual.Materials);ClaimedNodes.Add(actual.NodeKey);return true;
  }
  bool GiveGear(string key,int replaceIndex=-1) {
   var definition=GearCatalog.Get(key);if(!GearCatalog.IsForHero(definition,Hero))return false;
   if(definition.Kind==GearKind.Weapon){if(Weapons.Count>=3){if(replaceIndex<0||replaceIndex>=Weapons.Count)return false;Weapons[replaceIndex]=NewInstance(key);}else Weapons.Add(NewInstance(key));}
   else if(definition.Kind==GearKind.Armor)Armor=NewInstance(key);
   else if(definition.Kind==GearKind.Accessory){if(Accessories.Any(x=>x.DefinitionKey==key))GiveMaterials(MaterialBundle(Stage,RandomFor("duplicate:"+key+":"+InstanceCounter),2));else Accessories.Add(NewInstance(key));}
   else Consumables.Add(NewInstance(key));return true;
  }
  public CampState GetCamp(string node){RequireNode(node);if(!Camps.TryGetValue(node,out var camp)){camp=new CampState();Camps.Add(node,camp);}return camp;}
  public List<ForgeRecipe> ForgeCandidates()=>ForgeCandidates("");
  public List<ForgeRecipe> ForgeCandidates(string node) {
   var items=GearCatalog.All.Where(x=>(x.Kind==GearKind.Weapon||x.Kind==GearKind.Armor)&&GearCatalog.IsForHero(x,Hero)&&x.Stage>=Stage&&x.Stage<=Math.Min(3,Stage+1));
   return items.GroupBy(x=>new{x.Stage,x.Rarity}).Select(group=>{
    var allowed=group.Where(x=>x.Stage==Stage||x.IsStageEntry).ToList();bool unlocked=allowed.Count>=3;
    var scope=allowed.Count>0?allowed:group.ToList();var representative=scope[0];var costs=RecipeCosts(representative);
    return new ForgeRecipe{GearKey=representative.Key,Costs=costs,Unlocked=unlocked,TargetStage=group.Key.Stage,Rarity=group.Key.Rarity,PossibleGearKeys=scope.Select(x=>x.Key).ToList(),Affordable=costs.All(c=>Materials.TryGetValue(c.Key,out int value)&&value>=c.Value)};
   }).OrderByDescending(x=>x.Unlocked&&x.Affordable).ThenBy(x=>x.TargetStage).ThenBy(x=>x.Rarity).ToList();
  }
  Dictionary<string,int> RecipeCosts(GearDefinition gear) {
   // Next-stage entry gear deliberately uses the current stage's materials.
   int materialStage=gear.Stage>1&&gear.Rarity==GearRarity.Common?gear.Stage-1:gear.Stage;var rng=RandomFor("recipe:"+Hero+":"+gear.Stage+":"+gear.Rarity);
   var types=materialTypes.OrderBy(x=>rng.NextDouble()).ToArray();
   if(gear.Rarity==GearRarity.Common)return new Dictionary<string,int>{{materialStage+":"+types[0],2}};
   if(gear.Rarity==GearRarity.Uncommon)return new Dictionary<string,int>{{materialStage+":"+types[0],2},{materialStage+":"+types[1],1}};
   return new Dictionary<string,int>{{materialStage+":"+types[0],1},{materialStage+":"+types[1],1},{materialStage+":"+types[2],1}};
  }
  public bool Forge(string node,string gearKey,int replaceIndex=-1) {
   return BeginForge(node,gearKey)!=null;
  }
  public List<RewardOffer> BeginForge(string node,string gearKey) {
   var camp=GetCamp(node);if(camp.PendingForge!=null)return camp.PendingForge;if(camp.MainActionUsed||camp.PendingReforge!=null)return null;
   var recipe=ForgeCandidates(node).FirstOrDefault(x=>x.GearKey==gearKey);if(recipe==null||!recipe.Unlocked||!recipe.Affordable)return null;
   string forgeNode=node+":forge";var rng=RandomFor("forge-result:"+node+":"+recipe.TargetStage+":"+recipe.Rarity);var pool=recipe.PossibleGearKeys.Select(GearCatalog.Get).ToList();var selected=new List<GearDefinition>();
   // Include armor when the recipe has one, then give two distinct weapon alternatives.
   var armorPool=pool.Where(x=>x.Kind==GearKind.Armor).ToList();if(armorPool.Count>0){var armor=WeightedPick(armorPool,rng);selected.Add(armor);pool.Remove(armor);}
   while(selected.Count<3){var weapons=pool.Where(x=>x.Kind==GearKind.Weapon).ToList();var next=WeightedPick(weapons.Count>0?weapons:pool,rng);selected.Add(next);pool.Remove(next);}
   var offers=selected.Select(x=>EquipmentOffer(forgeNode,x)).ToList();foreach(var cost in recipe.Costs)Materials[cost.Key]-=cost.Value;
   camp.MainActionUsed=true;camp.PendingForge=offers;RewardNodes.Add(forgeNode,offers);return offers;
  }
  public bool ResolveForge(string node,string offerId,int replaceIndex=-1) {
   var camp=GetCamp(node);var offer=camp.PendingForge?.FirstOrDefault(x=>x.OfferId==offerId);if(offer==null||!Accept(offer,replaceIndex))return false;camp.PendingForge=null;return true;
  }
  public bool Upgrade(string node,string instanceId) {
   var camp=GetCamp(node);var gear=FindGear(instanceId);if(camp.MainActionUsed||gear==null||(gear.Definition.Kind!=GearKind.Weapon&&gear.Definition.Kind!=GearKind.Armor)||gear.Level>=1000)return false;
   gear.Level++;camp.MainActionUsed=true;return true;
  }
  public bool Rest(string node) {var camp=GetCamp(node);if(camp.MainActionUsed)return false;Life=Math.Min(LifeMax,Life+(int)Math.Ceiling(LifeMax*.3));camp.MainActionUsed=true;return true;}
  public PendingReforge RollReforge(string node,string instanceId) {
   var camp=GetCamp(node);if(camp.PendingReforge!=null)return camp.PendingReforge;
   var gear=FindGear(instanceId);if(camp.ReforgeAttempts>=3||gear==null||(gear.Definition.Kind!=GearKind.Weapon&&gear.Definition.Kind!=GearKind.Armor))return null;
   var choices=GearCatalog.Affixes.Where(x=>x.Kind==gear.Definition.Kind&&x.Key!=gear.Affix&&(x.Key!="efficient"||Hero==HeroClass.Mage)).ToArray();
   var rng=RandomFor("reforge:"+node+":"+camp.ReforgeAttempts);var affix=choices[rng.Next(choices.Length)];camp.ReforgeAttempts++;
   camp.PendingReforge=new PendingReforge{InstanceId=instanceId,OldAffix=gear.Affix,NewAffix=affix.Key,Title=affix.Name,Description=affix.Description};return camp.PendingReforge;
  }
  public bool ResolveReforge(string node,bool accept) {
   var camp=GetCamp(node);var pending=camp.PendingReforge;if(pending==null)return false;var gear=FindGear(pending.InstanceId);if(gear==null)return false;if(accept)gear.Affix=pending.NewAffix;camp.PendingReforge=null;return true;
  }
  public GearInstance FindGear(string instanceId)=>Weapons.Concat(Accessories).Concat(Consumables).Concat(Armor==null?Enumerable.Empty<GearInstance>():new[]{Armor}).FirstOrDefault(x=>x.InstanceId==instanceId);
  public BossDropResult ResolveBossDrop(string node,string nativeSymbol) {
   RequireNode(node);string claim=node+":enemy-drop";if(BossDrops.TryGetValue(claim,out var prior))return new BossDropResult{GearKey=prior.GearKey,Materials=new Dictionary<string,int>(prior.Materials),AlreadyClaimed=true,DuplicateConverted=prior.DuplicateConverted};
   var rng=RandomFor("boss-drop:"+node+":"+nativeSymbol);var pool=BossAccessorySymbols(nativeSymbol).Select(x=>"accessory:"+x).Where(x=>GearCatalog.TryGet(x,out _)).ToList();
   var result=new BossDropResult();if(pool.Count==0){result.Materials=MaterialBundle(Stage,rng,3);GiveMaterials(result.Materials);}else {
    var available=pool.Where(x=>!Accessories.Any(a=>a.DefinitionKey==x)).ToList();if(available.Count==0){result.DuplicateConverted=true;result.Materials=MaterialBundle(Stage,rng,3);GiveMaterials(result.Materials);}else{result.GearKey=available[rng.Next(available.Count)];GiveGear(result.GearKey);}
   }
   BossDrops.Add(claim,result);return result;
  }
  public static IReadOnlyList<string> BossAccessorySymbols(string nativeSymbol) {
   // Source audit: installed Terraria 1.4.5.8 ItemDropDatabase plus Player.OpenBossBag.
   // The six current encounter bosses are documented in docs/EQUIPMENT-VALIDATION.md.
   switch(nativeSymbol) {
    case "KingSlime":return new[]{"RoyalGel"};
    case "EyeofCthulhu":case "EyeOfCthulhu":return new[]{"EoCShield"};
    case "EaterofWorldsHead":case "EaterOfWorldsHead":return new[]{"WormScarf"};
    case "BrainofCthulhu":case "BrainOfCthulhu":return new[]{"BrainOfConfusion"};
    case "QueenBee":return new[]{"HiveBackpack","HoneyComb"};
    case "SkeletronHead":return new[]{"BoneGlove"};
    case "WallofFlesh":case "WallOfFlesh":return Array.Empty<string>();
    case "QueenSlimeBoss":return new[]{"VolatileGelatin"};
    case "Plantera":return new[]{"SporeSac"};
    case "Golem":return new[]{"ShinyStone"};
    case "DukeFishron":return new[]{"FishronWings"};
    case "HallowBoss":return new[]{"EmpressFlightBooster"};
    case "Mimic":return new[]{"TitanGlove","PhilosophersStone","StarCloak","CrossNecklace"};
    case "BigMimicCorruption":return new[]{"PutridScent"};
    case "BigMimicCrimson":return new[]{"FleshKnuckles"};
    case "Pixie":return new[]{"FastClock"};
    case "GiantBat":return new[]{"TrifoldMap"};
    case "Hornet":case "MossHornet":return new[]{"Bezoar"};
    case "CursedSkull":return new[]{"Nazar"};
    default:return Array.Empty<string>();
   }
  }
  public void GiveMaterials(IReadOnlyDictionary<string,int> material){foreach(var pair in material){if(pair.Value<0)throw new ArgumentOutOfRangeException(nameof(material));Materials.TryGetValue(pair.Key,out int value);Materials[pair.Key]=checked(value+pair.Value);}}
  static Dictionary<string,int> MaterialBundle(int stage,StableRandom rng,int count){var result=new Dictionary<string,int>();for(int i=0;i<count;i++){string key=stage+":"+materialTypes[rng.Next(materialTypes.Length)];result.TryGetValue(key,out int value);result[key]=value+1;}return result;}
  public static string DescribeMaterials(IReadOnlyDictionary<string,int> material)=>string.Join("、",material.Select(x=>GearCatalog.MaterialName(x.Key)+" ×"+x.Value));
  StableRandom RandomFor(string context){unchecked{ulong hash=14695981039346656037UL^(ulong)Seed;foreach(char c in context){hash^=c;hash*=1099511628211UL;}return new StableRandom(hash);}}
  static void RequireNode(string node){if(string.IsNullOrWhiteSpace(node)||node.Length>256)throw new ArgumentException("A stable map node key is required.",nameof(node));}
  public string Serialize()=>JsonSerializer.Serialize(this);
  public static AdventureRun Deserialize(string json) {
   var run=JsonSerializer.Deserialize<AdventureRun>(json)??throw new JsonException("Empty adventure save.");run.Validate();return run;
  }
  void Validate() {
   if(Version!=SaveVersion||!Enum.IsDefined(Hero)||Stage<1||Stage>3||LifeMax!=300*Stage||Life<0||Life>LifeMax||Weapons==null||Weapons.Count<1||Weapons.Count>3||Armor==null||Accessories==null||Consumables==null||Materials==null||RewardNodes==null||ClaimedNodes==null||Camps==null||BossDrops==null||RecentEquipment==null)throw new JsonException("Invalid adventure save.");
   var items=Weapons.Concat(Accessories).Concat(Consumables).Append(Armor).ToList();if(items.Select(x=>x.InstanceId).Distinct().Count()!=items.Count||items.Any(x=>string.IsNullOrEmpty(x.InstanceId)||!GearCatalog.TryGet(x.DefinitionKey,out _)||x.Level<0||x.Level>1000)||Materials.Any(x=>x.Value<0)||Camps.Values.Any(x=>x.ReforgeAttempts<0||x.ReforgeAttempts>3))throw new JsonException("Invalid adventure equipment or camp state.");
   if(Accessories.Select(x=>x.DefinitionKey).Distinct().Count()!=Accessories.Count)throw new JsonException("Duplicate accessories in adventure save.");
  }
  sealed class StableRandom {
   ulong state;
   public StableRandom(ulong seed){state=seed==0?0x9e3779b97f4a7c15UL:seed;}
   public double NextDouble(){unchecked{state^=state>>12;state^=state<<25;state^=state>>27;return ((state*2685821657736338717UL)>>11)*(1.0/9007199254740992.0);}}
   public int Next(int count){if(count<=0)throw new ArgumentOutOfRangeException(nameof(count));return (int)(NextDouble()*count);}
  }
 }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SpireTerraria.Next.Host {
 // The expedition save owns equipment identities. These are genuine BaseLib
 // models projected into the native deck, relic inventory and potion slots.
 internal static partial class AdventureInventoryModels {
  internal static Func<string,bool> CanUsePotion;
  internal static Func<string,Task<bool>> UsePotion;
  static readonly HashSet<string> consumed=new(StringComparer.Ordinal);
  static readonly HashSet<string> drinking=new(StringComparer.Ordinal);
  static readonly Dictionary<string,Texture2D> portraits=new(StringComparer.Ordinal);
  static readonly Dictionary<string,Texture2D> potionResources=new(StringComparer.Ordinal);
  static AdventureRun projectionRun;
  static bool syncing,installed;
  static readonly MethodInfo cardFactory=typeof(ModelDb).GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="Card"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0);
  static readonly MethodInfo relicFactory=typeof(ModelDb).GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="Relic"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0);
  static readonly MethodInfo potionFactory=typeof(ModelDb).GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="Potion"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0);

  internal static void Install(Harmony harmony) {
   if(installed)return;installed=true;
   foreach(string property in new[]{"Icon","IconOutline","BigIcon"})harmony.Patch(AccessTools.PropertyGetter(typeof(RelicModel),property),prefix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(RelicTexture)));
   foreach(string property in new[]{"Image","Outline","LargeImage"})harmony.Patch(AccessTools.PropertyGetter(typeof(PotionModel),property),prefix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(PotionTexture)));
   foreach(string property in new[]{"ImagePath","OutlinePath","LargeImagePath","PackedImagePath","PackedOutlinePath"})harmony.Patch(AccessTools.PropertyGetter(typeof(PotionModel),property),prefix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(PotionTexturePath)));
   harmony.Patch(AccessTools.Method(typeof(NCard),"UpdatePortrait"),postfix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(CardVisual)));
   harmony.Patch(AccessTools.Method(typeof(NRelic),"Reload"),postfix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(RelicVisual)));
   harmony.Patch(AccessTools.Method(typeof(NPotion),"Reload"),postfix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(PotionVisual)));
   harmony.Patch(AccessTools.Method(typeof(PotionModel),"EnqueueManualUse"),prefix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(EnqueuePotion)));
   harmony.Patch(AccessTools.Method(typeof(PotionModel),"Discard"),prefix:new HarmonyMethod(typeof(AdventureInventoryModels),nameof(DiscardPotion)));
  }
  static bool RelicTexture(RelicModel __instance,ref Texture2D __result) {
   if(__instance is not AdventureAccessory accessory)return true;
   __result=AdventureHostUi.ItemIcon(accessory.Definition.NativeSymbols[0])??MissingPortrait();return false;
  }
  static bool PotionTexture(PotionModel __instance,ref Texture2D __result) {
   if(__instance is not AdventurePotion potion)return true;
   __result=AdventureHostUi.ItemIcon(potion.Definition.NativeSymbols[0])??MissingPortrait();return false;
  }
  static bool PotionTexturePath(PotionModel __instance,ref string __result) {if(__instance is not AdventurePotion potion)return true;__result=AdventureHostUi.LocalIconPath(potion.Definition.NativeSymbols[0]);return false;}
  static void CardVisual(NCard __instance) {
   Texture2D texture=__instance.Model is AdventureGearCard gear?gear.CustomPortrait:__instance.Model is AdventureChoiceCard option?option.CustomPortrait:null;
   if(texture==null)return;
   // Apply the final native node texture, including when a host texture getter
   // was already inlined or its preload ran before local icons became ready.
   foreach(string field in new[]{"_portrait","_ancientPortrait"})if(AccessTools.Field(typeof(NCard),field)?.GetValue(__instance) is TextureRect portrait) {
    portrait.Texture=texture;portrait.Material=null;portrait.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;
   }
  }
  static void RelicVisual(NRelic __instance,RelicModel ____model) {
   if(____model is not AdventureAccessory accessory)return;
   var texture=AdventureHostUi.ItemIcon(accessory.Definition.NativeSymbols[0]);if(texture==null||__instance.Icon==null)return;
   __instance.Icon.Texture=texture;__instance.Icon.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;
   if(__instance.Outline!=null){__instance.Outline.Texture=texture;__instance.Outline.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;}
  }
  static void PotionVisual(NPotion __instance,PotionModel ____model) {
   if(____model is not AdventurePotion potion)return;
   var texture=AdventureHostUi.ItemIcon(potion.Definition.NativeSymbols[0]);if(texture==null||__instance.Image==null)return;
   __instance.Image.Texture=texture;__instance.Image.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;
   if(__instance.Outline!=null){__instance.Outline.Texture=texture;__instance.Outline.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;}
  }
  internal static void RegisterPotionTextures() {
   // Some host getters are inlined before Harmony is installed. Register the
   // exact model paths in Godot's memory cache as well, without creating files.
   foreach(var definition in GearCatalog.All.Where(IsPotion)) {
    var model=CanonicalPotion(definition.Key);var source=AdventureHostUi.ItemIcon(definition.NativeSymbols[0]);if(source==null)continue;
    string entry=model.Id.Entry.ToLowerInvariant();
    foreach(string path in new[]{"res://images/atlases/potion_atlas.sprites/"+entry+".tres","res://images/atlases/potion_outline_atlas.sprites/"+entry+".tres","res://images/potions/large/"+entry+".png"}) {
     if(potionResources.ContainsKey(path))continue;using var pixels=source.GetImage();var texture=ImageTexture.CreateFromImage(pixels);texture.TakeOverPath(path);potionResources[path]=texture;
    }
   }
   RefreshVisibleInventory();
  }
  internal static void RefreshVisibleInventory() {
   var root=NRun.Instance?.GlobalUi;if(root==null||!GodotObject.IsInstanceValid(root)||!root.IsInsideTree())return;
   foreach(var node in Descendants(root)) {
    if(node is NRelic relic&&AccessTools.Field(typeof(NRelic),"_model")?.GetValue(relic) is AdventureAccessory accessory)RelicVisual(relic,accessory);
    else if(node is NPotion potion&&AccessTools.Field(typeof(NPotion),"_model")?.GetValue(potion) is AdventurePotion model)PotionVisual(potion,model);
   }
  }
  static IEnumerable<Node> Descendants(Node parent) {foreach(Node child in parent.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
  static bool EnqueuePotion(PotionModel __instance) {
   if(__instance is not AdventurePotion potion)return true;
   _=Drink(potion);return false;
  }
  static bool DiscardPotion(PotionModel __instance) {
   if(__instance is not AdventurePotion potion)return true;
   // During a real-time encounter the worker owns the fixed inventory snapshot.
   // Discarding there would change subsequent indices before the battle ends.
   if(HostIntegration.Active!=null)return false;
   var run=AdventureSession.Current;var item=run?.Consumables.FirstOrDefault(x=>x.InstanceId==potion.InstanceId);
   if(item!=null){run.Consumables.Remove(item);AdventureSession.Save();Sync(potion.Owner);}return false;
  }
  internal static bool IsPotion(GearDefinition definition)=>definition.Kind==GearKind.Consumable&&definition.NativeSymbols[0].EndsWith("Potion",StringComparison.Ordinal);
  internal static CardModel CanonicalGear(string key) {
   var type=GearModelTypes.TryGetValue(key,out var value)?value:throw new KeyNotFoundException("No BaseLib equipment model for "+key);
   return (CardModel)cardFactory.MakeGenericMethod(type).Invoke(null,null);
  }
  internal static RelicModel CanonicalAccessory(string key) {
   var type=AccessoryModelTypes.TryGetValue(key,out var value)?value:throw new KeyNotFoundException("No BaseLib accessory model for "+key);
   return (RelicModel)relicFactory.MakeGenericMethod(type).Invoke(null,null);
  }
  internal static PotionModel CanonicalPotion(string key) {
   var type=PotionModelTypes.TryGetValue(key,out var value)?value:throw new KeyNotFoundException("No native potion model for "+key);
   return (PotionModel)potionFactory.MakeGenericMethod(type).Invoke(null,null);
  }
  internal static IEnumerable<CardModel> CardPoolEntries(HeroClass hero)=>GearCatalog.All.Where(g=>(g.Kind==GearKind.Weapon||g.Kind==GearKind.Armor||g.Kind==GearKind.Consumable&&!IsPotion(g))&&GearCatalog.IsForHero(g,hero)).Select(g=>CanonicalGear(g.Key));
  internal static IEnumerable<CardModel> StarterCards(HeroClass hero) {var run=AdventureRun.Create(0,hero);return run.Weapons.Append(run.Armor).Select(g=>CanonicalGear(g.DefinitionKey));}
  internal static IEnumerable<RelicModel> RelicPoolEntries()=>GearCatalog.All.Where(g=>g.Kind==GearKind.Accessory).Select(g=>CanonicalAccessory(g.Key));
  internal static IEnumerable<PotionModel> PotionPoolEntries()=>GearCatalog.All.Where(IsPotion).Select(g=>CanonicalPotion(g.Key));
  internal static IReadOnlyList<RelicModel> StarterRelics(HeroClass hero)=>AdventureRun.Create(0,hero).Accessories.Select(g=>CanonicalAccessory(g.DefinitionKey)).ToArray();
  internal static IReadOnlyList<PotionModel> StarterPotions(HeroClass hero)=>AdventureRun.Create(0,hero).Consumables.Where(g=>IsPotion(g.Definition)).Select(g=>CanonicalPotion(g.DefinitionKey)).ToArray();

  internal static CardModel GearCard(GearInstance gear) {
   var card=(AdventureGearCard)CanonicalGear(gear.DefinitionKey).ToMutable();
   card.Bind(gear,false);return card;
  }
  internal static CardModel ChoiceCard(AdventureHostUi.Choice choice) {
   string key=choice.Value is GearInstance instance?instance.DefinitionKey:choice.Value is RewardOffer offer?offer.GearKey:choice.Value is ForgeRecipe recipe?recipe.GearKey:null;
   CardModel model;
   if(key!=null&&GearModelTypes.ContainsKey(key)) {
    var gear=(AdventureGearCard)(choice.Value is GearInstance item?GearCard(item):CanonicalGear(key).ToMutable());
    gear.DisplayChoice=choice;model=gear;
   } else {
    var option=(AdventureChoiceCard)ModelDb.Card<AdventureChoiceCard>().ToMutable();option.Choice=choice;model=option;
   }
   model.Owner=AdventureSession.HostRun?.Players.FirstOrDefault();return model;
  }
  internal static AdventureHostUi.Choice GetChoice(CardModel card)=>card is AdventureGearCard gear?gear.DisplayChoice:card is AdventureChoiceCard option?option.Choice:null;
  internal static RelicModel Accessory(GearInstance gear) {var model=(AdventureAccessory)CanonicalAccessory(gear.DefinitionKey).ToMutable();model.InstanceId=gear.InstanceId;return model;}
  internal static PotionModel Potion(GearDefinition definition)=>(AdventurePotion)CanonicalPotion(definition.Key).ToMutable();

  internal static void Sync(Player player) {
   var run=AdventureSession.Current;
   if(syncing||run==null||player==null||!AdventureCharacters.TryGetHero(player.Character,out _))return;
   syncing=true;
   try {
    EnsureProjection(run);
    var desired=run.Weapons.Append(run.Armor).Where(x=>x!=null).ToList();
    var existing=player.Deck.Cards.OfType<AdventureGearCard>().ToList();var keep=new HashSet<CardModel>();
    foreach(var item in desired) {
     var card=existing.FirstOrDefault(c=>!keep.Contains(c)&&c.InstanceId==item.InstanceId&&c.GearKey==item.DefinitionKey&&c.CurrentUpgradeLevel<=item.Level)
      ??existing.FirstOrDefault(c=>!keep.Contains(c)&&string.IsNullOrEmpty(c.InstanceId)&&c.GearKey==item.DefinitionKey&&c.CurrentUpgradeLevel==item.Level)
      ??existing.FirstOrDefault(c=>!keep.Contains(c)&&string.IsNullOrEmpty(c.InstanceId)&&c.GearKey==item.DefinitionKey&&c.CurrentUpgradeLevel<item.Level);
     if(card==null){card=(AdventureGearCard)CanonicalGear(item.DefinitionKey).ToMutable();card.Owner=player;card.Bind(item,true);player.Deck.AddInternal(card);}
     else card.Bind(item,true);
     keep.Add(card);
    }
    foreach(var card in player.Deck.Cards.Where(c=>!keep.Contains(c)).ToArray())player.Deck.RemoveInternal(card);
    var relics=player.Relics.OfType<AdventureAccessory>().ToList();var keepRelics=new HashSet<RelicModel>();
    foreach(var item in run.Accessories) {
     var relic=relics.FirstOrDefault(r=>!keepRelics.Contains(r)&&r.InstanceId==item.InstanceId&&r.GearKey==item.DefinitionKey)
      ??relics.FirstOrDefault(r=>!keepRelics.Contains(r)&&string.IsNullOrEmpty(r.InstanceId)&&r.GearKey==item.DefinitionKey);
     if(relic==null){relic=(AdventureAccessory)Accessory(item);ObtainAccessory(player,relic);}else relic.InstanceId=item.InstanceId;
     keepRelics.Add(relic);
    }
    foreach(var relic in player.Relics.Where(r=>!keepRelics.Contains(r)).ToArray())player.RemoveRelicInternal(relic);
    SyncPotions(player,run);
   } finally{syncing=false;}
  }
  static void ObtainAccessory(Player player,AdventureAccessory relic) {
   var inventory=NRun.Instance?.GlobalUi.RelicInventory;
   if(player.RunState==null||inventory==null||!inventory.IsNodeReady()||AccessTools.Field(typeof(NRelicInventory),"_player")?.GetValue(inventory)!=player) {
    // Inventory restoration precedes NRun.Initialize; that native path creates
    // these holders fully visible rather than treating them as new rewards.
    player.AddRelicInternal(relic,silent:true);return;
   }
   // The native command records history, marks the relic seen and runs the
   // obtain animation needed to reveal newly created, initially transparent icons.
   _=CompleteAccessoryObtain(player,relic);
  }
  static async Task CompleteAccessoryObtain(Player player,AdventureAccessory relic) {
   try {await RelicCmd.Obtain(relic,player);}
   catch(Exception error) {
    GD.PrintErr("[SpireNext] accessory acquisition presentation: "+error);
    if(!player.Relics.Contains(relic))player.AddRelicInternal(relic);
    var inventory=NRun.Instance?.GlobalUi.RelicInventory;
    var holder=inventory?.RelicNodes.FirstOrDefault(h=>h.Relic?.Model==relic);
    if(holder?.Relic?.Icon!=null){var color=holder.Relic.Icon.Modulate;color.A=1;holder.Relic.Icon.Modulate=color;}
   }
  }
  static void EnsureProjection(AdventureRun run) {if(ReferenceEquals(projectionRun,run))return;projectionRun=run;consumed.Clear();drinking.Clear();}
  static void SyncPotions(Player player,AdventureRun run) {
   // Native slots stay at their normal size; additional bottles remain in the
   // expedition supply and fill a freed native slot without changing worker IDs.
   var desired=run.Consumables.Where(x=>IsPotion(x.Definition)&&!consumed.Contains(x.InstanceId)).Take(player.MaxPotionCount).ToList();
   var available=player.Potions.OfType<AdventurePotion>().ToList();var keep=new HashSet<PotionModel>();
   foreach(var item in desired) {
    var potion=available.FirstOrDefault(p=>!keep.Contains(p)&&p.InstanceId==item.InstanceId&&p.GearKey==item.DefinitionKey)
     ??available.FirstOrDefault(p=>!keep.Contains(p)&&string.IsNullOrEmpty(p.InstanceId)&&p.GearKey==item.DefinitionKey);
    if(potion!=null){potion.InstanceId=item.InstanceId;keep.Add(potion);}
   }
   foreach(var potion in player.Potions.Where(p=>!keep.Contains(p)).ToArray())player.DiscardPotionInternal(potion);
   foreach(var item in desired.Where(x=>!keep.OfType<AdventurePotion>().Any(p=>p.InstanceId==x.InstanceId))) {
    var potion=(AdventurePotion)Potion(item.Definition);potion.InstanceId=item.InstanceId;
    player.AddPotionInternal(potion);keep.Add(potion);
   }
  }
  internal static void SyncConsumed(IEnumerable<string> instanceIds) {
   var run=AdventureSession.Current;var player=AdventureSession.HostRun?.Players.FirstOrDefault();if(run==null||player==null)return;EnsureProjection(run);
   bool changed=false;foreach(string id in instanceIds??Array.Empty<string>())if(!string.IsNullOrEmpty(id))changed|=consumed.Add(id);
   if(changed&&!syncing)SyncPotions(player,run);
  }
  internal static async Task<bool> Drink(AdventurePotion potion) {
   if(potion==null||string.IsNullOrEmpty(potion.InstanceId)||!potion.PassesCustomUsabilityCheck||!drinking.Add(potion.InstanceId))return false;
   try {
    bool success=UsePotion!=null&&await UsePotion(potion.InstanceId);
    if(success)SyncConsumed(new[]{potion.InstanceId});return success;
   } catch(Exception error){GD.PrintErr("[SpireNext] 药水使用失败，保留库存："+error.Message);return false;}
   finally {drinking.Remove(potion.InstanceId);}
  }
  internal static bool PotionReady(string instanceId)=>!string.IsNullOrEmpty(instanceId)&&!consumed.Contains(instanceId)&&!drinking.Contains(instanceId)&&(CanUsePotion?.Invoke(instanceId)??false);
  internal static Texture2D MissingPortrait()=>ResourceLoader.Load<Texture2D>(CardModel.MissingPortraitPath);
  internal static Texture2D Portrait(string symbol,string kind) {
   var texture=AdventureHostUi.ItemIcon(symbol);if(texture==null)return MissingPortrait();
   string key=symbol+":"+texture.GetInstanceId();if(portraits.TryGetValue(key,out var cached))return cached;
   using var image=Image.CreateEmpty(300,220,false,Image.Format.Rgba8);
   bool armor=kind=="防具";var top=armor?new Color(.16f,.22f,.28f):new Color(.16f,.20f,.13f);
   for(int y=0;y<220;y++){float strength=1-y/330f;for(int x=0;x<300;x++)image.SetPixel(x,y,new Color(top.R*strength,top.G*strength,top.B*strength,1));}
   using var source=texture.GetImage();int scale=Math.Max(1,Math.Min(5,Math.Min(210/source.GetWidth(),160/source.GetHeight())));
   source.Resize(source.GetWidth()*scale,source.GetHeight()*scale,Image.Interpolation.Nearest);
   image.BlendRect(source,new Rect2I(0,0,source.GetWidth(),source.GetHeight()),new Vector2I((300-source.GetWidth())/2,(220-source.GetHeight())/2));
   var result=ImageTexture.CreateFromImage(image);result.TakeOverPath("res://spire_next/runtime/cards/"+symbol+"_"+kind+".tres");return portraits[key]=result;
  }
 }

 public abstract class AdventureGearCard:CustomCardModel {
  public abstract string GearKey {get;}
  public GearDefinition Definition=>GearCatalog.Get(GearKey);
  public string InstanceId {get;internal set;}="";
  GearInstance bound;
  bool track;
  internal AdventureHostUi.Choice DisplayChoice;
  protected AdventureGearCard():base(0,CardType.Skill,CardRarity.Common,TargetType.Self,true,false){}
  public override CardPoolModel Pool=>AdventureCharacters.PoolFor(Definition.Hero??HeroClass.Warrior);
  public override CardPoolModel VisualCardPool=>Definition.Kind==GearKind.Consumable?ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>():AdventureCharacters.PoolFor(Definition.Hero??HeroClass.Warrior);
  public override CardType Type=>Definition.Kind==GearKind.Weapon?CardType.Attack:CardType.Skill;
  public override CardRarity Rarity=>Definition.Rarity==GearRarity.Rare?CardRarity.Rare:Definition.Rarity==GearRarity.Uncommon?CardRarity.Uncommon:CardRarity.Common;
  public override int MaxUpgradeLevel=>Definition.Kind==GearKind.Weapon||Definition.Kind==GearKind.Armor?1000:0;
  protected override bool IsPlayable=>false;
  public override string Title=>DisplayChoice?.Title??Definition.Name+(CurrentUpgradeLevel>0?" +"+CurrentUpgradeLevel:"")+(string.IsNullOrEmpty(bound?.Affix)?"":" · "+GearCatalog.Affixes.FirstOrDefault(a=>a.Key==bound.Affix)?.Name);
  public override Texture2D CustomPortrait=>AdventureInventoryModels.Portrait(Definition.NativeSymbols[0],Definition.Kind==GearKind.Armor?"防具":"武器");
  public override string PortraitPath=>AdventureHostUi.ItemIcon(Definition.NativeSymbols[0])==null?CardModel.MissingPortraitPath:CustomPortrait.ResourcePath;
  public override IEnumerable<string> AllPortraitPaths=>new[]{PortraitPath};
  public override List<(string,string)> Localization=>new(){("title",Definition.Name),("description","{EquipmentText}"),("reward","选择泰拉装备")};
  protected override void AddExtraArgsToDescription(LocString description) {
   string text=DisplayChoice?.Description??GearCatalog.RarityName(Definition.Rarity)+" · 第 "+Definition.Stage+" 幕\n"+Definition.Description+(Definition.Kind==GearKind.Consumable?"":"\n强化：本件基础数值 +"+(CurrentUpgradeLevel*20L)+"%")+(string.IsNullOrEmpty(bound?.Affix)?"":"\n词条："+GearCatalog.Affixes.FirstOrDefault(a=>a.Key==bound.Affix)?.Description);
   description.Add("EquipmentText",text);
  }
  internal void Bind(GearInstance item,bool persist) {
   track=false;bound=item;InstanceId=item.InstanceId;
   if(CurrentUpgradeLevel>item.Level)throw new InvalidOperationException("Equipment projection cannot downgrade an existing native card.");
   while(CurrentUpgradeLevel<item.Level)UpgradeInternal();track=persist;
  }
  protected override void OnUpgrade() {
   if(!track||bound==null||Owner?.Deck?.Cards.Contains(this)!=true)return;
   bound.Level=Math.Min(1000,CurrentUpgradeLevel);AdventureSession.Save();
  }
 }
 public sealed class AdventureChoiceCard:CustomCardModel {
  internal AdventureHostUi.Choice Choice;
  public AdventureChoiceCard():base(0,CardType.Skill,CardRarity.Event,TargetType.Self,false,false){}
  public override CardPoolModel Pool=>AdventureCharacters.PoolFor(HeroClass.Warrior);
  public override int MaxUpgradeLevel=>0;
  protected override bool IsPlayable=>false;
  public override string Title=>Choice?.Title??"泰拉选项";
  public override Texture2D CustomPortrait=>Choice==null?AdventureInventoryModels.MissingPortrait():AdventureHostUi.ChoicePortrait(Choice);
  public override string PortraitPath=>CardModel.MissingPortraitPath;
  public override IEnumerable<string> AllPortraitPaths=>Array.Empty<string>();
  public override List<(string,string)> Localization=>new(){("title","泰拉选项"),("description","{ChoiceText}")};
  protected override void AddExtraArgsToDescription(LocString description)=>description.Add("ChoiceText",Choice?.Description??"");
 }
 public abstract class AdventureAccessory:CustomRelicModel {
  public abstract string GearKey {get;}
  public GearDefinition Definition=>GearCatalog.Get(GearKey);
  public string EquipmentDescription=>Definition.Description+"\n自动装备；不占饰品槽。同名饰品只生效一件。";
  public string InstanceId {get;internal set;}="";
  protected AdventureAccessory():base(false){}
  public override string PackedIconPath=>AdventureHostUi.LocalIconPath(Definition.NativeSymbols[0]);
  protected override string PackedIconOutlinePath=>PackedIconPath;
  protected override string BigIconPath=>PackedIconPath;
  public override RelicRarity Rarity=>Definition.Rarity==GearRarity.Rare?RelicRarity.Rare:Definition.Rarity==GearRarity.Uncommon?RelicRarity.Uncommon:RelicRarity.Common;
  public override bool HasUponPickupEffect=>false;
  public override List<(string,string)> Localization=>new(){("title",Definition.Name),("description",EquipmentDescription),("flavor","来自泰拉瑞亚的战利品。")};
 }
 public abstract class AdventurePotion:PotionModel,ILocalizationProvider {
  public abstract string GearKey {get;}
  public GearDefinition Definition=>GearCatalog.Get(GearKey);
  public string InstanceId {get;internal set;}="";
  public override PotionRarity Rarity=>PotionRarity.Common;
  public override PotionUsage Usage=>PotionUsage.CombatOnly;
  public override TargetType TargetType=>TargetType.Self;
  public override bool PassesCustomUsabilityCheck=>AdventureInventoryModels.PotionReady(InstanceId);
  public List<(string,string)> Localization=>new(){("title",Definition.Name),("description",Definition.Description+"\n使用效果与冷却按泰拉瑞亚实际药水结算。")};
  protected override async Task OnUse(PlayerChoiceContext context,Creature target) {await AdventureInventoryModels.Drink(this);}
 }
}

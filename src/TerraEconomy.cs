using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace SpireTerraria {
 internal static class TerraEconomy {
  internal static readonly int[] Stock=new int[3];
  static readonly HashSet<NMerchantSlot> discardedSlots=new HashSet<NMerchantSlot>();
  internal static void Reset(){Array.Clear(Stock);}
  internal static int Stage(Player p)=>Math.Clamp(p.RunState.CurrentActIndex+1,1,3);
  internal static int Unlock(Player p)=>EquipmentCatalog.UnlockAt(p.RunState.CurrentMapPoint?.coord.row??0);
  internal static TerraEquipment Create(Player p,EquipmentDef d)=>(TerraEquipment)p.RunState.CreateCard(TerraEquipment.Canonical(d.Key),p);
  internal static List<EquipmentDef> Eligible(Player p,bool? armor=null){var h=((Terrarian)p.Character).Hero;int stage=Stage(p),unlock=Unlock(p);return EquipmentCatalog.All.Where(d=>d.Hero==h&&d.Stage==stage&&d.Unlock<=unlock&&(!armor.HasValue||armor.Value==d.IsArmor)).ToList();}
  internal static List<EquipmentDef> Roll(Player p,int count,bool? armor=null,bool strong=false,IEnumerable<string> excluded=null,bool forShop=false){
   var blocked=new HashSet<string>(excluded??Array.Empty<string>());var owned=new HashSet<string>(p.Deck.Cards.OfType<TerraEquipment>().Select(x=>x.GearKey));var pool=Eligible(p,armor).Where(x=>!blocked.Contains(x.Key)).ToList();
   // An early-stage pool can contain only one eligible weapon. Older equipment is a safe
   // fallback; later-stage equipment must never leak into a reward or the merchant.
   if(pool.Count<count){var hero=((Terrarian)p.Character).Hero;pool.AddRange(EquipmentCatalog.All.Where(d=>d.Hero==hero&&d.Stage<Stage(p)&&(!armor.HasValue||armor.Value==d.IsArmor)&&!blocked.Contains(d.Key)).OrderByDescending(d=>d.Stage));}
   var rng=forShop?p.PlayerRng.Shops:p.PlayerRng.Rewards;
   var result=new List<EquipmentDef>();while(result.Count<count&&pool.Count>0){int newest=pool.Max(x=>x.Stage);var current=pool.Where(x=>x.Stage==newest).ToList();var fresh=current.Where(x=>!owned.Contains(x.Key)).ToList();var choices=fresh.Count>0?fresh:current;int Weight(EquipmentDef d)=>strong?(d.Rarity==1?25:d.Rarity==2?45:30):(d.Rarity==1?65:d.Rarity==2?28:7);int roll=rng.NextInt(choices.Sum(Weight));var next=choices[0];foreach(var d in choices){roll-=Weight(d);if(roll<0){next=d;break;}}result.Add(next);pool.Remove(next);}return result;
  }
  internal static List<CardModel> RewardChoices(Player p,bool strong=false){var weapons=Roll(p,2,false,strong);var armor=Roll(p,1,true,strong);return weapons.Concat(armor).Select(d=>(CardModel)Create(p,d)).ToList();}
  internal static LocString Text(string text){var loc=new LocString("cards",ModelDb.Card<TerraGrenades>().Id.Entry+".prompt");loc.Add("Text",text);return loc;}
  internal static void Install(Harmony h){
   h.Patch(AccessTools.Method(typeof(MerchantInventory),"CreateForNormalMerchant"),prefix:new HarmonyMethod(typeof(TerraEconomy),nameof(Shop)));
   h.Patch(AccessTools.Method(typeof(MerchantCardEntry),"Populate"),prefix:new HarmonyMethod(typeof(TerraEconomy),nameof(Populate)));
   h.Patch(AccessTools.Method(typeof(MerchantCardEntry),"CalcCost"),prefix:new HarmonyMethod(typeof(TerraEconomy),nameof(Price)));
   h.Patch(AccessTools.Method(typeof(NMerchantInventory),"Initialize"),prefix:new HarmonyMethod(typeof(TerraEconomy),nameof(ShopUi)));
   foreach(var slot in new[]{typeof(NMerchantCard),typeof(NMerchantRelic),typeof(NMerchantPotion),typeof(NMerchantCardRemoval)})h.Patch(AccessTools.Method(slot,"_ExitTree"),prefix:new HarmonyMethod(typeof(TerraEconomy),nameof(DiscardedSlotExit)));
   h.Patch(AccessTools.Method(typeof(CardPileCmd),"Add",new[]{typeof(CardModel),typeof(PileType),typeof(CardPilePosition),typeof(AbstractModel),typeof(bool)}),prefix:new HarmonyMethod(typeof(TerraEconomy),nameof(Add)));
  }
  static bool Shop(Player player,ref MerchantInventory __result){
   if(!(player.Character is Terrarian))return true;var inv=new MerchantInventory(player);
   var character=(List<MerchantCardEntry>)AccessTools.Field(typeof(MerchantInventory),"_characterCardEntries").GetValue(inv);
   var universal=(List<MerchantCardEntry>)AccessTools.Field(typeof(MerchantInventory),"_colorlessCardEntries").GetValue(inv);
   var defs=Roll(player,3,false,forShop:true);defs.AddRange(Roll(player,2,true,forShop:true));while(defs.Count<5){var remaining=Roll(player,5,null,false,defs.Select(x=>x.Key),true);if(remaining.Count==0)break;defs.Add(remaining[0]);}
   foreach(var d in defs.Take(5)){var entry=new MerchantCardEntry(player,inv,new[]{TerraEquipment.Canonical(d.Key)},TerraEquipment.Canonical(d.Key).Rarity);entry.Populate();character.Add(entry);}
   if(character.Count>0)character[player.PlayerRng.Shops.NextInt(character.Count)].SetOnSale();
   CardModel[] bombs=Stage(player)==1?new CardModel[]{ModelDb.Card<TerraGrenades>(),ModelDb.Card<TerraBombs>()}:new CardModel[]{ModelDb.Card<TerraGrenades>(),ModelDb.Card<TerraDynamite>()};
   foreach(var c in bombs){var entry=new MerchantCardEntry(player,inv,new[]{c},c.Rarity);entry.Populate();universal.Add(entry);}
   var accessories=TerraIntegration.AccessoryPool(player).OrderBy(x=>player.PlayerRng.Shops.NextInt(1000000)).Take(3).ToArray();foreach(var a in accessories)inv.AddRelicEntry(new MerchantRelicEntry(a.ToMutable(),player));
   var potions=(List<MerchantPotionEntry>)AccessTools.Field(typeof(MerchantInventory),"_potionEntries").GetValue(inv);potions.Add(new MerchantPotionEntry(ModelDb.Potion<TerraHealingPotion>().ToMutable(),player));potions.Add(new MerchantPotionEntry(ModelDb.Potion<TerraManaPotion>().ToMutable(),player));potions.Add(new MerchantPotionEntry(ModelDb.Potion<TerraHealingPotion>().ToMutable(),player));
   foreach(var e in inv.AllEntries)e.PurchaseCompleted+=(status,entry)=>{foreach(var slot in inv.AllEntries)slot.OnMerchantInventoryUpdated();};
   __result=inv;Plugin.Log("TERRA SHOP stage="+Stage(player)+" equipment="+string.Join(",",character.Select(x=>x.CreationResult.Card.Title))+" accessories="+accessories.Length);return false;
  }
  static bool Populate(MerchantCardEntry __instance){var p=(Player)AccessTools.Field(typeof(MerchantEntry),"_player").GetValue(__instance);if(!(p.Character is Terrarian))return true;var cards=(IEnumerable<CardModel>)AccessTools.Field(typeof(MerchantCardEntry),"_cardPool").GetValue(__instance);var canonical=cards.First();var card=p.RunState.CreateCard(canonical,p);AccessTools.Property(typeof(MerchantCardEntry),"CreationResult").SetValue(__instance,new CardCreationResult(card));__instance.CalcCost();return false;}
  static bool Price(MerchantCardEntry __instance){var c=__instance.CreationResult?.Card;if(!(c is TerraEquipment)&&!(c is TerraThrowable))return true;int price=c is TerraEquipment e?(e.Definition.Rarity==1?45:e.Definition.Rarity==2?80:125)+(e.Definition.Stage-1)*15:c is TerraDynamite?65:c is TerraBombs?40:30;if(__instance.IsOnSale)price/=2;AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(__instance,price);return false;}
  static void ShopUi(NMerchantInventory __instance,MerchantInventory inventory){
   if(!(inventory.Player.Character is Terrarian))return;
   // Original navigation dereferences every child slot's Entry, even hidden children.
   // Remove unused slots before Initialize builds navigation, including card removal.
   void Discard(NMerchantSlot slot){if(slot.IsInsideTree())discardedSlots.Add(slot);slot.GetParent()?.RemoveChild(slot);slot.QueueFree();}
   void Trim(string field,int count){var container=(Control)AccessTools.Field(typeof(NMerchantInventory),field).GetValue(__instance);if(container==null)return;var children=container.GetChildren().OfType<NMerchantSlot>().ToArray();foreach(var child in children.Skip(count))Discard(child);}
   Trim("_characterCardContainer",inventory.CharacterCardEntries.Count);Trim("_colorlessCardContainer",inventory.ColorlessCardEntries.Count);Trim("_relicContainer",inventory.RelicEntries.Count);Trim("_potionContainer",inventory.PotionEntries.Count);
   var removalField=AccessTools.Field(typeof(NMerchantInventory),"_cardRemovalNode");var removal=(NMerchantSlot)removalField.GetValue(__instance);if(removal!=null){Discard(removal);removalField.SetValue(__instance,null);}
  }
  // Unfilled slots never subscribed entry/player events. Their vanilla ExitTree
  // assumes FillSlot and Initialize already ran and dereferences a null entry.
  // Godot frees their signal connections with the node; initialized slots use
  // the original cleanup path unchanged.
  static bool DiscardedSlotExit(NMerchantSlot __instance)=>!discardedSlots.Remove(__instance);
  static bool Add(CardModel card,PileType newPileType,CardPilePosition position,AbstractModel clonedBy,bool skipVisuals,ref Task<CardPileAddResult> __result){if(newPileType!=PileType.Deck||!(card.Owner?.Character is Terrarian)||(!(card is TerraEquipment)&&!(card is TerraThrowable)))return true;__result=Acquire(card,position,clonedBy,skipVisuals);return false;}
  static async Task<CardPileAddResult> Acquire(CardModel card,CardPilePosition position,AbstractModel clonedBy,bool skipVisuals){
   var p=card.Owner;if(card is TerraThrowable throwable){int slot=throwable.ItemId==168?0:throwable.ItemId==166?1:2;Stock[slot]=Math.Min(9999,Stock[slot]+throwable.Quantity);Plugin.Log("THROWABLE BOUGHT id="+throwable.ItemId+" remaining="+Stock[slot]);return new CardPileAddResult{success=true,cardAdded=card,targetPile=PileType.Deck};}
   var incoming=(TerraEquipment)card;var owned=p.Deck.Cards.OfType<TerraEquipment>().Where(x=>x.Stat==incoming.Stat).ToArray();TerraEquipment replace=null;
   if(incoming.Stat==2&&owned.Length>0){var prefs=new CardSelectorPrefs(Text("替换护甲："+incoming.Title+"？选择现有护甲确认，取消则保留。"),1){Cancelable=true,RequireManualConfirmation=true};replace=(await CardSelectCmd.FromDeckGeneric(p,prefs,c=>owned.Contains(c))).OfType<TerraEquipment>().FirstOrDefault();if(replace==null)return new CardPileAddResult{success=false,cardAdded=card};}
   else if(incoming.Stat==0&&owned.Length>=3){var prefs=new CardSelectorPrefs(Text("获得 "+incoming.Title+"：选择一把武器替换，取消则放弃新武器。"),1){Cancelable=true,RequireManualConfirmation=true};replace=(await CardSelectCmd.FromDeckGeneric(p,prefs,c=>owned.Contains(c))).OfType<TerraEquipment>().FirstOrDefault();if(replace==null)return new CardPileAddResult{success=false,cardAdded=card};}
   // Add through the CardPile overload, then remove the old instance only on success.
   var result=await CardPileCmd.Add(card,p.Deck,position,clonedBy,skipVisuals);if(result.success&&replace!=null)await CardPileCmd.RemoveFromDeck(replace,false);Plugin.Log("EQUIPPED "+incoming.Title+" replaced="+replace?.Title);return result;
  }
 }
}

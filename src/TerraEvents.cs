using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireTerraria {
 // Keep the game's event layout, option buttons and selectors, but own every
 // action. No original card/relic callbacks are retained in Terraria events.
 internal static class TerraEvents {
  sealed class Page {
   public string Description;
   public List<EventOption> Options=new List<EventOption>();
  }
  static readonly ConditionalWeakTable<EventModel,Page> pages=new ConditionalWeakTable<EventModel,Page>();
  static readonly System.Reflection.MethodInfo setState=AccessTools.Method(typeof(EventModel),"SetEventState");
  static readonly System.Reflection.MethodInfo finish=AccessTools.Method(typeof(EventModel),"SetEventFinished");

  internal static void Install(Harmony h){
   h.Patch(AccessTools.Method(typeof(EventRoom),"EnterInternal"),prefix:new HarmonyMethod(typeof(TerraEvents),nameof(Enter)));
   h.Patch(AccessTools.Method(typeof(EventModel),"CreateInitialPortrait"),prefix:new HarmonyMethod(typeof(TerraEvents),nameof(Portrait)));
   h.Patch(AccessTools.Method(typeof(NEventLayout),"SetPortrait"),postfix:new HarmonyMethod(typeof(TerraEvents),nameof(PortraitLayout)));
   h.Patch(AccessTools.Method(typeof(AncientEventModel),"BeforeEventStarted"),prefix:new HarmonyMethod(typeof(TerraEvents),nameof(AncientStart)));
   h.Patch(AccessTools.Method(typeof(AncientEventModel),"GenerateInitialOptionsWrapper"),prefix:new HarmonyMethod(typeof(TerraEvents),nameof(AncientOptions)));
   // Neow overrides this getter. Patch actual declarations so custom run
   // modifiers cannot restore its original equipment-unrelated description.
   foreach(var t in typeof(AncientEventModel).Assembly.GetTypes().Where(t=>typeof(AncientEventModel).IsAssignableFrom(t))){
    var getter=t.GetProperty("InitialDescription",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly)?.GetMethod;
    if(getter!=null)h.Patch(getter,prefix:new HarmonyMethod(typeof(TerraEvents),nameof(AncientDescription)));
   }
  }
  static void Enter(EventRoom __instance,IRunState runState){
   var p=runState?.Players.FirstOrDefault();if(!(p?.Character is Terrarian)||__instance.CanonicalEvent is AncientEventModel||__instance.CanonicalEvent is TerraJourneyEvent)return;
   EventModel next;switch(p.PlayerRng.Rewards.NextInt(6)){
    case 0:next=ModelDb.Event<TerraWorkshopEvent>();break;
    case 1:next=ModelDb.Event<TerraTraderEvent>();break;
    case 2:next=ModelDb.Event<TerraNurseEvent>();break;
    case 3:next=ModelDb.Event<TerraCacheEvent>();break;
    case 4:next=ModelDb.Event<TerraTavernEvent>();break;
    default:next=ModelDb.Event<TerraAltarEvent>();break;
   }
   AccessTools.Field(typeof(EventRoom),"<CanonicalEvent>k__BackingField").SetValue(__instance,next);
   Plugin.Log("TERRA EVENT room="+next.Id);
  }
  static bool Portrait(EventModel __instance,ref Texture2D __result){if(!(__instance is TerraJourneyEvent e))return true;__result=TerraArt.Card(e.PortraitItem);return false;}
  static void PortraitLayout(NEventLayout __instance){
   if(!(AccessTools.Field(typeof(NEventLayout),"_event").GetValue(__instance) is TerraJourneyEvent))return;
   var portrait=AccessTools.Field(typeof(NEventLayout),"_portrait").GetValue(__instance) as TextureRect;if(portrait==null)return;
   // The default scene scales a full-screen illustration. Our equipment art
   // belongs in a bounded left panel, leaving the native text and options clear.
   portrait.Scale=Vector2.One;portrait.CustomMinimumSize=Vector2.Zero;
   portrait.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;portrait.StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered;
   portrait.AnchorLeft=.06f;portrait.AnchorRight=.44f;portrait.AnchorTop=.22f;portrait.AnchorBottom=.82f;
   portrait.OffsetLeft=portrait.OffsetRight=portrait.OffsetTop=portrait.OffsetBottom=0;
  }
  static bool AncientStart(AncientEventModel __instance,ref Task __result){
   if(!(__instance.Owner?.Character is Terrarian))return true;
   // Stage HP growth belongs to the campaign, not to a free full heal here.
   __result=Task.CompletedTask;return false;
  }
  static bool AncientDescription(AncientEventModel __instance,ref LocString __result){if(!(__instance.Owner?.Character is Terrarian))return true;__result=TerraEconomy.Text(AncientText(__instance.Owner));return false;}
  static string AncientText(Player p)=>"远古的馈赠化作泰拉瑞亚远征物资。\n选择一件本职业装备、锻造一件已有装备，或领取通用补给。\n第 "+TerraEconomy.Stage(p)+" 幕；每件装备的锻造等级独立计算。";
  static bool AncientOptions(AncientEventModel __instance,ref IReadOnlyList<EventOption> __result){
   if(!(__instance.Owner?.Character is Terrarian))return true;
   var p=__instance.Owner;var page=NewPage(__instance,AncientText(p));var gear=TerraEconomy.RewardChoices(p,true);
   page.Options.Add(Choice(__instance,"远征装备","从三件本幕、本职业装备中选择一件；超过三个武器时选择替换。",()=>ChooseGear(p,gear,0),gear.Count>0));
   page.Options.Add(Choice(__instance,"工匠的祝福","选择一件已有武器或整套护甲，免费锻造 +1。只提高此装备原始伤害或防御的 20%。",()=>Forge(p,0),p.Deck.Cards.OfType<TerraEquipment>().Any(x=>x.IsUpgradable)));
   page.Options.Add(Choice(__instance,"通用补给","获得 8 个手榴弹，并在药水栏有空位时获得 1 瓶治疗药水。投掷物不占三个武器槽。",async()=>{await Grenades(p);if(HasPotionSpace(p))await PotionCmd.TryToProcure<TerraHealingPotion>(p);return true;}));
   AccessTools.Field(typeof(AncientEventModel),"_generatedOptions").SetValue(__instance,page.Options);
   __result=page.Options;Plugin.Log("TERRA ANCIENT options="+__instance.Id+" count="+page.Options.Count);return false;
  }
  static Page NewPage(EventModel e,string description){pages.Remove(e);var page=new Page{Description=description};pages.Add(e,page);return page;}
  static EventOption Choice(EventModel e,string title,string description,Func<Task<bool>> action,bool enabled=true){
   Func<Task> callback=null;if(enabled)callback=async()=>{
    try{
     if(await action()){
      if(e is AncientEventModel)AccessTools.Method(typeof(AncientEventModel),"UpdateRunHistory").Invoke(e,null);
      finish.Invoke(e,new object[]{TerraEconomy.Text("你选择了「"+title+"」。物资已加入远征装备，效果会在泰拉瑞亚战斗中生效。")});
      Plugin.Log("TERRA EVENT chosen="+e.Id+" option="+title);
     }else Refresh(e);
    }catch(Exception ex){Plugin.Log("TERRA EVENT ERROR "+e.Id+" "+ex);Refresh(e,"这次操作未完成，可以选择其他选项或离开。");}
   };
   return new EventOption(e,callback,TerraEconomy.Text(title),TerraEconomy.Text(description),e.Id.Entry+".TERRA."+title,Array.Empty<IHoverTip>());
  }
  static void Refresh(EventModel e,string message=null){
   if(!pages.TryGetValue(e,out var page))return;
   page.Options=page.Options.Select(o=>new EventOption(o)).ToList();
   if(e is AncientEventModel)AccessTools.Field(typeof(AncientEventModel),"_generatedOptions").SetValue(e,page.Options);
   setState.Invoke(e,new object[]{TerraEconomy.Text(message??page.Description),page.Options});
  }
  static bool HasPotionSpace(Player p)=>p.PotionSlots.Any(x=>x==null);
  static Task Pay(Player p,int amount)=>amount>0?PlayerCmd.LoseGold(amount,p,GoldLossType.Spent):Task.CompletedTask;
  static async Task<bool> ChooseGear(Player p,IReadOnlyList<CardModel> cards,int cost){
   if(cards.Count==0||p.Gold<cost)return false;
   var chosen=await CardSelectCmd.FromChooseACardScreen(new BlockingPlayerChoiceContext(),cards,p,true);if(chosen==null)return false;
   var result=await CardPileCmd.Add(chosen,PileType.Deck);if(!result.success)return false;
   await Pay(p,cost);return true;
  }
  static async Task<bool> Forge(Player p,int baseCost){
   var prefs=new CardSelectorPrefs(TerraEconomy.Text(baseCost==0?"选择一件装备锻造 +1：仅此装备增加原始属性的 20%。":"选择一件装备锻造 +1。费用 "+baseCost+" 金币，每已有一级再加 15 金币。"),1){Cancelable=true,RequireManualConfirmation=true};
   var card=(await CardSelectCmd.FromDeckGeneric(p,prefs,c=>c is TerraEquipment&&c.IsUpgradable)).OfType<TerraEquipment>().FirstOrDefault();if(card==null)return false;
   long cost=baseCost==0?0:(long)baseCost+15L*card.CurrentUpgradeLevel;if(cost>p.Gold)return false;
   int before=card.CurrentUpgradeLevel;CardCmd.Upgrade(card,CardPreviewStyle.EventLayout);if(card.CurrentUpgradeLevel==before)return false;
   await Pay(p,(int)cost);Plugin.Log("TERRA FORGE key="+card.GearKey+" level="+card.CurrentUpgradeLevel);return true;
  }
  static async Task<bool> Accessory(Player p,TerraAccessory accessory,int cost){if(accessory==null||p.Gold<cost)return false;await RelicCmd.Obtain(accessory.ToMutable(),p);await Pay(p,cost);return true;}
  static async Task<bool> Potion(Player p,bool mana,int cost){if(!HasPotionSpace(p)||p.Gold<cost)return false;var result=mana?await PotionCmd.TryToProcure<TerraManaPotion>(p):await PotionCmd.TryToProcure<TerraHealingPotion>(p);if(!result.success)return false;await Pay(p,cost);return true;}
  static async Task Grenades(Player p){var card=p.RunState.CreateCard(ModelDb.Card<TerraGrenades>(),p);await CardPileCmd.Add(card,PileType.Deck);}

  internal static IReadOnlyList<EventOption> Options(TerraJourneyEvent e){
   var p=e.Owner;int stage=TerraEconomy.Stage(p);var page=NewPage(e,e.Story);int forgeCost=20+15*stage,equipmentCost=35+15*stage;
   var gear=TerraEconomy.RewardChoices(p,e.Variant==5);var accessory=TerraIntegration.NextAccessory(p,((Terrarian)p.Character).Hero);
   switch(e.Variant){
    case 0:
     page.Options.Add(Choice(e,"指定装备锻造","选择一件装备锻造 +1。费用 "+forgeCost+" 金币起，每已有一级再加 15 金币；不会增强其他装备。",()=>Forge(p,forgeCost),p.Gold>=forgeCost&&p.Deck.Cards.OfType<TerraEquipment>().Any(x=>x.IsUpgradable)));
     page.Options.Add(Choice(e,"购买工坊装备","支付 "+equipmentCost+" 金币，从三件本幕、本职业装备中选择一件。取消或放弃替换不收费。",()=>ChooseGear(p,gear,equipmentCost),p.Gold>=equipmentCost&&gear.Count>0));
     break;
    case 1:
     page.Options.Add(Choice(e,"挑选旅商装备","支付 "+equipmentCost+" 金币，从三件本幕、本职业装备中选择一件。",()=>ChooseGear(p,gear,equipmentCost),p.Gold>=equipmentCost&&gear.Count>0));
     int accessoryCost=60+stage*20;
     page.Options.Add(Choice(e,accessory==null?"饰品已售罄":"购买「"+accessory.Name+"」","支付 "+accessoryCost+" 金币。"+(accessory?.Effect??"暂无适合当前职业与阶段的未拥有饰品。"),()=>Accessory(p,accessory,accessoryCost),accessory!=null&&p.Gold>=accessoryCost));
     break;
    case 2:
     int nurseCost=20+stage*10,heal=(int)Math.Ceiling(p.Creature.MaxHp*.4);
     page.Options.Add(Choice(e,"护士治疗","支付 "+nurseCost+" 金币，恢复最多 "+heal+" 点当前生命；最大生命不变。",async()=>{if(p.Gold<nurseCost||p.Creature.CurrentHp>=p.Creature.MaxHp)return false;await CreatureCmd.Heal(p.Creature,heal);await Pay(p,nurseCost);return true;},p.Gold>=nurseCost&&p.Creature.CurrentHp<p.Creature.MaxHp));
     page.Options.Add(Choice(e,"购买治疗药水","支付 20 金币，获得一瓶治疗药水。",()=>Potion(p,false,20),p.Gold>=20&&HasPotionSpace(p)));
     page.Options.Add(Choice(e,"购买魔力药水","支付 20 金币，获得一瓶魔力药水。",()=>Potion(p,true,20),p.Gold>=20&&HasPotionSpace(p)));
     break;
    case 3:
     page.Options.Add(Choice(e,"搜寻遗留装备","从三件本幕、本职业装备中选择一件。",()=>ChooseGear(p,gear,0),gear.Count>0));
     page.Options.Add(Choice(e,"搬走爆破补给","获得 8 个手榴弹和 5 个炸弹，不占三个武器槽。",async()=>{await Grenades(p);await CardPileCmd.Add(p.RunState.CreateCard(ModelDb.Card<TerraBombs>(),p),PileType.Deck);return true;}));
     break;
    case 4:
     var tavern=TerraEconomy.Eligible(p,true).Where(d=>d.Items[0]>=3797&&d.Items[0]<=3882).Take(3).Select(d=>(CardModel)TerraEconomy.Create(p,d)).ToList();
     var tavernCards=tavern.Count>0?tavern:gear;
     page.Options.Add(Choice(e,tavern.Count>0?"酒馆老板的盔甲":"旅人的装备补给",tavern.Count>0?"选择一套当前进度可获得的酒馆盔甲。保留原版套装效果；不启动塔防。":"选择一件本幕、本职业装备。酒馆高级盔甲随制作阶段开放。",()=>ChooseGear(p,tavernCards,0),tavernCards.Count>0));
     var tavernAccessory=TerraIntegration.AccessoryPool(p).FirstOrDefault(a=>a.ItemId>=3809&&a.ItemId<=3812);
     if(tavernAccessory!=null)page.Options.Add(Choice(e,"酒馆饰品「"+tavernAccessory.Name+"」",tavernAccessory.Effect+"获得后永久自动装备。",()=>Accessory(p,tavernAccessory,0)));
     page.Options.Add(Choice(e,"爆破支援","获得 8 个手榴弹。所有职业都可使用，不占武器槽。",async()=>{await Grenades(p);return true;}));
     break;
    case 5:
     int damage=(int)Math.Ceiling(p.Creature.MaxHp*.2);
     page.Options.Add(Choice(e,"以鲜血换取装备","选择一件偏向罕见、稀有的本幕装备，获得后损失 "+damage+" 点当前生命。",async()=>{if(p.Creature.CurrentHp<=damage||!await ChooseGear(p,gear,0))return false;await CreatureCmd.SetCurrentHp(p.Creature,p.Creature.CurrentHp-damage);return true;},p.Creature.CurrentHp>damage&&gear.Count>0));
     page.Options.Add(Choice(e,"拿走祭坛旁的药水","免费获得一瓶魔力药水，需要一个空药水槽。",()=>Potion(p,true,0),HasPotionSpace(p)));
     break;
   }
   page.Options.Add(Choice(e,"继续前进","离开这里，不领取其他物资。",()=>Task.FromResult(true)));
   return page.Options;
  }
 }
 public abstract class TerraJourneyEvent:CustomEventModel,ILocalizationProvider {
  string ILocalizationProvider.LocTable=>"events";
  public abstract int Variant {get;}
  protected TerraJourneyEvent():base(false){}
  public int PortraitItem=>new[]{989,54,188,168,3809,273}[Variant];
  public string EventTitle=>new[]{"哥布林工坊","游商的货摊","林间诊所","废弃营地","酒馆老板","猩红祭坛"}[Variant];
  public string Story=>new[]{
   "哥布林工匠摆出了铁砧。你可以锻造已有装备，或者购买一件适合当前阶段的新装备。",
   "游商把武器和饰品铺在地毯上。每件商品都能带进泰拉瑞亚战斗；购买前可以查看装备。",
   "护士愿意处理伤口，也备好了治疗药水与魔力药水。这里的治疗不会提高最大生命。",
   "营地已经无人驻守。你可以挑走一件遗留装备，或者带走通用爆破补给。",
   "酒馆老板把装备运到了尖塔。无需守护水晶或等待波次，挑选补给便能继续远征。",
   "祭坛以鲜血换取装备。你也可以避开仪式，只拿走旁边的一瓶魔力药水。"
  }[Variant];
  public override List<(string,string)> Localization=>new List<(string,string)>{("title",EventTitle),("pages.INITIAL.description",Story)};
  public override LocString InitialDescription=>TerraEconomy.Text(Story);
  public override IEnumerable<string> GetAssetPaths(IRunState runState)=>new[]{"res://scenes/events/default_event_layout.tscn"};
  protected override IReadOnlyList<EventOption> GenerateInitialOptions()=>TerraEvents.Options(this);
 }
 public sealed class TerraWorkshopEvent:TerraJourneyEvent {public override int Variant=>0;}
 public sealed class TerraTraderEvent:TerraJourneyEvent {public override int Variant=>1;}
 public sealed class TerraNurseEvent:TerraJourneyEvent {public override int Variant=>2;}
 public sealed class TerraCacheEvent:TerraJourneyEvent {public override int Variant=>3;}
 public sealed class TerraTavernEvent:TerraJourneyEvent {public override int Variant=>4;}
 public sealed class TerraAltarEvent:TerraJourneyEvent {public override int Variant=>5;}
}

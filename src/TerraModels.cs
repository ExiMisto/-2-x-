using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rooms;

namespace SpireTerraria {
 public abstract class Terrarian : CustomCharacterModel {
  public abstract HeroClass Hero {get;}
  public override bool AllowInVanillaRandomCharacterSelect=>false;
  public override Color NameColor=>new Color("8dce83");
  public override CharacterGender Gender=>CharacterGender.Masculine;
  public override int StartingHp=>CampaignRules.Starter(Hero,BattleKind.Normal,1).MaxLife;
  public override CardPoolModel CardPool=>TerraEquipment.PoolFor(Hero);
  public override RelicPoolModel RelicPool=>ModelDb.RelicPool<TerraRelicPool>();
  public override PotionPoolModel PotionPool=>ModelDb.PotionPool<TerraPotionPool>();
  public override IEnumerable<CardModel> StartingDeck=>EquipmentCatalog.Starters(Hero).Select(TerraEquipment.Canonical);
  public override IReadOnlyList<RelicModel> StartingRelics=>new RelicModel[]{ModelDb.Relic<HermesBoots>(),ModelDb.Relic<CloudBottle>(),ModelDb.Relic<BandRegeneration>()};
  public override IReadOnlyList<PotionModel> StartingPotions=>new PotionModel[]{ModelDb.Potion<TerraHealingPotion>(),ModelDb.Potion<TerraManaPotion>()};
  public override List<(string,string)> Localization=>new List<(string,string)>{("title","泰拉瑞亚 · "+CampaignRules.ClassName(Hero)),("titleObject","泰拉瑞亚旅人"),("description",new[]{"挥舞剑刃与回旋镖，在近身战斗中成长。","用弓与枪械保持距离，寻找射击机会。","操控法杖与魔法武器，管理魔力消耗。","让召唤物追击敌人，用鞭子指挥战斗。"}[(int)Hero]+"\n沿尖塔地图远征，选择武器、防具与饰品。\n当前为不保存的单人开发版。"),("pronounObject","他"),("pronounSubject","他"),("pronounPossessive","他的"),("possessiveAdjective","他的"),("bestiaryQuote","来自另一个世界的敌人。"),("cardsModifierTitle","装备"),("cardsModifierDescription","你的武器与防具")};
  // Shared native scenes provide the animation contracts used by non-combat rooms.
  public override string CustomVisualPath=>"res://scenes/creature_visuals/ironclad.tscn";
  public override string CustomTrailPath=>"res://scenes/vfx/card_trail_ironclad.tscn";
  public override string CustomIconTexturePath=>TerraArt.Resource(CampaignRules.Armor(Hero)[0]);
  public override string CustomIconOutlineTexturePath=>CustomIconTexturePath;
  public override Control CustomIcon=>new TextureRect{Texture=TerraArt.Load("portrait-"+Hero),Size=new Vector2(64,64),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};
  public override string CustomIconPath=>"res://scenes/ui/character_icons/ironclad_icon.tscn";
  public override string CustomEnergyCounterPath=>"res://scenes/combat/energy_counters/ironclad_energy_counter.tscn";
  public override string CustomRestSiteAnimPath=>"res://scenes/rest_site/characters/ironclad_rest_site.tscn";
  public override string CustomMerchantAnimPath=>"res://scenes/merchant/characters/ironclad_merchant.tscn";
  public override string CustomCharacterSelectBg=>"res://scenes/screens/char_select/char_select_bg_ironclad.tscn";
  public override string CustomCharacterSelectIconPath=>"res://images/packed/character_select/char_select_ironclad.png";
  public override string CustomCharacterSelectLockedIconPath=>"res://images/packed/character_select/char_select_ironclad_locked.png";
  public override string CustomCharacterSelectTransitionPath=>"res://materials/transitions/ironclad_transition_mat.tres";
  public override string CustomMapMarkerPath=>"res://images/packed/map/icons/map_marker_ironclad.png";
  public override string CustomArmPointingTexturePath=>"res://images/ui/hands/multiplayer_hand_ironclad_point.png";
  public override string CustomArmRockTexturePath=>"res://images/ui/hands/multiplayer_hand_ironclad_rock.png";
  public override string CustomArmPaperTexturePath=>"res://images/ui/hands/multiplayer_hand_ironclad_paper.png";
  public override string CustomArmScissorsTexturePath=>"res://images/ui/hands/multiplayer_hand_ironclad_scissors.png";
  public override string CharacterSelectSfx=>"event:/sfx/characters/ironclad/ironclad_select";
  public override string CharacterTransitionSfx=>"event:/sfx/ui/wipe_ironclad";
  public override List<string> GetArchitectAttackVfx()=>new List<string>();
 }
 public sealed class TerraWarrior:Terrarian {public override HeroClass Hero=>HeroClass.Warrior;}
 public sealed class TerraRanger:Terrarian {public override HeroClass Hero=>HeroClass.Ranger;}
 public sealed class TerraMage:Terrarian {public override HeroClass Hero=>HeroClass.Mage;}
 public sealed class TerraSummoner:Terrarian {public override HeroClass Hero=>HeroClass.Summoner;}

 public abstract class TerraCardPool:CardPoolModel {
  public abstract HeroClass Hero {get;}
  public override string Title=>"Ironclad";
  public override string EnergyColorName=>"ironclad";
  public override string CardFrameMaterialPath=>"card_frame_red";
  public override Color DeckEntryCardColor=>new Color("628d55");
  public override bool IsColorless=>false;
  protected override CardModel[] GenerateAllCards()=>EquipmentCatalog.All.Where(x=>x.Hero==Hero).Select(x=>(CardModel)TerraEquipment.Canonical(x.Key)).Concat(new CardModel[]{ModelDb.Card<TerraGrenades>(),ModelDb.Card<TerraBombs>(),ModelDb.Card<TerraDynamite>()}).ToArray();
 }
 public sealed class WarriorPool:TerraCardPool {public override HeroClass Hero=>HeroClass.Warrior;}
 public sealed class RangerPool:TerraCardPool {public override HeroClass Hero=>HeroClass.Ranger;}
 public sealed class MagePool:TerraCardPool {public override HeroClass Hero=>HeroClass.Mage;}
 public sealed class SummonerPool:TerraCardPool {public override HeroClass Hero=>HeroClass.Summoner;}

 public abstract class TerraEquipment:CustomCardModel {
  public abstract string GearKey {get;}
  public EquipmentDef Definition=>EquipmentCatalog.Get(GearKey);
  public int Stat=>Definition.IsArmor?2:0;
  public HeroClass Hero=>Definition.Hero;
  public int ItemId=>Definition.Items[0];
  public override CardPoolModel Pool=>PoolFor(Hero);
  public override CardPoolModel VisualCardPool=>ModelDb.CardPool<WarriorPool>();
  public override string Title=>Definition.Name+(CurrentUpgradeLevel>0?" +"+CurrentUpgradeLevel:"");
  public override CardRarity Rarity=>Definition.Rarity==3?CardRarity.Rare:Definition.Rarity==2?CardRarity.Uncommon:CardRarity.Common;
  public override CardType Type=>Stat==0?CardType.Attack:CardType.Skill;
  public override int MaxUpgradeLevel=>int.MaxValue;
  public override Texture2D CustomPortrait=>TerraArt.Card(ItemId);
  public override string PortraitPath=>CardModel.MissingPortraitPath;
  public override IEnumerable<string> AllPortraitPaths=>Array.Empty<string>();
  public override List<(string,string)> Localization=>new List<(string,string)>{("title",Definition.Name),("description","{EquipmentText}"),("reward","选择一件武器或护甲")};
  public TerraEquipment():base(0,CardType.Skill,CardRarity.Common,TargetType.Self,true,false){}
  protected override void AddExtraArgsToDescription(LocString d){d.Add("EquipmentText",EquipmentCatalog.RarityName(Definition.Rarity)+" · 第 "+Definition.Stage+" 幕 · "+CampaignRules.ClassName(Hero)+"\n"+(Stat==0?"装备武器；最多携带三把。":"替换整套护甲，保留原版套装效果。")+"\n每级锻造增加原始"+(Stat==0?"伤害":"防御")+"的 20%，仅此装备生效。\n当前锻造加成："+(CurrentUpgradeLevel*20L)+"%。");}
  public static TerraEquipment Canonical(string key)=>(TerraEquipment)typeof(ModelDb).GetMethod("Card").MakeGenericMethod(typeof(TerraEquipment).Assembly.GetType("SpireTerraria."+key)).Invoke(null,null);
  public static TerraEquipment WeaponFor(HeroClass c)=>Canonical(EquipmentCatalog.Starters(c)[0]);
  public static CardPoolModel PoolFor(HeroClass c)=>c==HeroClass.Warrior?ModelDb.CardPool<WarriorPool>():c==HeroClass.Ranger?ModelDb.CardPool<RangerPool>():c==HeroClass.Mage?ModelDb.CardPool<MagePool>():ModelDb.CardPool<SummonerPool>();
 }
 public abstract class TerraThrowable:CustomCardModel {
  public abstract int ItemId {get;}public abstract string Name {get;}public abstract int Quantity {get;}
  public override string Title=>Name+" ×"+Quantity;
  public override CardPoolModel Pool=>ModelDb.CardPool<WarriorPool>();
  public override CardPoolModel VisualCardPool=>ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>();
  public override int MaxUpgradeLevel=>0;
  public override Texture2D CustomPortrait=>TerraArt.Card(ItemId);
  public override string PortraitPath=>CardModel.MissingPortraitPath;
  public override IEnumerable<string> AllPortraitPaths=>Array.Empty<string>();
  public override List<(string,string)> Localization=>new List<(string,string)>{("title",Name),("prompt","{Text}"),("description","获得 "+Quantity+" 个"+Name+"。\n通用消耗品，不占武器槽。\n战斗中按 4 / 5 / 6 选择，鼠标投掷。\n爆炸可能伤到自己。")};
  protected TerraThrowable():base(0,CardType.Skill,CardRarity.Uncommon,TargetType.Self,true,false){}
 }
 public sealed class TerraGrenades:TerraThrowable {public override int ItemId=>168;public override string Name=>"手榴弹";public override int Quantity=>8;}
 public sealed class TerraBombs:TerraThrowable {public override int ItemId=>166;public override string Name=>"炸弹";public override int Quantity=>5;}
 public sealed class TerraDynamite:TerraThrowable {public override int ItemId=>167;public override string Name=>"雷管";public override int Quantity=>3;public override CardRarity Rarity=>CardRarity.Rare;}

 public abstract class TerraAccessory:CustomRelicModel {
  public abstract int ItemId {get;}
  public abstract string Name {get;}
  public abstract string Effect {get;}
  public virtual int MinStage=>ItemId==490||ItemId==491||ItemId==489||ItemId==2998||ItemId==536||ItemId==554||ItemId==555||ItemId==862||ItemId==485?2:ItemId==935||ItemId==1162||ItemId==3811||ItemId==3812?3:1;
  public TerraAccessory():base(false){}
  public override RelicRarity Rarity=>RelicRarity.Common;
  public override string PackedIconPath=>TerraArt.Resource(ItemId);
  protected override string PackedIconOutlinePath=>TerraArt.Resource(ItemId);
  protected override string BigIconPath=>TerraArt.Resource(ItemId);
  public override List<(string,string)> Localization=>new List<(string,string)>{("title",Name),("description",Effect+"\n自动装备，不占泰拉瑞亚原版饰品槽。"),("flavor","来自泰拉瑞亚的战利品。")};
 }
 public sealed class HermesBoots:TerraAccessory {public override int ItemId=>54;public override string Name=>"赫尔墨斯靴";public override string Effect=>"持续奔跑后提高移动速度。";}
 public sealed class CloudBottle:TerraAccessory {public override int ItemId=>53;public override string Name=>"云朵瓶";public override string Effect=>"可以在空中再次跳跃。";}
 public sealed class BandRegeneration:TerraAccessory {public override int ItemId=>49;public override string Name=>"再生手环";public override string Effect=>"提高生命再生速度。";}
 public sealed class WarriorEmblem:TerraAccessory {public override int ItemId=>490;public override string Name=>"战士徽章";public override string Effect=>"近战伤害提高 15%。";}
 public sealed class RangerEmblem:TerraAccessory {public override int ItemId=>491;public override string Name=>"游侠徽章";public override string Effect=>"远程伤害提高 15%。";}
 public sealed class MageEmblem:TerraAccessory {public override int ItemId=>489;public override string Name=>"巫士徽章";public override string Effect=>"魔法伤害提高 15%。";}
 public sealed class SummonerEmblem:TerraAccessory {public override int ItemId=>2998;public override string Name=>"召唤师徽章";public override string Effect=>"召唤伤害提高 15%。";}
 public sealed class Shackle:TerraAccessory {public override int ItemId=>216;public override string Name=>"脚镣";public override string Effect=>"防御提高 1。";}
 public sealed class Aglet:TerraAccessory {public override int ItemId=>285;public override string Name=>"疾风脚镯";public override string Effect=>"移动速度提高 5%。";}
 public sealed class Anklet:TerraAccessory {public override int ItemId=>212;public override string Name=>"疾风之靴";public override string Effect=>"移动速度提高 10%。";}
 public sealed class SharkTooth:TerraAccessory {public override int ItemId=>3212;public override string Name=>"鲨牙项链";public override string Effect=>"提高护甲穿透。";}
 public sealed class AvengerEmblem:TerraAccessory {public override int ItemId=>935;public override string Name=>"复仇者徽章";public override string Effect=>"所有伤害提高 12%。";}
 public sealed class ApprenticeScarf:TerraAccessory {public override int ItemId=>3809;public override string Name=>"学徒围巾";public override string Effect=>"增加哨兵上限并强化召唤伤害。";}
 public sealed class SquireShield:TerraAccessory {public override int ItemId=>3810;public override string Name=>"侍卫护盾";public override string Effect=>"增加哨兵上限并强化召唤伤害。";}
 public sealed class HuntressBuckler:TerraAccessory {public override int ItemId=>3811;public override string Name=>"女猎人圆盾";public override string Effect=>"增加哨兵上限并强化召唤伤害。";}
 public sealed class MonkBelt:TerraAccessory {public override int ItemId=>3812;public override string Name=>"武僧腰带";public override string Effect=>"增加哨兵上限并强化召唤伤害。";}
 public sealed class CobaltShieldAccessory:TerraAccessory {public override int ItemId=>156;public override string Name=>"钴护盾";public override string Effect=>"免疫击退，增加防御。";}
 public sealed class ObsidianShieldAccessory:TerraAccessory {public override int ItemId=>397;public override string Name=>"黑曜石护盾";public override string Effect=>"免疫击退与灼热物块，增加防御。";}
 public sealed class SpectreBootsAccessory:TerraAccessory {public override int ItemId=>405;public override string Name=>"幽灵靴";public override string Effect=>"奔跑加速并短暂飞行。";}
 public sealed class FeralClawsAccessory:TerraAccessory {public override int ItemId=>211;public override string Name=>"猛爪手套";public override string Effect=>"提高近战及鞭子的攻击速度。";}
 public sealed class TitanGloveAccessory:TerraAccessory {public override int ItemId=>536;public override string Name=>"强力手套";public override string Effect=>"提高近战击退与武器大小。";}
 public sealed class ManaBandAccessory:TerraAccessory {public override int ItemId=>982;public override string Name=>"魔力再生手环";public override string Effect=>"提高魔力上限与魔力再生。";}
 public sealed class MagicCuffsAccessory:TerraAccessory {public override int ItemId=>1595;public override string Name=>"魔法手铐";public override string Effect=>"提高魔力上限，受伤时恢复魔力。";}
 public sealed class ManaFlowerAccessory:TerraAccessory {public override int ItemId=>555;public override string Name=>"魔力花";public override string Effect=>"降低魔力消耗。补充药水仍由尖塔药水栏管理。";}
 public sealed class PygmyNecklaceAccessory:TerraAccessory {public override int ItemId=>1158;public override string Name=>"矮人项链";public override string Effect=>"召唤物上限增加一。";}
 public sealed class CrossNecklaceAccessory:TerraAccessory {public override int ItemId=>554;public override string Name=>"十字项链";public override string Effect=>"延长受伤后的无敌时间。";}
 public sealed class StarVeilAccessory:TerraAccessory {public override int ItemId=>862;public override string Name=>"星星面纱";public override string Effect=>"延长受伤后的无敌时间，受伤时落下星星。";}
 public sealed class MoonCharmAccessory:TerraAccessory {public override int ItemId=>485;public override string Name=>"月亮护符";public override string Effect=>"夜间变为狼人并增强身体能力。";}
 public sealed class TerraRelicPool:RelicPoolModel {
  public override string EnergyColorName=>"ironclad";
  protected override IEnumerable<RelicModel> GenerateAllRelics()=>new RelicModel[]{ModelDb.Relic<HermesBoots>(),ModelDb.Relic<CloudBottle>(),ModelDb.Relic<BandRegeneration>(),ModelDb.Relic<WarriorEmblem>(),ModelDb.Relic<RangerEmblem>(),ModelDb.Relic<MageEmblem>(),ModelDb.Relic<SummonerEmblem>(),ModelDb.Relic<Shackle>(),ModelDb.Relic<Aglet>(),ModelDb.Relic<Anklet>(),ModelDb.Relic<SharkTooth>(),ModelDb.Relic<AvengerEmblem>(),ModelDb.Relic<ApprenticeScarf>(),ModelDb.Relic<SquireShield>(),ModelDb.Relic<HuntressBuckler>(),ModelDb.Relic<MonkBelt>(),ModelDb.Relic<CobaltShieldAccessory>(),ModelDb.Relic<ObsidianShieldAccessory>(),ModelDb.Relic<SpectreBootsAccessory>(),ModelDb.Relic<FeralClawsAccessory>(),ModelDb.Relic<TitanGloveAccessory>(),ModelDb.Relic<ManaBandAccessory>(),ModelDb.Relic<MagicCuffsAccessory>(),ModelDb.Relic<ManaFlowerAccessory>(),ModelDb.Relic<PygmyNecklaceAccessory>(),ModelDb.Relic<CrossNecklaceAccessory>(),ModelDb.Relic<StarVeilAccessory>(),ModelDb.Relic<MoonCharmAccessory>()};
 }
 public abstract class TerraPotion:PotionModel,BaseLib.Abstracts.ILocalizationProvider {
  public abstract int Kind {get;}
  public override PotionRarity Rarity=>PotionRarity.Common;
  public override PotionUsage Usage=>PotionUsage.CombatOnly;
  public override TargetType TargetType=>TargetType.Self;
  public override bool PassesCustomUsabilityCheck=>Plugin.Realtime&&Plugin.PotionReady(Kind);
  public List<(string,string)> Localization=>new List<(string,string)>{("title",Kind==0?"治疗药水":"魔力药水"),("description",Kind==0?"恢复 100 生命。\n受到 60 秒药水虚弱限制。":"恢复 100 魔力。\n施加魔力病 5 秒。")};
  protected override Task OnUse(PlayerChoiceContext c,Creature t)=>Plugin.Drink(Kind);
 }
 public sealed class TerraHealingPotion:TerraPotion {public override int Kind=>0;}
 public sealed class TerraManaPotion:TerraPotion {public override int Kind=>1;}
 public sealed class TerraPotionPool:PotionPoolModel {
  public override string EnergyColorName=>"ironclad";
  protected override IEnumerable<PotionModel> GenerateAllPotions()=>new PotionModel[]{ModelDb.Potion<TerraHealingPotion>(),ModelDb.Potion<TerraManaPotion>()};
 }
 // The native worker owns enemy AI. One silent Spire creature carries its room lifecycle.
 public abstract class TerraEncounter:EncounterModel {
  public override IEnumerable<MonsterModel> AllPossibleMonsters=>new[]{ModelDb.Monster<LeafSlimeS>()};
  protected override IReadOnlyList<(MonsterModel,string)> GenerateMonsters()=>new[]{(ModelDb.Monster<LeafSlimeS>().ToMutable(),(string)null)};
 }
 public sealed class TerraNormal:TerraEncounter {public override RoomType RoomType=>RoomType.Monster;}
 public sealed class TerraElite:TerraEncounter {public override RoomType RoomType=>RoomType.Elite;}
 public sealed class TerraBoss:TerraEncounter {public override RoomType RoomType=>RoomType.Boss;}
}

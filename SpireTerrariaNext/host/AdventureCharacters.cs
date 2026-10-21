using System;
using System.Collections.Generic;
using System.Linq;
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace SpireTerraria.Next.Host {
 // BaseLib owns registration, localization and the native character selection flow.
 // Combat and item effects are supplied exclusively by the independent worker.
 public abstract class AdventureCharacter : CustomCharacterModel {
  public abstract HeroClass Hero {get;}
  public override bool AllowInVanillaRandomCharacterSelect=>false;
  public override Color NameColor=>AdventureCharacters.HeroColor(Hero);
  public override CharacterGender Gender=>CharacterGender.Masculine;
  public override int StartingHp=>300;
  public override CardPoolModel CardPool=>AdventureCharacters.PoolFor(Hero);
  public override RelicPoolModel RelicPool=>ModelDb.RelicPool<AdventureRelicPool>();
  public override PotionPoolModel PotionPool=>ModelDb.PotionPool<AdventurePotionPool>();
  public override IEnumerable<CardModel> StartingDeck=>AdventureInventoryModels.StarterCards(Hero);
  public override IReadOnlyList<RelicModel> StartingRelics=>AdventureInventoryModels.StarterRelics(Hero).ToArray();
  public override IReadOnlyList<PotionModel> StartingPotions=>AdventureInventoryModels.StarterPotions(Hero).ToArray();
  public override List<(string,string)> Localization=>new List<(string,string)> {
   ("title","泰拉瑞亚 · "+GearCatalog.HeroName(Hero)),("titleObject","泰拉旅人"),
   ("description",AdventureCharacters.Description(Hero)+"\n沿尖塔路线探索，用武器、防具、饰品和药水迎战。\n初始生命 300，每幕提高初始值的 100%。"),
   ("pronounObject","他"),("pronounSubject","他"),("pronounPossessive","他的"),("possessiveAdjective","他的"),
   ("bestiaryQuote","从泰拉瑞亚启程，向尖塔深处进发。"),("cardsModifierTitle","装备"),("cardsModifierDescription","你的武器、防具与通用投掷物")
  };
  // Native scenes satisfy non-combat animation contracts; no Ironclad model,
  // starting cards, relic effects or potions are inherited by these characters.
  public override string CustomVisualPath=>"res://scenes/creature_visuals/ironclad.tscn";
  public override string CustomTrailPath=>"res://scenes/vfx/card_trail_ironclad.tscn";
  public override string CustomIconTexturePath=>"res://images/packed/character_select/char_select_ironclad.png";
  public override string CustomIconOutlineTexturePath=>CustomIconTexturePath;
  public override Control CustomIcon=>new TextureRect {Texture=AdventureCharacters.Icon(Hero),CustomMinimumSize=new Vector2(64,64),Size=new Vector2(64,64),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};
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
 public sealed class AdventureWarrior:AdventureCharacter {public override HeroClass Hero=>HeroClass.Warrior;}
 public sealed class AdventureRanger:AdventureCharacter {public override HeroClass Hero=>HeroClass.Ranger;}
 public sealed class AdventureMage:AdventureCharacter {public override HeroClass Hero=>HeroClass.Mage;}
 public sealed class AdventureSummoner:AdventureCharacter {public override HeroClass Hero=>HeroClass.Summoner;}

 public abstract class AdventureCardPool:CardPoolModel {
  public abstract HeroClass Hero {get;}
  public override string Title=>"泰拉瑞亚 · "+GearCatalog.HeroName(Hero);
  public override string EnergyColorName=>"ironclad";
  public override string CardFrameMaterialPath=>"card_frame_red";
  public override Color DeckEntryCardColor=>AdventureCharacters.HeroColor(Hero);
  public override bool IsColorless=>false;
  protected override CardModel[] GenerateAllCards()=>AdventureInventoryModels.CardPoolEntries(Hero).ToArray();
 }
 public sealed class AdventureWarriorPool:AdventureCardPool {public override HeroClass Hero=>HeroClass.Warrior;}
 public sealed class AdventureRangerPool:AdventureCardPool {public override HeroClass Hero=>HeroClass.Ranger;}
 public sealed class AdventureMagePool:AdventureCardPool {public override HeroClass Hero=>HeroClass.Mage;}
 public sealed class AdventureSummonerPool:AdventureCardPool {public override HeroClass Hero=>HeroClass.Summoner;}
 public sealed class AdventureRelicPool:RelicPoolModel {
  public override string EnergyColorName=>"ironclad";
  protected override IEnumerable<RelicModel> GenerateAllRelics()=>AdventureInventoryModels.RelicPoolEntries();
 }
 public sealed class AdventurePotionPool:PotionPoolModel {
  public override string EnergyColorName=>"ironclad";
  protected override IEnumerable<PotionModel> GenerateAllPotions()=>AdventureInventoryModels.PotionPoolEntries();
 }

 internal static class AdventureCharacters {
  static readonly Dictionary<HeroClass,Texture2D> badges=new();
  static bool installed;
  internal static void Install(Harmony harmony) {
   if(installed)return;installed=true;
   CustomCharacterUtils.TryOrderCustomCharacters<AdventureWarrior,AdventureRanger,AdventureMage,AdventureSummoner>();
   foreach(string property in new[]{"IconTexture","IconOutlineTexture"})harmony.Patch(AccessTools.PropertyGetter(typeof(CharacterModel),property),prefix:new HarmonyMethod(typeof(AdventureCharacters),nameof(CharacterTexture)));
   harmony.Patch(AccessTools.Method(typeof(NCharacterSelectButton),"Init"),postfix:new HarmonyMethod(typeof(AdventureCharacters),nameof(CharacterButton)));
   harmony.Patch(AccessTools.Method(typeof(NCharacterSelectScreen),"OnSubmenuOpened"),postfix:new HarmonyMethod(typeof(AdventureCharacters),nameof(RefreshSelectionIcons)));
   harmony.Patch(AccessTools.Method(typeof(NCharacterSelectScreen),"SelectCharacter"),postfix:new HarmonyMethod(typeof(AdventureCharacters),nameof(CharacterBackground)));
  }
  internal static bool IsTerrariaCharacter(CharacterModel character)=>character is AdventureCharacter;
  internal static bool TryGetHero(CharacterModel character,out HeroClass hero){if(character is AdventureCharacter adventure){hero=adventure.Hero;return true;}hero=default;return false;}
  internal static AdventureCharacter Canonical(HeroClass hero)=>hero switch {
   HeroClass.Warrior=>ModelDb.Character<AdventureWarrior>(),HeroClass.Ranger=>ModelDb.Character<AdventureRanger>(),HeroClass.Mage=>ModelDb.Character<AdventureMage>(),HeroClass.Summoner=>ModelDb.Character<AdventureSummoner>(),_=>throw new ArgumentOutOfRangeException(nameof(hero))
  };
  internal static CardPoolModel PoolFor(HeroClass hero)=>hero switch {
   HeroClass.Warrior=>ModelDb.CardPool<AdventureWarriorPool>(),HeroClass.Ranger=>ModelDb.CardPool<AdventureRangerPool>(),HeroClass.Mage=>ModelDb.CardPool<AdventureMagePool>(),HeroClass.Summoner=>ModelDb.CardPool<AdventureSummonerPool>(),_=>throw new ArgumentOutOfRangeException(nameof(hero))
  };
  internal static Color HeroColor(HeroClass hero)=>new Color(hero==HeroClass.Warrior?"cfb075":hero==HeroClass.Ranger?"8dc48d":hero==HeroClass.Mage?"8eb9e2":"c29bd9");
  internal static string Description(HeroClass hero)=>hero switch {
   HeroClass.Warrior=>"挥动附魔剑，近身迎击怪物。\n专属成长：近战武器与战士护甲。",
   HeroClass.Ranger=>"用金弓保持距离，寻找射击机会。\n专属成长：弓枪与射手护甲。",
   HeroClass.Mage=>"用水矢掌控战场，合理调度魔力。\n专属成长：法杖、魔法书与法师护甲。",
   HeroClass.Summoner=>"以雀杖召唤伙伴，以皮鞭指挥进攻。\n专属成长：召唤武器、鞭子与召唤师护甲。",_=>throw new ArgumentOutOfRangeException(nameof(hero))
  };
  internal static Texture2D Icon(HeroClass hero) {
   string symbol=hero==HeroClass.Warrior?"EnchantedSword":hero==HeroClass.Ranger?"GoldBow":hero==HeroClass.Mage?"WaterBolt":"BabyBirdStaff";
   return AdventureHostUi.ItemIcon(symbol)??Badge(hero);
  }
  static bool CharacterTexture(CharacterModel __instance,ref Texture2D __result){if(__instance is not AdventureCharacter character)return true;__result=Icon(character.Hero);return false;}
  static void CharacterButton(NCharacterSelectButton __instance,CharacterModel character) {
   if(character is not AdventureCharacter adventure)return;
   if(AccessTools.Field(typeof(NCharacterSelectButton),"_icon")?.GetValue(__instance) is TextureRect icon){icon.Texture=Icon(adventure.Hero);icon.Material=null;icon.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;}
  }
  static void RefreshSelectionIcons(NCharacterSelectScreen __instance){RefreshSelectionIconsIn(__instance);}
  static void CharacterBackground(NCharacterSelectScreen __instance,CharacterModel characterModel) {
   if(AccessTools.Field(typeof(NCharacterSelectScreen),"_bgContainer")?.GetValue(__instance) is not Control container)return;
   var art=container.GetNodeOrNull<TextureRect>("SpireNextCharacterArt");
   if(characterModel is not AdventureCharacter character){if(art!=null)art.Visible=false;return;}
   var texture=AdventureHostUi.CharacterScene(character.Hero);if(texture==null)return;
   if(art==null){art=new TextureRect{Name="SpireNextCharacterArt",ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCovered,MouseFilter=Control.MouseFilterEnum.Ignore,TextureFilter=CanvasItem.TextureFilterEnum.Nearest};container.AddChild(art);art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);}
   art.Texture=texture;art.Visible=true;container.MoveChild(art,container.GetChildCount()-1);
  }
  static void RefreshSelectionIconsIn(Node node) {
   if(node is NCharacterSelectButton button&&button.Character is AdventureCharacter)CharacterButton(button,button.Character);
   foreach(Node child in node.GetChildren())RefreshSelectionIconsIn(child);
  }
  // A small code-owned class emblem is available before local item art is loaded.
  // It carries no dependencies on extracted textures or the previous bridge.
  static Texture2D Badge(HeroClass hero) {
   if(badges.TryGetValue(hero,out var texture))return texture;
   using var image=Image.CreateEmpty(64,64,false,Image.Format.Rgba8);image.Fill(Colors.Transparent);
   Color ink=HeroColor(hero),edge=new Color("253a37"),light=new Color("f3e8c3");
   void Fill(int x,int y,int width,int height,Color color)=>image.FillRect(new Rect2I(x,y,width,height),color);
   Fill(4,4,56,56,edge);Fill(7,7,50,50,new Color("15211f"));Fill(7,7,50,2,ink);Fill(7,55,50,2,ink);
   if(hero==HeroClass.Warrior){for(int i=0;i<7;i++)Fill(16+i*4,40-i*4,6,6,light);Fill(13,39,16,4,ink);Fill(17,35,4,16,ink);Fill(12,46,6,6,ink);}
   else if(hero==HeroClass.Ranger){for(int i=0;i<8;i++){int x=23+(int)Math.Round(Math.Sin(i*Math.PI/7)*14);Fill(x,16+i*4,4,5,ink);}Fill(23,16,2,32,light);Fill(14,30,35,3,light);Fill(44,26,4,11,ink);}
   else if(hero==HeroClass.Mage){Fill(18,22,27,24,ink);Fill(21,24,21,19,edge);Fill(30,24,3,19,ink);Fill(24,28,3,9,light);Fill(36,29,3,7,light);Fill(39,14,3,8,light);Fill(36,17,9,2,light);}
   else{Fill(30,16,4,30,ink);Fill(22,24,20,14,ink);Fill(18,28,28,6,ink);Fill(26,26,12,10,light);Fill(28,29,2,3,edge);Fill(35,29,2,3,edge);}
   texture=ImageTexture.CreateFromImage(image);badges.Add(hero,texture);return texture;
  }
 }
}

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs.History;

namespace SpireTerraria {
 internal static class TerraArt {
  static readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
  internal static Texture2D Load(string file){if(textures.TryGetValue(file,out var t))return t;string path=Path.Combine(Plugin.Root,"assets","ui",file+".png");if(!File.Exists(path))path=Path.Combine(Plugin.Root,"assets","ui","item-29.png");using(var img=Image.LoadFromFile(path))t=ImageTexture.CreateFromImage(img);textures[file]=t;return t;}
  internal static Texture2D Item(int id)=>Load("item-"+id);
  internal static string Resource(int id){string path=Path.Combine(Plugin.Root,"assets","ui","native-"+id+".tres");if(!File.Exists(path)){var result=ResourceSaver.Save(Item(id),path);if(result!=Error.Ok)throw new IOException("Native texture resource: "+result);}return path;}
  internal static Texture2D Card(int id){string key="card-"+id;if(textures.TryGetValue(key,out var t))return t;using(var img=Image.CreateEmpty(500,380,false,Image.Format.Rgba8)){
   img.Fill(new Color("1c2b32"));using(var bg=Load("background-7").GetImage()){bg.Resize(500,300,Image.Interpolation.Nearest);img.BlitRect(bg,new Rect2I(0,0,500,300),new Vector2I(0,80));}
   using(var item=Item(id).GetImage()){int factor=Math.Max(2,Math.Min(190/item.GetWidth(),190/item.GetHeight()));item.Resize(item.GetWidth()*factor,item.GetHeight()*factor,Image.Interpolation.Nearest);img.BlendRect(item,new Rect2I(0,0,item.GetWidth(),item.GetHeight()),new Vector2I((500-item.GetWidth())/2,(380-item.GetHeight())/2));}t=ImageTexture.CreateFromImage(img);
  }textures[key]=t;return t;}
 }
 internal static class TerraIntegration {
  internal static void Install(Harmony h){
   void Patch(Type t,string method,string prefix=null,string postfix=null){h.Patch(AccessTools.Method(t,method),prefix:prefix==null?null:new HarmonyMethod(typeof(TerraIntegration),prefix),postfix:postfix==null?null:new HarmonyMethod(typeof(TerraIntegration),postfix));}
   Patch(typeof(NGame),"StartNewSingleplayerRun",nameof(Start),nameof(Started));
   Patch(typeof(RewardsSet),"WithRewardsFromRoom",nameof(Rewards));
   h.Patch(AccessTools.Constructor(typeof(CombatRoom),new[]{typeof(EncounterModel),typeof(IRunState)}),prefix:new HarmonyMethod(typeof(TerraIntegration),nameof(Encounter)));
   Patch(typeof(NCharacterSelectButton),"Init",postfix:nameof(CharacterButton));
   Patch(typeof(NCharacterSelectScreen),"SelectCharacter",postfix:nameof(CharacterBackground));
   Patch(typeof(NCardRewardSelectionScreen),"_Ready",postfix:nameof(GearHeader));
   Patch(typeof(TreasureRoomRelicSynchronizer),"BeginRelicPicking",postfix:nameof(TreasureAccessories));
   Patch(typeof(TreasureRoom),"DoExtraRewardsIfNeeded",nameof(TreasureExtraRewards));
   foreach(var p in new[]{"Icon","IconOutline","BigIcon"})h.Patch(AccessTools.PropertyGetter(typeof(RelicModel),p),prefix:new HarmonyMethod(typeof(TerraIntegration),nameof(RelicTexture)));
   foreach(var p in new[]{"Image","Outline","LargeImage"})h.Patch(AccessTools.PropertyGetter(typeof(PotionModel),p),prefix:new HarmonyMethod(typeof(TerraIntegration),nameof(PotionTexture)));
   foreach(var p in new[]{"ImagePath","OutlinePath","LargeImagePath","PackedImagePath","PackedOutlinePath"})h.Patch(AccessTools.PropertyGetter(typeof(PotionModel),p),prefix:new HarmonyMethod(typeof(TerraIntegration),nameof(PotionPath)));
   foreach(var p in new[]{"IconTexture","IconOutlineTexture"})h.Patch(AccessTools.PropertyGetter(typeof(CharacterModel),p),prefix:new HarmonyMethod(typeof(TerraIntegration),nameof(CharacterTexture)));
   TerraEconomy.Install(h);
   TerraEvents.Install(h);
  }
  static void Start(CharacterModel character,ref bool shouldSave,ref string seed){if(character is Terrarian t){shouldSave=false;if(JourneyTest.Enabled)seed="TERRARIA-V03-UI";Plugin.Begin(t);}}
  static void Started(CharacterModel character,ref Task<RunState> __result){if(character is Terrarian)__result=Plugin.FinishStart(__result);}
  static void Encounter(ref EncounterModel encounter,IRunState runState){if(runState?.Players.FirstOrDefault()?.Character is Terrarian)encounter=(encounter.RoomType==RoomType.Boss?(EncounterModel)ModelDb.Encounter<TerraBoss>():encounter.RoomType==RoomType.Elite?ModelDb.Encounter<TerraElite>():ModelDb.Encounter<TerraNormal>()).ToMutable();}
  static bool RelicTexture(RelicModel __instance,ref Texture2D __result){if(!(__instance is TerraAccessory r))return true;__result=TerraArt.Item(r.ItemId);return false;}
  static bool PotionTexture(PotionModel __instance,ref Texture2D __result){if(!(__instance is TerraPotion p))return true;__result=TerraArt.Item(p.Kind==0?188:189);return false;}
  static bool PotionPath(PotionModel __instance,ref string __result){if(!(__instance is TerraPotion p))return true;__result=TerraArt.Resource(p.Kind==0?188:189);return false;}
  static bool CharacterTexture(CharacterModel __instance,ref Texture2D __result){if(!(__instance is Terrarian t))return true;__result=TerraArt.Item(CampaignRules.Armor(t.Hero)[0]);return false;}
  static void GearHeader(NCardRewardSelectionScreen __instance){if(Plugin.Demo)__instance.GetNode<NCommonBanner>("UI/Banner").label.SetTextAutoSize("选择一件装备");}
  static void CharacterButton(NCharacterSelectButton __instance,CharacterModel character){if(!(character is Terrarian t))return;var icon=(TextureRect)AccessTools.Field(typeof(NCharacterSelectButton),"_icon").GetValue(__instance);icon.Texture=TerraArt.Card(CampaignRules.Weapons(t.Hero)[0]);icon.Material=null;icon.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;}
  static void CharacterBackground(NCharacterSelectScreen __instance,CharacterModel characterModel){
   if(!(characterModel is Terrarian t))return;var container=(Control)AccessTools.Field(typeof(NCharacterSelectScreen),"_bgContainer").GetValue(__instance);
   foreach(Node c in container.GetChildren()){container.RemoveChild(c);c.QueueFree();}
   var scene=new Control{MouseFilter=Control.MouseFilterEnum.Ignore};scene.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);container.AddChild(scene);
   var bg=new TextureRect{Texture=TerraArt.Load("preview-"+t.Hero),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCovered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};scene.AddChild(bg);bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
   var shade=new ColorRect{Color=new Color(.025f,.045f,.035f,.5f),MouseFilter=Control.MouseFilterEnum.Ignore};scene.AddChild(shade);shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
   var emblem=new TextureRect{Texture=TerraArt.Load("portrait-"+t.Hero),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};scene.AddChild(emblem);emblem.AnchorLeft=.67f;emblem.AnchorRight=.88f;emblem.AnchorTop=.23f;emblem.AnchorBottom=.72f;emblem.OffsetLeft=emblem.OffsetRight=emblem.OffsetTop=emblem.OffsetBottom=0;
  }
  static bool Rewards(RewardsSet __instance,AbstractRoom room,ref RewardsSet __result){
   var p=__instance.Player;if(!(p.Character is Terrarian t)||!(room is CombatRoom))return true;
   __instance.EmptyForRoom(room);
   var options=new CardCreationOptions(new[]{t.CardPool},CardCreationSource.Encounter,CardRarityOddsType.Uniform);
   var cards=TerraEconomy.RewardChoices(p,room.RoomType!=RoomType.Monster);
   __instance.Rewards.Add(new GoldReward(room.RoomType==RoomType.Boss?100:room.RoomType==RoomType.Elite?40:15,p));
   if(cards.Count>0)__instance.Rewards.Add(new TerraGearReward(cards,p,options));
   if(room.RoomType==RoomType.Elite||room.RoomType==RoomType.Boss){var relic=NextAccessory(p,t.Hero);if(relic!=null)__instance.Rewards.Add(new RelicReward(relic.ToMutable(),p));}
   if(p.Potions.Count()<p.MaxPotionCount){PotionModel potion=(t.Hero==HeroClass.Mage||t.Hero==HeroClass.Summoner)&&p.PlayerRng.Rewards.NextInt(3)==0?(PotionModel)ModelDb.Potion<TerraManaPotion>():ModelDb.Potion<TerraHealingPotion>();__instance.Rewards.Add(new PotionReward(potion.ToMutable(),p));}
   __result=__instance;Plugin.Log("NATIVE REWARDS room="+room.RoomType+" stage="+TerraEconomy.Stage(p)+" choices="+string.Join(",",cards.Cast<TerraEquipment>().Select(c=>c.GearKey)));return false;
  }
  internal static IEnumerable<TerraAccessory> AccessoryPool(Player p){
   if(!(p.Character is Terrarian t))return Array.Empty<TerraAccessory>();
   return ModelDb.RelicPool<TerraRelicPool>().AllRelics.OfType<TerraAccessory>().Where(r=>r.MinStage<=TerraEconomy.Stage(p)&&CampaignRules.IsAccessoryAllowed(r.ItemId,t.Hero)&&!p.Relics.Any(owned=>owned.Id==r.Id));
  }
  internal static TerraAccessory NextAccessory(Player p,HeroClass hero){var pool=AccessoryPool(p).Where(r=>CampaignRules.IsAccessoryAllowed(r.ItemId,hero)).ToArray();return pool.Length==0?null:pool[p.PlayerRng.Rewards.NextInt(pool.Length)];}
  static void TreasureAccessories(TreasureRoomRelicSynchronizer __instance){
   var player=(Player)AccessTools.Property(typeof(TreasureRoomRelicSynchronizer),"LocalPlayer").GetValue(__instance);if(!(player.Character is Terrarian t))return;
   var relics=(List<RelicModel>)AccessTools.Field(typeof(TreasureRoomRelicSynchronizer),"_currentRelics").GetValue(__instance);if(relics==null)return;
   relics.Clear();var accessory=NextAccessory(player,t.Hero);if(accessory!=null)relics.Add(accessory);
   Plugin.Log("TERRA CHEST stage="+TerraEconomy.Stage(player)+" accessory="+(accessory?.Name??"none; pool exhausted"));
  }
  static bool TreasureExtraRewards(TreasureRoom __instance,ref Task __result){
   var state=(IRunState)AccessTools.Field(typeof(TreasureRoom),"_runState").GetValue(__instance);if(!(state?.Players.FirstOrDefault()?.Character is Terrarian))return true;
   // The chest's own relic picker owns the reward. Opening a second rewards screen
   // first was duplicating the treasure flow and then showing the vanilla relic.
   __result=Task.CompletedTask;return false;
  }
  sealed class TerraGearReward:CardReward {
   bool historyRecorded;
   // This explicit-card base constructor does not subscribe OnRelicObtained. The
   // generated-card constructor does, so no inherited event subscription needs cleanup.
   public TerraGearReward(IEnumerable<CardModel> c,Player p,CardCreationOptions options):base(c,CardCreationSource.Encounter,p,options){}
   public override LocString Description=>TerraEconomy.Text("选择一件武器或护甲");
   protected override async Task<bool> OnSelect(){
    while(true){
     var options=Cards.Select(c=>new CardCreationResult(c)).ToList();
     var alternatives=CardRewardAlternative.Generate(this);var sync=RunManager.Instance.PlayerChoiceSynchronizer;uint choice=sync.ReserveChoiceId(Player);int? index;
     if(LocalContext.IsMe(Player)){
      NCardRewardSelectionScreen screen=null;
      try{
       screen=NCardRewardSelectionScreen.ShowScreen(options,alternatives);
       if(screen!=null)index=await screen.OptionSelected();
       else{var selected=CardSelectCmd.Selector?.GetSelectedCardReward(options,alternatives);if(!selected.HasValue)throw new InvalidOperationException("Terraria equipment selector unset during test");index=selected.Value.alternative!=null?options.Count+alternatives.ToList().IndexOf(selected.Value.alternative):selected.Value.card!=null?(int?)options.FindIndex(c=>c.Card==selected.Value.card):null;}
      }finally{
       // The reward screen must close before the deck replacement screen opens.
       // No card node is moved between overlays or animated as a vanilla card reward.
       if(screen!=null&&GodotObject.IsInstanceValid(screen)&&!screen.IsQueuedForDeletion())NOverlayStack.Instance?.Remove(screen);
      }
      sync.SyncLocalChoice(Player,choice,PlayerChoiceResult.FromIndex(index));
     }else index=(await sync.WaitForRemoteChoice(Player,choice)).AsIndexOrNull();
     if(!index.HasValue)return false;
     if(index.Value<0||index.Value>=options.Count+alternatives.Count)throw new InvalidOperationException("Invalid Terraria equipment reward choice");
     if(index.Value>=options.Count){var alternative=alternatives[index.Value-options.Count];await alternative.OnSelect();if(alternative.AfterSelected==PostAlternateCardRewardAction.DoNothing)continue;if(alternative.AfterSelected==PostAlternateCardRewardAction.EndSelectionAndCompleteReward){RecordHistory(null,null);return true;}return false;}
     var incoming=options[index.Value].Card;var result=await CardPileCmd.Add(incoming,PileType.Deck,skipVisuals:true);
     if(!result.success){Plugin.Log("TERRA REWARD replacement cancelled; reward retained");return false;}
     RecordHistory(result.cardAdded,incoming);Plugin.Log("TERRA REWARD acquired "+result.cardAdded.Title);return true;
    }
   }
   void RecordHistory(CardModel picked,CardModel offered){
    if(historyRecorded)return;historyRecorded=true;var history=Player.RunState.CurrentMapPointHistoryEntry?.GetEntry(Player.NetId);if(history==null)return;
    if(picked!=null)history.CardChoices.Add(new CardChoiceHistoryEntry(picked,wasPicked:true));foreach(var card in Cards)if(card!=offered)history.CardChoices.Add(new CardChoiceHistoryEntry(card,wasPicked:false));
   }
   public override void OnSkipped(){RecordHistory(null,null);}
  }
 }
}

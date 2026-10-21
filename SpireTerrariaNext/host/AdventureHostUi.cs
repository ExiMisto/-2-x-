using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpireTerraria.Next.Host {
 // Native card/room scenes own layout, focus, controller navigation and backdrops.
 // Choice cards and owned items are registered BaseLib models; the independent
 // adventure state remains authoritative for equipment and native effects.
 internal static class AdventureHostUi {
  internal sealed class Choice {
   internal string Title,Description,Kind;
   internal string NativeSymbol=null;
   internal object Value;
   internal Choice(string title,string description,string kind,object value=null){Title=title;Description=description;Kind=kind;Value=value;}
  }
  sealed class CardFace {internal Choice Choice;internal Texture2D Portrait;}
  static readonly ConditionalWeakTable<CardModel,CardFace> faces=new();
  static readonly Dictionary<string,string> texts=new();
  static readonly Dictionary<string,Texture2D> portraits=new();
  static readonly Dictionary<string,Texture2D> nativeIcons=new();
  static readonly Dictionary<HeroClass,Texture2D> characterScenes=new();
  static readonly ConditionalWeakTable<NPotionHolder,Action> inventoryPotionActions=new();
  static readonly ConditionalWeakTable<MerchantEntry,RewardOffer> shopOffers=new();
  static readonly ConditionalWeakTable<AbstractModel,Choice> shopModels=new();
  sealed class TreasureProjection {internal AdventureRun Run;internal string Node;internal RewardOffer Offer;internal bool Duplicate,Awarded;}
  static readonly ConditionalWeakTable<TreasureRoomRelicSynchronizer,TreasureProjection> treasureProjections=new();
  sealed class TerrariaEventPage {internal string Node,Theme,Title,Story,Portrait;internal bool Busy;}
  static readonly ConditionalWeakTable<EventModel,TerrariaEventPage> convertedEvents=new();
  static bool installed;
  static Task preloadTask;
  static int textId;
  internal static NChooseACardSelectionScreen CurrentSelection;
  internal static IReadOnlyList<CardModel> CurrentCards;
  internal static string SelectionPurpose;
  internal static Reward LastEquipmentReward;
  internal static Reward LastBossReward;
  internal static readonly Queue<int> TestChoices=new();
  static bool Active=>(!SpireSelfTest.Enabled||SpireSelfTest.AdventureEnabled)&&AdventureSession.HostRun?.Players.Count==1&&AdventureSession.HostRun.Players[0].Character is AdventureCharacter;
  static Player Player=>AdventureSession.HostRun?.Players.FirstOrDefault();
  internal static LocString Text(string text){string key=(++textId).ToString();texts[key]=text;return new LocString("spire_next",key);}

  internal static void Install(Harmony harmony) {
   if(installed)return;installed=true;
   Patch(harmony,typeof(LocString),"GetRawText",nameof(LiteralText));
   Patch(harmony,typeof(LocString),"GetFormattedText",nameof(LiteralText));
   Patch(harmony,typeof(LocString),"Exists",nameof(LiteralExists),false,Type.EmptyTypes);
   PatchGetter(harmony,typeof(CardModel),"Title",nameof(CardTitle));
   PatchGetter(harmony,typeof(CardModel),"TitleLocString",nameof(CardTitleLoc));
   PatchGetter(harmony,typeof(CardModel),"Description",nameof(CardDescription));
   PatchGetter(harmony,typeof(CardModel),"Portrait",nameof(CardPortrait));
   PatchGetter(harmony,typeof(CardModel),"Rarity",nameof(DisplayRarity));
   PatchGetter(harmony,typeof(CardModel),"HoverTips",nameof(ChoiceHoverTips));
   foreach(var type in new[]{typeof(RelicModel),typeof(PotionModel)}) {
    PatchGetter(harmony,type,"Title",nameof(ShopModelTitle));
    PatchGetter(harmony,type,"DynamicDescription",nameof(ShopModelDescription));
    PatchGetter(harmony,type,"HoverTips",nameof(InventoryHoverTips));
   }
   foreach(string property in new[]{"Icon","IconOutline","BigIcon"})PatchGetter(harmony,typeof(RelicModel),property,nameof(ShopModelTexture));
   foreach(string property in new[]{"Image","Outline","LargeImage"})PatchGetter(harmony,typeof(PotionModel),property,nameof(ShopModelTexture));
   Patch(harmony,typeof(CardModel),"GetDescriptionForPile",nameof(CardDescriptionText),false,new[]{typeof(PileType),typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature)});
   Patch(harmony,typeof(CardModel),"GetDescriptionForUpgradePreview",nameof(CardDescriptionText));
   Patch(harmony,typeof(NCard),"UpdateVisuals",nameof(CardVisuals),true);
   Patch(harmony,typeof(RewardsSet),"GenerateWithoutOffering",nameof(PrepareRewards));
   Patch(harmony,typeof(Reward),"FromSerializable",nameof(RestoreReward));
   Patch(harmony,typeof(RestSiteOption),"Generate",nameof(CampOptions),true);
   PatchGetter(harmony,typeof(RestSiteOption),"Title",nameof(CampTitle));
   PatchGetter(harmony,typeof(RestSiteOption),"Icon",nameof(CampIcon));
   PatchGetter(harmony,typeof(RestSiteOption),"IconPath",nameof(CampIconPath));
   Patch(harmony,typeof(MerchantInventory),"CreateForNormalMerchant",nameof(ShopCreated));
   foreach(var type in new[]{typeof(MerchantCardEntry),typeof(MerchantPotionEntry),typeof(MerchantRelicEntry)})Patch(harmony,type,"OnTryPurchase",nameof(ShopPurchase));
   Patch(harmony,typeof(MerchantCardRemovalEntry),"OnTryPurchase",nameof(DisableRemoval),false,new[]{typeof(MerchantInventory),typeof(bool)});
   Patch(harmony,typeof(TreasureRoomRelicSynchronizer),"BeginRelicPicking",nameof(TreasureAccessories),true);
   Patch(harmony,typeof(TreasureRoomRelicSynchronizer),"OnRoomExited",nameof(TreasureExited),true);
   Patch(harmony,typeof(TreasureRoom),"DoExtraRewardsIfNeeded",nameof(TreasureExtraRewards));
   Patch(harmony,typeof(NTreasureRoom),"OpenChest",nameof(TreasureOpened),true);
   Patch(harmony,typeof(NTreasureRoomRelicCollection),"InitializeRelics",nameof(TreasureInitialized),true);
   Patch(harmony,typeof(Player),"AddRelicInternal",nameof(TreasureAccessoryObtained),true);
   Patch(harmony,typeof(NMapScreen),"Open",nameof(MapOpened),true,new[]{typeof(bool)});
   foreach(var type in new[]{typeof(EventModel),typeof(AncientEventModel)})Patch(harmony,type,"GenerateInitialOptionsWrapper",nameof(EventOptions));
   PatchGetter(harmony,typeof(EventModel),"Title",nameof(EventTitle));
   Patch(harmony,typeof(EventModel),"CreateInitialPortrait",nameof(EventPortrait));
   Patch(harmony,typeof(NEventLayout),"SetPortrait",nameof(EventPortraitLayout),true);
   // Initial stories and layout types have overrides on several native events.
   // Patch declarations once, while keeping every original ancient portrait.
   foreach(var type in typeof(EventModel).Assembly.GetTypes().Where(t=>typeof(EventModel).IsAssignableFrom(t))) {
    foreach(var property in new[]{"InitialDescription","LayoutType"}) {
     var getter=type.GetProperty(property,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)?.GetMethod;
     if(getter!=null)harmony.Patch(getter,prefix:new HarmonyMethod(AccessTools.Method(typeof(AdventureHostUi),property=="InitialDescription"?nameof(EventStory):nameof(EventLayout))));
    }
    foreach(string name in new[]{"BeforeEventStarted","OnEventFinished"}) {
     var method=type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly);
     if(method==null||method.IsAbstract)continue;
     if(name=="OnEventFinished"&&type==typeof(EventModel))continue;
     harmony.Patch(method,prefix:new HarmonyMethod(AccessTools.Method(typeof(AdventureHostUi),name=="BeforeEventStarted"?nameof(EventBeforeStarted):nameof(EventFinishedHook))));
    }
   }
   GD.Print("[SpireNext Adventure] native rewards, camp, shop, chest and events installed");
  }
  static void Patch(Harmony h,Type type,string method,string callback,bool postfix=false,Type[] args=null) {
   var target=args==null?AccessTools.Method(type,method):AccessTools.Method(type,method,args);
   if(target==null)throw new MissingMethodException(type.FullName,method);
   var patch=new HarmonyMethod(AccessTools.Method(typeof(AdventureHostUi),callback));
   if(postfix)h.Patch(target,postfix:patch);else h.Patch(target,prefix:patch);
  }
  static void PatchGetter(Harmony h,Type type,string prop,string callback){var target=AccessTools.PropertyGetter(type,prop);if(target==null)throw new MissingMemberException(type.FullName,prop);h.Patch(target,prefix:new HarmonyMethod(AccessTools.Method(typeof(AdventureHostUi),callback)));}
  static bool LiteralText(LocString __instance,ref string __result){if(__instance.LocTable!="spire_next")return true;__result=texts.TryGetValue(__instance.LocEntryKey,out var value)?value:"";return false;}
  static bool LiteralExists(LocString __instance,ref bool __result){if(__instance.LocTable!="spire_next")return true;__result=true;return false;}
  static bool CardTitle(CardModel __instance,ref string __result){if(!faces.TryGetValue(__instance,out var face))return true;__result=face.Choice.Title;return false;}
  static bool CardTitleLoc(CardModel __instance,ref LocString __result){if(!faces.TryGetValue(__instance,out var face))return true;__result=Text(face.Choice.Title);return false;}
  static bool CardDescription(CardModel __instance,ref LocString __result){if(!faces.TryGetValue(__instance,out var face))return true;__result=Text(face.Choice.Description);return false;}
  static bool CardDescriptionText(CardModel __instance,ref string __result){if(!faces.TryGetValue(__instance,out var face))return true;__result=face.Choice.Description;return false;}
  static bool CardPortrait(CardModel __instance,ref Texture2D __result){if(!faces.TryGetValue(__instance,out var face))return true;__result=face.Portrait;return false;}
  static bool ChoiceHoverTips(CardModel __instance,ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result){if(!faces.TryGetValue(__instance,out _))return true;__result=Array.Empty<MegaCrit.Sts2.Core.HoverTips.IHoverTip>();return false;}
  static bool DisplayRarity(CardModel __instance,ref CardRarity __result){if(!faces.TryGetValue(__instance,out var face))return true;GearDefinition gear=face.Choice.Value is GearInstance instance?instance.Definition:face.Choice.Value is RewardOffer offer&&offer.GearKey!=""?GearCatalog.Get(offer.GearKey):null;__result=gear?.Rarity==GearRarity.Rare?CardRarity.Rare:gear?.Rarity==GearRarity.Uncommon?CardRarity.Uncommon:CardRarity.Common;return false;}
  static bool ShopModelTitle(AbstractModel __instance,ref LocString __result){if(__instance is AdventureAccessory)return true;if(!shopModels.TryGetValue(__instance,out var choice))return true;__result=Text(choice.Title);return false;}
  static bool ShopModelDescription(AbstractModel __instance,ref LocString __result){if(__instance is AdventureAccessory)return true;if(!shopModels.TryGetValue(__instance,out var choice))return true;__result=Text(choice.Description);return false;}
  static bool ShopModelTexture(AbstractModel __instance,ref Texture2D __result){if(!shopModels.TryGetValue(__instance,out var choice))return true;__result=ItemIcon(choice.NativeSymbol)??Portrait(choice.Kind);return false;}
  static bool InventoryHoverTips(AbstractModel __instance,ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result){if(__instance is AdventureAccessory)return true;if(!shopModels.TryGetValue(__instance,out var choice))return true;__result=new MegaCrit.Sts2.Core.HoverTips.IHoverTip[]{new MegaCrit.Sts2.Core.HoverTips.HoverTip(Text(choice.Title),Text(choice.Description))};return false;}
  internal static Texture2D ItemIcon(string symbol)=>symbol!=null&&nativeIcons.TryGetValue(symbol,out var texture)?texture:null;
  internal static string LocalIconPath(string symbol)=>ItemIcon(symbol)?.ResourcePath??CardModel.MissingPortraitPath;
  internal static Texture2D CharacterScene(HeroClass hero)=>characterScenes.TryGetValue(hero,out var texture)?texture:null;
  internal static async Task LoadBattleIcons(HostBridge bridge,BattleConfiguration config) {
   await LoadIcons(bridge,config.Weapons.Concat(config.Armor).Concat(config.Accessories).Concat(config.Consumables));
  }
  internal static async Task PreloadItemIcons() {
   var pending=preloadTask;if(pending==null||pending.IsFaulted||pending.IsCanceled){pending=PreloadIcons();preloadTask=pending;}
   try{await pending;}catch{if(ReferenceEquals(preloadTask,pending))preloadTask=null;throw;}
  }
  static async Task PreloadIcons() {
   string worker=System.IO.Path.Combine(HostIntegration.ModDirectory,"worker","SpireNext.Worker.exe"),game=System.IO.Path.GetFullPath(System.IO.Path.Combine(HostIntegration.ModDirectory,"..","..","..","Terraria"));
   using var bridge=await HostBridge.Open(worker,game);
   await LoadIcons(bridge,GearCatalog.All.SelectMany(g=>g.NativeSymbols).Concat(new[]{"IronAnvil","IronOre","Silk","Bone","CrystalShard","SoulofNight","GoldCoin"}));
   foreach(HeroClass hero in Enum.GetValues<HeroClass>()) {
    var scene=await bridge.Call(Operation.SceneArt,Protocol.Body(w=>w.Write((int)hero)));using var reader=Protocol.Reader(scene);
    int width=reader.ReadInt32(),height=reader.ReadInt32();if(width!=640||height!=360)throw new System.IO.InvalidDataException("Invalid local character art dimensions.");
    byte[] pixels=reader.ReadBytes(width*height*4);if(pixels.Length!=width*height*4)throw new System.IO.EndOfStreamException();Protocol.RequireEnd(reader);
    using var image=Image.CreateFromData(width,height,false,Image.Format.Rgba8,pixels);characterScenes[hero]=ImageTexture.CreateFromImage(image);
   }
   AdventureInventoryModels.RegisterPotionTextures();
   await bridge.Call(Operation.Shutdown);
   GD.Print("[SpireNext Adventure] local item icons ready: "+nativeIcons.Count);
  }
  static async Task LoadIcons(HostBridge bridge,IEnumerable<string> symbols) {
   string[] missing=symbols.Distinct().Where(s=>!nativeIcons.ContainsKey(s)).ToArray();
   for(int offset=0;offset<missing.Length;offset+=12){var batch=missing.Skip(offset).Take(12).ToArray();var packet=await bridge.Call(Operation.ItemIcons,Protocol.Body(w=>{w.Write(batch.Length);foreach(string symbol in batch)w.Write(symbol);}));using var reader=Protocol.Reader(packet);int count=reader.ReadInt32();if(count!=batch.Length)throw new System.IO.InvalidDataException("Item icon count mismatch.");for(int i=0;i<count;i++){string symbol=reader.ReadString();int width=reader.ReadInt32(),height=reader.ReadInt32();if(symbol!=batch[i]||width<1||height<1||width>128||height>128)throw new System.IO.InvalidDataException("Invalid local item icon.");byte[] pixels=reader.ReadBytes(width*height*4);if(pixels.Length!=width*height*4)throw new System.IO.EndOfStreamException();using var icon=Image.CreateFromData(width,height,false,Image.Format.Rgba8,pixels);var texture=ImageTexture.CreateFromImage(icon);texture.TakeOverPath("res://spire_next/runtime/items/"+symbol+".tres");nativeIcons[symbol]=texture;}Protocol.RequireEnd(reader);}
  }
  internal static RelicModel InventoryRelic(GearInstance gear)=>AdventureInventoryModels.Accessory(gear);
  internal static NPotionHolder InventoryPotion(Control parent,GearDefinition gear,Action use) {var holder=NPotionHolder.Create(false);inventoryPotionActions.Add(holder,use);parent.AddChild(holder);holder.AddPotion(NPotion.Create(AdventureInventoryModels.Potion(gear)));return holder;}
  static void CardVisuals(NCard __instance) {
   if(__instance.Model==null||!faces.TryGetValue(__instance.Model,out var face))return;
   foreach(string name in new[]{"_energyIcon","_energyLabel","_unplayableEnergyIcon","_starIcon","_starLabel","_unplayableStarIcon"})if(AccessTools.Field(typeof(NCard),name)?.GetValue(__instance) is CanvasItem item)item.Visible=false;
   if(AccessTools.Field(typeof(NCard),"_typeLabel")?.GetValue(__instance) is GodotObject label)label.Set("text",face.Choice.Kind);
  }
  internal static async Task<Choice> Choose(string purpose,IReadOnlyList<Choice> choices,bool canSkip=true) {
   if(choices==null||choices.Count==0)return null;
   bool restoreMap=NMapScreen.Instance?.IsOpen==true;
   if(restoreMap) {
    // The map is above the overlay stack during its act introduction. Wait for
    // that native animation and close it before opening a visible choice page.
    ulong deadline=Godot.Time.GetTicksMsec()+12000;
    while(MapBusy()&&Godot.Time.GetTicksMsec()<deadline)await NGame.Instance.ToSignal((SceneTree)Engine.GetMainLoop(),SceneTree.SignalName.ProcessFrame);
    NMapScreen.Instance.Close(false);
   }
   await PreloadItemIcons();
   var cards=choices.Select(choice=>{var card=AdventureInventoryModels.ChoiceCard(choice);faces.Add(card,new CardFace{Choice=choice,Portrait=ChoicePortrait(choice)});return card;}).ToList();
   SelectionPurpose=purpose;CurrentCards=cards;
   var screen=NChooseACardSelectionScreen.ShowScreen(cards,canSkip);CurrentSelection=screen;
   if(AccessTools.Field(screen.GetType(),"_banner")?.GetValue(screen) is NCommonBanner banner)banner.ChangeText(purpose);
   if(SpireSelfTest.AdventureEnabled)_=SelectForTest(screen,cards);
   try {var selected=(await screen.CardsSelected()).FirstOrDefault();return selected==null?null:faces.GetValue(selected,_=>throw new InvalidOperationException("Unknown choice card.")).Choice;}
   finally{CurrentSelection=null;CurrentCards=null;SelectionPurpose=null;if(restoreMap&&NMapScreen.Instance!=null)NMapScreen.Instance.Open(false);}
  }
  static bool MapBusy(){var map=NMapScreen.Instance;if(map==null)return false;if(NGame.Instance.Transition?.InTransition==true)return true;foreach(string field in new[]{"_actAnimTween","_tween"})if(AccessTools.Field(typeof(NMapScreen),field)?.GetValue(map) is Tween tween&&GodotObject.IsInstanceValid(tween)&&tween.IsRunning())return true;return false;}
  static async Task SelectForTest(NChooseACardSelectionScreen screen,List<CardModel> cards) {
   // This invokes the exact native card holder callback after the real screen is
   // visible; it does not short circuit selection or claim an unseen reward.
   try {
    ulong deadline=Godot.Time.GetTicksMsec()+10000;
    while(!ChoicePresentationReady(screen,cards)) {
     if(Godot.Time.GetTicksMsec()>=deadline)throw new TimeoutException("Native choice card presentation did not finish.");
     await NGame.Instance.ToSignal((SceneTree)Engine.GetMainLoop(),SceneTree.SignalName.ProcessFrame);
    }
    await NGame.Instance.ToSignal((SceneTree)Engine.GetMainLoop(),SceneTree.SignalName.ProcessFrame);
    await SpireSelfTest.Capture("choice-"+SpireSelfTest.ChoiceCaptureCount+++".png");
    int index=TestChoices.Count>0?TestChoices.Dequeue():0;index=Math.Clamp(index,0,cards.Count-1);
    var holder=FindHolder(screen,cards[index]);if(holder==null)throw new InvalidOperationException("Native selection card holder not found.");
    AccessTools.Method(screen.GetType(),"SelectHolder").Invoke(screen,new object[]{holder});
   }catch(Exception e){GD.PrintErr("[SpireNext Adventure] auto selection: "+e);}
  }
  internal static bool ChoicePresentationReady(NChooseACardSelectionScreen screen,IReadOnlyList<CardModel> cards) {
   if(screen==null||!GodotObject.IsInstanceValid(screen)||!screen.IsInsideTree()||!screen.IsVisibleInTree()||cards==null||cards.Count==0)return false;
   // Native choice holders fade from black to white for a full second. Alpha
   // alone is already opaque during that animation and cannot prove readiness.
   foreach(string field in new[]{"_cardTween","_fadeTween"})if(AccessTools.Field(screen.GetType(),field)?.GetValue(screen) is Tween tween&&GodotObject.IsInstanceValid(tween)&&tween.IsRunning())return false;
   foreach(var card in cards) {
    if(FindHolder(screen,card) is not Control holder||!holder.IsVisibleInTree())return false;
    for(Node node=holder;node!=null;node=node.GetParent())if(node is CanvasItem item) {
     Color color=item.Modulate;if(color.A<.99f||color.R<.99f||color.G<.99f||color.B<.99f)return false;
    }
   }
   return true;
  }
  static object FindHolder(Node node,CardModel card) {
   var prop=node.GetType().GetProperty("Card");object value=prop?.GetValue(node);
   if(value is NCard nc&&ReferenceEquals(nc.Model,card)&&node.GetType().Name.Contains("Holder"))return node;
   prop=node.GetType().GetProperty("CardModel");if(ReferenceEquals(prop?.GetValue(node),card)&&node.GetType().Name.Contains("Holder"))return node;
   foreach(Node child in node.GetChildren()){var found=FindHolder(child,card);if(found!=null)return found;}return null;
  }

  static void MapOpened(){if(Active&&AdventureSession.HostRun!=null)_=EnsureClassChosen();}
  internal static Task EnsureClassChosen(){AdventureSession.EnsureRun();return Task.CompletedTask;}
  static RewardKind Kind(string kind)=>kind.IndexOf("Boss",StringComparison.OrdinalIgnoreCase)>=0?RewardKind.Boss:kind.IndexOf("Elite",StringComparison.OrdinalIgnoreCase)>=0?RewardKind.Elite:kind.IndexOf("Treasure",StringComparison.OrdinalIgnoreCase)>=0?RewardKind.Treasure:RewardKind.Normal;
  internal static async Task<bool> ChooseReward(string node,string kind,string boss="") {
   AdventureSession.EnsureRun();var run=AdventureSession.Current;
   var offers=run.Rewards(node,Kind(kind),boss);if(run.ClaimedNodes.Contains(node))return false;
   var selected=await Choose(Kind(kind)==RewardKind.Treasure?"宝箱 · 泰拉饰品":"战利品 · 装备或保留材料",offers.Select(o=>new Choice(o.Title,o.Description,o.GearKey==""?"材料":GearCatalog.Get(o.GearKey).Kind==GearKind.Armor?"防具":GearCatalog.Get(o.GearKey).Kind==GearKind.Accessory?"饰品":"武器",o)).ToList());
   if(selected==null)return false;var offer=(RewardOffer)selected.Value;
   int replacement=await ReplacementFor(offer.GearKey);if(replacement==-2)return false;
   bool accepted=run.Accept(offer,replacement);if(accepted)AdventureSession.Save();return accepted;
  }
  static async Task<int> ReplacementFor(string key) {
   var run=AdventureSession.Current;if(key==""||GearCatalog.Get(key).Kind!=GearKind.Weapon||run.Weapons.Count<3)return -1;
   var selected=await Choose("三个武器槽已满 · 选择替换",run.Weapons.Select((item,i)=>new Choice(GearName(item),GearDescription(item)+"\n选择此项将替换这件武器。","替换",i)).ToList());return selected==null?-2:(int)selected.Value;
  }
  internal static string GearName(GearInstance item)=>item.Definition.Name+(item.Level>0?" +"+item.Level:"")+(string.IsNullOrEmpty(item.Affix)?"":" · "+GearCatalog.Affixes.FirstOrDefault(x=>x.Key==item.Affix)?.Name);
  internal static string GearDescription(GearInstance item)=>item.Definition.Description+"\n强化：基础数值 +"+(item.Level*20)+"%"+(string.IsNullOrEmpty(item.Affix)?"":"\n词条："+GearCatalog.Affixes.FirstOrDefault(x=>x.Key==item.Affix)?.Description);
  static IEnumerable<GearInstance> Equipment()=>AdventureSession.Current.Weapons.Concat(AdventureSession.Current.Armor==null?Array.Empty<GearInstance>():new[]{AdventureSession.Current.Armor});
  internal static async Task<bool> Forge() {
   var run=AdventureSession.Current;string node=AdventureSession.NodeKey;var camp=run.GetCamp(node);var outputs=camp.PendingForge;
   if(outputs==null||outputs.Count==0) {
    var recipes=run.ForgeCandidates(node).Where(x=>x.Unlocked&&x.Affordable).OrderByDescending(x=>GearCatalog.Get(x.GearKey).Stage).ThenBy(x=>x.Title).ToList();
    if(recipes.Count==0){await Choose("材料不足 · 收集后再来",new[]{new Choice("材料袋",AdventureRun.DescribeMaterials(run.Materials)+"\n普通：同类×2\n罕见：两类，共3份\n稀有：三类各1份","材料")});return false;}
    var selected=await Choose("选择锻造配方 · 产物随机三选一",recipes.Select(r=>new Choice(r.Title,r.Description+"\n消耗："+AdventureRun.DescribeMaterials(r.Costs),"锻造",r)).ToList());if(selected==null)return false;
    outputs=run.BeginForge(node,((ForgeRecipe)selected.Value).GearKey);AdventureSession.Save();
   }
   if(outputs==null||outputs.Count==0)return false;
   var output=await Choose("锻造完成 · 选择一件装备",outputs.Select(o=>new Choice(o.Title,o.Description,o.GearKey==""?"材料":GearCatalog.Get(o.GearKey).Kind==GearKind.Armor?"防具":"武器",o)).ToList(),false);
   int replace=await ReplacementFor(((RewardOffer)output.Value).GearKey);if(replace==-2)return false;
   bool result=run.ResolveForge(node,((RewardOffer)output.Value).OfferId,replace);if(result)AdventureSession.Save();return result;
  }
  internal static async Task<bool> Upgrade() {
   var selected=await Choose("强化 · 仅该件装备基础数值 +20%",Equipment().Select(g=>new Choice(GearName(g),GearDescription(g)+"\n本次强化后：+"+((g.Level+1)*20)+"%","强化",g)).ToList());if(selected==null)return false;
   bool result=AdventureSession.Current.Upgrade(AdventureSession.NodeKey,((GearInstance)selected.Value).InstanceId);if(result)AdventureSession.Save();return result;
  }
  internal static async Task<bool> Reforge() {
   var run=AdventureSession.Current;string node=AdventureSession.NodeKey;var camp=run.GetCamp(node);if(camp.ReforgeRemaining<=0&&camp.PendingReforge==null)return false;
   var pending=camp.PendingReforge;
   if(pending==null){var selected=await Choose("免费重铸 · 剩余 "+camp.ReforgeRemaining+" / 3",Equipment().Select(g=>new Choice(GearName(g),GearDescription(g),"重铸",g)).ToList());if(selected==null)return false;pending=run.RollReforge(node,((GearInstance)selected.Value).InstanceId);AdventureSession.Save();}
   if(pending==null)return false;
   var choice=await Choose("重铸结果 · 可以保留原词条",new[]{new Choice(pending.Title,pending.Description+"\n采用新词条。","采用",true),new Choice("保留原词条",string.IsNullOrEmpty(pending.OldAffix)?"保留无词条状态。":"保留当前词条。","保留",false)},false);
   bool result=run.ResolveReforge(node,(bool)choice.Value);AdventureSession.Save();RefreshCamp();return result;
  }
  static void PrepareRewards(RewardsSet __instance) {
   // Tutorial and scripted fights can bypass GenerateRewardsFor. Intercept the
   // common populate step after the native room has become the current room.
   if(!Active||__instance.Room is not CombatRoom)return;
   AdventureSession.EnsureRun();var rewards=__instance.Rewards;
   if(rewards.Any(x=>x is AdventureReward)){LastEquipmentReward=rewards.First(x=>x is AdventureReward);return;}
   ReplaceRewards(__instance.Player,__instance.Room,ref rewards);
  }
  static void ReplaceRewards(Player player,AbstractRoom room,ref List<Reward> __result) {
   if(!Active||AdventureSession.Current==null||room is not CombatRoom)return;
   __result.RemoveAll(x=>x is CardReward||x is SpecialCardReward||x is RelicReward||x is PotionReward);
   LastEquipmentReward=new AdventureReward(player,AdventureSession.NodeKey,AdventureSession.RoomKind,AdventureSession.VerifiedBossSymbol);__result.Add(LastEquipmentReward);
   LastBossReward=null;
   if(AdventureSession.VerifiedBossSymbol!=""){LastBossReward=new EnemyDropReward(player,AdventureSession.NodeKey,AdventureSession.VerifiedBossSymbol);__result.Add(LastBossReward);}
  }
  static bool RestoreReward(SerializableReward save,Player player,ref Reward __result) {
   var marker=save.PredeterminedModelId;if(marker==null||marker.Category!="SPIRE_NEXT")return true;
   AdventureSession.EnsureRun();if(AdventureSession.Current==null)throw new InvalidOperationException("Adventure reward restore requires its expedition equipment save.");
   if(marker.Entry=="EQUIPMENT") {
    string node=AdventureSession.NodeKey;
    if(!AdventureSession.Current.RewardNodes.ContainsKey(node))throw new InvalidOperationException("Saved equipment reward options are missing; refusing to replace them with a reroll.");
    LastEquipmentReward=new AdventureReward(player,node,AdventureSession.RoomKind,AdventureSession.VerifiedBossSymbol);__result=LastEquipmentReward;return false;
   }
   if(marker.Entry=="BOSS_DROP") {
    string symbol=AdventureSession.VerifiedBossSymbol;if(symbol=="")throw new InvalidOperationException("Saved enemy reward has no verified native victory.");
    LastBossReward=new EnemyDropReward(player,AdventureSession.NodeKey,symbol);__result=LastBossReward;return false;
   }
   throw new InvalidOperationException("Unknown adventure reward marker: "+marker.Entry);
  }
  sealed class AdventureReward:Reward {
   readonly string node,kind,boss;
   internal AdventureReward(Player p,string n,string k,string b):base(p){node=n;kind=k;boss=b;}
   protected override RewardType RewardType=>RewardType.Card;
   public override int RewardsSetIndex=>1;
   public override LocString Description=>Text("选择泰拉装备 / 锻造材料");
   public override bool IsPopulated=>true;
   public override void Populate(){}
   public override void MarkContentAsSeen(){}
   public override SerializableReward ToSerializable(){AdventureSession.Current.Rewards(node,Kind(kind),boss);AdventureSession.Save();return new SerializableReward{RewardType=RewardType.Card,PredeterminedModelId=new ModelId("SPIRE_NEXT","EQUIPMENT")};}
   public override Control CreateIcon()=>new TextureRect{Texture=Portrait("武器"),CustomMinimumSize=new Vector2(58,42),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};
   protected override Task<bool> OnSelect()=>ChooseReward(node,kind,boss);
  }
  sealed class EnemyDropReward:Reward {
   readonly string node,boss;
   readonly BossDropResult preview;
   internal EnemyDropReward(Player player,string nodeKey,string symbol):base(player){node=nodeKey;boss=symbol;preview=AdventureRun.Deserialize(AdventureSession.Current.Serialize()).ResolveBossDrop(node,boss);}
   protected override RewardType RewardType=>RewardType.Relic;
   public override int RewardsSetIndex=>2;
   public override bool IsPopulated=>true;
   protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips=>preview.GearKey!=""?AdventureInventoryModels.CanonicalAccessory(preview.GearKey).HoverTips:Array.Empty<MegaCrit.Sts2.Core.HoverTips.IHoverTip>();
   public override LocString Description {
    get {return Text(BossName(boss)+" · "+(preview.GearKey!=""?GearCatalog.Get(preview.GearKey).Name:"锻造材料"+(preview.DuplicateConverted?"（重复饰品转换）":"")));}
   }
   public override void Populate(){}
   public override void MarkContentAsSeen(){}
   public override SerializableReward ToSerializable(){AdventureSession.Save();return new SerializableReward{RewardType=RewardType.Relic,PredeterminedModelId=new ModelId("SPIRE_NEXT","BOSS_DROP")};}
   public override Control CreateIcon()=>new TextureRect{Texture=ItemIcon(preview.GearKey!=""?GearCatalog.Get(preview.GearKey).NativeSymbols[0]:"IronOre")??Portrait("材料"),TextureFilter=CanvasItem.TextureFilterEnum.Nearest,CustomMinimumSize=new Vector2(58,42),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};
   protected override Task<bool> OnSelect(){AdventureSession.Current.ResolveBossDrop(node,boss);AdventureSession.Save();return Task.FromResult(true);}
  }
  static string BossName(string symbol)=>symbol switch {
   "KingSlime"=>"史莱姆王","EyeofCthulhu" or "EyeOfCthulhu"=>"克苏鲁之眼","EaterofWorldsHead" or "EaterOfWorldsHead"=>"世界吞噬怪","BrainofCthulhu" or "BrainOfCthulhu"=>"克苏鲁之脑","QueenBee"=>"蜂王","SkeletronHead"=>"骷髅王","WallofFlesh" or "WallOfFlesh"=>"血肉墙","QueenSlimeBoss"=>"史莱姆皇后","SkeletronPrime"=>"机械骷髅王","Plantera"=>"世纪之花","Golem"=>"石巨人","DukeFishron"=>"猪龙鱼公爵","HallowBoss"=>"光之女皇","Mimic"=>"宝箱怪","BigMimicCorruption"=>"腐化宝箱怪","BigMimicCrimson"=>"猩红宝箱怪","Pixie"=>"妖精","GiantBat"=>"巨型蝙蝠","Hornet" or "MossHornet"=>"黄蜂","CursedSkull"=>"诅咒骷髅头",_=>"敌人专属掉落"
  };
  static void CampOptions(Player player,ref List<RestSiteOption> __result) {
   if(!Active)return;AdventureSession.EnsureRun();__result.Clear();__result.Add(new AdventureCampOption(player,"REST"));__result.Add(new AdventureCampOption(player,"UPGRADE"));__result.Add(new AdventureCampOption(player,"FORGE"));__result.Add(new AdventureCampOption(player,"REFORGE"));
  }
  static bool CampTitle(RestSiteOption __instance,ref LocString __result){if(__instance is not AdventureCampOption option)return true;__result=Text(option.Caption);return false;}
  static bool CampIcon(RestSiteOption __instance,ref Texture2D __result){if(__instance is not AdventureCampOption option)return true;__result=ItemIcon(option.Action=="REST"?"HealingPotion":"IronAnvil");return false;}
  static bool CampIconPath(RestSiteOption __instance,ref string __result){if(__instance is not AdventureCampOption option)return true;__result=LocalIconPath(option.Action=="REST"?"HealingPotion":"IronAnvil");return false;}
  sealed class AdventureCampOption:RestSiteOption {
   internal readonly string Action;
   internal AdventureCampOption(Player player,string action):base(player){Action=action;}
   public override string OptionId=>"SPIRE_NEXT_"+Action;
   public override IEnumerable<string> AssetPaths=>Array.Empty<string>();
   internal string Caption=>Action=="REST"?"休息":Action=="UPGRADE"?"强化 +20%":Action=="FORGE"?"材料锻造":"免费重铸 ("+AdventureSession.Current.GetCamp(AdventureSession.NodeKey).ReforgeRemaining+"/3)";
   public override LocString Description=>Text(Action=="REST"?"恢复泰拉角色 30% 最大生命。":Action=="UPGRADE"?"选择一件武器或防具，基础数值增加20%。消耗本次火堆行动。":Action=="FORGE"?"用随机收集的材料锻造装备，可提前制作下一阶段的入门装备。消耗本次火堆行动。":"每个火堆共享3次机会；可保留原词条，不消耗休息或锻造机会。");
   public override bool IsEnabled=>Action=="REFORGE"?(AdventureSession.Current.GetCamp(AdventureSession.NodeKey).ReforgeRemaining>0||AdventureSession.Current.GetCamp(AdventureSession.NodeKey).PendingReforge!=null):Action=="FORGE"&&AdventureSession.Current.GetCamp(AdventureSession.NodeKey).PendingForge?.Count>0||!AdventureSession.Current.GetCamp(AdventureSession.NodeKey).MainActionUsed;
   public override async Task<bool> OnSelect(){if(Action=="REFORGE"){await Reforge();return false;}bool result=Action=="FORGE"?await Forge():Action=="UPGRADE"?await Upgrade():AdventureSession.Current.Rest(AdventureSession.NodeKey);if(result){AdventureSession.PushHostHealth();AdventureSession.Save();RefreshCamp();if(NRestSiteRoom.Instance!=null)AccessTools.Method(typeof(NRestSiteRoom),"ShowProceedButton")?.Invoke(NRestSiteRoom.Instance,null);}return false;}
  }
  static void RefreshCamp(){var room=NRestSiteRoom.Instance;if(room==null)return;AccessTools.Method(typeof(NRestSiteRoom),"UpdateRestSiteOptions")?.Invoke(room,null);}

  static bool ShopCreated(Player player,ref MerchantInventory __result) {
   if(!Active)return true;AdventureSession.EnsureRun();var run=AdventureSession.Current;int index=0;
   var inventory=new MerchantInventory(player);
   // Native navigation includes this slot even when equipment removal is not
   // offered. Bind a completed entry so every native slot has a valid model.
   var removal=new MerchantCardRemovalEntry(player);removal.SetUsed();AccessTools.Property(typeof(MerchantInventory),"CardRemovalEntry").SetValue(inventory,removal);
   var character=(List<MerchantCardEntry>)AccessTools.Field(typeof(MerchantInventory),"_characterCardEntries").GetValue(inventory);
   var universal=(List<MerchantCardEntry>)AccessTools.Field(typeof(MerchantInventory),"_colorlessCardEntries").GetValue(inventory);
   var potionEntries=(List<MerchantPotionEntry>)AccessTools.Field(typeof(MerchantInventory),"_potionEntries").GetValue(inventory);
   List<GearDefinition> Pick(IEnumerable<GearDefinition> candidates,int count,string group) {
    var pool=candidates.OrderBy(g=>g.Key).ToList();var result=new List<GearDefinition>();
    while(result.Count<count&&pool.Count>0){int at=(int)(AdventureSession.StableHash(run.Seed+"/"+AdventureSession.NodeKey+"/shop-"+group+"/"+index+"/"+result.Count)%(uint)pool.Count);result.Add(pool[at]);pool.RemoveAt(at);}return result;
   }
   void CardEntry(GearDefinition gear,List<MerchantCardEntry> target) {
    var canonical=AdventureInventoryModels.CanonicalGear(gear.Key);var entry=new MerchantCardEntry(player,inventory,new[]{canonical},canonical.Rarity);entry.Populate();
    var actual=player.RunState.CreateCard(canonical,player);AccessTools.Property(typeof(MerchantCardEntry),"CreationResult").SetValue(entry,new CardCreationResult(actual));entry.CalcCost();
    var offer=ShopOffer(gear.Key,index++);shopOffers.Add(entry,offer);target.Add(entry);
   }
   var equipment=GearCatalog.For(run.Hero,run.Stage).Where(g=>!AdventureRun.IsPrimitive(g)).ToArray();
   foreach(var gear in Pick(equipment.Where(g=>g.Kind==GearKind.Weapon),3,"weapon").Concat(Pick(equipment.Where(g=>g.Kind==GearKind.Armor),2,"armor")))CardEntry(gear,character);
   foreach(var gear in Pick(GearCatalog.All.Where(g=>g.Kind==GearKind.Consumable&&g.Stage<=run.Stage&&!AdventureInventoryModels.IsPotion(g)),2,"explosive"))CardEntry(gear,universal);
   var accessories=GearCatalog.All.Where(g=>g.Kind==GearKind.Accessory&&g.Stage<=run.Stage&&GearCatalog.IsForHero(g,run.Hero)&&!run.Accessories.Any(a=>a.DefinitionKey==g.Key));
   foreach(var gear in Pick(accessories,3,"accessory")){var model=AdventureInventoryModels.CanonicalAccessory(gear.Key).ToMutable();var entry=new MerchantRelicEntry(model,player);shopOffers.Add(entry,ShopOffer(gear.Key,index++));inventory.AddRelicEntry(entry);}
   var potions=GearCatalog.All.Where(g=>AdventureInventoryModels.IsPotion(g)&&g.Stage==run.Stage).OrderBy(g=>g.Key).ToList();
   for(int i=0;i<3&&potions.Count>0;i++){var gear=potions[i%potions.Count];var entry=new MerchantPotionEntry(AdventureInventoryModels.Potion(gear),player);shopOffers.Add(entry,ShopOffer(gear.Key,index++));potionEntries.Add(entry);}
   foreach(var entry in inventory.AllEntries)entry.PurchaseCompleted+=(_,__)=>{foreach(var slot in inventory.AllEntries)slot.OnMerchantInventoryUpdated();};
   __result=inventory;return false;
  }
  static RewardOffer ShopOffer(string gearKey,int index){var run=AdventureSession.Current;string node=AdventureSession.NodeKey+":shop:"+index;var gear=GearCatalog.Get(gearKey);var offer=new RewardOffer{OfferId=node,NodeKey=node,GearKey=gearKey,Title=gear.Name,Description=gear.Description};run.RewardNodes[node]=new List<RewardOffer>{offer};return offer;}
  static bool ShopPurchase(MerchantEntry __instance,MerchantInventory inventory,bool ignoreCost,ref Task<(bool,int)> __result){if(!shopOffers.TryGetValue(__instance,out var offer))return true;__result=Buy(__instance,inventory,ignoreCost,offer);return false;}
  static bool DisableRemoval(ref Task<(bool,int)> __result){if(!Active)return true;__result=Task.FromResult((false,0));return false;}
  static async Task<(bool,int)> Buy(MerchantEntry entry,MerchantInventory inventory,bool ignoreCost,RewardOffer offer){int cost=ignoreCost?0:entry.Cost;if(!entry.IsStocked||inventory.Player.Gold<cost)return(false,0);int replace=await ReplacementFor(offer.GearKey);if(replace==-2||inventory.Player.Gold<cost)return(false,0);if(!AdventureSession.Current.Accept(offer,replace))return(false,0);if(cost>0)await PlayerCmd.LoseGold(cost,inventory.Player);AdventureSession.Save();return(true,cost);}
  static void TreasureAccessories(TreasureRoomRelicSynchronizer __instance) {
   if(!Active)return;var run=AdventureSession.EnsureRun();string node=AdventureSession.NodeKey;
   var relics=(List<RelicModel>)AccessTools.Field(typeof(TreasureRoomRelicSynchronizer),"_currentRelics").GetValue(__instance);relics.Clear();
   var projection=new TreasureProjection{Run=run,Node=node};treasureProjections.Remove(__instance);treasureProjections.Add(__instance,projection);
   if(run.ClaimedNodes.Contains(node))return;
   projection.Offer=run.Rewards(node,RewardKind.Treasure).Single();projection.Duplicate=run.Accessories.Any(x=>x.DefinitionKey==projection.Offer.GearKey);
   // Exhausted chest loot converts to materials through the same expedition
   // rule, without briefly adding a duplicate relic to the native inventory.
   if(!projection.Duplicate)relics.Add(AdventureInventoryModels.CanonicalAccessory(projection.Offer.GearKey));
   AdventureSession.Save();
  }
  static void TreasureExited(TreasureRoomRelicSynchronizer __instance)=>treasureProjections.Remove(__instance);
  static bool TreasureExtraRewards(ref Task __result){if(!Active)return true;__result=Task.CompletedTask;return false;}
  static void TreasureInitialized(NTreasureRoomRelicCollection __instance) {
   if(!Active||!treasureProjections.TryGetValue(RunManager.Instance.TreasureRoomRelicSynchronizer,out var projection)||!projection.Duplicate)return;
   if(__instance.GetNodeOrNull<Label>("%EmptyLabel") is Label label)label.Text="重复饰品已转为锻造材料";
  }
  static void TreasureAccessoryObtained(Player __instance,RelicModel relic) {
   if(!Active||__instance!=Player||relic is not AdventureAccessory accessory||!string.IsNullOrEmpty(accessory.InstanceId)||AdventureSession.HostRun?.CurrentRoom is not TreasureRoom)return;
   if(!treasureProjections.TryGetValue(RunManager.Instance.TreasureRoomRelicSynchronizer,out var projection)||projection.Awarded||projection.Duplicate||!ReferenceEquals(projection.Run,AdventureSession.Current)||projection.Offer?.GearKey!=accessory.GearKey)return;
   if(!projection.Run.Accept(projection.Offer))return;
   accessory.InstanceId=projection.Run.Accessories.Single(x=>x.DefinitionKey==accessory.GearKey).InstanceId;projection.Awarded=true;
  }
  static void TreasureOpened(ref Task __result) {
   if(!Active||!treasureProjections.TryGetValue(RunManager.Instance.TreasureRoomRelicSynchronizer,out var projection))return;
   __result=FinishTreasure(__result,projection);
  }
  static async Task FinishTreasure(Task nativeOpen,TreasureProjection projection) {
   await nativeOpen;if(!ReferenceEquals(projection.Run,AdventureSession.Current))return;
   if(projection.Duplicate&&projection.Offer!=null&&!projection.Awarded)projection.Awarded=projection.Run.Accept(projection.Offer);
   // Native skipping forfeits this chest, just as selecting its relic claims it.
   projection.Run.ClaimedNodes.Add(projection.Node);AdventureSession.Save();
  }

  static bool EventOptions(EventModel __instance,ref IReadOnlyList<EventOption> __result) {
   if(!Active)return true;AdventureSession.EnsureRun();var page=EventPage(__instance);__result=BuildEventOptions(__instance,page);
   if(__instance is AncientEventModel)AccessTools.Field(typeof(AncientEventModel),"_generatedOptions").SetValue(__instance,__result.ToList());
   AdventureSession.Save();return false;
  }
  internal static string EventThemeFor(string identity,bool ancient) {
   string name=identity.Split('.').Last();
   if(ancient)return name switch {"Neow"=>"departure","Darv"=>"ancient-forge","Nonupeipe"=>"treasury","Orobas"=>"stars","Pael"=>"restoration","Tanx"=>"war-cache","Tezcatara"=>"blood-pact","Vakuu"=>"exchange",_=>"echoes"};
   return name switch {
    "Amalgamator" or "TinkerTime" or "TheArchitect" or "EndlessConveyor" or "InfestedAutomaton"=>"workshop",
    "FakeMerchant" or "RelicTrader" or "WelcomeToWongos" or "ThisOrThat"=>"trader",
    "AbyssalBaths" or "Wellspring" or "TeaMaster" or "RoundTeaParty" or "RoomFullOfCheese" or "ZenWeaver"=>"clinic",
    "TrashHeap" or "SunkenTreasury" or "TheLegendsWereTrue" or "WarHistorianRepy" or "TheLanternKey" or "FieldOfManSizedHoles"=>"cache",
    "BattlewornDummy" or "PunchOff" or "Bugslayer" or "Trial"=>"tavern",
    "BrainLeech" or "GraveOfTheForgotten" or "Symbiote" or "SpiritGrafter" or "SunkenStatue" or "DoorsOfLightAndDark"=>"altar",
    "HungryForMushrooms" or "DenseVegetation" or "ColossalFlower" or "MorphicGrove" or "SapphireSeed" or "JungleMazeAdventure"=>"garden",
    "CrystalSphere" or "StoneOfAllTime" or "TabletOfTruth" or "ColorfulPhilosophers" or "Reflections" or "WaterloggedScriptorium"=>"crystal",
    "PotionCourier" or "TheFutureOfPotions" or "RanwidTheElder"=>"alchemist",
    _=>new[]{"workshop","trader","clinic","cache","tavern","altar","garden","crystal","alchemist"}[(int)(AdventureSession.StableHash(name)%9)]
   };
  }
  static TerrariaEventPage EventPage(EventModel model) {
   if(convertedEvents.TryGetValue(model,out var cached))return cached;
   var run=AdventureSession.EnsureRun();string theme=EventThemeFor(model.GetType().Name,model is AncientEventModel);
   (string title,string story,string portrait)=theme switch {
    "workshop"=>("哥布林工坊","铁砧旁火星四溅。工匠愿意修整你手中的装备，也卖几件刚出炉的新货。","IronAnvil"),
    "trader"=>("游商的货摊","地毯上摆着本职业装备和真正的泰拉饰品。你可以用金币换取立即生效的战力。","GoldCoin"),
    "clinic"=>("林间诊所","护士翻开药箱：处理伤口、带走药水，还是把剩下的补给留到下一场？","HealingPotion"),
    "cache"=>("废弃冒险营地","营火已熄，武器箱和爆破箱依然完好。背包只能再带走其中一批物资。","Grenade"),
    "tavern"=>("酒馆老板的补给","酒馆老板把训练装备运进了尖塔。挑选护甲、哨兵武器或爆破支援，领取后便能继续赶路。","DD2FlameburstTowerT1Popper"),
    "altar"=>("猩红献祭坛","祭坛上的强力装备回应着你的心跳。鲜血能换来战力，旁边的药瓶则无需举行仪式。","Bone"),
    "garden"=>("树妖的隐秘苗圃","藤蔓包住了旧箱子。树妖可以收集纤维，也能把矿石培育成更珍贵的锻造材料。","Silk"),
    "crystal"=>("水晶研究台","棱镜映出下一阶段的装备。魔法师可以精炼材料，或在一件装备上刻下指定词条。","CrystalShard"),
    "alchemist"=>("炼金师的行囊","药剂、魔力和炸药挤满了木箱。选择与你当前战斗方式最合适的一批。","ManaPotion"),
    "departure"=>("涅奥 · 远征启程","涅奥把祝福化为泰拉旅途的第一份礼物：一件新装备、一次独立强化，或一整批远行补给。","EnchantedSword"),
    "ancient-forge"=>("达维 · 岩壳铸炉","古老的炉膛只认坚实的护甲。你可以挑选新防具、以伤口换取两次加固，或带走下一阶段的矿料。","IronAnvil"),
    "treasury"=>("诺努佩佩 · 漂流宝库","水面浮来一座小宝库。饰品、金币和下一阶段的材料各占一个漂浮的托盘。","GoldCoin"),
    "stars"=>("奥罗巴斯 · 星界观测","星图折射出你的职业与未来。领取本职业的强力武器、给武器刻印，或保存凝结的星辉。","CrystalShard"),
    "restoration"=>("佩尔 · 生命回响","温暖的脉动缝合伤口。远古者也愿意把恢复的力量封在药瓶或防护饰品里。","HealingPotion"),
    "war-cache"=>("坦克斯 · 战争储备","古老的军械库再次开启。整套护甲、通用炸药和适合你的武器等待出征。","Dynamite"),
    "blood-pact"=>("特兹卡塔拉 · 鲜血契约","力量需要代价。你可以用当前生命换取稀有装备，也可以献出金币，接受较温和的馈赠。","Bone"),
    "exchange"=>("瓦库 · 命运交换","同样的物资有不同的未来。把金币变成饰品，把旧武器拆成新材料，或把眼前战力留在手中。","SoulofNight"),
    _=>("远古回声 · 工匠遗产","回声留下三种不同的传承：给武器强化、给护甲刻印，或接过尚未成形的锻造素材。","IronOre")
   };
   cached=new TerrariaEventPage{Node=AdventureSession.NodeKey+":event:"+model.Id,Theme=theme,Title=title,Story=story+"\n第 "+run.Stage+" 幕 · "+HeroName(run.Hero)+"；每次相遇只能选择一条路线。",Portrait=portrait};convertedEvents.Add(model,cached);return cached;
  }
  static string HeroName(HeroClass hero)=>hero==HeroClass.Warrior?"战士":hero==HeroClass.Ranger?"射手":hero==HeroClass.Mage?"法师":"召唤师";
  static bool EventTitle(EventModel __instance,ref LocString __result){if(!Active)return true;__result=Text(EventPage(__instance).Title);return false;}
  static bool EventStory(EventModel __instance,ref LocString __result){if(!Active)return true;__result=Text(EventPage(__instance).Story);return false;}
  static bool EventLayout(EventModel __instance,ref EventLayoutType __result){if(!Active||__instance is AncientEventModel)return true;__result=EventLayoutType.Default;return false;}
  static bool EventBeforeStarted(EventModel __instance,ref Task __result){if(!Active)return true;__result=Task.CompletedTask;return false;}
  static bool EventFinishedHook()=>!Active;
  static bool EventPortrait(EventModel __instance,ref Texture2D __result){if(!Active||__instance is AncientEventModel)return true;__result=ItemIcon(EventPage(__instance).Portrait)??Portrait("材料");return false;}
  static void EventPortraitLayout(NEventLayout __instance){if(!Active||AccessTools.Field(typeof(NEventLayout),"_event").GetValue(__instance) is not EventModel model||model is AncientEventModel)return;if(AccessTools.Field(typeof(NEventLayout),"_portrait").GetValue(__instance) is not TextureRect image)return;image.Scale=Vector2.One;image.Material=null;image.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;image.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;image.StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered;image.AnchorLeft=.10f;image.AnchorRight=.39f;image.AnchorTop=.28f;image.AnchorBottom=.73f;image.OffsetLeft=image.OffsetRight=image.OffsetTop=image.OffsetBottom=0;}
  static IReadOnlyList<EventOption> BuildEventOptions(EventModel model,TerrariaEventPage page) {
   var run=AdventureSession.Current;int stage=run.Stage;var result=new List<EventOption>();int price=35+stage*15,nurse=20+stage*10,blood=(int)Math.Ceiling(run.LifeMax*.2);
   void Add(string key,string title,string description,Func<Task<bool>> action,bool enabled=true)=>result.Add(new EventOption(model,enabled?()=>ResolveEvent(model,page,title,action):null,Text(title),Text(description+(enabled?"":"\n条件不足，暂时无法选择。")),"SPIRE_NEXT_EVENT_"+key,Array.Empty<MegaCrit.Sts2.Core.HoverTips.IHoverTip>()));
   void Materials(string key,string title,Dictionary<string,int> bundle,int hp=0)=>Add(key,title,(hp>0?"失去 "+hp+" 点当前生命，":"")+"获得 "+AdventureRun.DescribeMaterials(bundle)+"。",()=>{if(run.Life<=hp)return Task.FromResult(false);run.GiveMaterials(bundle);run.Life-=hp;return Task.FromResult(true);},run.Life>hp);
   void Gear(string key,string title,string scope,int cost=0,int hp=0)=>Add(key,title,(cost>0?"支付 "+cost+" 金币，":"")+(hp>0?"失去 "+hp+" 点当前生命，":"")+"从三件本幕、本职业"+(scope=="armor"?"整套防具":scope=="weapon"?"武器":"装备")+"中选择一件；取消选择或替换不会付费。",()=>EventGear(page,scope,cost,hp),Player.Gold>=cost&&run.Life>hp);
   void Heal(string key,string title,double ratio,int cost=0)=>Add(key,title,(cost>0?"支付 "+cost+" 金币，":"")+"恢复最多 "+(int)Math.Ceiling(run.LifeMax*ratio)+" 点当前生命，最大生命不变。",async()=>{if(Player.Gold<cost||run.Life>=run.LifeMax)return false;run.Life=Math.Min(run.LifeMax,run.Life+(int)Math.Ceiling(run.LifeMax*ratio));if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;},Player.Gold>=cost&&run.Life<run.LifeMax);
   void Supplies(string key,string title,string[] symbols,int count,int cost=0)=>Add(key,title,(cost>0?"支付 "+cost+" 金币，":"")+"获得 "+string.Join("、",symbols.Select(s=>GearCatalog.Get("consumable:"+s).Name+" ×"+count))+"。药水进入原生药水栏，投掷物不占武器槽。",()=>EventSupplies(page,key,symbols,count,cost),Player.Gold>=cost);
   void Accessory(string key,string title,int cost=0)=>Add(key,title,(cost>0?"支付 "+cost+" 金币，":"")+"从两件当前阶段尚未拥有的泰拉饰品中选择一件；无可选饰品时改为三份精华。",()=>EventAccessory(page,cost),Player.Gold>=cost);
   string healing=stage==1?"HealingPotion":stage==2?"GreaterHealingPotion":"SuperHealingPotion",mana=stage==1?"ManaPotion":stage==2?"GreaterManaPotion":"SuperManaPotion";
   if(!run.ClaimedNodes.Contains(page.Node))switch(page.Theme) {
    case "workshop":
     Add("WORKSHOP_FORGE","请工匠锻造 +1","支付 "+(20+stage*15)+" 金币，选择一件武器或护甲；仅该件基础属性增加20%，不占火堆次数。",()=>EventUpgrade(page,1,20+stage*15),Player.Gold>=20+stage*15);
     Gear("WORKSHOP_BUY","购买工坊装备","equipment",price);Materials("WORKSHOP_SCRAP","带走工坊边角料",EventMaterials(stage,("ore",2),("weave",1)));break;
    case "trader":Gear("TRADER_EQUIPMENT","挑选旅商装备","equipment",price);Accessory("TRADER_ACCESSORY","购买旅商饰品",60+stage*20);Supplies("TRADER_STOCK","低价爆破包",new[]{stage==1?"Grenade":"Dynamite"},3,20);break;
    case "clinic":Heal("CLINIC_HEAL","接受护士治疗",.4,nurse);Supplies("CLINIC_HEALING","购买治疗药水",new[]{healing},2,20);Supplies("CLINIC_MANA","购买魔力药水",new[]{mana},2,20);break;
    case "cache":Gear("CACHE_EQUIPMENT","打开遗留武器箱","equipment");Supplies("CACHE_EXPLOSIVES","搬走爆破补给",new[]{"Grenade",stage==1?"Bomb":"Dynamite"},3);Materials("CACHE_DISMANTLE","拆开箱子取材料",EventMaterials(stage,("ore",2),("bone",2)));break;
    case "tavern":Gear("TAVERN_ARMOR","领取酒馆防具","armor");Add("TAVERN_SENTRIES","选择酒馆武器",run.Hero==HeroClass.Summoner?"从当前阶段可用的酒馆哨兵中选择一件，无需启动塔防。":"挑选一件适合本职业的训练武器。",()=>EventGear(page,run.Hero==HeroClass.Summoner?"sentry":"weapon",0,0));Supplies("TAVERN_BLAST","领取爆破支援",new[]{"Beenade","Grenade"},3);break;
    case "altar":Gear("ALTAR_BLOOD","以鲜血换强力装备","rare",0,blood);Supplies("ALTAR_MANA","拿走祭坛旁的药水",new[]{mana},2);Materials("ALTAR_ESSENCE","剥取猩红精华",EventMaterials(stage,("essence",3),("bone",1)),(int)Math.Ceiling(run.LifeMax*.1));break;
    case "garden":Materials("GARDEN_WEAVE","收集树妖的纤维",EventMaterials(stage,("weave",3),("bone",1)));Add("GARDEN_REFINE","培育矿石晶芽","消耗本幕矿料 ×2，得到下一阶段水晶 ×2；第三幕得到本幕水晶 ×3。",()=>EventExchange(stage,"ore",2,Math.Min(3,stage+1),"crystal",stage==3?3:2),HasMaterial(stage,"ore",2));Heal("GARDEN_REST","在苗圃短暂休憩",.18);break;
    case "crystal":Add("CRYSTAL_AFFIX","刻下指定装备词条","支付 "+(25+stage*10)+" 金币，选择一件装备，再从至多三个固定词条中挑选；不消耗火堆重铸机会。",()=>EventAffix(page,false,25+stage*10),Player.Gold>=25+stage*10);Add("CRYSTAL_TRANSMUTE","将纤维精炼为精华","消耗本幕纤维 ×2，获得本幕精华 ×2。",()=>EventExchange(stage,"weave",2,stage,"essence",2),HasMaterial(stage,"weave",2));Materials("CRYSTAL_SHARDS","收好研究台的碎晶",EventMaterials(stage,("crystal",2)));break;
    case "alchemist":Supplies("ALCHEMY_HEALTH","带走治疗剂",new[]{healing},3);Supplies("ALCHEMY_MANA","带走魔力剂",new[]{mana},3);Supplies("ALCHEMY_MIXED","带走混合战备箱",new[]{"MolotovCocktail","Beenade"},2);break;
    case "departure":Gear("NEOW_EQUIPMENT","远征装备","equipment");Add("NEOW_FORGE","工匠的祝福","免费为一件武器或护甲强化 +1，只增加该件基础属性的20%。",()=>EventUpgrade(page,1));Supplies("NEOW_SUPPLIES","远行补给",new[]{"Grenade",healing,mana},2);break;
    case "ancient-forge":Gear("DARV_ARMOR","挑选岩壳防具","armor");Add("DARV_REINFORCE","以伤口淬炼护甲","失去最大生命10%的当前生命，仅现有护甲强化 +2。",()=>EventUpgrade(page,2,0,true,(int)Math.Ceiling(run.LifeMax*.1)),run.Life>(int)Math.Ceiling(run.LifeMax*.1));Materials("DARV_ORE","保留未来矿藏",EventMaterials(Math.Min(3,stage+1),("ore",3),("bone",1)));break;
    case "treasury":Accessory("NONU_ACCESSORY","接过漂浮饰品");Add("NONU_GOLD","取走宝库金币","获得 "+(55+stage*20)+" 金币，用于后续商店与工坊。",async()=>{await PlayerCmd.GainGold(55+stage*20,Player);return true;});Materials("NONU_FUTURE","取走封存的锻材",EventMaterials(Math.Min(3,stage+1),("weave",2),("crystal",2)));break;
    case "stars":Gear("OROBAS_WEAPON","接过星图中的武器","strong-weapon");Add("OROBAS_AFFIX","给当前武器刻下星印","为一把武器选择指定词条，只作用于该件装备。",()=>EventAffix(page,true));Materials("OROBAS_STARLIGHT","保存未来星辉",EventMaterials(Math.Min(3,stage+1),("crystal",3),("essence",1)));break;
    case "restoration":Heal("PAEL_RESTORE","拥抱生命回响",.65);Supplies("PAEL_BOTTLES","将回响封入药瓶",new[]{healing,mana},3);Accessory("PAEL_GUARD","选择守护饰品");break;
    case "war-cache":Gear("TANX_ARMOR","开启护甲军械箱","armor");Gear("TANX_WEAPON","开启职业武器箱","weapon");Supplies("TANX_EXPLOSIVES","领取战争爆破储备",new[]{"Grenade","Dynamite","Beenade"},3);break;
    case "blood-pact":Gear("TEZ_BLOOD","以鲜血接受稀有装备","rare",0,blood);Accessory("TEZ_GOLD","以金币签订饰品契约",50+stage*15);Supplies("TEZ_QUIET","避开仪式，带走药瓶",new[]{healing},2);break;
    case "exchange":Accessory("VAKUU_TRADE","将金币换作命运饰品",30+stage*10);Add("VAKUU_SALVAGE","拆解一把备用武器","选择一把武器拆除，换得下一阶段矿料 ×2、水晶 ×2、精华 ×1；至少保留一把武器。",()=>EventSalvage(page),run.Weapons.Count>1);Gear("VAKUU_EQUIPMENT","保留眼前战力","equipment");break;
    default:Add("ECHO_WEAPON","继承武器锻造","选择一把武器强化 +1，仅此武器基础伤害增加20%。",()=>EventUpgrade(page,1,0,false,0,true));Add("ECHO_ARMOR","继承护甲刻印","为现有护甲挑选一个词条，不消耗火堆重铸机会。",()=>EventAffix(page,false,0,true));Materials("ECHO_MATERIALS","继承工匠材料",EventMaterials(stage,("ore",2),("weave",2),("essence",1)));break;
   }
   // Old callbacks remain safe even if a stale native option is invoked twice.
   result.Add(new EventOption(model,()=>{if(page.Busy)return Task.CompletedTask;run.ClaimedNodes.Add(page.Node);AdventureSession.Save();AccessTools.Method(typeof(EventModel),"SetEventFinished").Invoke(model,new object[]{Text("你收好行囊，继续向高塔深处出发。")});return Task.CompletedTask;},Text("继续前进"),Text("离开这里，保留现有物资。"),"SPIRE_NEXT_LEAVE",Array.Empty<MegaCrit.Sts2.Core.HoverTips.IHoverTip>()));return result;
  }
  static async Task ResolveEvent(EventModel model,TerrariaEventPage page,string title,Func<Task<bool>> action) {
   if(page.Busy||AdventureSession.Current.ClaimedNodes.Contains(page.Node))return;page.Busy=true;
   try {
    if(!await action()){RefreshEvent(model,page);return;}
    AdventureSession.Current.ClaimedNodes.Add(page.Node);AdventureSession.PushHostHealth();AdventureSession.Save();
    if(model is AncientEventModel)AccessTools.Method(typeof(AncientEventModel),"UpdateRunHistory")?.Invoke(model,null);
    AccessTools.Method(typeof(EventModel),"SetEventFinished").Invoke(model,new object[]{Text("你选择了「"+title+"」。物资与装备已加入泰拉远征，下一场战斗将直接生效。")});
   }catch(Exception error){GD.PrintErr("[SpireNext Adventure] event "+page.Theme+": "+error);RefreshEvent(model,page,"这次选择未完成。可以重试、选择其他路线或离开。");}
   finally{page.Busy=false;}
  }
  static void RefreshEvent(EventModel model,TerrariaEventPage page,string message=null){var options=BuildEventOptions(model,page);if(model is AncientEventModel)AccessTools.Field(typeof(AncientEventModel),"_generatedOptions").SetValue(model,options.ToList());AccessTools.Method(typeof(EventModel),"SetEventState").Invoke(model,new object[]{Text(message??page.Story),options});}
  static Dictionary<string,int> EventMaterials(int stage,params (string Kind,int Count)[] entries)=>entries.ToDictionary(x=>stage+":"+x.Kind,x=>x.Count);
  static bool HasMaterial(int stage,string kind,int count)=>AdventureSession.Current.Materials.TryGetValue(stage+":"+kind,out int actual)&&actual>=count;
  static Task<bool> EventExchange(int sourceStage,string source,int count,int targetStage,string target,int result){var run=AdventureSession.Current;if(!HasMaterial(sourceStage,source,count))return Task.FromResult(false);run.Materials[sourceStage+":"+source]-=count;run.GiveMaterials(EventMaterials(targetStage,(target,result)));return Task.FromResult(true);}
  static IReadOnlyList<RewardOffer> EventGearOffers(TerrariaEventPage page,string scope) {
   var run=AdventureSession.Current;string node=page.Node+":gear:"+scope;if(run.RewardNodes.TryGetValue(node,out var cached))return cached;
   var pool=GearCatalog.For(run.Hero,run.Stage).Where(g=>(g.Kind==GearKind.Weapon||g.Kind==GearKind.Armor)&&!AdventureRun.IsPrimitive(g));
   if(scope=="armor")pool=pool.Where(g=>g.Kind==GearKind.Armor);
   if(scope=="weapon"||scope=="strong-weapon")pool=pool.Where(g=>g.Kind==GearKind.Weapon);
   if(scope=="sentry"){var sentries=pool.Where(g=>g.Kind==GearKind.Weapon&&g.NativeSymbols[0].StartsWith("DD2")).ToArray();if(sentries.Length>0)pool=sentries;}
   if(scope=="rare"){var rare=pool.Where(g=>g.Rarity==GearRarity.Rare).ToArray();if(rare.Length>0)pool=rare;}
   else if(scope=="strong-weapon"){var strong=pool.Where(g=>g.Rarity!=GearRarity.Common).ToArray();if(strong.Length>0)pool=strong;}
   var owned=run.Weapons.Select(g=>g.DefinitionKey).Append(run.Armor.DefinitionKey).ToHashSet();
   var chosen=pool.OrderBy(g=>owned.Contains(g.Key)?1:0).ThenBy(g=>AdventureSession.StableHash(run.Seed+"/"+node+"/"+g.Key)).Take(3).Select(g=>new RewardOffer{NodeKey=node,OfferId=node+":"+g.Key,GearKey=g.Key,Title=g.Name,Description=GearCatalog.RarityName(g.Rarity)+" · "+g.Description}).ToList();run.RewardNodes[node]=chosen;AdventureSession.Save();return chosen;
  }
  static async Task<bool> EventGear(TerrariaEventPage page,string scope,int cost,int hp) {
   var run=AdventureSession.Current;if(Player.Gold<cost||run.Life<=hp)return false;
   var offers=EventGearOffers(page,scope);var choice=await Choose(page.Title+" · 选择一件装备",offers.Select(o=>new Choice(o.Title,o.Description,GearCatalog.Get(o.GearKey).Kind==GearKind.Armor?"防具":"武器",o)).ToList());if(choice==null)return false;
   var offer=(RewardOffer)choice.Value;int replacement=await ReplacementFor(offer.GearKey);if(replacement==-2||Player.Gold<cost||run.Life<=hp)return false;
   if(!run.Accept(offer,replacement))return false;run.Life-=hp;if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;
  }
  static async Task<bool> EventAccessory(TerrariaEventPage page,int cost) {
   var run=AdventureSession.Current;if(Player.Gold<cost)return false;string node=page.Node+":accessory";
   if(!run.RewardNodes.TryGetValue(node,out var offers)){offers=GearCatalog.All.Where(g=>g.Kind==GearKind.Accessory&&g.Stage<=run.Stage&&GearCatalog.IsForHero(g,run.Hero)&&!run.Accessories.Any(a=>a.DefinitionKey==g.Key)).OrderBy(g=>AdventureSession.StableHash(run.Seed+"/"+node+"/"+g.Key)).Take(2).Select(g=>new RewardOffer{NodeKey=node,OfferId=node+":"+g.Key,GearKey=g.Key,Title=g.Name,Description=g.Description}).ToList();run.RewardNodes[node]=offers;AdventureSession.Save();}
   if(offers.Count==0){run.GiveMaterials(EventMaterials(run.Stage,("essence",3)));if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;}
   var choice=await Choose(page.Title+" · 选择饰品",offers.Select(o=>new Choice(o.Title,o.Description,"饰品",o)).ToList());if(choice==null||Player.Gold<cost||!run.Accept((RewardOffer)choice.Value))return false;if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;
  }
  static async Task<bool> EventSupplies(TerrariaEventPage page,string key,string[] symbols,int count,int cost) {
   var run=AdventureSession.Current;if(Player.Gold<cost)return false;
   // Each child claim is stable, so a partially completed interrupted callback
   // cannot give the same bottle or explosive a second time on retry.
   for(int i=0;i<symbols.Length;i++)for(int n=0;n<count;n++){string node=page.Node+":supply:"+key+":"+i+":"+n;if(run.ClaimedNodes.Contains(node))continue;string gearKey="consumable:"+symbols[i];var offer=new RewardOffer{NodeKey=node,OfferId=node,GearKey=gearKey};run.RewardNodes[node]=new List<RewardOffer>{offer};if(!run.Accept(offer))return false;}
   if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;
  }
  static async Task<bool> EventUpgrade(TerrariaEventPage page,int levels,int cost=0,bool armorOnly=false,int hp=0,bool weaponOnly=false) {
   var run=AdventureSession.Current;if(Player.Gold<cost||run.Life<=hp)return false;var pool=Equipment().Where(g=>g.Level<=1000-levels&&(!armorOnly||g.Definition.Kind==GearKind.Armor)&&(!weaponOnly||g.Definition.Kind==GearKind.Weapon)).ToList();
   var choice=await Choose(page.Title+" · 选择独立强化的装备",pool.Select(g=>new Choice(GearName(g),GearDescription(g)+"\n本次仅此装备 +"+levels+"。","强化",g)).ToList());if(choice==null||Player.Gold<cost||run.Life<=hp)return false;((GearInstance)choice.Value).Level+=levels;run.Life-=hp;if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;
  }
  static async Task<bool> EventAffix(TerrariaEventPage page,bool weaponOnly,int cost=0,bool armorOnly=false) {
   if(Player.Gold<cost)return false;var pool=Equipment().Where(g=>(!weaponOnly||g.Definition.Kind==GearKind.Weapon)&&(!armorOnly||g.Definition.Kind==GearKind.Armor)).ToList();var selected=await Choose(page.Title+" · 选择刻印装备",pool.Select(g=>new Choice(GearName(g),GearDescription(g),"重铸",g)).ToList());if(selected==null)return false;
   var gear=(GearInstance)selected.Value;var affixes=GearCatalog.Affixes.Where(a=>a.Kind==gear.Definition.Kind&&a.Key!=gear.Affix&&(a.Key!="efficient"||AdventureSession.Current.Hero==HeroClass.Mage)).OrderBy(a=>AdventureSession.StableHash(page.Node+"/"+gear.InstanceId+"/"+a.Key)).Take(3).ToList();var chosen=await Choose("选择一项词条 · 只对 "+GearName(gear)+" 生效",affixes.Select(a=>new Choice(a.Name,a.Description,"重铸",a)).ToList());if(chosen==null||Player.Gold<cost)return false;gear.Affix=((AffixDefinition)chosen.Value).Key;if(cost>0)await PlayerCmd.LoseGold(cost,Player);return true;
  }
  static async Task<bool> EventSalvage(TerrariaEventPage page) {
   var run=AdventureSession.Current;if(run.Weapons.Count<=1)return false;var selected=await Choose("拆解备用武器 · 至少保留一把",run.Weapons.Select(g=>new Choice(GearName(g),"拆除这件武器，换取下一阶段矿料×2、水晶×2、精华×1。","材料",g)).ToList());if(selected==null||run.Weapons.Count<=1)return false;run.Weapons.Remove((GearInstance)selected.Value);run.GiveMaterials(EventMaterials(Math.Min(3,run.Stage+1),("ore",2),("crystal",2),("essence",1)));return true;
  }
  internal static Texture2D ChoicePortrait(Choice choice) {
   string symbol=choice.NativeSymbol;
   if(string.IsNullOrEmpty(symbol))symbol=choice.Value is GearInstance item?item.Definition.NativeSymbols[0]:choice.Value is RewardOffer offer&&offer.GearKey!=""?GearCatalog.Get(offer.GearKey).NativeSymbols[0]:choice.Value is ForgeRecipe recipe?GearCatalog.Get(recipe.GearKey).NativeSymbols[0]:null;
   symbol??=choice.Kind=="材料"?"IronOre":choice.Kind=="锻造"||choice.Kind=="强化"||choice.Kind=="重铸"?"IronAnvil":choice.Kind=="药水"?"HealingPotion":null;
   if(symbol!=null&&ItemIcon(symbol)!=null)return AdventureInventoryModels.Portrait(symbol,choice.Kind);
   return Portrait(choice.Kind);
  }
  static Texture2D Portrait(string kind) {
   if(portraits.TryGetValue(kind,out var cached))return cached;
   using var image=Image.CreateEmpty(256,192,false,Image.Format.Rgba8);image.Fill(new Color(.075f,.10f,.13f));
   Color accent=kind=="材料"?new Color(.38f,.8f,.85f):kind=="防具"?new Color(.55f,.66f,.82f):kind=="药水"?new Color(.95f,.35f,.42f):new Color(.95f,.76f,.4f);
   void Rect(int x,int y,int w,int h,Color c){image.FillRect(new Rect2I(x,y,w,h),c);}
   for(int y=0;y<192;y+=16)for(int x=0;x<256;x+=16)if((x+y)%32==0)Rect(x,y,16,16,new Color(.08f,.115f,.15f));
   Rect(20,18,216,2,accent.Darkened(.5f));Rect(20,173,216,2,accent.Darkened(.5f));
   if(kind=="防具"){Rect(96,52,64,68,accent);Rect(72,60,24,30,accent);Rect(160,60,24,30,accent);Rect(106,120,18,28,accent);Rect(134,120,18,28,accent);Rect(115,46,26,16,new Color(.12f,.16f,.22f));}
   else if(kind=="材料"){for(int i=0;i<3;i++){int x=73+i*36,y=75+(i%2)*24;Rect(x,y,29,27,accent.Darkened(i*.13f));Rect(x+5,y-5,20,5,accent.Lightened(.15f));}}
   else if(kind=="锻造"){Rect(74,86,108,18,accent);Rect(94,104,68,16,accent);Rect(110,120,36,20,accent);Rect(90,140,76,9,accent);Rect(78,51,62,17,accent.Lightened(.15f));Rect(101,68,13,26,new Color(.5f,.32f,.2f));}
   else if(kind=="药水"||kind=="投掷物"){Rect(110,48,36,16,accent.Darkened(.2f));Rect(116,64,24,20,accent);Rect(94,84,68,60,accent);Rect(105,91,13,35,accent.Lightened(.45f));}
   else {for(int i=0;i<66;i++)Rect(84+i,133-i,10,10,accent);Rect(78,131,34,9,accent.Darkened(.15f));Rect(91,137,9,20,new Color(.52f,.3f,.18f));Rect(86,154,18,8,accent);}
   var texture=ImageTexture.CreateFromImage(image);portraits[kind]=texture;return texture;
  }
 }
}

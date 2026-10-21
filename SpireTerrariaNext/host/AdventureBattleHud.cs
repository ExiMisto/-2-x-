using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace SpireTerraria.Next.Host {
 // The native inventories own their nodes and input. Battle presentation only
 // measures their visible holders, rather than replacing the player's items.
 internal sealed class AdventureBattleHud:IDisposable {
  internal const int BattleCanvasLayer=110;
  readonly Node nativeHoverContainer,hoverParent;
  readonly int hoverIndex,nativeHoverLayer;
  readonly Node.ProcessModeEnum nativeHoverProcess;
  readonly CanvasLayer hoverLayer;
  bool disposed;
  internal int AccessoryCount {get;private set;}
  internal int PotionCount {get;private set;}
  internal int VisibleAccessoryCount {get;private set;}
  internal int VisiblePotionCount {get;private set;}
  internal static float TopBarBottom {
   get {
    var bar=NRun.Instance?.GlobalUi.TopBar;
    return bar==null?80:Math.Max(bar.Hp.GetGlobalRect().End.Y,bar.Portrait.GetGlobalRect().End.Y);
   }
  }
  internal float Bottom {
   get {
    var inventory=NRun.Instance?.GlobalUi.RelicInventory;
    float bottom=TopBarBottom;
    if(inventory!=null)foreach(var holder in inventory.RelicNodes)
     if(holder.IsVisibleInTree()&&holder.Size.Y>0)bottom=Math.Max(bottom,holder.GetGlobalRect().End.Y);
    return bottom+8;
   }
  }
  internal AdventureBattleHud(BattleConfiguration configuration,Action<int> consume) {
   nativeHoverContainer=NGame.Instance?.HoverTipsContainer;
   if(nativeHoverContainer!=null) {
    nativeHoverProcess=nativeHoverContainer.ProcessMode;nativeHoverContainer.ProcessMode=Node.ProcessModeEnum.Always;
    if(nativeHoverContainer is CanvasLayer nativeLayer){nativeHoverLayer=nativeLayer.Layer;nativeLayer.Layer=BattleCanvasLayer+1;}
    else {
     hoverParent=nativeHoverContainer.GetParent();hoverIndex=nativeHoverContainer.GetIndex();
     hoverLayer=new CanvasLayer{Layer=BattleCanvasLayer+1,Name="SpireTerrariaNativeTooltips",ProcessMode=Node.ProcessModeEnum.Always};
     NGame.Instance.AddChild(hoverLayer);nativeHoverContainer.Reparent(hoverLayer,true);
    }
   }
   NRun.Instance?.GlobalUi.RelicInventory.ShowImmediately();
   AdventureInventoryModels.RefreshVisibleInventory();UpdateCounts();
  }
  internal void Update(AdvancedBattleState state) {UpdateCounts();}
  void UpdateCounts() {
   var player=AdventureSession.HostRun?.Players.FirstOrDefault();
   AccessoryCount=player?.Relics.OfType<AdventureAccessory>().Count()??0;
   PotionCount=AdventureSession.Current?.Consumables.Count(c=>GearCatalog.Get(c.DefinitionKey).NativeSymbols[0].Contains("Potion")&&!(HostIntegration.Active?.ConsumedInstanceIds.Contains(c.InstanceId)??false))??0;
   var inventory=NRun.Instance?.GlobalUi.RelicInventory;
   VisibleAccessoryCount=inventory?.RelicNodes.Count(holder=>holder.Relic?.Model is AdventureAccessory&&Readable(holder.Relic.Icon))??0;
   var potions=NRun.Instance?.GlobalUi.TopBar.PotionContainer;
   VisiblePotionCount=potions==null?0:Descendants(potions).OfType<NPotion>().Count(p=>p.Model is AdventurePotion&&Readable(p.Image));
  }
  static bool Readable(TextureRect icon) {
   if(icon==null||!icon.IsVisibleInTree()||icon.Texture==null||icon.Modulate.A*icon.SelfModulate.A<.05f||icon.Size.X<1||icon.Size.Y<1)return false;
   return icon.GetGlobalRect().Intersects(icon.GetViewportRect());
  }
  static IEnumerable<Node> Descendants(Node parent) {foreach(Node child in parent.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
  public void Dispose() {
   if(disposed)return;disposed=true;bool restored=nativeHoverContainer==null;
   if(GodotObject.IsInstanceValid(nativeHoverContainer)) {
    nativeHoverContainer.ProcessMode=nativeHoverProcess;
    if(nativeHoverContainer is CanvasLayer nativeLayer){nativeLayer.Layer=nativeHoverLayer;restored=nativeLayer.Layer==nativeHoverLayer;}
    else if(GodotObject.IsInstanceValid(hoverParent)) {
     nativeHoverContainer.Reparent(hoverParent,true);hoverParent.MoveChild(nativeHoverContainer,Math.Min(hoverIndex,hoverParent.GetChildCount()-1));
     restored=nativeHoverContainer.GetParent()==hoverParent&&nativeHoverContainer.GetIndex()==hoverIndex;
    }
   } else restored=true; // Returning to the menu may already have freed its UI.
   if(GodotObject.IsInstanceValid(hoverLayer)&&hoverLayer.GetChildCount()==0)hoverLayer.QueueFree();
   if(SpireSelfTest.Enabled)SpireSelfTest.Report["nativeTooltipLayerRestored"]=restored;
   if(!restored)throw new InvalidOperationException("The native tooltip container was not restored after Terraria combat.");
  }
 }
}

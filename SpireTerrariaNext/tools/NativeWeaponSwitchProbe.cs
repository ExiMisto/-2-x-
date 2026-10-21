using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Xna.Framework;
using SpireTerraria.Next;

internal static class WeaponSwitchProbe {
 static readonly List<object> samples=new List<object>();
 static readonly List<object> drawSamples=new List<object>();
 static readonly HashSet<int> visibleWeaponSlots=new HashSet<int>();
 static readonly List<string> checks=new List<string>();
 static object Get(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(target);}
 static int TypeId(object item){return (int)Get(item,"type");}
 static void Check(bool okay,string label){if(!okay)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("PASS "+label);}
 static AdvancedBattleState State(NativeArena arena){using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))arena.WriteAdvancedState(writer);memory.Position=0;using(var reader=new BinaryReader(memory))return AdvancedBattleState.Read(reader);}}
 static void Image(NativeArena arena,string output,string name){string session=(string)Get(arena,"frameSession");using(FrameExchange reader=FrameExchange.OpenReader(session)){SharedFrame frame;reader.ReadLatest(0,out frame);using(var bitmap=new System.Drawing.Bitmap(frame.Width,frame.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,frame.Width,frame.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{System.Runtime.InteropServices.Marshal.Copy(frame.Pixels,0,data.Scan0,frame.Pixels.Length);}finally{bitmap.UnlockBits(data);}bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}}}
 static void Sample(NativeArena arena,object player,string phase,int slot,int cycle) {
  object held=player.GetType().GetProperty("HeldItem").GetValue(player,null),last=Get(player,"lastVisualizedSelectedItem");
  int heldId=TypeId(held),lastId=TypeId(last),selected=(int)player.GetType().GetProperty("selectedItem").GetValue(player,null);Array inventory=(Array)Get(player,"inventory");
  if(heldId!=lastId||selected!=slot||heldId!=TypeId(inventory.GetValue(slot)))throw new InvalidOperationException("Visual mismatch "+phase+" cycle="+cycle+" slot="+slot+" selected="+selected+" held="+heldId+" last="+lastId);
  Vector2 position=(Vector2)Get(player,"position"),hand=(Vector2)Get(player,"itemLocation");int animation=(int)Get(player,"itemAnimation");float rotation=(float)Get(player,"itemRotation");
  if(animation>0&&(Vector2.Distance(position,hand)>110||float.IsNaN(rotation)||float.IsInfinity(rotation)))throw new InvalidOperationException("Invalid visual pose for slot "+slot);
  object renderer=Get(arena,"renderer");int layers=(int)renderer.GetType().GetProperty("PlayerDrawCount",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(renderer,null);
  if(layers<6)throw new InvalidOperationException("Native player layers missing");
  if(cycle<3&&phase=="attack-4") {
   var details=new List<object>();Array nativeItems=(Array)player.GetType().Assembly.GetType("Terraria.GameContent.TextureAssets",true).GetField("Item").GetValue(null);object asset=nativeItems.GetValue(heldId);object itemTexture=asset.GetType().GetProperty("Value").GetValue(asset,null);
   foreach(object data in (System.Collections.IEnumerable)Get(renderer,"drawData")) {
    var texture=(Microsoft.Xna.Framework.Graphics.Texture2D)Get(data,"texture");
    if(object.ReferenceEquals(texture,itemTexture)&&Get(data,"sourceRect") is Rectangle){Rectangle frame=(Rectangle)Get(data,"sourceRect");if(frame.Width>0&&frame.Height>0)visibleWeaponSlots.Add(slot);}
    details.Add(new{texture=texture.Name,width=texture.Width,height=texture.Height,position=Get(data,"position").ToString(),source=Get(data,"sourceRect")==null?null:Get(data,"sourceRect").ToString(),color=Get(data,"color").ToString(),scale=Get(data,"scale").ToString(),rotation=Get(data,"rotation")});
   }
   var entry=new{slot=slot,held=heldId,noUseGraphic=Get(held,"noUseGraphic"),useStyle=Get(held,"useStyle"),allowed=Get(player,"IsAllowedToHoldItems"),justDropped=Get(player,"JustDroppedAnItem"),details=details};drawSamples.Add(entry);
  }
  samples.Add(new{cycle=cycle,phase=phase,slot=slot,selected=selected,held=heldId,last=lastId,animation=animation,rotation=rotation,hand=hand.ToString(),position=position.ToString(),layers=layers});
 }
 [STAThread] static int Main(string[] args) {
  string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);bool passed=false;Exception failure=null;
  try {
   var runtime=new TerrariaRuntime(args[0]);
   var configuration=new BattleConfiguration{Seed=9316,Life=10000,LifeMax=10000,EnemyCount=40,MaxConcurrent=3,Weapons=new[]{"EnchantedSword","GoldBow","WaterBolt"},WeaponLevels=new[]{0,0,0},WeaponAffixes=new[]{"","",""},Armor=new[]{"WoodHelmet","WoodBreastplate","WoodGreaves"}};
   using(var arena=new NativeArena(runtime,configuration)) {
    var initial=State(arena);object player=Get(arena,"player");int[] order={0,1,2,1,0,2,0,2,1};var swordRotations=new HashSet<float>();var active=new HashSet<int>();
    for(int cycle=0;cycle<order.Length;cycle++) {
     int slot=order[cycle];float targetX=initial.PlayerX+180,targetY=initial.PlayerY+(cycle%2==0?-25:-100);
     arena.AdventureStep(1,0,targetX,targetY,slot);Sample(arena,player,"switch-idle",slot,cycle);
     for(int tick=0;tick<18;tick++) {
      arena.AdventureStep(1,8,targetX,targetY,slot);Sample(arena,player,"attack-"+tick,slot,cycle);
      if((int)Get(player,"itemAnimation")>0){active.Add(slot);if(slot==0)swordRotations.Add((float)Get(player,"itemRotation"));}
      if(cycle<3&&tick==4)Image(arena,output,"switch-"+slot+"-"+configuration.Weapons[slot]);
     }
    }
    Check(samples.Count==171,"171 idle and active ticks match HeldItem lastVisualizedSelectedItem and selected native slot");
    Check(active.Count==3,"Sword bow and magic each animate after repeated slot switches");
    Check(swordRotations.Count>8,"Sword resumes a native changing swing after bow and magic switches");
    Check(visibleWeaponSlots.SetEquals(new[]{0,1,2}),"Sword bow and spellbook each draw a positive-size native source rectangle");
    for(int tick=0;tick<30;tick++){int slot=tick%3;arena.AdventureStep(1,8,initial.PlayerX+180,initial.PlayerY-50,slot);Sample(arena,player,"rapid-switch",slot,tick);}
    Check(samples.Count==201,"Rapid switching every tick keeps the visual item synchronized");
    Type main=runtime.NativeAssembly.GetType("Terraria.Main",true);Check((bool)main.GetField("dedServ").GetValue(null),"Presentation restores the dedicated-server simulation context");
    object renderer=Get(arena,"renderer");FieldInfo menu=main.GetField("gameMenu"),screen=main.GetField("screenPosition"),dedicated=main.GetField("dedServ");
    object previousMenu=menu.GetValue(null),previousScreen=screen.GetValue(null),previousDedicated=dedicated.GetValue(null);Vector2 sentinel=new Vector2(111,222);
    try {
     menu.SetValue(null,false);screen.SetValue(null,sentinel);dedicated.SetValue(null,true);
     ((NativeRenderer)renderer).RenderScene((Array)Get(arena,"tiles"),player,null,null,0,0);
     Check(!(bool)menu.GetValue(null)&&(bool)dedicated.GetValue(null)&&(Vector2)screen.GetValue(null)==sentinel,"Successful native player drawing restores gameMenu screenPosition and dedServ");
     FieldInfo drawMethod=renderer.GetType().GetField("drawDataDraw",BindingFlags.NonPublic|BindingFlags.Instance);object originalMethod=drawMethod.GetValue(renderer);bool threw=false;
     try {
      drawMethod.SetValue(renderer,null);
      try{renderer.GetType().GetMethod("DrawNativePlayer",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(renderer,new object[]{player,new Vector2(333,444)});}catch(TargetInvocationException error){threw=error.InnerException is NullReferenceException;}
     }finally{drawMethod.SetValue(renderer,originalMethod);}
     Check(threw&&!(bool)menu.GetValue(null)&&(bool)dedicated.GetValue(null)&&(Vector2)screen.GetValue(null)==sentinel,"Forced draw failure restores all three static guards in finally");
     bool animationThrew=false;
     try{((NativeRenderer)renderer).UpdatePlayerPresentation(new object());}catch(NullReferenceException){animationThrew=true;}
     Check(animationThrew&&(bool)dedicated.GetValue(null),"Failed presentation calculation restores dedServ in finally");
    }finally{menu.SetValue(null,previousMenu);screen.SetValue(null,previousScreen);dedicated.SetValue(null,previousDedicated);}
   }
   passed=true;
  }catch(Exception error){failure=error;Console.Error.WriteLine(error);}
  File.WriteAllText(Path.Combine(output,"report.json"),new JavaScriptSerializer().Serialize(new{passed=passed,checks=checks,samples=samples,drawSamples=drawSamples,failure=failure==null?null:failure.ToString()}),new UTF8Encoding(false));return passed?0:1;
 }
}

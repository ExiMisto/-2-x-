using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Xna.Framework;
using SpireTerraria.Next;

internal static class NativeRecoveryContentProbe {
 const BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static readonly List<object> samples=new List<object>();
 static readonly List<string> failures=new List<string>();
 static readonly Dictionary<string,object> report=new Dictionary<string,object>();
 static string output;
 static object Get(object target,string name) {SnapshotPlayer snapshot=target as SnapshotPlayer;if(snapshot!=null)return snapshot.Read(name);Type type=target as Type??target.GetType();FieldInfo field=type.GetField(name,Fields);if(field==null)throw new MissingFieldException(type.FullName,name);return field.GetValue(target is Type?null:target);}
 static void Set(object target,string name,object value) {Type type=target as Type??target.GetType();type.GetField(name,Fields).SetValue(target is Type?null:target,value);}
 static double Number(object target,string name){return Convert.ToDouble(Get(target,name),System.Globalization.CultureInfo.InvariantCulture);}
 static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
 static AdvancedBattleState State(NativeArena arena) {using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))arena.WriteAdvancedState(writer);memory.Position=0;using(var reader=new BinaryReader(memory))return AdvancedBattleState.Read(reader);}}
 static Dictionary<string,object> Snapshot(object player) {
  var result=new Dictionary<string,object>();
  foreach(string field in new[]{"statDefense","statManaMax2","manaRegenBonus","manaCost","meleeDamage","rangedDamage","magicDamage","minionDamage","meleeSpeed","itemAnimationMax","moveSpeed","armorPenetration","maxMinions","maxTurrets","rocketBoots","accRunSpeed","noKnockback","fireWalk","magicCuffs","manaFlower","longInvince","wolfAcc","wereWolf","setSquireT2","setSquireT3","setHuntressT2","setHuntressT3","setApprenticeT2","setApprenticeT3","setMonkT2","setMonkT3","head","body","legs","shield","shoe","waist","neck"})result[field]=Get(player,field);
  object cloak=Get(player,"starCloakItem");result["starCloakItem"]=cloak==null?0:Get(cloak,"type");return result;
 }
 static double Baseline(Dictionary<string,object> baseline,string field){return Convert.ToDouble(baseline[field],System.Globalization.CultureInfo.InvariantCulture);}
 static void AccessoryEffect(string symbol,object player,Dictionary<string,object> baseline) {
  switch(symbol) {
   case "WarriorEmblem":Require(Number(player,"meleeDamage")>Baseline(baseline,"meleeDamage")+.1,"warrior emblem raises native melee damage");break;
   case "RangerEmblem":Require(Number(player,"rangedDamage")>Baseline(baseline,"rangedDamage")+.1,"ranger emblem raises native ranged damage");break;
   case "SorcererEmblem":Require(Number(player,"magicDamage")>Baseline(baseline,"magicDamage")+.1,"sorcerer emblem raises native magic damage");break;
   case "SummonerEmblem":Require(Number(player,"minionDamage")>Baseline(baseline,"minionDamage")+.1,"summoner emblem raises native summon damage");break;
   case "Shackle":Require(Number(player,"statDefense")==Baseline(baseline,"statDefense")+1,"shackle raises native defense by one");break;
   case "Aglet":Require(Number(player,"moveSpeed")>Baseline(baseline,"moveSpeed")+.04,"aglet raises native movement speed");break;
   case "AnkletoftheWind":Require(Number(player,"moveSpeed")>Baseline(baseline,"moveSpeed")+.09,"anklet raises native movement speed");break;
   case "SharkToothNecklace":Require(Number(player,"armorPenetration")>=Baseline(baseline,"armorPenetration")+5,"shark tooth raises native armor penetration");break;
   case "AvengerEmblem":foreach(string field in new[]{"meleeDamage","rangedDamage","magicDamage","minionDamage"})Require(Number(player,field)>Baseline(baseline,field)+.1,"avenger emblem raises native "+field);break;
   case "ApprenticeScarf":case "SquireShield":case "HuntressBuckler":case "MonkBelt":Require(Number(player,"maxTurrets")>Baseline(baseline,"maxTurrets")&&Number(player,"minionDamage")>Baseline(baseline,"minionDamage")+.09,"tavern accessory raises native sentry capacity and summon damage");break;
   case "ObsidianShield":Require((bool)Get(player,"noKnockback")&&(bool)Get(player,"fireWalk")&&Number(player,"statDefense")>=Baseline(baseline,"statDefense")+2,"obsidian shield grants native defense and immunities");break;
   case "SpectreBoots":Require(Number(player,"rocketBoots")>0&&Number(player,"accRunSpeed")>Baseline(baseline,"accRunSpeed"),"spectre boots enable native flight and running");break;
   // Player.Update stores the reciprocal timing multiplier after processing
   // equipment: a native +12% bonus ends at 1/1.12, rather than 1.12.
   case "FeralClaws":Require(Number(player,"meleeSpeed")>0&&Number(player,"meleeSpeed")<Baseline(baseline,"meleeSpeed")&&Number(player,"itemAnimationMax")<Baseline(baseline,"itemAnimationMax"),"feral claws reduce native melee timing and actual swing duration");break;
   case "ManaRegenerationBand":Require(Number(player,"statManaMax2")>=Baseline(baseline,"statManaMax2")+20&&Number(player,"manaRegenBonus")>Baseline(baseline,"manaRegenBonus"),"mana regeneration band raises native mana and regeneration");break;
   case "MagicCuffs":Require((bool)Get(player,"magicCuffs")&&Number(player,"statManaMax2")>=Baseline(baseline,"statManaMax2")+20,"magic cuffs enable native mana-on-hurt behavior");break;
   case "ManaFlower":Require((bool)Get(player,"manaFlower")&&Number(player,"manaCost")<Baseline(baseline,"manaCost")-.05,"mana flower enables native cost reduction");break;
   case "PygmyNecklace":Require(Number(player,"maxMinions")==Baseline(baseline,"maxMinions")+1,"pygmy necklace raises native minion capacity");break;
   case "StarVeil":Require((bool)Get(player,"longInvince")&&Number(player,"starCloakItem")>0,"star veil enables native invincibility and star-cloak behavior");break;
   case "MoonCharm":Require((bool)Get(player,"wolfAcc")&&(bool)Get(player,"wereWolf"),"moon charm enables native full-moon werewolf transformation");break;
   default:throw new NotSupportedException("No effect assertion for "+symbol);
  }
 }
 static string ArmorSetFlag(string key) {
  if(key.EndsWith(":squire",StringComparison.Ordinal))return "setSquireT2";
  if(key.EndsWith(":valhalla",StringComparison.Ordinal))return "setSquireT3";
  if(key.EndsWith(":huntress",StringComparison.Ordinal))return "setHuntressT2";
  if(key.EndsWith(":red-riding",StringComparison.Ordinal))return "setHuntressT3";
  if(key.EndsWith(":apprentice",StringComparison.Ordinal))return "setApprenticeT2";
  if(key.EndsWith(":dark-artist",StringComparison.Ordinal))return "setApprenticeT3";
  if(key.EndsWith(":monk",StringComparison.Ordinal))return "setMonkT2";
  if(key.EndsWith(":shinobi",StringComparison.Ordinal))return "setMonkT3";
  throw new NotSupportedException("No armor-set assertion for "+key);
 }
 static string Slug(string key){return key.Replace(':','_').Replace('/','_');}
 static object SaveImage(NativeArena arena,string name) {
  string session=(string)Get(arena,"frameSession");
  using(var reader=FrameExchange.OpenReader(session)) {
   SharedFrame frame;Require(reader.ReadLatest(0,out frame)==FrameReadStatus.Available,"render produced a shared frame");
   Require(frame.Width==640&&frame.Height==360&&frame.Pixels.Length==640*360*4,"native frame dimensions match the renderer");
   int colored=0;for(int p=4;p<frame.Pixels.Length;p+=4)if(frame.Pixels[p]!=frame.Pixels[0]||frame.Pixels[p+1]!=frame.Pixels[1]||frame.Pixels[p+2]!=frame.Pixels[2])colored++;
   Require(colored>5000,"render contains visible scene geometry and content");
   using(var bitmap=new System.Drawing.Bitmap(frame.Width,frame.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
    var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,frame.Width,frame.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try{System.Runtime.InteropServices.Marshal.Copy(frame.Pixels,0,data.Scan0,frame.Pixels.Length);}finally{bitmap.UnlockBits(data);}
    bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);
   }
   return new{width=frame.Width,height=frame.Height,nonUniformPixels=colored};
  }
 }
 static Dictionary<string,object> Exercise(TerrariaRuntime runtime,string key,string[] armor,string[] accessories,bool saveImage,string weapon="WoodenSword") {
  var row=new Dictionary<string,object>();row["key"]=key;row["armor"]=armor;row["accessories"]=accessories;
  try {
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Stage=3,Weapons=new[]{weapon},Armor=armor,Accessories=accessories,Life=10000,LifeMax=10000,EnemyCount=8,MaxConcurrent=3})) {
    object player=Get(arena,"player");Set(runtime.NativeAssembly.GetType("Terraria.Main",true),"moonPhase",0);var start=State(arena);
    // A non-autoswing weapon needs a released input before its first press.
    // Feral Claws enables autoswing, so skipping this tick gives an invalid
    // comparison against an idle baseline rather than an unmodified swing.
    arena.AdventureStep(1,0,start.PlayerX+180,start.PlayerY-25,0);
    FieldInfo blink=player.GetType().GetField("immuneNoBlink",Fields);if(blink!=null)blink.SetValue(player,true);
    int ticks=key.StartsWith("weapon:",StringComparison.Ordinal)?36:12;
    for(int i=0;i<ticks/2;i++) {Set(player,"immune",true);Set(player,"immuneTime",120);arena.AdventureStep(2,(byte)(i%8<6?8:0),start.PlayerX+180,start.PlayerY-25,0);}
    var end=State(arena);row["nativeFields"]=Snapshot(player);row["ticks"]=end.Tick;row["frames"]=end.Frame;row["activeProjectiles"]=end.Projectiles;
    object renderer=Get(arena,"renderer");int layers=(int)renderer.GetType().GetProperty("PlayerDrawCount",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(renderer,null);row["playerDrawLayers"]=layers;row["textures"]=renderer.GetType().GetProperty("TextureCount",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(renderer,null);
    Require(end.Tick==ticks+1&&end.Frame>=ticks/2+2&&!end.Dead,"equipped player completes its native ticks and renders every sample");Require(layers>=6,"native layered player renderer produced armor/accessory draw data");
    if(key.StartsWith("weapon:",StringComparison.Ordinal))Require(end.Projectiles>0,"native sentry staff creates its persistent sentry projectile");
    if(saveImage)row["image"]=SaveImage(arena,Slug(key));
    if(key.StartsWith("armor:",StringComparison.Ordinal)) {
     string flag=ArmorSetFlag(key);Require((bool)Get(player,flag),"native full set flag activates: "+flag);Array equipped=(Array)Get(player,"armor");int expected=0;
     for(int slot=0;slot<3;slot++)expected+=(int)Get(equipped.GetValue(slot),"defense");
     Require(Number(player,"statDefense")>=expected&&expected>20,"native armor defense includes all three pieces");Require(Number(player,"maxTurrets")>=2&&Number(player,"minionDamage")>1,"native Tavernkeeper set grants its sentry and summon bonuses");
    }
    row["passed"]=true;
   }
  }catch(Exception error){row["passed"]=false;row["error"]=error.ToString();failures.Add(key+": "+error);Console.Error.WriteLine("FAIL "+key+": "+error.Message);}
  return row;
 }
 static Dictionary<string,object> BaselineRun(TerrariaRuntime runtime,bool extended) {
  var row=Exercise(runtime,extended?"baseline-extra-slot":"baseline",new string[0],extended?new[]{"Shackle","Shackle","Shackle","Shackle","Shackle","Shackle","Shackle"}:new string[0],false);
  Require((bool)row["passed"],"baseline native battle/render succeeds");return (Dictionary<string,object>)row["nativeFields"];
 }
 static void Run(TerrariaRuntime runtime,string input) {
  var baseline=BaselineRun(runtime,false);var extraBaseline=BaselineRun(runtime,true);report["baseline"]=baseline;report["extraSlotBaseline"]=extraBaseline;
  int armorCount=0,accessoryCount=0,extraCount=0,weaponCount=0;
  foreach(string line in File.ReadAllLines(input)) {
   string[] parts=line.Split('\t');string key=parts[0],kind=parts[1];string[] symbols=parts[3].Split(' ');
   if(kind=="Weapon") {
    var row=Exercise(runtime,key,new string[0],new string[0],true,symbols[0]);samples.Add(row);weaponCount++;if((bool)row["passed"])Console.WriteLine("PASS "+key+" native sentry attack/render");continue;
   }
   if(kind=="Armor") {
    var row=Exercise(runtime,key,symbols,new string[0],true);samples.Add(row);armorCount++;if((bool)row["passed"])Console.WriteLine("PASS "+key+" native set/defense/render");continue;
   }
   foreach(bool extended in new[]{false,true}) {
    string label=key+(extended?":extra-slot":"");string[] accessories=extended?new[]{"Shackle","Shackle","Shackle","Shackle","Shackle","Shackle","Shackle",symbols[0]}:symbols;
    var row=Exercise(runtime,label,new string[0],accessories,true);samples.Add(row);if(extended)extraCount++;else accessoryCount++;
    if(!(bool)row["passed"])continue;
    try {
     // Assert against the actual player fields captured after native Player.Update.
     var actual=(Dictionary<string,object>)row["nativeFields"];AccessorySnapshotEffect(symbols[0],actual,extended?extraBaseline:baseline);Console.WriteLine("PASS "+label+" native effect/render");
    }catch(Exception error){row["passed"]=false;row["error"]=error.ToString();failures.Add(label+": "+error);Console.Error.WriteLine("FAIL "+label+": "+error.Message);}
   }
  }
  report["armorChecked"]=armorCount;report["accessoriesChecked"]=accessoryCount;report["extraSlotAccessoriesChecked"]=extraCount;report["weaponsChecked"]=weaponCount;
  Require(armorCount==8&&accessoryCount==22&&extraCount==22&&weaponCount==4,"all recovered definitions tested in native combat/render");
 }
 // Snapshot validation keeps the comparison stable after arena disposal and
 // process-wide native arrays have been replaced by the next independent sample.
 static void AccessorySnapshotEffect(string symbol,Dictionary<string,object> actual,Dictionary<string,object> baseline) {
  var player=new SnapshotPlayer(actual);AccessoryEffect(symbol,player,baseline);
 }
 sealed class SnapshotPlayer {
  readonly Dictionary<string,object> values;
  internal SnapshotPlayer(Dictionary<string,object> values){this.values=values;}
  internal object Read(string name){return values[name];}
 }
 [STAThread]static int Main(string[] args) {
  bool completed=false;output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);var watch=System.Diagnostics.Stopwatch.StartNew();
  try{Run(new TerrariaRuntime(args[0]),args[2]);completed=true;}catch(Exception error){report["fatalError"]=error.ToString();failures.Add(error.ToString());Console.Error.WriteLine(error);}
  report["passed"]=completed&&failures.Count==0;report["durationSeconds"]=watch.Elapsed.TotalSeconds;report["samples"]=samples;report["failures"]=failures;report["completedAtUtc"]=DateTime.UtcNow.ToString("o");
  File.WriteAllText(Path.Combine(output,"verification.json"),new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(report),new UTF8Encoding(false));return completed&&failures.Count==0?0:1;
 }
}

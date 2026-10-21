using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Xna.Framework;
using SpireTerraria.Next;

internal static class NativeCombatTextProbe {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static readonly List<string> checks=new List<string>();
 static readonly Dictionary<string,object> report=new Dictionary<string,object>();
 static string output;static Type main,textType;static Array pool;
 static object Get(object target,string field){Type type=target as Type??target.GetType();return type.GetField(field,All).GetValue(target is Type?null:target);}
 static void Set(object target,string field,object value){Type type=target as Type??target.GetType();type.GetField(field,All).SetValue(target is Type?null:target,value);}
 static void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("PASS "+label);}
 static void Clear(){textType.GetMethod("clearAll",All).Invoke(null,null);}
 static void Update(int ticks){for(int i=0;i<ticks;i++)textType.GetMethod("UpdateCombatText",All).Invoke(null,null);}
 static List<object> Active(){var result=new List<object>();foreach(object entry in pool)if((bool)Get(entry,"active"))result.Add(entry);return result;}
 static object Find(string number,bool crit,bool dot){foreach(object entry in Active())if((string)Get(entry,"text")==number&&(bool)Get(entry,"crit")==crit&&(bool)Get(entry,"dot")==dot)return entry;return null;}
 static byte[] Render(NativeArena arena){typeof(NativeArena).GetMethod("Render",All).Invoke(arena,null);using(var reader=FrameExchange.OpenReader((string)Get(arena,"frameSession"))){SharedFrame frame;reader.ReadLatest(0,out frame);if(frame==null)throw new InvalidOperationException("No rendered frame.");return (byte[])frame.Pixels.Clone();}}
 static void Save(byte[] pixels,string name){using(var bitmap=new System.Drawing.Bitmap(640,360,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,640,360),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{System.Runtime.InteropServices.Marshal.Copy(pixels,0,data.Scan0,pixels.Length);}finally{bitmap.UnlockBits(data);}bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}}
 static void VerifyPixels(NativeArena arena,string name){
  var entries=Active();var lifetimes=new List<int>();foreach(object entry in entries)lifetimes.Add((int)Get(entry,"lifeTime"));
  byte[] visible=Render(arena);Save(visible,name);var renderer=(NativeRenderer)Get(arena,"renderer");Check(renderer.CombatTextDrawCount>0,name+" draws native combat messages");
  foreach(object entry in entries)Set(entry,"active",false);byte[] hidden=Render(arena);
  int changed=0;for(int i=0;i<visible.Length;i+=4)if(visible[i]!=hidden[i]||visible[i+1]!=hidden[i+1]||visible[i+2]!=hidden[i+2])changed++;
  Check(changed>10,name+" has visible glyph pixels, not just an active data entry");report[name+".glyphPixels"]=changed;
  for(int i=0;i<entries.Count;i++){Set(entries[i],"active",true);Check((int)Get(entries[i],"lifeTime")==lifetimes[i],name+" drawing leaves native lifetime unchanged");}
 }
 static int Id(Assembly game,string type,string symbol){return Convert.ToInt32(game.GetType(type,true).GetField(symbol).GetRawConstantValue());}
 static void Run(TerrariaRuntime runtime){
  Assembly game=runtime.NativeAssembly;main=game.GetType("Terraria.Main",true);textType=game.GetType("Terraria.CombatText",true);
  object previousFonts=Get(game.GetType("Terraria.GameContent.FontAssets",true),"CombatText");
  using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{"WoodenSword"},EnemyCount=3,MaxConcurrent=1,Life=1000,LifeMax=1000})){
   pool=(Array)Get(main,"combatText");object hero=Get(arena,"player"),enemy=Get(arena,"npc");
   Vector2 p=(Vector2)Get(hero,"position");Set(enemy,"position",p+new Vector2(135,0));Set(enemy,"lifeMax",5000);Set(enemy,"life",5000);Set(enemy,"defense",10);Set(enemy,"dontTakeDamage",false);
   MethodInfo strike=enemy.GetType().GetMethod("StrikeNPC",new[]{typeof(int),typeof(float),typeof(int),typeof(bool),typeof(bool),typeof(int)});
   Clear();int life=(int)Get(enemy,"life");int normal=(int)strike.Invoke(enemy,new object[]{45,0f,1,false,false,0});
   Check(normal==life-(int)Get(enemy,"life")&&Find(normal.ToString(),false,false)!=null,"ordinary glyph equals actual native damage after defense");
   report["normalDamage"]=normal;Update(10);VerifyPixels(arena,"01-normal");
   Clear();life=(int)Get(enemy,"life");int critical=(int)strike.Invoke(enemy,new object[]{45,0f,1,true,false,0});
   Check(critical==life-(int)Get(enemy,"life")&&Find(critical.ToString(),true,false)!=null&&critical>normal,"critical glyph equals actual native critical damage");
   report["criticalDamage"]=critical;Update(10);VerifyPixels(arena,"02-critical");
   var renderer=(NativeRenderer)Get(arena,"renderer");object[] fonts=(object[])Get(renderer,"combatFonts");MethodInfo measure=fonts[0].GetType().GetMethod("MeasureString",new[]{typeof(string)});
   Vector2 normalSize=(Vector2)measure.Invoke(fonts[0],new object[]{"88"}),critSize=(Vector2)measure.Invoke(fonts[1],new object[]{"88"});
   Check(!object.ReferenceEquals(fonts[0],fonts[1])&&critSize.Y>normalSize.Y,"critical damage uses the larger native crit font");
   Clear();Set(hero,"immune",false);life=(int)Get(hero,"statLife");
   object reason=game.GetType("Terraria.DataStructures.PlayerDeathReason",true).GetMethod("ByNPC").Invoke(null,new object[]{0});
   MethodInfo hurt=hero.GetType().GetMethod("Hurt",new[]{reason.GetType(),typeof(int),typeof(int),typeof(bool),typeof(bool),typeof(bool),typeof(int),typeof(bool)});
   double hurtDamage=(double)hurt.Invoke(hero,new object[]{reason,37,1,false,false,false,-1,false});
   object hurtText=Find(((int)hurtDamage).ToString(),false,false);
   Check(hurtDamage>0&&life-(int)Get(hero,"statLife")== (int)hurtDamage&&hurtText!=null,"player hurt glyph equals actual native life lost");
   Check((Color)Get(hurtText,"color")== (Color)Get(textType,"DamagedFriendly"),"player damage retains native friendly damage color");report["playerHurt"]=hurtDamage;Update(10);VerifyPixels(arena,"03-player-hurt");
   Clear();bool sawDot=false;int dotStart=(int)Get(enemy,"life");
   enemy.GetType().GetMethod("AddBuff",new[]{typeof(int),typeof(int),typeof(bool)}).Invoke(enemy,new object[]{Id(game,"Terraria.ID.BuffID","OnFire"),600,false});
   for(int i=0;i<360&&!sawDot;i++){
    Set(enemy,"position",p+new Vector2(135,0));Set(enemy,"velocity",Vector2.Zero);arena.AdventureStep(1,0,p.X+135,p.Y,0);
    foreach(object entry in Active())if((bool)Get(entry,"dot")){sawDot=true;report["dotDamage"]=Get(entry,"text");break;}
   }
   Check(sawDot&&(int)Get(enemy,"life")<dotStart,"native burning damage creates a real DoT glyph");Update(8);VerifyPixels(arena,"04-dot");
   Clear();int index=(int)textType.GetMethod("NewText",new[]{typeof(Rectangle),typeof(Color),typeof(int),typeof(bool),typeof(bool)}).Invoke(null,new object[]{new Rectangle((int)p.X,(int)p.Y,20,42),Color.White,1,false,false});
   object fading=pool.GetValue(index);Update(8);Vector2 first=(Vector2)Get(fading,"position");int lifetime=(int)Get(fading,"lifeTime");Update(10);
   Check((int)Get(fading,"lifeTime")<lifetime&&(Vector2)Get(fading,"position")!=first,"native updates animate and age combat text");Update(140);Check(Active().Count==0,"native lifetime removes expired messages");
   Clear();strike.Invoke(enemy,new object[]{45,0f,1,false,false,0});Check(Active().Count>0,"room cleanup test begins with a live native message");
  }
  Check(Active().Count==0,"leaving a combat room clears all combat messages");
  Check(object.ReferenceEquals(previousFonts,Get(game.GetType("Terraria.GameContent.FontAssets",true),"CombatText")),"renderer disposal restores original native font bindings");
  using(var next=new NativeArena(runtime,new BattleConfiguration{Seed=9317,Weapons=new[]{"WoodenSword"},Life=1000,LifeMax=1000}))Check(Active().Count==0&&((NativeRenderer)Get(next,"renderer")).CombatTextDrawCount==0,"next room has no stale damage text");
 }
 [STAThread]static int Main(string[] args){bool passed=false;try{output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Run(new TerrariaRuntime(args[0]));passed=true;}catch(Exception error){report["failure"]=error.ToString();Console.Error.WriteLine(error);}report["passed"]=passed;report["checks"]=checks;File.WriteAllText(Path.Combine(output,"combat-text-verification.json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));return passed?0:1;}
}

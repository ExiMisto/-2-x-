using System;
using System.IO;
using System.Diagnostics;
using TerrariaICEY.Bridge;
using SpireTerraria;
class NativeProbe {
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static int Main(string[] args){
  try{using(var b=new NativeBridge(args[0],args[1])){
   Console.WriteLine("Terraria "+b.Version);EngineOutput o=b.Step(Arena.Input(true));
   float start=o.X,minY=o.Y;int totalHits=0;var timer=Stopwatch.StartNew();
   for(int t=0;t<90;t++){var i=Arena.Input(false);i.Right=t<55;i.Jump=t>=12&&t<27;o=b.Step(i);minY=Math.Min(minY,o.Y);}
   Require(o.X>start+80,"native movement");Require(minY<Arena.Floor-45,"native jump");Require(Math.Abs(o.Y-Arena.Floor)<3,"solid floor collision");
   for(int weapon=0;weapon<3;weapon++){
    int hits=0,projectiles=0,damage=0;
    for(int t=0;t<150;t++){var i=Arena.Input(t==0);i.Selected=weapon;i.Use=t%40!=39;i.MouseX=Arena.ViewX+300;i.MouseY=Arena.Floor-35;
     i.Targets=new[]{new TargetState{Id=1,X=Arena.ViewX+233,Y=Arena.Floor-85,Width=75,Height=85,Life=1000,MaxLife=1000,Damage=0}};
     o=b.Step(i);hits+=o.Hits.Length;foreach(var h in o.Hits)damage+=h.Damage;projectiles=Math.Max(projectiles,o.Projectiles.Length);
     if(t==20||t==100){var frame=b.Render(Arena.Render());if(t==20){Require(frame.Effects.Length>0,"weapon "+weapon+" native rendered pixels");b.Export("weapon-"+weapon+".png");}}
    }
    Console.WriteLine("WEAPON "+weapon+" hits="+hits+" damage="+damage+" projectiles="+projectiles+" held="+o.HeldDamage);
    Require(hits>0,"weapon "+weapon+" real native hit detection");totalHits+=hits;
   }
   File.WriteAllText(Path.Combine(args[0],"native-worker.log"),b.Log);
   Console.WriteLine("PASS complete ticks=540 hits="+totalHits+" elapsedMs="+timer.ElapsedMilliseconds);
  }return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}

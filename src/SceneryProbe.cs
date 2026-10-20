using System;
using SpireTerraria;
class SceneryProbe {
 static int Main(string[] args){try{using(var b=new NativeBridge(args[0],args[1],true)){
  foreach(HeroClass c in Enum.GetValues(typeof(HeroClass))){var s=CampaignRules.Starter(c,BattleKind.Normal,1);b.Configure(s);var o=b.Step(Arena.CampaignInput(s,true));for(int n=0;n<3;n++)o=b.Step(Arena.CampaignInput(s,false));var r=Arena.CampaignRender(s,o);b.ArtMode(2);r.HidePlayer=true;b.Render(r);b.Export("preview-"+c+".png");b.ArtMode(1);r.HidePlayer=false;b.Render(r);b.Export("portrait-"+c+".png");b.ArtMode(0);Console.WriteLine("SCENERY "+c+" life="+o.Life+" defense="+o.Defense);}
  Console.WriteLine("ASSETS "+b.ExportAssets());
 }return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

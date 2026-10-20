using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using TMain=Terraria.Main;

static class SpireScenery {
 internal static int ArtMode;
 static readonly MethodInfo drawTile=AccessTools.Method(typeof(Terraria.GameContent.Drawing.TileDrawing),"DrawSingleTile");
 internal static void Prepare(SpireTerraria.BattleSetup s){
  int left=(int)s.Left/16,top=(int)s.Top/16,floor=(int)s.Floor/16,right=left+s.Width/16;
  for(int x=left;x<right;x++)for(int y=top;y<floor+8;y++){
   var old=TMain.tile[x,y];var t=new Tile();t.CopyFrom(old);TMain.tile[x,y]=t;
   if(t.active()&&t.type!=19){t.type=(ushort)(y==floor?2:0);}
  }
  for(int x=left+2;x<right-2;x++){
   if((x*17+s.Seed)%7==0){var t=new Tile();t.active(true);t.type=3;t.frameX=(short)(((x*13)%5)*18);TMain.tile[x,floor-1]=t;}
  }
  for(int x=left;x<right;x++)for(int y=top;y<floor+8;y++)if(TMain.tile[x,y].active()&&TMain.tile[x,y].type!=3)WorldGen.TileFrame(x,y,false,true);
 }
 static Texture2D Texture(string name){return TMain.Assets.Request<Texture2D>(name,AssetRequestMode.ImmediateLoad).Value;}
 internal static bool Draw(){
  if(!SpireRuntime.Enabled||SpireRuntime.Current==null)return true;
  if(ArtMode==1){TMain.graphics.GraphicsDevice.Clear(Color.Transparent);return false;}
  var s=SpireRuntime.Current;var screen=TMain.screenPosition;
  int oldDepth=NativeVisualScale.Depth;NativeVisualScale.Depth=0;
  TMain.graphics.GraphicsDevice.Clear(new Color(24,39,69));
  var b=TMain.spriteBatch;
  b.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,TMain.Rasterizer);
  var sky=Texture("Images/Background_0");b.Draw(sky,new Rectangle(0,0,TMain.screenWidth,TMain.screenHeight),Color.White);
  int floorY=(int)(s.Floor-screen.Y);
  int[] layers={7,9,10,11};float[] scales={1.15f,.85f,.9f,.8f};float[] parallax={.08f,.16f,.24f,.36f};int[] levels={-70,40,65,110};
  for(int i=0;i<layers.Length;i++){
   var bg=Texture("Images/Background_"+layers[i]);int width=(int)(bg.Width*scales[i]),height=(int)(bg.Height*scales[i]);
   int offset=-(int)(screen.X*parallax[i])%width,baseY=floorY-height+levels[i];
   for(int x=offset-width;x<TMain.screenWidth+width;x+=width)b.Draw(bg,new Rectangle(x,baseY,width,height),Color.White);
  }
  b.End();
  NativeMaterials.Begin(false);
  try{
   int x0=Math.Max(1,(int)screen.X/16-3),x1=Math.Min(TMain.maxTilesX-2,(int)(screen.X+TMain.screenWidth)/16+3);
   int y0=Math.Max(1,(int)screen.Y/16-3),y1=Math.Min(TMain.maxTilesY-2,(int)(screen.Y+TMain.screenHeight)/16+3);
   var drawing=AccessTools.Field(typeof(NativeMaterials),"tiles").GetValue(null);
   for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){var t=TMain.tile[x,y];if(t!=null&&t.active())drawTile.Invoke(drawing,new object[]{screen,Vector2.Zero,x,y});}
  }finally{NativeMaterials.End(false);NativeVisualScale.Depth=oldDepth;}
  return ArtMode!=2;
 }
 internal static int ExportAssets(){
  string path=Path.Combine(Terraria.Program.SavePath,"ui-export");Directory.CreateDirectory(path);int count=0;
  int[] ids={989,55,3510,95,743,165,4672,5069,231,151,228,3266,54,53,49,216,285,212,3212,193,935,490,491,489,2998,405,1162,3809,3810,3811,3812,188,189,29,23,1};
  var itemList=ids.ToList();itemList.AddRange(new[]{166,167,168,156,397,211,536,982,1595,555,1158,554,862,485});foreach(var d in SpireTerraria.EquipmentCatalog.All)itemList.AddRange(d.Items);foreach(SpireTerraria.HeroClass hero in Enum.GetValues(typeof(SpireTerraria.HeroClass)))for(int stage=1;stage<=3;stage++){itemList.AddRange(SpireTerraria.CampaignRules.Weapons(hero,stage));itemList.AddRange(SpireTerraria.CampaignRules.Armor(hero,stage));}
  foreach(int id in itemList.Distinct()){var texture=Texture("Images/Item_"+id);using(var f=File.Create(Path.Combine(path,"item-"+id+".png")))texture.SaveAsPng(f,texture.Width,texture.Height);count++;}
  for(int id=0;id<20;id++){var texture=Texture("Images/Background_"+id);using(var f=File.Create(Path.Combine(path,"background-"+id+".png")))texture.SaveAsPng(f,texture.Width,texture.Height);count++;}
  return count;
 }
}

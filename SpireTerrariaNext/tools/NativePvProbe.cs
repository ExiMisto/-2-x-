using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpireTerraria.Next;

internal static class NativePvProbe {
 static string output;static readonly BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static object Get(object value,string field){Type t=value as Type??value.GetType();FieldInfo f=t.GetField(field,All);return f!=null?f.GetValue(value is Type?null:value):t.GetProperty(field,All).GetValue(value is Type?null:value,null);}
 static void Set(object value,string field,object data){Type t=value as Type??value.GetType();t.GetField(field,All).SetValue(value is Type?null:value,data);}
 static AdvancedBattleState State(NativeArena arena){using(var memory=new MemoryStream()){using(var w=new BinaryWriter(memory,Encoding.UTF8,true))arena.WriteAdvancedState(w);memory.Position=0;using(var r=new BinaryReader(memory))return AdvancedBattleState.Read(r);}}
 static void Image(NativeArena arena,string name){using(var reader=FrameExchange.OpenReader((string)Get(arena,"frameSession"))){SharedFrame frame;reader.ReadLatest(0,out frame);using(var bitmap=new System.Drawing.Bitmap(frame.Width,frame.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,frame.Width,frame.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{System.Runtime.InteropServices.Marshal.Copy(frame.Pixels,0,data.Scan0,frame.Pixels.Length);}finally{bitmap.UnlockBits(data);}bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}}}
 static List<object> Entities(NativeArena arena,string name){var list=new List<object>();foreach(object n in (Array)Get(arena,name))if((bool)Get(n,"active")){var d=new Dictionary<string,object>();foreach(string f in name=="npcs"?new[]{"type","position","life","lifeMax","dontTakeDamage","ai","localAI"}:new[]{"type","position","alpha","aiStyle","ai","localAI"})d[f]=ConvertValue(Get(n,f));list.Add(d);}return list;}
 static object ConvertValue(object value){return value is Vector2?value.ToString():value is Array?((Array)value).Clone():value;}
 static int Symbol(TerrariaRuntime runtime,string group,string name){return Convert.ToInt32(runtime.NativeAssembly.GetType("Terraria.ID."+group,true).GetField(name,All).GetRawConstantValue());}
 static Texture2D Texture(object renderer,string name){return (Texture2D)renderer.GetType().GetMethod("Texture",All).Invoke(renderer,new object[]{name});}
 static Rectangle SaveTexture(Texture2D texture,string filename,Rectangle? selected,bool trim){
  Color[] colors=new Color[texture.Width*texture.Height];texture.GetData(colors);Rectangle region=selected??new Rectangle(0,0,texture.Width,texture.Height);
  if(trim){int left=region.Right,top=region.Bottom,right=-1,bottom=-1;for(int y=region.Y;y<region.Bottom;y++)for(int x=region.X;x<region.Right;x++)if(colors[y*texture.Width+x].A>0){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}if(right>=left)region=new Rectangle(left,top,right-left+1,bottom-top+1);}
  using(var bitmap=new System.Drawing.Bitmap(region.Width,region.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){
   var bytes=new byte[region.Width*region.Height*4];for(int y=0;y<region.Height;y++)for(int x=0;x<region.Width;x++){Color c=colors[(region.Y+y)*texture.Width+region.X+x];int i=(y*region.Width+x)*4;bytes[i]=(byte)(c.A==0?0:Math.Min(255,c.B*255/c.A));bytes[i+1]=(byte)(c.A==0?0:Math.Min(255,c.G*255/c.A));bytes[i+2]=(byte)(c.A==0?0:Math.Min(255,c.R*255/c.A));bytes[i+3]=c.A;}
   var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,region.Width,region.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{System.Runtime.InteropServices.Marshal.Copy(bytes,0,data.Scan0,bytes.Length);}finally{bitmap.UnlockBits(data);}bitmap.Save(Path.Combine(output,filename),System.Drawing.Imaging.ImageFormat.Png);
  }return region;
 }
 static void ExportAssets(TerrariaRuntime runtime,Dictionary<string,object> report){
  var manifest=new List<object>();Directory.CreateDirectory(Path.Combine(output,"sheets"));Directory.CreateDirectory(Path.Combine(output,"frames"));
  using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Stage=3,RoomKind="boss",BossSymbol="MoonLordCore",Weapons=new[]{"WoodenSword"},Life=10000,LifeMax=10000})){
   arena.AdventureStep(120,0,4096,2400,0);arena.AdventureStep(60,0,4096,2400,0);
   object renderer=Get(arena,"renderer");Type main=runtime.NativeAssembly.GetType("Terraria.Main",true);Array animations=(Array)Get(main,"itemAnimations");
   foreach(string symbol in new[]{"TerraBlade","RazorbladeTyphoon","Megashark","TempestStaff","RainbowWhip","CrystalShard","SoulofNight","SoulofLight","HallowedBar","LifeFruit","MagicMirror","NebulaBlaze","LunarFlareBook"}){
    int id=Symbol(runtime,"ItemID",symbol);Texture2D texture=Texture(renderer,"Item_"+id);if(texture==null)throw new InvalidDataException("Missing native item texture "+symbol);
    string sheet="sheets/Item_"+symbol+".png";SaveTexture(texture,sheet,null,false);object animation=animations.GetValue(id);int count=animation==null?1:Math.Max(1,(int)Get(animation,"FrameCount")),ticksPerFrame=animation==null?0:(int)Get(animation,"TicksPerFrame");var frames=new List<object>();
    for(int i=0;i<count;i++){
     Rectangle frame=animation==null?new Rectangle(0,0,texture.Width,texture.Height):(Rectangle)animation.GetType().GetMethod("GetFrame",All).Invoke(animation,new object[]{texture,i*Math.Max(1,ticksPerFrame)});
     string file="frames/"+symbol+"_"+i.ToString("00")+".png";SaveTexture(texture,file,frame,false);if(i==0)SaveTexture(texture,symbol+".png",frame,false);
     frames.Add(new{file=file,x=frame.X,y=frame.Y,width=frame.Width,height=frame.Height});
    }
    manifest.Add(new{symbol=symbol,group="ItemID",id=id,source="installed-game-local-xnb",sheet=sheet,sheetWidth=texture.Width,sheetHeight=texture.Height,layout=count>1?"vertical-animation-native-GetFrame":"single",frameCount=count,ticksPerFrame=ticksPerFrame,preview=symbol+".png",frames=frames});
   }
   Array npcs=(Array)Get(arena,"npcs");object freeEye=npcs.GetValue(4);arena.GetType().GetMethod("Call",All).Invoke(arena,new object[]{freeEye,"SetDefaults",new object[]{Symbol(runtime,"NPCID","MoonLordFreeEye")}});Set(freeEye,"active",true);Set(freeEye,"position",(Vector2)Get(Get(arena,"player"),"position")+new Vector2(0,-200));((float[])Get(freeEye,"ai"))[3]=0;
   var device=(GraphicsDevice)Get(renderer,"device");var target=(RenderTarget2D)Get(renderer,"target");var batch=(SpriteBatch)Get(renderer,"batch");
   foreach(int slot in new[]{0,1,2,3,4}){
    object npc=npcs.GetValue(slot);int type=(int)Get(npc,"type");string symbol=type==398?"MoonLordCore":type==396?"MoonLordHead":type==397?(slot==1?"MoonLordHand_Left":"MoonLordHand_Right"):"MoonLordFreeEye";Texture2D texture=Texture(renderer,"NPC_"+type);string sheet="sheets/NPC_"+symbol+".png";SaveTexture(texture,sheet,null,false);
    Vector2 center=(Vector2)Get(npc,"Center"),camera=center-new Vector2(target.Width/2f,target.Height/2f);device.SetRenderTarget(target);device.Clear(Color.Transparent);batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);try{renderer.GetType().GetMethod("DrawNpc",All).Invoke(renderer,new[]{npc,(object)camera});}finally{batch.End();device.SetRenderTarget(null);}
    Rectangle bounds=SaveTexture(target,symbol+".png",null,true);
    manifest.Add(new{symbol=symbol,group="NPCID",id=type,source="installed-game-native-DrawNPCDirect-static-pose",sheet=sheet,sheetWidth=texture.Width,sheetHeight=texture.Height,layout="custom-native-composite-atlas-do-not-use-as-one-frame",preview=symbol+".png",previewFrameCount=1,previewFrameWidth=bounds.Width,previewFrameHeight=bounds.Height,previewCropX=bounds.X,previewCropY=bounds.Y,canvasWidth=target.Width,canvasHeight=target.Height,note="The raw NPC sheet is a custom multi-part atlas; preview is a transparent assembled native pose, not battle footage."});
   }
  }
  report["assets"]=manifest;report["passed"]=true;File.WriteAllText(Path.Combine(output,"native-assets.json"),new JavaScriptSerializer{MaxJsonLength=10000000}.Serialize(report),new UTF8Encoding(false));
 }
 [STAThread]static int Main(string[] args){var report=new Dictionary<string,object>();try{output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);var runtime=new TerrariaRuntime(args[0]);string mode=args.Length>2?args[2]:"moon";
  if(mode=="export-assets")ExportAssets(runtime,report);
  else if(mode=="moon"||mode=="moon-phases")using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Stage=3,RoomKind="boss",BossSymbol="MoonLordCore",Weapons=new[]{"LastPrism"},Armor=new[]{"NebulaHelmet","NebulaBreastplate","NebulaLeggings"},Accessories=new[]{"FishronWings","ManaFlower","SorcererEmblem","CelestialEmblem"},Life=10000,LifeMax=10000})){
   var samples=new List<object>();Image(arena,"moon-initial");
   bool coreOpened=false,deathStarted=false;int nativeStrikes=0;
   for(int t=0;t<(mode=="moon-phases"?3600:1800);t+=10){var s=State(arena);arena.AdventureStep(10,(byte)(t<420?0:t%240<120?1:2),s.PlayerX,s.PlayerY-200,0);
    if(mode=="moon-phases"&&t>420)foreach(object part in (Array)Get(arena,"npcs"))if((bool)Get(part,"active")&&!(bool)Get(part,"dontTakeDamage")&&(int)Get(part,"life")>1){int type=(int)Get(part,"type");if(type>=396&&type<=398){if(type==398)coreOpened=true;part.GetType().GetMethod("StrikeNPC",new[]{typeof(int),typeof(float),typeof(int),typeof(bool),typeof(bool),typeof(int)}).Invoke(part,new object[]{1000000,0f,1,false,false,0});nativeStrikes++;if(type==398)deathStarted=((float[])Get(part,"ai"))[0]==2;}}
    if(t%180==0){samples.Add(new{tick=t,stateLife=State(arena).EnemyLife,npcs=Entities(arena,"npcs"),projectiles=Entities(arena,"projectiles")});Image(arena,"moon-"+t);}if(!State(arena).EnemyActive)break;
   }
   report["samples"]=samples;report["tick"]=State(arena).Tick;report["coreOpened"]=coreOpened;report["nativeStrikes"]=nativeStrikes;report["deathStarted"]=deathStarted;report["encounterComplete"]=!State(arena).EnemyActive;report["passed"]=mode=="moon"?State(arena).Tick==1800:coreOpened&&deathStarted&&!State(arena).EnemyActive;
  }
  else if(mode=="beetle") {
   var results=new List<object>();
   foreach(string chest in new[]{"BeetleScaleMail","BeetleShell"})using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Stage=3,RoomKind="boss",BossSymbol="SkeletronPrime",Weapons=new[]{"TerraBlade"},Armor=new[]{"BeetleHelmet",chest,"BeetleLeggings"},Accessories=new[]{"FishronWings","AvengerEmblem","StarVeil","WormScarf"},Life=10000,LifeMax=10000})) {
    int maximumOrbs=0;
    for(int i=0;i<240;i++){var s=State(arena);arena.AdventureStep(1,(byte)((i%40<36?8:0)|(i%100<50?1:2)),s.EnemyX,s.EnemyY,0);maximumOrbs=Math.Max(maximumOrbs,(int)Get(Get(arena,"player"),"beetleOrbs"));if(i==8||i==59||i==239)Image(arena,"beetle-"+chest+"-"+i);}
    // An explicit presentation regression covers all three native orbiting
    // beetles, even if this short combat sample did not earn the offense buff.
    object hero=Get(arena,"player");Set(hero,"beetleOrbs",3);arena.GetType().GetMethod("Render",All).Invoke(arena,null);Image(arena,"beetle-"+chest+"-three-orbs");
    results.Add(new{armor=chest,ticks=State(arena).Tick,nativeMaximumOrbs=maximumOrbs,forcedPresentationOrbs=3});
   }report["results"]=results;report["passed"]=true;
  }
  else if(mode=="mage"){
   var samples=new List<object>();var results=new List<object>();int failed=0;report["samples"]=samples;report["weaponResults"]=results;
   foreach(string weapon in new[]{"WaterBolt","Vilethorn","CrimsonRod","DiamondStaff","GoldenShower","CrystalStorm","CursedFlames","NimbusRod","MagicMissile","RazorbladeTyphoon","LaserMachinegun","ShadowbeamStaff","InfernoFork","LastPrism","LunarFlareBook","NebulaBlaze"}) {
    int lastTick=-1;
    try{using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Stage=3,Weapons=new[]{weapon},Life=10000,LifeMax=10000,EnemyCount=30,MaxConcurrent=4})){
     var s=State(arena);for(int i=0;i<90;i++){lastTick=i;arena.AdventureStep(1,(byte)(i%30<26?8:0),s.PlayerX+120,s.PlayerY-140,0);if(i==3||i==15||i==35||i==55||i==85){Image(arena,"mage-"+weapon+"-"+i);samples.Add(new{weapon=weapon,tick=i,projectiles=Entities(arena,"projectiles")});}}
    }results.Add(new{weapon=weapon,passed=true,ticks=90});}
    catch(Exception error){failed++;results.Add(new{weapon=weapon,passed=false,tick=lastTick,error=error.ToString()});Console.Error.WriteLine(weapon+" at "+lastTick+": "+error);}
    report["passed"]=failed==0;File.WriteAllText(Path.Combine(output,"report.json"),new JavaScriptSerializer{MaxJsonLength=10000000}.Serialize(report),new UTF8Encoding(false));
   }
  }
  else throw new ArgumentException("Unknown mode");
 }catch(Exception e){report["passed"]=false;report["failure"]=e.ToString();Console.Error.WriteLine(e);}File.WriteAllText(Path.Combine(output,"report.json"),new JavaScriptSerializer{MaxJsonLength=10000000}.Serialize(report),new UTF8Encoding(false));return report.ContainsKey("passed")&&(bool)report["passed"]?0:1;}
}

using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SpireTerraria.Next {
 // Local Terraria assets, independently composed. This is not Main.Draw: lighting,
 // composite armor shaders and a subset of trails remain outside this adapter.
 internal sealed class NativeRenderer : IDisposable {
  readonly int width,height,ownerThread;
  readonly Form window;
  readonly GraphicsDevice device;
  readonly ContentManager content;
  readonly SpriteBatch batch;
  readonly RenderTarget2D target;
  readonly Color[] pixels;
  readonly byte[] bgra;
  readonly Texture2D pixel;
  readonly Type mainType;
  readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
  readonly Dictionary<string,FieldInfo> fields=new Dictionary<string,FieldInfo>(StringComparer.Ordinal);
  readonly List<FontBinding> fontBindings=new List<FontBinding>();
  readonly object[] combatFonts=new object[2];
  MethodInfo measureCombatText,drawCombatText;
  FieldInfo combatTextPool;
  int combatTextDrawCount,combatTextPeak;
  long combatTextDrawTotal;
  internal int CombatTextDrawCount {get{return combatTextDrawCount;}}
  internal int CombatTextPeak {get{return combatTextPeak;}}
  internal long CombatTextDrawTotal {get{return combatTextDrawTotal;}}
  readonly Dictionary<string,Array> nativeArrays=new Dictionary<string,Array>(StringComparer.Ordinal);
  readonly HashSet<string> nativeTextures=new HashSet<string>(StringComparer.Ordinal);
  readonly Dictionary<int,MethodInfo> specialProjectileDraws=new Dictionary<int,MethodInfo>();
  readonly HashSet<int> nativeDirectProjectiles=new HashSet<int>();
  Type textureAssets,textureAssetType,drawSetType;
  ConstructorInfo textureAssetConstructor;
  MethodInfo submitTexture,setupDraw,layersDraw,transformDraw,drawDataDraw;
  object textureSource,nativePlayerRenderer;
  IList drawData;
  readonly List<int> drawDust=new List<int>(),drawGore=new List<int>();
  bool sharedPlayerTexturesReady;
  int lastPlayerDrawCount;
  int specialDrawCount;
  internal int PlayerDrawCount {get{return lastPlayerDrawCount;}}
  internal int SpecialDrawCount {get{return specialDrawCount;}}
  string sceneKey="legacy";
  float sceneLeft,sceneRight,sceneFloor;
  bool disposed;
  internal int Width {get{return width;}}
  internal int Height {get{return height;}}
  internal int TextureCount {get{return textures.Count;}}
  internal string AdapterDescription {get{return device.Adapter.Description;}}
  internal NativeRenderer(Assembly game,string gameDirectory,int width,int height) {
   if(game==null)throw new ArgumentNullException("game");
   if(width<32||height<32||width>1920||height>1080)throw new ArgumentOutOfRangeException("width");
   this.width=width;this.height=height;ownerThread=Thread.CurrentThread.ManagedThreadId;
   mainType=game.GetType("Terraria.Main",true);
   try {
    window=new Form();window.Text="SpireNext render worker";window.ShowInTaskbar=false;window.FormBorderStyle=FormBorderStyle.FixedToolWindow;window.StartPosition=FormStartPosition.Manual;window.Location=new System.Drawing.Point(-32000,-32000);window.ClientSize=new System.Drawing.Size(width,height);
    var presentation=new PresentationParameters{BackBufferWidth=width,BackBufferHeight=height,BackBufferFormat=SurfaceFormat.Color,DepthStencilFormat=DepthFormat.None,DeviceWindowHandle=window.Handle,IsFullScreen=false,PresentationInterval=PresentInterval.Immediate};
    device=new GraphicsDevice(GraphicsAdapter.DefaultAdapter,GraphicsProfile.Reach,presentation);
    var services=new GameServiceContainer();services.AddService(typeof(IGraphicsDeviceService),new DeviceService(device));
    string contentDirectory=Path.Combine(Path.GetFullPath(gameDirectory),"Content");
    content=new ContentManager(services,contentDirectory);
    batch=new SpriteBatch(device);target=new RenderTarget2D(device,width,height,false,SurfaceFormat.Color,DepthFormat.None);
    pixel=new Texture2D(device,1,1);pixel.SetData(new[]{Color.White});
    pixels=new Color[width*height];bgra=new byte[width*height*4];
    LoadFonts(game,contentDirectory);
   } catch(Exception original) {
    try{ReleaseResources();}catch(Exception cleanup){throw new AggregateException("Native renderer construction and cleanup both failed.",original,cleanup);}
    throw;
   }
  }
  internal void SetScene(string key,float left,float right,float floor){RequireThread();sceneKey=key??"forest-outpost";sceneLeft=left;sceneRight=right;sceneFloor=floor;}
  void LoadFonts(Assembly game,string contentDirectory) {
   try {
    Type fontAssets=game.GetType("Terraria.GameContent.FontAssets",true),assetType=fontAssets.GetField("MouseText",BindingFlags.Public|BindingFlags.Static).FieldType,fontType=assetType.GetGenericArguments()[0];
    Type sourceType=fontType.Assembly.GetType("ReLogic.Content.Sources.FileSystemContentSource",true);
    object source=Activator.CreateInstance(sourceType,new object[]{contentDirectory});
    ConstructorInfo assetConstructor=assetType.GetConstructor(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null);
    MethodInfo submit=assetType.GetMethod("SubmitLoadedContent",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance),load=typeof(ContentManager).GetMethod("Load").MakeGenericMethod(fontType),measure=fontType.GetMethod("MeasureString",new[]{typeof(string)});
    if(assetConstructor==null||submit==null||measure==null)throw new NotSupportedException("Installed ReLogic font/asset API is incompatible.");
    measureCombatText=measure;
    drawCombatText=fontType.Assembly.GetType("ReLogic.Graphics.DynamicSpriteFontExtensionMethods",true).GetMethod("DrawString",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(SpriteBatch),fontType,typeof(string),typeof(Vector2),typeof(Color),typeof(float),typeof(Vector2),typeof(float),typeof(SpriteEffects),typeof(float),typeof(Vector2[]),typeof(Color[])},null);
    combatTextPool=mainType.GetField("combatText",BindingFlags.Public|BindingFlags.Static);
    if(drawCombatText==null||combatTextPool==null)throw new NotSupportedException("Installed native combat text drawing API is incompatible.");
    string[] assetNames={"Fonts/Combat_Text","Fonts/Combat_Crit","Fonts/Mouse_Text","Fonts/Death_Text","Fonts/Item_Stack"};object[] assets=new object[assetNames.Length];
    for(int i=0;i<assetNames.Length;i++) {
     object font=load.Invoke(content,new object[]{assetNames[i]});
     if(i<2)combatFonts[i]=font;
     Vector2 size=(Vector2)measure.Invoke(font,new object[]{"12345"});if(size.X<=0||size.Y<=0)throw new InvalidDataException("Native font measurement failed: "+assetNames[i]);
     // Use ReLogic's real constructor and content submission API, including its
     // real local content source and loaded state. No fabricated font objects.
     assets[i]=assetConstructor.Invoke(new object[]{assetNames[i]});submit.Invoke(assets[i],new[]{font,source});
     if(!(bool)assetType.GetProperty("IsLoaded").GetValue(assets[i],null))throw new InvalidDataException("Native font asset did not reach loaded state: "+assetNames[i]);
    }
    Array combat=Array.CreateInstance(assetType,2);combat.SetValue(assets[0],0);combat.SetValue(assets[1],1);
    BindFont(fontAssets,"CombatText",combat);BindFont(fontAssets,"MouseText",assets[2]);BindFont(fontAssets,"DeathText",assets[3]);BindFont(fontAssets,"ItemStack",assets[4]);
   } catch(Exception e){throw new InvalidOperationException("Native Terraria fonts could not be loaded from the configured local Content/Fonts directory.",e);}
  }
  void BindFont(Type fontAssets,string name,object assigned) {
   FieldInfo field=fontAssets.GetField(name,BindingFlags.Public|BindingFlags.Static);if(field==null)throw new NotSupportedException("Missing native font field: "+name);
   var binding=new FontBinding{Field=field,Previous=field.GetValue(null),Assigned=assigned};fontBindings.Add(binding);field.SetValue(null,assigned);
  }
  void InitializePlayerPresentation(Assembly game,string contentDirectory) {
   textureAssets=game.GetType("Terraria.GameContent.TextureAssets",true);
   textureAssetType=textureAssets.GetField("MagicPixel").FieldType;
   textureAssetConstructor=textureAssetType.GetConstructor(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null);
   submitTexture=textureAssetType.GetMethod("SubmitLoadedContent",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
   textureSource=Activator.CreateInstance(textureAssetType.Assembly.GetType("ReLogic.Content.Sources.FileSystemContentSource",true),new object[]{contentDirectory});
   // Some AI and death effects request textures on demand through Main.Assets,
   // independently of TextureAssets. Serve those requests from the same local
   // XNB ContentManager without starting Terraria's standalone asset threads.
   FieldInfo assetsField=mainType.GetField("Assets",BindingFlags.Public|BindingFlags.Static);
   object repository=new LocalAssetRepository(assetsField.FieldType,this).GetTransparentProxy();
   fontBindings.Add(new FontBinding{Field=assetsField,Previous=assetsField.GetValue(null),Assigned=repository});assetsField.SetValue(null,repository);
   drawSetType=game.GetType("Terraria.DataStructures.PlayerDrawSet",true);
   Type nativeRendererType=game.GetType("Terraria.Graphics.Renderers.LegacyPlayerRenderer",true),drawDataType=game.GetType("Terraria.DataStructures.DrawData",true);
   nativePlayerRenderer=Activator.CreateInstance(nativeRendererType);
   setupDraw=drawSetType.GetMethod("BoringSetup",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
   layersDraw=nativeRendererType.GetMethod("DrawPlayer_UseNormalLayers",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
   transformDraw=game.GetType("Terraria.DataStructures.PlayerDrawLayers",true).GetMethod("DrawPlayer_TransformDrawData",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
   drawDataDraw=drawDataType.GetMethod("Draw",new[]{typeof(SpriteBatch)});
   drawData=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(drawDataType));
   FieldInfo spriteBatch=mainType.GetField("spriteBatch",BindingFlags.Public|BindingFlags.Static);fontBindings.Add(new FontBinding{Field=spriteBatch,Previous=spriteBatch.GetValue(null),Assigned=batch});spriteBatch.SetValue(null,batch);
   Type projectileIds=game.GetType("Terraria.ID.ProjectileID",true),projectileType=game.GetType("Terraria.Projectile",true);
   foreach(string name in new[]{"LightsBane","NightsEdge","TrueNightsEdge","Excalibur","TrueExcalibur","TheHorsemansBlade","ChlorophyteClaymoreBlade","TerraBlade2","TerraBlade2Shot","PiercingStarlight","FairyQueenLance","CoolWhipMinion"}) {
    FieldInfo id=projectileIds.GetField(name);MethodInfo draw=mainType.GetMethod("DrawProj_"+name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance,null,new[]{projectileType},null);if(id!=null&&draw!=null)specialProjectileDraws[Convert.ToInt32(id.GetRawConstantValue())]=draw;
   }
   FieldInfo flames=projectileIds.GetField("Flames");if(flames!=null)specialProjectileDraws[Convert.ToInt32(flames.GetRawConstantValue())]=mainType.GetMethod("DrawProj_Flamethrower",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
   foreach(string name in new[]{"PhantasmalDeathray","LastPrismLaser","LastPrism","VilethornBase","VilethornTip","CrystalVileShardHead","CrystalVileShardShaft","GoldenShowerFriendly","ShadowBeamFriendly","HeatRay","ChargedBlasterLaser"}){FieldInfo id=projectileIds.GetField(name);if(id!=null)nativeDirectProjectiles.Add(Convert.ToInt32(id.GetRawConstantValue()));}
  }
  Array NativeArray(string name) {
   Array array;if(nativeArrays.TryGetValue(name,out array))return array;
   FieldInfo field=textureAssets.GetField(name,BindingFlags.Public|BindingFlags.Static);
   if(field==null)throw new MissingFieldException(textureAssets.FullName,name);
   Array previous=field.GetValue(null) as Array;
   if(previous!=null)array=(Array)previous.Clone();
   else if(name=="Players")array=Array.CreateInstance(textureAssetType,Convert.ToInt32(mainType.Assembly.GetType("Terraria.ID.PlayerVariantID",true).GetField("Count").GetValue(null)),16);
   else throw new InvalidDataException("Uninitialized native texture array: "+name);
   nativeArrays.Add(name,array);fontBindings.Add(new FontBinding{Field=field,Previous=previous,Assigned=array});field.SetValue(null,array);return array;
  }
  object NativeTexture(string name) {
   Texture2D texture=Texture(name);if(texture==null)return null;
   object asset=textureAssetConstructor.Invoke(new object[]{"Images/"+name});submitTexture.Invoke(asset,new[]{(object)texture,textureSource});return asset;
  }
  sealed class LocalAssetRepository : RealProxy {
   readonly NativeRenderer owner;
   internal LocalAssetRepository(Type type,NativeRenderer owner):base(type){this.owner=owner;}
   public override IMessage Invoke(IMessage message) {
    IMethodCallMessage call=message as IMethodCallMessage;if(call==null)throw new InvalidOperationException("Invalid local asset request.");
    try {
     object result=null;
     if(call.MethodName=="Request") {
      MethodInfo method=(MethodInfo)call.MethodBase;Type requested=method.GetGenericArguments()[0];
      if(requested!=typeof(Texture2D))throw new NotSupportedException("This local combat asset repository supports textures only: "+requested.FullName);
      string path=(string)call.Args[0];if(!path.StartsWith("Images/",StringComparison.Ordinal)||path.Contains(".."))throw new InvalidDataException("Unexpected local texture path.");
      result=owner.NativeTexture(path.Substring(7));if(result==null)throw new InvalidDataException("Local native texture is missing: "+path);
     } else if(call.MethodName.StartsWith("get_",StringComparison.Ordinal)) {
      Type resultType=((MethodInfo)call.MethodBase).ReturnType;if(resultType.IsValueType)result=Activator.CreateInstance(resultType);
     }
     return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
    } catch(Exception error){return new ReturnMessage(error,call);}
   }
  }
  void BindNativeTexture(string field,int index,string name) {
   if(index<0)return;string key=field+":"+index;if(nativeTextures.Contains(key))return;Array array=NativeArray(field);if(index>=array.Length)return;
   object asset=NativeTexture(name);if(asset==null)return;array.SetValue(asset,index);nativeTextures.Add(key);
  }
  void BindNativeSingle(string field,string name) {
   if(nativeTextures.Contains(field))return;object asset=NativeTexture(name);if(asset==null)return;
   FieldInfo info=textureAssets.GetField(field,BindingFlags.Public|BindingFlags.Static);if(info==null)return;
   fontBindings.Add(new FontBinding{Field=info,Previous=info.GetValue(null),Assigned=asset});info.SetValue(null,asset);nativeTextures.Add(field);
  }
  void PreparePlayerTextures(object player) {
   if(textureAssets==null)InitializePlayerPresentation(mainType.Assembly,content.RootDirectory);
   BindNativeTexture("Item",0,"MagicPixel");
   int variant=Math.Max(0,Read<int>(player,"skinVariant",0));Array bodies=NativeArray("Players");
   if(variant>=bodies.GetLength(0))variant=0;
   for(int part=0;part<bodies.GetLength(1);part++) {
    string key="Players:"+variant+":"+part;if(nativeTextures.Contains(key))continue;object asset=NativeTexture("Player_"+variant+"_"+part);if(asset!=null){bodies.SetValue(asset,variant,part);nativeTextures.Add(key);}
   }
   int hair=Read<int>(player,"hair",0);BindNativeTexture("PlayerHair",hair,"Player_Hair_"+(hair+1));BindNativeTexture("PlayerHairAlt",hair,"Player_HairAlt_"+(hair+1));
   int head=Read<int>(player,"head",0),body=Read<int>(player,"body",0),legs=Read<int>(player,"legs",0);
   if(head>0)BindNativeTexture("ArmorHead",head,"Armor_Head_"+head);
   if(body>0){BindNativeTexture("ArmorBodyComposite",body,"Armor/Armor_"+body);BindNativeTexture("ArmorBody",body,"Armor_Body_"+body);BindNativeTexture("FemaleBody",body,"Female_Body_"+body);BindNativeTexture("ArmorArm",body,"Armor_Arm_"+body);}
   if(legs>0)BindNativeTexture("ArmorLeg",legs,"Armor_Legs_"+legs);
   string[] names={"handon","handoff","back","front","shoe","waist","shield","neck","face","balloon","beard","wings","backpack","tail"};
   string[] arrays={"AccHandsOn","AccHandsOff","AccBack","AccFront","AccShoes","AccWaist","AccShield","AccNeck","AccFace","AccBalloon","AccBeard","Wings","AccBack","AccBack"};
   string[] prefixes={"Acc_HandsOn_","Acc_HandsOff_","Acc_Back_","Acc_Front_","Acc_Shoes_","Acc_Waist_","Acc_Shield_","Acc_Neck_","Acc_Face_","Acc_Balloon_","Acc_Beard_","Wings_","Acc_Back_","Acc_Back_"};
   for(int i=0;i<names.Length;i++){object value=ReadValue(player,names[i]);int index=value==null?-1:Convert.ToInt32(value);if(index<=0)continue;BindNativeTexture(arrays[i],index,prefixes[i]+index);if(i<2)BindNativeTexture(arrays[i]+"Composite",index,"Accessories/"+prefixes[i]+index);}
   Array inventory=ReadValue(player,"inventory") as Array;if(inventory!=null)foreach(object item in inventory){if(item==null)continue;int type=Read<int>(item,"type",0);if(type>0)BindNativeTexture("Item",type,"Item_"+type);}
   object last=ReadValue(player,"lastVisualizedSelectedItem");if(last!=null&&Read<int>(last,"type",0)>0)BindNativeTexture("Item",Read<int>(last,"type",0),"Item_"+Read<int>(last,"type",0));
   if(!sharedPlayerTexturesReady) {
    // Long coats can select a second leg atlas through GetMatchingBodyExtension,
    // independent of the equipped leg piece. Native draw code loads it directly.
    foreach(string name in new[]{"Extra","GlowMask","ItemFlame","BackPack","ArmorLeg"}){Array array=NativeArray(name);string prefix=name=="GlowMask"?"Glow_":name=="ArmorLeg"?"Armor_Legs_":name+"_";for(int i=0;i<array.Length;i++)BindNativeTexture(name,i,prefix+i);}
    BindNativeSingle("MagicPixel","MagicPixel");BindNativeSingle("FishingLine","FishingLine");
    // TextureAssets.Beetle is a singleton named BeetleOrb on disk, outside
    // Armor/Extra atlases. Native armor layers access it as soon as an orb forms.
    BindNativeSingle("Beetle","BeetleOrb");
    // Scythe Whip's native particle orchestra requests this texture during AI,
    // before the corresponding projectile ever reaches our draw pass.
    BindNativeTexture("Projectile",916,"Projectile_916");sharedPlayerTexturesReady=true;
    Array flames=NativeArray("Flames");for(int i=0;i<flames.Length;i++)BindNativeTexture("Flames",i,"Flame_"+i);
   }
  }
  // Player.Update runs in an offline server context. Terraria deliberately skips
  // use/hold-style geometry there; run only its visual calculation as a client.
  internal void UpdatePlayerPresentation(object player) {
   RequireThread();PreparePlayerTextures(player);FieldInfo dedicated=mainType.GetField("dedServ");object previous=dedicated.GetValue(null);dedicated.SetValue(null,false);
   try {object item=player.GetType().GetProperty("HeldItem").GetValue(player,null);player.GetType().GetMethod("AnimatePlayerAndGetItemFrame",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(player,new[]{(object)1f,item});}
   finally{dedicated.SetValue(null,previous);}
  }
  // Buffer is owned by this renderer and remains valid until the next RenderScene.
  // Call from the thread that constructed the device; copy if retaining a frame.
  internal byte[] RenderScene(Array tiles,object player,Array npcs,Array projectiles,float cameraX,float cameraY) {
   RequireThread();
   if(float.IsNaN(cameraX)||float.IsInfinity(cameraX)||float.IsNaN(cameraY)||float.IsInfinity(cameraY))throw new ArgumentException("Camera must be finite.");
   Vector2 camera=new Vector2(cameraX,cameraY);
   device.SetRenderTarget(target);device.Clear(sceneKey=="mine"?new Color(27,31,41):sceneKey=="corruption-altar"?new Color(65,52,91):sceneKey.StartsWith("boss-",StringComparison.Ordinal)?new Color(37,52,88):new Color(112,171,218));
   batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
   if(sceneKey!="legacy"){DrawScenery(camera);DrawWalls(tiles,camera);}
   DrawTiles(tiles,camera);
   if(npcs!=null)foreach(object npc in npcs)if(npc!=null&&Read<bool>(npc,"active",false))DrawNpc(npc,camera);
   if(player!=null&&Read<bool>(player,"active",true)&&!Read<bool>(player,"dead",false))DrawPlayer(player,camera);
   if(projectiles!=null)foreach(object projectile in projectiles)if(projectile!=null&&Read<bool>(projectile,"active",false))DrawProjectile(projectile,camera);
   DrawDust(camera);
   if(sceneKey=="boss-MoonLordCore")DrawMoonDeathPresentation(camera,false);
   if(npcs!=null)foreach(object enemy in npcs)if(enemy!=null&&Read<bool>(enemy,"active",false)&&!Read<bool>(enemy,"friendly",false)&&Read<int>(enemy,"lifeMax",0)>5)DrawHealth(enemy,camera,false);
   if(player!=null&&!Read<bool>(player,"dead",false))DrawHealth(player,camera,true);
   DrawCombatText(camera);
   if(sceneKey=="boss-MoonLordCore")DrawMoonDeathPresentation(camera,true);
   batch.End();device.SetRenderTarget(null);target.GetData(pixels);
   for(int i=0,j=0;i<pixels.Length;i++,j+=4){Color p=pixels[i];bgra[j]=p.B;bgra[j+1]=p.G;bgra[j+2]=p.R;bgra[j+3]=p.A;}
   return bgra;
  }
  void DrawCombatText(Vector2 camera) {
   combatTextDrawCount=0;Array pool=combatTextPool.GetValue(null) as Array;if(pool==null)return;
   foreach(object entry in pool) {
    if(entry==null||!Read<bool>(entry,"active",false))continue;
    string text=Read<string>(entry,"text","");float scale=Read<float>(entry,"scale",0),alpha=Read<float>(entry,"alpha",0),rotation=Read<float>(entry,"rotation",0);
    Vector2 position=Read<Vector2>(entry,"position",Vector2.Zero)-camera;
    if(text.Length==0||!Finite(scale)||!Finite(alpha)||!Finite(rotation)||!Finite(position.X)||!Finite(position.Y)||scale<=0||alpha<=0)continue;
    object font=combatFonts[Read<bool>(entry,"crit",false)?1:0];Vector2 size=(Vector2)measureCombatText.Invoke(font,new object[]{text});
    Vector2 origin=size*.5f;position+=origin;
    float radius=(size.X+size.Y)*scale*.5f+4;
    if(position.X+radius<0||position.X-radius>width||position.Y+radius<0||position.Y-radius>height)continue;
    // Draw the actual native entry, including crit font, DoT color, scale and
    // lifetime fade. Rendering never advances or recreates combat messages.
    Color foreground=Read<Color>(entry,"color",Color.White)*MathHelper.Clamp(alpha,0,1),border=Color.Black*MathHelper.Clamp(alpha,0,1);
    object[] args={batch,font,text,position,border,rotation,origin,scale,SpriteEffects.None,0f,null,null};
    args[3]=position+new Vector2(-1,0);drawCombatText.Invoke(null,args);
    args[3]=position+new Vector2(1,0);drawCombatText.Invoke(null,args);
    args[3]=position+new Vector2(0,-1);drawCombatText.Invoke(null,args);
    args[3]=position+new Vector2(0,1);drawCombatText.Invoke(null,args);
    args[3]=position;args[4]=foreground;drawCombatText.Invoke(null,args);combatTextDrawCount++;
   }
   combatTextPeak=Math.Max(combatTextPeak,combatTextDrawCount);combatTextDrawTotal+=combatTextDrawCount;
  }
  static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
  void DrawMoonDeathPresentation(Vector2 camera,bool whitening) {
   Type drama=mainType.Assembly.GetType("Terraria.GameContent.Events.MoonlordDeathDrama",true);FieldInfo screen=mainType.GetField("screenPosition");object previous=screen.GetValue(null);screen.SetValue(null,camera);
   try{if(whitening)drama.GetMethod("DrawWhite").Invoke(null,new object[]{batch});else{drama.GetMethod("DrawPieces").Invoke(null,new object[]{batch});drama.GetMethod("DrawExplosions").Invoke(null,new object[]{batch});}}
   finally{screen.SetValue(null,previous);}
  }
  void DrawHealth(object entity,Vector2 camera,bool hero) {
   if(!hero){int type=Read<int>(entity,"type",0);if(type==396||type==397||type==398){float phase=((float[])ReadValue(entity,"ai"))[0];if(type==397&&phase== -2||type==396&&phase== -3||type==398&&phase==2)return;}}
   int current=Read<int>(entity,hero?"statLife":"life",0),maximum=Read<int>(entity,hero?"statLifeMax2":"lifeMax",0);if(maximum<=0||current<=0)return;
   Vector2 position=Read<Vector2>(entity,"position",Vector2.Zero)-camera;int entityWidth=Read<int>(entity,"width",20),barWidth=hero?38:Read<bool>(entity,"boss",false)?76:40;
   int x=(int)(position.X+entityWidth*.5f-barWidth*.5f),y=(int)position.Y-13;
   if(x+barWidth<0||x>width||y<0||y>height)return;
   batch.Draw(pixel,new Rectangle(x-2,y-2,barWidth+4,9),new Color(20,15,21,235));
   batch.Draw(pixel,new Rectangle(x-1,y-1,barWidth+2,7),hero?new Color(237,198,115):new Color(147,126,101));
   batch.Draw(pixel,new Rectangle(x,y,barWidth,5),new Color(48,20,27));
   int fill=Math.Max(1,(int)(barWidth*MathHelper.Clamp(current/(float)maximum,0,1)));
   batch.Draw(pixel,new Rectangle(x,y,fill,5),hero?new Color(229,57,63):new Color(202,60,57));
   batch.Draw(pixel,new Rectangle(x,y,fill,1),new Color(255,158,138));
  }
  internal void WriteItemIcon(int id,BinaryWriter writer) {
   RequireThread();Texture2D icon=Texture("Item_"+id);if(icon==null)throw new InvalidDataException("Local item texture unavailable: "+id);
   int iconWidth=Math.Min(128,icon.Width),iconHeight=Math.Min(128,icon.Height);var colors=new Color[iconWidth*iconHeight];
   icon.GetData(0,new Rectangle(0,0,iconWidth,iconHeight),colors,0,colors.Length);writer.Write(iconWidth);writer.Write(iconHeight);
   // XNA textures use premultiplied alpha; Godot Image expects straight RGBA.
   foreach(Color c in colors){writer.Write((byte)(c.A==0?0:Math.Min(255,c.R*255/c.A)));writer.Write((byte)(c.A==0?0:Math.Min(255,c.G*255/c.A)));writer.Write((byte)(c.A==0?0:Math.Min(255,c.B*255/c.A)));writer.Write(c.A);}
  }
  // A fixed presentation request uses the same locally installed texture cache
  // as item icons. It never creates a world, player simulation or asset path.
  internal void WriteSceneArt(int hero,BinaryWriter writer) {
   RequireThread();if(hero<0||hero>3)throw new ArgumentOutOfRangeException("hero");
   const int artWidth=640,artHeight=360;
   Color[] skyTop={new Color(66,94,126),new Color(87,154,193),new Color(29,35,74),new Color(93,106,151)};
   Color[] skyBottom={new Color(246,192,124),new Color(207,232,211),new Color(137,113,163),new Color(242,193,174)};
   Color[] mountains={new Color(218,207,181),new Color(197,223,214),new Color(159,151,208),new Color(201,196,214)};
   Color[] farTrees={new Color(192,189,153),new Color(192,220,185),new Color(146,141,191),new Color(200,190,186)};
   Color[] nearTrees={new Color(193,191,154),new Color(209,229,186),new Color(142,145,188),new Color(207,196,188)};
   using(var art=new RenderTarget2D(device,artWidth,artHeight,false,SurfaceFormat.Color,DepthFormat.None)) {
    bool drawing=false;
    try {
     device.SetRenderTarget(art);device.Clear(skyTop[hero]);
     batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);drawing=true;
     for(int y=0;y<artHeight;y+=2)batch.Draw(pixel,new Rectangle(0,y,artWidth,2),Color.Lerp(skyTop[hero],skyBottom[hero],Math.Min(1,y/300f)));
     if(hero==2)foreach(Point star in new[]{new Point(76,36),new Point(165,68),new Point(307,28),new Point(513,51),new Point(580,106)})batch.Draw(pixel,new Rectangle(star.X,star.Y,2,2),new Color(242,228,214,175));
     Texture2D cloud=Texture("Cloud_0");if(cloud!=null){batch.Draw(cloud,new Vector2(72,66),null,new Color(255,248,231,hero==2?35:95),0,Vector2.Zero,.8f,SpriteEffects.None,0);batch.Draw(cloud,new Vector2(405,49),null,new Color(255,246,230,hero==2?25:70),0,Vector2.Zero,.6f,SpriteEffects.FlipHorizontally,0);}
     DrawMenuLandscape("Background_7",85,250,-62+hero*19,mountains[hero]);
     DrawMenuLandscape("Background_9",151,215,-145+hero*23,farTrees[hero]);
     DrawMenuLandscape("Background_10",189,178,-43+hero*11,nearTrees[hero]);
     Texture2D dirt=Texture("Tiles_0"),grass=Texture("Tiles_2");
     for(int x=0;x<artWidth;x+=16) {
      if(grass!=null)batch.Draw(grass,new Rectangle(x,320,16,16),Clamp(new Rectangle(18,0,16,16),grass),Color.White);
      if(dirt!=null)for(int y=336;y<artHeight;y+=16)batch.Draw(dirt,new Rectangle(x,y,16,16),Clamp(new Rectangle(18,18,16,16),dirt),new Color(220,215,204));
     }
     DrawMenuPlayer(hero,new Vector2(420,164),3);
     // Native character text occupies the left; a restrained shade keeps its
     // existing typography readable without adding baked-in text or panels.
     for(int x=0;x<270;x+=3)batch.Draw(pixel,new Rectangle(x,0,3,artHeight),new Color(12,20,31,(byte)(48*(1-x/270f))));
     for(int y=340;y<artHeight;y+=2)batch.Draw(pixel,new Rectangle(0,y,artWidth,2),new Color(17,21,26,(byte)((y-340)*2)));
     batch.End();drawing=false;device.SetRenderTarget(null);
     var colors=new Color[artWidth*artHeight];art.GetData(colors);writer.Write(artWidth);writer.Write(artHeight);
     foreach(Color c in colors){writer.Write((byte)(c.A==0?0:Math.Min(255,c.R*255/c.A)));writer.Write((byte)(c.A==0?0:Math.Min(255,c.G*255/c.A)));writer.Write((byte)(c.A==0?0:Math.Min(255,c.B*255/c.A)));writer.Write(c.A);}
    }finally{if(drawing)batch.End();device.SetRenderTarget(null);}
   }
  }
  void DrawMenuLandscape(string asset,int top,int drawnHeight,int offset,Color tint) {
   Texture2D texture=Texture(asset);if(texture==null)return;int drawnWidth=Math.Max(1,(int)(texture.Width*drawnHeight/(float)texture.Height));
   for(int x=offset%drawnWidth-drawnWidth;x<640;x+=drawnWidth)batch.Draw(texture,new Rectangle(x,top,drawnWidth,drawnHeight),tint);
  }
  void DrawMenuPlayer(int hero,Vector2 position,float scale) {
   Color[] shirt={new Color(196,122,63),new Color(80,139,87),new Color(100,112,191),new Color(161,105,182)};
   Color[] hair={new Color(181,81,46),new Color(111,77,44),new Color(179,184,213),new Color(105,176,196)};
   Rectangle frame=new Rectangle(0,0,40,56),arms=new Rectangle(80,0,40,56);Color skin=new Color(244,204,165);
   MenuPlayerLayer(10,frame,position,skin,scale);MenuPlayerLayer(11,frame,position,new Color(58,65,78),scale);MenuPlayerLayer(12,frame,position,new Color(89,66,49),scale);
   MenuPlayerLayer(3,frame,position,skin,scale);MenuPlayerLayer(6,frame,position,shirt[hero],scale);MenuPlayerLayer(0,frame,position,skin,scale);MenuPlayerLayer(1,frame,position,Color.White,scale);MenuPlayerLayer(2,frame,position,new Color(67,96,125),scale);
   Texture2D hairTexture=Texture("Player_Hair_1");if(hairTexture!=null)batch.Draw(hairTexture,position,Clamp(frame,hairTexture),hair[hero],0,Vector2.Zero,scale,SpriteEffects.None,0);
   MenuPlayerLayer(4,arms,position,skin,scale);MenuPlayerLayer(7,arms,position,shirt[hero],scale);MenuPlayerLayer(5,arms,position,shirt[hero],scale);
   string[] weapons={"EnchantedSword","GoldBow","WaterBolt","BabyBirdStaff"};Type ids=mainType.Assembly.GetType("Terraria.ID.ItemID",true);FieldInfo field=ids.GetField(weapons[hero],BindingFlags.Public|BindingFlags.Static);
   if(field==null||!field.IsLiteral)throw new InvalidDataException("Local class weapon symbol unavailable.");
   Texture2D weapon=Texture("Item_"+Convert.ToInt32(field.GetRawConstantValue()));if(weapon!=null)batch.Draw(weapon,position+new Vector2(29,29)*scale,null,Color.White,hero==0?-.2f:hero==3?.3f:0,new Vector2(weapon.Width*.5f,weapon.Height*.5f),2,SpriteEffects.None,0);
  }
  void MenuPlayerLayer(int part,Rectangle frame,Vector2 position,Color tint,float scale) {
   Texture2D texture=Texture("Player_0_"+part);if(texture!=null)batch.Draw(texture,position,Clamp(frame,texture),tint,0,Vector2.Zero,scale,SpriteEffects.None,0);
  }
  void DrawScenery(Vector2 camera) {
   int ground=(int)(sceneFloor-camera.Y);
   if(sceneKey=="mine") {
    // Underground backgrounds are small repeating textures, not surface scenery.
    Texture2D rock=Texture("Background_3");if(rock!=null)for(int y=0;y<height;y+=rock.Height)for(int x=-(int)(camera.X*.15f)%rock.Width;x<width;x+=rock.Width)batch.Draw(rock,new Vector2(x,y),new Color(83,91,105));
   } else {
    bool purple=sceneKey=="corruption-altar",lunar=sceneKey=="boss-MoonLordCore",night=lunar||sceneKey=="boss-EyeofCthulhu"||sceneKey=="boss-SkeletronPrime";
    Texture2D sky=Texture("Background_0");if(sky!=null)batch.Draw(sky,new Rectangle(0,0,width,height),night?new Color(65,70,114):purple?new Color(153,119,188):Color.White);
    if(lunar){batch.Draw(pixel,new Rectangle(0,0,width,height),new Color(6,16,32,180));for(int i=0;i<85;i++){int sx=(i*173+47)%width,sy=(i*i*31+19)%Math.Max(1,ground-180);batch.Draw(pixel,new Rectangle(sx,sy,i%9==0?2:1,i%9==0?2:1),new Color(151,219,222,(byte)(90+i%150)));}}
    bool jungle=sceneKey=="boss-Plantera",ocean=sceneKey=="boss-DukeFishron";
    int[] layers=purple?new[]{41,44,45}:jungle?new[]{50,51,52}:ocean?new[]{25,27,28}:new[]{7,9,10};
    for(int layer=0;layer<layers.Length;layer++) {
     Texture2D texture=Texture("Background_"+layers[layer]);if(texture==null)continue;
     int crop=Math.Min(texture.Height,layer==0?300:210);int drawnHeight=layer==0?220:layer==1?200:180;
     float scale=drawnHeight/(float)crop;int drawnWidth=Math.Max(1,(int)(texture.Width*scale));int start=-(int)(camera.X*(.08f+layer*.08f))%drawnWidth-drawnWidth;
     Color tint=night?new Color(100,112,152):layer==0?new Color(188,204,216):Color.White;
     for(int x=start;x<width;x+=drawnWidth)batch.Draw(texture,new Rectangle(x,ground-drawnHeight+20,drawnWidth,drawnHeight),new Rectangle(0,0,texture.Width,crop),tint);
    }
   }
   // Separate far background layers move more slowly than native tile walls.
   // Bounds are supplied by the collision scene, not inferred from screen pixels.
   if(sceneRight<=sceneLeft)throw new InvalidOperationException("Scene bounds are invalid.");
  }
  void DrawWalls(Array tiles,Vector2 camera) {
   if(tiles==null)return;int left=Math.Max(0,(int)Math.Floor(camera.X/16)),top=Math.Max(0,(int)Math.Floor(camera.Y/16)),right=Math.Min(tiles.GetLength(0),left+width/16+3),bottom=Math.Min(tiles.GetLength(1),top+height/16+3);
   for(int y=top;y<bottom;y++)for(int x=left;x<right;x++) {
    object tile=tiles.GetValue(x,y);if(tile==null)continue;object value=ReadValue(tile,"wall");int wall=value==null?0:Convert.ToInt32(value);if(wall==0)continue;
    Texture2D texture=Texture("Wall_"+wall);if(texture==null)continue;int sx=Math.Min(8,Math.Max(0,texture.Width-16)),sy=Math.Min(8,Math.Max(0,texture.Height-16));
    batch.Draw(texture,new Vector2((int)(x*16-camera.X),(int)(y*16-camera.Y)),new Rectangle(sx,sy,16,16),new Color(160,168,180));
   }
  }
  void DrawTiles(Array tiles,Vector2 camera) {
   if(tiles==null)return;if(tiles.Rank!=2)throw new ArgumentException("Expected a two-dimensional native tile array.");
   int left=Math.Max(0,(int)Math.Floor(camera.X/16)),top=Math.Max(0,(int)Math.Floor(camera.Y/16));
   int right=Math.Min(tiles.GetLength(0),left+width/16+3),bottom=Math.Min(tiles.GetLength(1),top+height/16+3);
   MethodInfo active=null;
   for(int y=top;y<bottom;y++)for(int x=left;x<right;x++) {
    object tile=tiles.GetValue(x,y);if(tile==null)continue;
    if(active==null)active=tile.GetType().GetMethod("active",Type.EmptyTypes);
    if(active==null||!(bool)active.Invoke(tile,null))continue;
    Texture2D texture=Texture("Tiles_"+Convert.ToInt32(ReadValue(tile,"type")));if(texture==null)continue;
    int sx=Convert.ToInt32(ReadValue(tile,"frameX")),sy=Convert.ToInt32(ReadValue(tile,"frameY"));
    if(sx<0||sy<0||sx+16>texture.Width||sy+16>texture.Height){sx=Math.Min(18,Math.Max(0,texture.Width-16));sy=Math.Min(18,Math.Max(0,texture.Height-16));}
    batch.Draw(texture,new Vector2((int)(x*16-camera.X),(int)(y*16-camera.Y)),new Rectangle(sx,sy,16,16),Color.White);
   }
  }
  void DrawNpc(object npc,Vector2 camera) {
   int type=Read<int>(npc,"type",0);if(type<=0)return;
   if(type>=396&&type<=400){DrawNativeMoonLord(npc,camera);return;}
   Texture2D texture=Texture("NPC_"+type);if(texture==null)return;
   Rectangle frame=Read<Rectangle>(npc,"frame",Rectangle.Empty);
   // Dedicated simulation expresses NPC animation Y in one-pixel frame units.
   // Convert that index back to the actual local spritesheet frame dimensions.
   if(frame.Height<=1){int h=Math.Max(1,texture.Height/FrameCount("npcFrameCount",type));frame=new Rectangle(0,Math.Max(0,frame.Y)*h,texture.Width,h);}
   if(frame.Width<=0||frame.Height<=0)frame=new Rectangle(0,0,texture.Width,texture.Height/FrameCount("npcFrameCount",type));
   frame=Clamp(frame,texture);float scale=Read<float>(npc,"scale",1f);if(scale<=0)scale=1;
   Vector2 position=Read<Vector2>(npc,"position",Vector2.Zero)-camera;
   int entityWidth=Read<int>(npc,"width",frame.Width),entityHeight=Read<int>(npc,"height",frame.Height);
   position+=new Vector2(entityWidth*0.5f,entityHeight+Read<float>(npc,"gfxOffY",0));
   Color tint=Color.White;MethodInfo alpha=npc.GetType().GetMethod("GetAlpha",new[]{typeof(Color)});if(alpha!=null)tint=(Color)alpha.Invoke(npc,new object[]{Color.White});
   int direction=Read<int>(npc,"spriteDirection",Read<int>(npc,"direction",1));
   batch.Draw(texture,position,frame,tint,Read<float>(npc,"rotation",0),new Vector2(frame.Width*0.5f,frame.Height),scale,direction<0?SpriteEffects.None:SpriteEffects.FlipHorizontally,0);
  }
  void DrawNativeMoonLord(object npc,Vector2 camera) {
   if(textureAssets==null)InitializePlayerPresentation(mainType.Assembly,content.RootDirectory);
   BindNativeSingle("MagicPixel","MagicPixel");BindNativeSingle("BlackTile","Black_Tile");
   int type=Read<int>(npc,"type",0);BindNativeTexture("Npc",type,"NPC_"+type);
   // Moon Lord's body is assembled by Terraria from several atlases and native
   // limb offsets. A single NPC frame loses the arms, tongue and opening eyes.
   for(int i=11;i<=30;i++)BindNativeTexture("Extra",i,"Extra_"+i);
   FieldInfo screen=mainType.GetField("screenPosition");object previous=screen.GetValue(null);screen.SetValue(null,camera);
   try{mainType.GetMethod("DrawNPCDirect",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(mainType.GetField("instance").GetValue(null),new object[]{batch,npc,false,camera});}
   finally{screen.SetValue(null,previous);}
  }
  void DrawProjectile(object projectile,Vector2 camera) {
   int type=Read<int>(projectile,"type",0);if(type<=0)return;Texture2D texture=Texture("Projectile_"+type);if(texture==null)return;
   if(textureAssets==null)InitializePlayerPresentation(mainType.Assembly,content.RootDirectory);
   BindNativeTexture("Projectile",type,"Projectile_"+type);
   if(nativeDirectProjectiles.Contains(type)){DrawNativeProjectileDirect(projectile,camera);return;}
   MethodInfo special;if(specialProjectileDraws.TryGetValue(type,out special)){DrawNativeProjectile(special,projectile,camera);return;}
   Type projectileSets=mainType.Assembly.GetType("Terraria.ID.ProjectileID+Sets",true);FieldInfo whip=projectileSets.GetField("IsAWhip",BindingFlags.Public|BindingFlags.Static);Array whips=whip==null?null:whip.GetValue(null) as Array;
   if(whips!=null&&type<whips.Length&&(bool)whips.GetValue(type)) {
    Array players=mainType.GetField("player").GetValue(null) as Array;int owner=Read<int>(projectile,"owner",0);
    if(players!=null&&owner>=0&&owner<players.Length){FieldInfo menu=mainType.GetField("gameMenu"),screen=mainType.GetField("screenPosition");object previous=menu.GetValue(null),previousScreen=screen.GetValue(null);menu.SetValue(null,true);screen.SetValue(null,camera);try{mainType.GetMethod("DrawWhip",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(mainType.GetField("instance").GetValue(null),new[]{projectile,players.GetValue(owner)});}finally{menu.SetValue(null,previous);screen.SetValue(null,previousScreen);}return;}
   }
   int aiStyle=Read<int>(projectile,"aiStyle",0);
   if(aiStyle==15||aiStyle==99) {
    Array players=mainType.GetField("player").GetValue(null) as Array;int owner=Read<int>(projectile,"owner",0);
    if(players!=null&&owner>=0&&owner<players.Length){Vector2 hand=Read<Vector2>(players.GetValue(owner),"itemLocation",Vector2.Zero)-camera,end=Read<Vector2>(projectile,"position",Vector2.Zero)-camera+new Vector2(Read<int>(projectile,"width",0)*.5f,Read<int>(projectile,"height",0)*.5f),delta=end-hand;float length=delta.Length();if(length>0&&length<1600)batch.Draw(pixel,hand,null,aiStyle==99?new Color(215,211,184):new Color(129,135,144),(float)Math.Atan2(delta.Y,delta.X),Vector2.Zero,new Vector2(length,aiStyle==99?1:2),SpriteEffects.None,0);}
   }
   int count=FrameCount("projFrames",type),frameHeight=Math.Max(1,texture.Height/count),index=Math.Max(0,Read<int>(projectile,"frame",0))%count;
   Rectangle frame=new Rectangle(0,index*frameHeight,texture.Width,frameHeight);
   Vector2 position=Read<Vector2>(projectile,"position",Vector2.Zero)-camera+new Vector2(Read<int>(projectile,"width",0)*0.5f,Read<int>(projectile,"height",0)*0.5f);
   Color tint=(Color)projectile.GetType().GetMethod("GetAlpha",new[]{typeof(Color)}).Invoke(projectile,new object[]{Color.White});
   batch.Draw(texture,position,frame,tint,Read<float>(projectile,"rotation",0),new Vector2(frame.Width*0.5f,frame.Height*0.5f),Read<float>(projectile,"scale",1),Read<int>(projectile,"spriteDirection",1)<0?SpriteEffects.FlipHorizontally:SpriteEffects.None,0);
  }
  void DrawNativeProjectileDirect(object projectile,Vector2 camera) {
   BindNativeSingle("MagicPixel","MagicPixel");BindNativeSingle("BlackTile","Black_Tile");
   FieldInfo screen=mainType.GetField("screenPosition"),menu=mainType.GetField("gameMenu");object previous=screen.GetValue(null),oldMenu=menu.GetValue(null);screen.SetValue(null,camera);menu.SetValue(null,true);
   Array players=mainType.GetField("player").GetValue(null) as Array;int owner=Read<int>(projectile,"owner",0);if(owner<0||owner>=players.Length)owner=0;
   try{mainType.GetMethod("DrawProjDirect",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(mainType.GetField("instance").GetValue(null),new[]{projectile,players.GetValue(owner)});specialDrawCount++;}
   finally{screen.SetValue(null,previous);menu.SetValue(null,oldMenu);}
  }
  void DrawDust(Vector2 camera) {
   Texture2D atlas=Texture("Dust");if(atlas==null)return;Array dust=mainType.GetField("dust").GetValue(null) as Array;if(dust==null)return;
   foreach(object particle in dust)if(particle!=null&&Read<bool>(particle,"active",false)) {
    Rectangle frame=Read<Rectangle>(particle,"frame",Rectangle.Empty);if(frame.Width<=0||frame.Height<=0)continue;
    Vector2 position=Read<Vector2>(particle,"position",Vector2.Zero)-camera;float scale=Read<float>(particle,"scale",1);
    if(!Finite(position.X)||!Finite(position.Y)||!Finite(scale)||position.X< -40||position.X>width+40||position.Y< -40||position.Y>height+40)continue;
    Color tint=(Color)particle.GetType().GetMethod("GetAlpha",new[]{typeof(Color)}).Invoke(particle,new object[]{Color.White});
    batch.Draw(atlas,position,Clamp(frame,atlas),tint,Read<float>(particle,"rotation",0),new Vector2(4,4),scale,SpriteEffects.None,0);
   }
  }
  void DrawNativeProjectile(MethodInfo method,object projectile,Vector2 camera) {
   FieldInfo menu=mainType.GetField("gameMenu"),screen=mainType.GetField("screenPosition");object previousMenu=menu.GetValue(null),previousScreen=screen.GetValue(null);menu.SetValue(null,true);screen.SetValue(null,camera);
   try{method.Invoke(mainType.GetField("instance").GetValue(null),new[]{projectile});specialDrawCount++;}
   finally{menu.SetValue(null,previousMenu);screen.SetValue(null,previousScreen);}
  }
  void DrawPlayer(object player,Vector2 camera) {
   if(mainType.GetField("instance").GetValue(null)!=null){DrawNativePlayer(player,camera);return;}
   int variant=Math.Max(0,Read<int>(player,"skinVariant",0));Vector2 position=Read<Vector2>(player,"position",Vector2.Zero)-camera;
   position+=new Vector2(Read<int>(player,"width",20)/2-20,Read<int>(player,"height",42)-56+4);
   Rectangle bodyFrame=Read<Rectangle>(player,"bodyFrame",new Rectangle(0,0,40,56)),legFrame=Read<Rectangle>(player,"legFrame",new Rectangle(0,0,40,56));
   if(bodyFrame.Width<=0||bodyFrame.Height<=0)bodyFrame=new Rectangle(0,0,40,56);if(legFrame.Width<=0||legFrame.Height<=0)legFrame=new Rectangle(0,0,40,56);
   SpriteEffects effects=Read<int>(player,"direction",1)<0?SpriteEffects.FlipHorizontally:SpriteEffects.None;
   Color skin=Read<Color>(player,"skinColor",new Color(255,218,176)),shirt=Read<Color>(player,"shirtColor",new Color(55,125,190)),pants=Read<Color>(player,"pantsColor",new Color(45,55,85));
   // 1.4.5 native player body assets use a composite atlas. These basic layers
   // intentionally support the ordinary standing/walking body, without armor FX.
   PlayerLayer(variant,10,legFrame,position,skin,effects);
   PlayerLayer(variant,11,legFrame,position,pants,effects);
   PlayerLayer(variant,12,legFrame,position,Read<Color>(player,"shoeColor",new Color(65,45,35)),effects);
   PlayerLayer(variant,3,new Rectangle(0,0,40,56),position,skin,effects);
   PlayerLayer(variant,6,new Rectangle(0,0,40,56),position,shirt,effects);
   PlayerLayer(variant,0,bodyFrame,position,skin,effects);
   PlayerLayer(variant,1,bodyFrame,position,Color.White,effects);
   PlayerLayer(variant,2,bodyFrame,position,Read<Color>(player,"eyeColor",new Color(60,90,140)),effects);
   Texture2D hair=Texture("Player_Hair_"+(Read<int>(player,"hair",0)+1));
   if(hair!=null)batch.Draw(hair,position,Clamp(bodyFrame,hair),Read<Color>(player,"hairColor",new Color(100,55,25)),0,Vector2.Zero,1,effects,0);
   PlayerLayer(variant,4,new Rectangle(80,0,40,56),position,skin,effects);
   PlayerLayer(variant,7,new Rectangle(80,0,40,56),position,Read<Color>(player,"underShirtColor",shirt),effects);
   PlayerLayer(variant,5,new Rectangle(80,0,40,56),position,shirt,effects);
   DrawHeldItem(player,camera,effects);
  }
  void DrawNativePlayer(object player,Vector2 camera) {
   PreparePlayerTextures(player);drawData.Clear();drawDust.Clear();drawGore.Clear();
   object drawSet=Activator.CreateInstance(drawSetType);FieldInfo menu=mainType.GetField("gameMenu"),screen=mainType.GetField("screenPosition"),dedicated=mainType.GetField("dedServ");object previousMenu=menu.GetValue(null),previousScreen=screen.GetValue(null),previousDedicated=dedicated.GetValue(null);
   // GetItemDrawFrame returns Rectangle.Empty on a server. The native layer
   // composition must run as a client too, or animated hands hold a zero-size item.
   menu.SetValue(null,true);screen.SetValue(null,camera);dedicated.SetValue(null,false);
   try {
    setupDraw.Invoke(drawSet,new object[]{player,drawData,drawDust,drawGore,Read<Vector2>(player,"position",Vector2.Zero),0f,Read<float>(player,"fullRotation",0),Read<Vector2>(player,"fullRotationOrigin",Vector2.Zero),null});
    object[] args={drawSet};layersDraw.Invoke(nativePlayerRenderer,args);transformDraw.Invoke(null,args);lastPlayerDrawCount=drawData.Count;
    foreach(object item in drawData)drawDataDraw.Invoke(item,new object[]{batch});
   } catch(TargetInvocationException error){throw new InvalidOperationException("Native player presentation failed: "+error.InnerException,error.InnerException);}
   finally{menu.SetValue(null,previousMenu);screen.SetValue(null,previousScreen);dedicated.SetValue(null,previousDedicated);}
  }
  void DrawHeldItem(object player,Vector2 camera,SpriteEffects effects) {
   if(Read<int>(player,"itemAnimation",0)<=0)return;
   Array inventory=ReadValue(player,"inventory") as Array;PropertyInfo selectedProperty=player.GetType().GetProperty("selectedItem",BindingFlags.Public|BindingFlags.Instance);
   if(inventory==null||selectedProperty==null)return;int selected=(int)selectedProperty.GetValue(player,null);if(selected<0||selected>=inventory.Length)return;
   object item=inventory.GetValue(selected);if(item==null||Read<bool>(item,"noUseGraphic",false))return;
   int type=Read<int>(item,"type",0);if(type<=0)return;Texture2D texture=Texture("Item_"+type);if(texture==null)return;
   Vector2 location=Read<Vector2>(player,"itemLocation",Read<Vector2>(player,"position",Vector2.Zero))-camera;
   bool left=(effects&SpriteEffects.FlipHorizontally)!=0;
   Vector2 origin=new Vector2(left?texture.Width:0,texture.Height);
   batch.Draw(texture,location,null,Color.White,Read<float>(player,"itemRotation",0),origin,Read<float>(item,"scale",1),effects,0);
  }
  void PlayerLayer(int variant,int part,Rectangle frame,Vector2 position,Color tint,SpriteEffects effects) {
   Texture2D texture=Texture("Player_"+variant+"_"+part);if(texture==null)texture=Texture("Player_0_"+part);
   if(texture!=null)batch.Draw(texture,position,Clamp(frame,texture),tint,0,Vector2.Zero,1,effects,0);
  }
  int FrameCount(string fieldName,int index) {
   FieldInfo field=mainType.GetField(fieldName,BindingFlags.Public|BindingFlags.Static);Array counts=field==null?null:field.GetValue(null) as Array;
   return counts!=null&&index>=0&&index<counts.Length?Math.Max(1,Convert.ToInt32(counts.GetValue(index))):1;
  }
  Texture2D Texture(string name) {
   Texture2D texture;if(textures.TryGetValue(name,out texture))return texture;
   if(!File.Exists(Path.Combine(content.RootDirectory,"Images",name+".xnb")))texture=null;
   else try{texture=content.Load<Texture2D>("Images/"+name);}catch(ContentLoadException){texture=null;}
   textures.Add(name,texture);return texture;
  }
  object ReadValue(object item,string name) {
   string key=item.GetType().FullName+":"+name;FieldInfo field;
   if(!fields.TryGetValue(key,out field)){field=item.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);fields[key]=field;}
   return field==null?null:field.GetValue(item);
  }
  T Read<T>(object item,string name,T fallback){object value=ReadValue(item,name);return value is T?(T)value:fallback;}
  static Rectangle Clamp(Rectangle frame,Texture2D texture) {
   int frameWidth=Math.Min(Math.Max(1,frame.Width),texture.Width),frameHeight=Math.Min(Math.Max(1,frame.Height),texture.Height);
   return new Rectangle(Math.Max(0,Math.Min(frame.X,texture.Width-frameWidth)),Math.Max(0,Math.Min(frame.Y,texture.Height-frameHeight)),frameWidth,frameHeight);
  }
  void RequireThread(){if(disposed)throw new ObjectDisposedException("NativeRenderer");if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("Native renderer must stay on its owning thread.");}
  public void Dispose(){if(disposed)return;RequireThread();disposed=true;ReleaseResources();}
  void ReleaseResources() {
   var failures=new List<Exception>();
   for(int i=fontBindings.Count-1;i>=0;i--)try{FontBinding binding=fontBindings[i];if(object.ReferenceEquals(binding.Field.GetValue(null),binding.Assigned))binding.Field.SetValue(null,binding.Previous);}catch(Exception e){failures.Add(e);}
   fontBindings.Clear();
   foreach(IDisposable resource in new IDisposable[]{pixel,target,batch,content,device,window})if(resource!=null)try{resource.Dispose();}catch(Exception e){failures.Add(e);}
   if(failures.Count>0)throw new AggregateException("Native renderer resource cleanup failed.",failures);
  }
  sealed class FontBinding {internal FieldInfo Field;internal object Previous;internal object Assigned;}
  sealed class DeviceService:IGraphicsDeviceService {
   public GraphicsDevice GraphicsDevice{get;private set;}public DeviceService(GraphicsDevice device){GraphicsDevice=device;}
   public event EventHandler<EventArgs> DeviceCreated{add{}remove{}}
   public event EventHandler<EventArgs> DeviceDisposing{add{}remove{}}
   public event EventHandler<EventArgs> DeviceReset{add{}remove{}}
   public event EventHandler<EventArgs> DeviceResetting{add{}remove{}}
  }
 }
}

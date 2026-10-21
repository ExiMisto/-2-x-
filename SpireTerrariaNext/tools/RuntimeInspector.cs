using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

internal static class RuntimeInspector {
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int flags);
 static Assembly game;
 static string gameDir, stateDir;
 static readonly Dictionary<short,OpCode> opcodes = new Dictionary<short,OpCode>();
 static readonly Dictionary<string,Assembly> resolved = new Dictionary<string,Assembly>(StringComparer.OrdinalIgnoreCase);
 static Assembly Resolve(object sender, ResolveEventArgs args) {
  string name=new AssemblyName(args.Name).Name;Assembly result;if(resolved.TryGetValue(name,out result))return result;string file=name+".dll";
  string local=Path.Combine(gameDir,file); if(File.Exists(local)){result=Assembly.LoadFrom(local);resolved[name]=result;return result;}
  foreach(string resource in game.GetManifestResourceNames()) {
   if(!resource.EndsWith("."+file,StringComparison.OrdinalIgnoreCase)&&!resource.Equals(file,StringComparison.OrdinalIgnoreCase))continue;
   using(Stream input=game.GetManifestResourceStream(resource))using(var buffer=new MemoryStream()) {
    input.CopyTo(buffer); byte[] bytes=buffer.ToArray();
    if(bytes.Length<2||bytes[0]!='M'||bytes[1]!='Z')using(var ds=new DeflateStream(new MemoryStream(bytes),CompressionMode.Decompress))using(var decoded=new MemoryStream()){ds.CopyTo(decoded);bytes=decoded.ToArray();}
    result=Assembly.Load(bytes);resolved[name]=result;return result;
   }
  }
  return null;
 }
 [STAThread] static int Main(string[] args) {
  try {
   gameDir=Path.GetFullPath(args[0]);stateDir=Path.GetFullPath(args[1]);Directory.CreateDirectory(stateDir);
   AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
   game=Assembly.LoadFrom(Path.Combine(gameDir,"Terraria.exe"));
   if(args.Length>2&&args[2]=="texture")Texture();
   else if(args.Length>2&&args[2]=="scene")Scene();
   else if(args.Length>2&&args[2]=="data")Data();
   else if(args.Length>3&&args[2]=="members")foreach(MemberInfo member in FindType(args[3]).GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))Console.WriteLine(member.MemberType+" "+member);
   else if(args.Length>3&&args[2]=="il")DumpIl(args[3],args.Length>4?args[4]:null);
   else Inspect();
   return 0;
  } catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
 static Type FindType(string name){Type type=game.GetType(name);if(type==null)type=Assembly.Load("ReLogic").GetType(name);if(type==null)throw new TypeLoadException(name);return type;}
 static void Set(object obj,string field,object value){obj.GetType().GetField(field,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);}
 static void Scene() {
  Type main=game.GetType("Terraria.Main",true),program=game.GetType("Terraria.Program",true);
  program.GetField("SavePath",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,Path.Combine(stateDir,"native-state"));RuntimeHelpers.RunClassConstructor(main.TypeHandle);
  main.GetField("rand").SetValue(null,Activator.CreateInstance(game.GetType("Terraria.Utilities.UnifiedRandom"),new object[]{123}));
  Type netplay=game.GetType("Terraria.Netplay",true),remoteClient=game.GetType("Terraria.RemoteClient",true),networkText=game.GetType("Terraria.Localization.NetworkText",true);Array clients=(Array)netplay.GetField("Clients").GetValue(null);
  for(int i=0;i<clients.Length;i++)clients.SetValue(Activator.CreateInstance(remoteClient),i);
  object deathMessage=networkText.GetMethod("FromLiteral",new[]{typeof(string)}).Invoke(null,new object[]{"SpireNext isolated death-broadcast test"});
  object previousDedicated=main.GetField("dedServ").GetValue(null);main.GetField("dedServ").SetValue(null,true);
  game.GetType("Terraria.Chat.ChatHelper",true).GetMethod("BroadcastChatMessageAs",new[]{typeof(byte),networkText,typeof(Color),typeof(int)}).Invoke(null,new object[]{(byte)255,deathMessage,Color.White,-1});
  main.GetField("dedServ").SetValue(null,previousDedicated);int sockets=0;foreach(object client in clients)if(remoteClient.GetField("Socket").GetValue(client)!=null)sockets++;
  if(sockets!=0)throw new InvalidOperationException("Offline client initialization created a socket.");Console.WriteLine("NATIVE_OFFLINE_BROADCAST clients="+clients.Length+" socketObjects="+sockets+" success=True");
  Type tileType=game.GetType("Terraria.Tile",true),playerType=game.GetType("Terraria.Player",true),npcType=game.GetType("Terraria.NPC",true),projectileType=game.GetType("Terraria.Projectile",true);
  Array tiles=Array.CreateInstance(tileType,48,24),npcs=Array.CreateInstance(npcType,2),projectiles=Array.CreateInstance(projectileType,1);
  for(int x=0;x<48;x++)for(int y=15;y<24;y++){object tile=Activator.CreateInstance(tileType);Set(tile,"type",(ushort)(y==15?2:0));Set(tile,"frameX",(short)18);Set(tile,"frameY",(short)18);tileType.GetMethod("active",new[]{typeof(bool)}).Invoke(tile,new object[]{true});tiles.SetValue(tile,x,y);}
  object player=Activator.CreateInstance(playerType);Set(player,"active",true);Set(player,"position",new Vector2(270,198));Set(player,"bodyFrame",new Rectangle(0,0,40,56));Set(player,"legFrame",new Rectangle(0,0,40,56));Set(player,"direction",1);
  Array players=(Array)main.GetField("player").GetValue(null);players.SetValue(player,0);
  object npc=Activator.CreateInstance(npcType);MethodInfo defaults=npcType.GetMethod("SetDefaults",new[]{typeof(int),game.GetType("Terraria.NPCSpawnParams",true)});defaults.Invoke(npc,new[]{(object)1,Activator.CreateInstance(game.GetType("Terraria.NPCSpawnParams",true))});Set(npc,"active",true);Set(npc,"position",new Vector2(410,214));npcs.SetValue(npc,0);
  object projectile=Activator.CreateInstance(projectileType);projectileType.GetMethod("SetDefaults",new[]{typeof(int)}).Invoke(projectile,new object[]{1});Set(projectile,"active",true);Set(projectile,"position",new Vector2(350,190));Set(projectile,"rotation",1.2f);projectiles.SetValue(projectile,0);
  Type fontAssets=game.GetType("Terraria.GameContent.FontAssets",true);object originalFonts=fontAssets.GetField("CombatText").GetValue(null);
  using(var renderer=new SpireTerraria.Next.NativeRenderer(game,gameDir,640,360)) {
   Type combatType=game.GetType("Terraria.CombatText",true);Array combatPool=(Array)main.GetField("combatText").GetValue(null);for(int i=0;i<combatPool.Length;i++)combatPool.SetValue(Activator.CreateInstance(combatType),i);
   foreach(string fieldName in new[]{"MouseText","DeathText","ItemStack"}) {
    object asset=fontAssets.GetField(fieldName).GetValue(null),font=asset.GetType().GetProperty("Value").GetValue(asset,null);Vector2 size=(Vector2)font.GetType().GetMethod("MeasureString",new[]{typeof(string)}).Invoke(font,new object[]{"12345"});Console.WriteLine("NATIVE_FONT "+fieldName+" loaded="+asset.GetType().GetProperty("IsLoaded").GetValue(asset,null)+" size="+size);
   }
   MethodInfo newText=combatType.GetMethod("NewText",new[]{typeof(Rectangle),typeof(Color),typeof(string),typeof(bool),typeof(bool)});
   int normal=(int)newText.Invoke(null,new object[]{new Rectangle(400,210,30,30),Color.White,"19",false,false}),critical=(int)newText.Invoke(null,new object[]{new Rectangle(400,210,30,30),Color.Yellow,"38",true,false});
   if(normal!=0||critical!=1)throw new InvalidOperationException("Native CombatText did not use the free pool slots.");
   for(int i=0;i<12;i++)combatType.GetMethod("UpdateCombatText").Invoke(null,null);
   Console.WriteLine("NATIVE_COMBAT_TEXT normalSlot="+normal+" criticalSlot="+critical+" normalActive="+combatType.GetField("active").GetValue(combatPool.GetValue(normal))+" criticalActive="+combatType.GetField("active").GetValue(combatPool.GetValue(critical)));
   byte[] bytes=renderer.RenderScene(tiles,player,npcs,projectiles,0,0);
   using(var bitmap=new System.Drawing.Bitmap(640,360,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){
    var locked=bitmap.LockBits(new System.Drawing.Rectangle(0,0,640,360),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);System.Runtime.InteropServices.Marshal.Copy(bytes,0,locked.Scan0,bytes.Length);bitmap.UnlockBits(locked);bitmap.Save(Path.Combine(stateDir,"native-scene.png"),System.Drawing.Imaging.ImageFormat.Png);
   }
   var watch=Stopwatch.StartNew();for(int i=0;i<60;i++)renderer.RenderScene(tiles,player,npcs,projectiles,i*0.1f,0);watch.Stop();
   Console.WriteLine("NATIVE_SCENE bytes="+bytes.Length+" textures="+renderer.TextureCount+" avgMs="+(watch.Elapsed.TotalMilliseconds/60).ToString("0.00")+" adapter="+renderer.AdapterDescription);
  }
  if(!object.ReferenceEquals(originalFonts,fontAssets.GetField("CombatText").GetValue(null)))throw new InvalidOperationException("Font assets were not restored after renderer disposal.");
  int before=GetGuiResources(Process.GetCurrentProcess().Handle,1);bool rejected=false;
  try{using(var bad=new SpireTerraria.Next.NativeRenderer(game,Path.Combine(stateDir,"missing-game"),640,360)){}}
  catch(InvalidOperationException error){rejected=error.Message.StartsWith("Native Terraria fonts",StringComparison.Ordinal);Console.WriteLine("NATIVE_MISSING_FONTS rejected="+rejected);}
  int after=GetGuiResources(Process.GetCurrentProcess().Handle,1);Console.WriteLine("NATIVE_FAILURE_CLEANUP userHandlesBefore="+before+" userHandlesAfter="+after);
  if(!rejected||after!=before)throw new InvalidOperationException("Failed construction leaked window resources or did not report missing fonts.");
 }
 static void Data() {
  Type main=game.GetType("Terraria.Main",true),program=game.GetType("Terraria.Program",true);
  program.GetField("SavePath",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,Path.Combine(stateDir,"native-state"));
  RuntimeHelpers.RunClassConstructor(main.TypeHandle);
  main.GetField("rand").SetValue(null,Activator.CreateInstance(game.GetType("Terraria.Utilities.UnifiedRandom"),new object[]{123}));
  foreach(string method in new[]{"Initialize_TileAndNPCData1","Initialize_TileAndNPCData2"})main.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
  Type itemType=game.GetType("Terraria.Item",true);MethodInfo defaults=itemType.GetMethod("SetDefaults",new[]{typeof(int),game.GetType("Terraria.GameContent.Items.ItemVariant",true)});
  for(int id=0;id<6500;id++)try{defaults.Invoke(Activator.CreateInstance(itemType),new object[]{id,null});}catch(Exception e){Console.WriteLine("ITEM_FAIL id="+id+" "+e);break;}
 }
 static void Inspect() {
  Console.WriteLine("Game "+game.FullName+" file="+FileVersionInfo.GetVersionInfo(game.Location).FileVersion);
  foreach(AssemblyName reference in game.GetReferencedAssemblies())Console.WriteLine("REF "+reference);
  foreach(string name in new[]{"Terraria.Main","Terraria.Program","Terraria.GameContent.TextureAssets","Terraria.Initializers.AssetInitializer","Terraria.Initializers.LaunchInitializer"}) {
   Type t=game.GetType(name);if(t==null)continue;
   Console.WriteLine("TYPE "+t.FullName+" base="+t.BaseType);
   foreach(ConstructorInfo c in t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))Console.WriteLine("CTOR "+c);
   foreach(MethodInfo m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
    if(m.Name.IndexOf("Initial",StringComparison.OrdinalIgnoreCase)>=0||m.Name.IndexOf("Content",StringComparison.OrdinalIgnoreCase)>=0||m.Name=="Update"||m.Name=="Draw"||m.Name.IndexOf("Launch",StringComparison.OrdinalIgnoreCase)>=0||m.Name.IndexOf("Graphics",StringComparison.OrdinalIgnoreCase)>=0||m.Name=="LoadPlayers"||m.Name=="LoadWorlds")Console.WriteLine("METHOD "+m+" virtual="+m.IsVirtual+" public="+m.IsPublic+" family="+m.IsFamily);
   foreach(FieldInfo f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
    if(f.Name=="instance"||f.Name.IndexOf("SavePath",StringComparison.OrdinalIgnoreCase)>=0||f.Name.IndexOf("graphics",StringComparison.OrdinalIgnoreCase)>=0||f.Name.IndexOf("asset",StringComparison.OrdinalIgnoreCase)>=0||f.Name=="spriteBatch"||f.Name=="Item")Console.WriteLine("FIELD "+f+" static="+f.IsStatic);
  }
 }
 static void DumpIl(string typeName,string methodName) {
  foreach(FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static))if(field.FieldType==typeof(OpCode)){OpCode code=(OpCode)field.GetValue(null);opcodes[code.Value]=code;}
  Type t=FindType(typeName);
  var methods=new List<MethodBase>();methods.AddRange(t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static));methods.AddRange(t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly));
  foreach(MethodBase m in methods){if(methodName!=null&&m.Name!=methodName)continue;MethodBody body=m.GetMethodBody();if(body==null)continue;Console.WriteLine("\n"+m.DeclaringType+"::"+m);byte[] il=body.GetILAsByteArray();
   for(int i=0;i<il.Length;){int offset=i;short key=il[i++];if(key==0xfe)key=(short)(0xfe00|il[i++]);OpCode op=opcodes[key];object operand="";int token;
    switch(op.OperandType){
     case OperandType.InlineNone:break;
     case OperandType.ShortInlineI:operand=(sbyte)il[i++];break;
     case OperandType.ShortInlineVar:operand=il[i++];break;
     case OperandType.InlineVar:operand=BitConverter.ToUInt16(il,i);i+=2;break;
     case OperandType.InlineI:operand=BitConverter.ToInt32(il,i);i+=4;break;
     case OperandType.InlineI8:operand=BitConverter.ToInt64(il,i);i+=8;break;
     case OperandType.ShortInlineR:operand=BitConverter.ToSingle(il,i);i+=4;break;
     case OperandType.InlineR:operand=BitConverter.ToDouble(il,i);i+=8;break;
     case OperandType.ShortInlineBrTarget:operand="IL_"+(i+1+(sbyte)il[i]).ToString("x4");i++;break;
     case OperandType.InlineBrTarget:operand="IL_"+(i+4+BitConverter.ToInt32(il,i)).ToString("x4");i+=4;break;
     case OperandType.InlineSwitch:int count=BitConverter.ToInt32(il,i);i+=4+count*4;operand="switch "+count;break;
     case OperandType.InlineString:token=BitConverter.ToInt32(il,i);i+=4;try{operand="\""+m.Module.ResolveString(token)+"\"";}catch{operand=token;}break;
     default:token=BitConverter.ToInt32(il,i);i+=4;try{operand=m.Module.ResolveMember(token,t.GetGenericArguments(),m.IsGenericMethod?m.GetGenericArguments():null);}catch{operand="token "+token.ToString("x8");}break;
    }
    MemberInfo member=operand as MemberInfo;
    Console.WriteLine("IL_"+offset.ToString("x4")+": "+op.Name+" "+(member!=null?member.DeclaringType+"::"+member:operand));
   }
  }
 }
 sealed class GraphicsService : IGraphicsDeviceService {
  public GraphicsDevice GraphicsDevice{get;private set;}
  public GraphicsService(GraphicsDevice graphics){GraphicsDevice=graphics;}
  public event EventHandler<EventArgs> DeviceCreated {add{}remove{}}
  public event EventHandler<EventArgs> DeviceDisposing {add{}remove{}}
  public event EventHandler<EventArgs> DeviceReset {add{}remove{}}
  public event EventHandler<EventArgs> DeviceResetting {add{}remove{}}
 }
 static void Texture() {
  using(var form=new Form()) {
   form.Text="SpireNext isolated graphics probe";form.ShowInTaskbar=false;form.FormBorderStyle=FormBorderStyle.FixedToolWindow;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-32000,-32000);form.ClientSize=new System.Drawing.Size(320,180);
   IntPtr handle=form.Handle;
   var pp=new PresentationParameters{BackBufferWidth=320,BackBufferHeight=180,BackBufferFormat=SurfaceFormat.Color,DepthStencilFormat=DepthFormat.None,DeviceWindowHandle=handle,IsFullScreen=false,PresentationInterval=PresentInterval.Immediate};
   using(var graphics=new GraphicsDevice(GraphicsAdapter.DefaultAdapter,GraphicsProfile.Reach,pp)) {
    var services=new GameServiceContainer();services.AddService(typeof(IGraphicsDeviceService),new GraphicsService(graphics));
    using(var content=new ContentManager(services,Path.Combine(gameDir,"Content")))using(var batch=new SpriteBatch(graphics))using(var target=new RenderTarget2D(graphics,320,180,false,SurfaceFormat.Color,DepthFormat.None)) {
     Texture2D item=content.Load<Texture2D>("Images/Item_1");
     using(var sheet=new System.Drawing.Bitmap(640,240))using(var painter=System.Drawing.Graphics.FromImage(sheet)) {
      painter.Clear(System.Drawing.Color.FromArgb(35,45,55));painter.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
      foreach(int part in new[]{0,1,2,3,4,5,6,7,8,9,10,11,12,13,15}) {
       Texture2D texture=content.Load<Texture2D>("Images/Player_0_"+part);string filename=Path.Combine(stateDir,"player-part-"+part+".png");using(var file=File.Create(filename))texture.SaveAsPng(file,texture.Width,texture.Height);
       using(var bitmap=new System.Drawing.Bitmap(filename)){int col=part%8,row=part/8;painter.DrawImage(bitmap,new System.Drawing.Rectangle(col*80,row*120,80,112),new System.Drawing.Rectangle(0,0,40,56),System.Drawing.GraphicsUnit.Pixel);painter.DrawString(part+" "+texture.Width+"x"+texture.Height,System.Drawing.SystemFonts.DefaultFont,System.Drawing.Brushes.White,new System.Drawing.PointF(col*80,row*120+108));}
      }
      sheet.Save(Path.Combine(stateDir,"native-player-parts.png"),System.Drawing.Imaging.ImageFormat.Png);
     }
     Console.WriteLine("NATIVE_TEXTURE Item_1 "+item.Width+"x"+item.Height+" format="+item.Format);
     using(var file=File.Create(Path.Combine(stateDir,"native-item-1.png")))item.SaveAsPng(file,item.Width,item.Height);
     graphics.SetRenderTarget(target);graphics.Clear(new Color(18,31,49));
     batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
     batch.Draw(item,new Rectangle(96,26,item.Width*4,item.Height*4),Color.White);batch.End();graphics.SetRenderTarget(null);
     using(var file=File.Create(Path.Combine(stateDir,"native-texture-render.png")))target.SaveAsPng(file,320,180);
     Color[] pixels=new Color[320*180];target.GetData(pixels);int changed=0;foreach(Color pixel in pixels)if(pixel.R!=18||pixel.G!=31||pixel.B!=49)changed++;
     Console.WriteLine("GPU_RENDER changedPixels="+changed+" adapter="+graphics.Adapter.Description+" profile="+graphics.GraphicsProfile);
     if(changed<16)throw new InvalidOperationException("No native texture pixels were rendered.");
     graphics.Clear(Color.Black);batch.Begin();batch.Draw(target,Vector2.Zero,Color.White);batch.End();graphics.Present();Console.WriteLine("GPU_PRESENT hidden-window-handle="+handle);
    }
   }
  }
 }
}

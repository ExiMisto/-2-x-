using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SpireTerraria.Next {
 // A deliberately small native combat adapter. It invokes the installed game's
 // simulation; it does not implement replacement damage, NPC AI or player physics.
 internal sealed class NativeArena : IDisposable {
  const int WorldWidth=512,WorldHeight=256,Floor=168;
  readonly int viewWidth,viewHeight;
  const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
  static bool tablesReady;
  readonly Assembly game;
  readonly Type main;
  readonly Dictionary<string,MemberInfo> members=new Dictionary<string,MemberInfo>();
  readonly Dictionary<string,MethodInfo> methods=new Dictionary<string,MethodInfo>();
  readonly Array tiles,players,npcs,projectiles;
  readonly object player,npc,triggers;
  readonly object sceneState,sceneMetrics;
  readonly NativeRenderer renderer;
  readonly FrameExchange exchange;
  readonly string frameSession;
  readonly BattleConfiguration configuration;
  readonly Random encounterRandom;
  readonly List<EncounterEnemy> encounter=new List<EncounterEnemy>();
  readonly List<object> extraAccessories=new List<object>();
  readonly List<string> spawnedSymbols=new List<string>();
  static NativeArena updatingArena;
  static MethodInfo advancedPlayerUpdate;
  readonly List<int> consumedSlots=new List<int>();
  int consumableSlot=-1,requestedConsumable=-1;byte previousConsumableInput;bool consumedCurrent;
  int remainingToSpawn,defeated,spawned,recovered,maximumConcurrent=1,selectedWeapon;
  int arenaLeftTile=176,arenaRightTile=336;
  string sceneKey="legacy",title="蓝史莱姆试炼";
  bool advanced;
  int tick;
  long frameId;
  bool disposed,faulted;
  internal NativeArena(TerrariaRuntime runtime,int seed,string weapon,string enemy):this(runtime,seed,weapon,enemy,null){}
  internal NativeArena(TerrariaRuntime runtime,BattleConfiguration configuration):this(runtime,configuration.Seed,configuration.Weapons[0],"BlueSlime",configuration){}
  NativeArena(TerrariaRuntime runtime,int seed,string weapon,string enemy,BattleConfiguration supplied) {
   // Reject unsupported content before touching the process-wide native state.
   if(supplied==null&&weapon!="EnchantedSword"&&weapon!="WoodenSword"&&weapon!="WaterBolt")throw new InvalidDataException("This milestone supports EnchantedSword, WoodenSword and WaterBolt.");
   if(enemy!="BlueSlime")throw new InvalidDataException("This milestone supports BlueSlime.");
   if(supplied!=null){supplied.Validate();using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))supplied.Write(writer);memory.Position=0;using(var reader=new BinaryReader(memory))configuration=BattleConfiguration.Read(reader);}advanced=true;}
   viewWidth=advanced&&configuration.BossSymbol=="MoonLordCore"?1280:640;viewHeight=viewWidth==1280?720:360;
   encounterRandom=new Random(seed);runtime.EnsureItemRuntime();game=runtime.NativeAssembly;main=game.GetType("Terraria.Main",true);
   if(advanced)ValidateSymbols();
   Set(main,"dedServ",true);Set(main,"netMode",0);Set(main,"myPlayer",0);
   Set(main,"rand",New("Terraria.Utilities.UnifiedRandom",seed));
   if(!tablesReady){Call(main,"Initialize_TileAndNPCData1");Call(main,"Initialize_TileAndNPCData2");}
   Set(main,"maxTilesX",WorldWidth);Set(main,"maxTilesY",WorldHeight);
   Set(main,"worldSurface",(double)(Floor-8));Set(main,"rockLayer",(double)(Floor+24));
   Set(main,"leftWorld",0f);Set(main,"rightWorld",(float)(WorldWidth*16));Set(main,"topWorld",0f);Set(main,"bottomWorld",(float)(WorldHeight*16));
   Set(main,"screenWidth",viewWidth);Set(main,"screenHeight",viewHeight);Set(main,"screenPosition",Vector2.Zero);
   // Combat rooms keep a stable night so nighttime enemies and bosses engage
   // normally instead of repeatedly entering native daytime despawn behaviour.
   Set(main,"dayTime",false);Set(main,"time",16200d);Set(main,"gameMenu",false);
   Type tileType=game.GetType("Terraria.Tile",true);tiles=Array.CreateInstance(tileType,WorldWidth,WorldHeight);Set(main,"tile",tiles);
   MethodInfo activate=tileType.GetMethod("active",new[]{typeof(bool)});
   for(int x=0;x<WorldWidth;x++)for(int y=0;y<WorldHeight;y++) {
    object tile=Activator.CreateInstance(tileType);
    if(y>=Floor){activate.Invoke(tile,new object[]{true});Set(tile,"type",(ushort)(y==Floor?2:0));Set(tile,"frameX",(short)18);Set(tile,"frameY",(short)(y==Floor?0:18));}
    tiles.SetValue(tile,x,y);
   }
   if(advanced)BuildScene();
   foreach(string name in new[]{"player","npc","projectile","item","dust","gore","combatText"})FillEntities(name);
   // 1.4.5 keeps pickup text on PopupText itself, outside Main's entity arrays.
   Array popups=(Array)Get(game.GetType("Terraria.PopupText",true),"popupText");for(int i=0;i<popups.Length;i++)popups.SetValue(New("Terraria.PopupText"),i);
   players=(Array)Get(main,"player");npcs=(Array)Get(main,"npc");projectiles=(Array)Get(main,"projectile");
   if(!tablesReady)InitializeTables();
   Call(Get(main,"GameViewMatrix"),"SetViewportOverride",new Viewport(0,0,viewWidth,viewHeight));
   Set(main,"rand",New("Terraria.Utilities.UnifiedRandom",seed));
   Set(main,"_gameUpdateCount",0u);Set(main,"UnpausedUpdateSeed",(ulong)(uint)seed);
   Set(main,"timeForVisualEffects",0d);Set(main,"GlobalTimeWrappedHourly",0f);
   player=players.GetValue(0);Set(player,"active",true);Set(player,"dead",false);
   Set(player,"position",new Vector2(256*16,Floor*16-42));Set(player,"velocity",Vector2.Zero);
   sceneState=New("Terraria.SceneState");sceneMetrics=New("Terraria.SceneMetrics");Set(main,"SceneState",sceneState);ResetMoonPresentation();
   int maximumLife=advanced?configuration.LifeMax:300;
   Set(player,"statLifeMax",maximumLife);Set(player,"statLifeMax2",maximumLife);Set(player,"statLife",advanced?configuration.Life:300);
   Set(player,"statManaMax",100);Set(player,"statManaMax2",100);Set(player,"statMana",100);
   Set(player,"direction",1);Set(player,"fallStart",Floor);
   object item=((Array)Get(player,"inventory")).GetValue(0);Call(item,"SetDefaults",Symbol("Terraria.ID.ItemID",weapon));
   if(advanced)EquipPlayer();
   SelectItem(0);
   Set(player,"lastVisualizedSelectedItem",Call(item,"Clone"));
   npc=npcs.GetValue(0);Call(npc,"SetDefaults",Symbol("Terraria.ID.NPCID",enemy));
   Set(npc,"active",true);Set(npc,"position",new Vector2(272*16,Floor*16-40));Set(npc,"target",0);
   if(advanced){Set(npc,"active",false);remainingToSpawn=configuration.RoomKind=="normal"?(configuration.EnemyCount==0?encounterRandom.Next(6,11):configuration.EnemyCount):1;maximumConcurrent=configuration.RoomKind=="normal"?(configuration.MaxConcurrent==0?encounterRandom.Next(3,5):configuration.MaxConcurrent):1;SpawnAvailable();}
   triggers=Get(Get(game.GetType("Terraria.GameInput.PlayerInput",true),"Triggers"),"Current");
   Call(Get(game.GetType("Terraria.GameInput.PlayerInput",true),"Triggers"),"Reset");
   frameSession=Guid.NewGuid().ToString("N");
   try {
    renderer=new NativeRenderer(game,runtime.GameDirectory,viewWidth,viewHeight);if(advanced)renderer.SetScene(sceneKey,arenaLeftTile*16,arenaRightTile*16,Floor*16);
    exchange=FrameExchange.CreateWriter(frameSession,viewWidth*viewHeight*4);
    Render();
   } catch {if(exchange!=null)exchange.Dispose();if(renderer!=null)renderer.Dispose();throw;}
  }
  void ValidateSymbols() {
   foreach(string symbol in configuration.Weapons)Symbol("Terraria.ID.ItemID",symbol);
   foreach(string symbol in configuration.Armor)if(symbol.Length>0)Symbol("Terraria.ID.ItemID",symbol);
   foreach(string symbol in configuration.Accessories)Symbol("Terraria.ID.ItemID",symbol);
   foreach(string symbol in configuration.Consumables)Symbol("Terraria.ID.ItemID",symbol);
   if(configuration.RoomKind!="normal")Symbol("Terraria.ID.NPCID",BossForStage());
   foreach(string affix in configuration.WeaponAffixes)if(affix!=""&&affix!="fierce"&&affix!="keen"&&affix!="rapid"&&affix!="efficient")throw new InvalidDataException("Unsupported weapon affix: "+affix);
   if(configuration.ArmorAffix!=""&&configuration.ArmorAffix!="warded"&&configuration.ArmorAffix!="nimble")throw new InvalidDataException("Unsupported armor affix.");
  }
  string BossForStage() {
   string expected=configuration.Stage==1?(configuration.RoomKind=="elite"?"KingSlime":"EyeofCthulhu"):configuration.Stage==2?(configuration.RoomKind=="elite"?"QueenSlimeBoss":"SkeletronPrime"):(configuration.RoomKind=="elite"?"Plantera":"DukeFishron");
   string symbol=String.IsNullOrEmpty(configuration.BossSymbol)?expected:configuration.BossSymbol;
   if(symbol!="KingSlime"&&symbol!="EyeofCthulhu"&&symbol!="QueenSlimeBoss"&&symbol!="SkeletronPrime"&&symbol!="Plantera"&&symbol!="DukeFishron"&&symbol!="MoonLordCore")throw new InvalidDataException("This native encounter adapter does not yet support boss "+symbol+".");return symbol;
  }
  void EquipPlayer() {
   Array inventory=(Array)Get(player,"inventory"),armor=(Array)Get(player,"armor");
   for(int i=0;i<configuration.Weapons.Length;i++) {
    object item=inventory.GetValue(i);Call(item,"SetDefaults",Symbol("Terraria.ID.ItemID",configuration.Weapons[i]));string affix=configuration.WeaponAffixes[i];
    int damage=(int)Get(item,"damage");Set(item,"damage",Scale(damage,1+configuration.WeaponLevels[i]*0.2+(affix=="fierce"?0.12:0)));
    if(affix=="keen")Set(item,"crit",Math.Min(100,(int)Get(item,"crit")+8));
    if(affix=="rapid"){Set(item,"useTime",Math.Max(1,Scale((int)Get(item,"useTime"),1/1.1)));Set(item,"useAnimation",Math.Max(1,Scale((int)Get(item,"useAnimation"),1/1.1)));}
    if(affix=="efficient")Set(item,"mana",Scale((int)Get(item,"mana"),0.85));
   }
   bool bonusPlaced=false;
   for(int i=0;i<configuration.Armor.Length;i++)if(configuration.Armor[i].Length>0){object item=armor.GetValue(i);Call(item,"SetDefaults",Symbol("Terraria.ID.ItemID",configuration.Armor[i]));int defense=Scale((int)Get(item,"defense"),1+configuration.ArmorLevel*0.2);if(!bonusPlaced&&configuration.ArmorAffix=="warded"){defense+=6;bonusPlaced=true;}Set(item,"defense",defense);}
   Set(player,"extraAccessory",true);Set(player,"extraAccessorySlots",2);
   for(int i=0;i<configuration.Accessories.Length;i++){object item=i<7?armor.GetValue(3+i):New("Terraria.Item");Call(item,"SetDefaults",Symbol("Terraria.ID.ItemID",configuration.Accessories[i]));if(!(bool)Get(item,"accessory"))throw new InvalidDataException("Configured item is not an accessory: "+configuration.Accessories[i]);if(i>=7)extraAccessories.Add(item);}
   // Resolve native ammo categories rather than assuming all ranged weapons use bullets.
   var ammoKinds=new HashSet<int>();int ammoSlot=54;
   string[] ammunition={"WoodenArrow","MusketBall","RocketI","SandBlock","FallenStar","PoisonDart","Stake","CandyCorn","Nail","StyngerBolt","Flare","JackOLantern","Gel","Seed","Snowball"};
   for(int i=0;i<configuration.Weapons.Length;i++){int needed=(int)Get(inventory.GetValue(i),"useAmmo");if(needed==0||!ammoKinds.Add(needed))continue;object item=inventory.GetValue(ammoSlot++);bool found=false;foreach(string symbol in ammunition){Call(item,"SetDefaults",Symbol("Terraria.ID.ItemID",symbol));if((int)Get(item,"ammo")==needed){Set(item,"stack",9999);found=true;break;}}if(!found)throw new InvalidDataException("No supported native ammunition for "+configuration.Weapons[i]+".");}
   Set(game.GetType("Terraria.GameContent.Events.DD2Event",true),"DownedInvasionT3",true);
   Set(player,"statManaMax",200);Set(player,"statManaMax2",200);Set(player,"statMana",200);
  }
  static int Scale(int basis,double multiplier){return Math.Max(0,Math.Min(1000000,(int)Math.Round(basis*multiplier,MidpointRounding.AwayFromZero)));}
  void BuildScene() {
   bool boss=configuration.RoomKind!="normal";int half=boss?88:44;arenaLeftTile=256-half;arenaRightTile=256+half;
   string requested=configuration.SceneKey,bossSymbol=boss?BossForStage():"";
   sceneKey=boss?"boss-"+bossSymbol:(requested=="mine"||requested=="corruption-altar"?requested:"forest-outpost");
   title=sceneKey=="forest-outpost"?"林间哨站":sceneKey=="mine"?"废弃矿井":sceneKey=="corruption-altar"?"腐化祭坛":bossSymbol=="KingSlime"?"史莱姆王 · 林间王庭":bossSymbol=="EyeofCthulhu"?"克苏鲁之眼 · 废弃观测台":bossSymbol=="QueenSlimeBoss"?"史莱姆皇后 · 水晶庭院":bossSymbol=="SkeletronPrime"?"机械骷髅王 · 废弃工厂":bossSymbol=="Plantera"?"世纪之花 · 丛林祭殿":bossSymbol=="MoonLordCore"?"月亮领主 · 群星观测台":"猪龙鱼公爵 · 风暴港口";
   int brick=Tile(sceneKey=="forest-outpost"||bossSymbol=="KingSlime"||bossSymbol=="DukeFishron"?"WoodBlock":sceneKey=="corruption-altar"?"Ebonstone":bossSymbol=="QueenSlimeBoss"?"PearlstoneBrick":bossSymbol=="Plantera"?"RichMahogany":"GrayBrick");
   for(int y=Floor-25;y<Floor;y++)for(int x=arenaLeftTile-2;x<arenaLeftTile;x++)PlaceTile(x,y,brick,0,0);
   for(int y=Floor-25;y<Floor;y++)for(int x=arenaRightTile;x<arenaRightTile+2;x++)PlaceTile(x,y,brick,0,0);
   if(sceneKey=="forest-outpost"||bossSymbol=="KingSlime") {
    TimberHouse(245,Floor,23,10,boss?"BorealWood":"Wood",boss?"BlueDynastyShingles":"RedDynastyShingles");
    TimberHouse(arenaLeftTile+5,Floor,14,8,"Wood","RedDynastyShingles");
    Fence(229,242,Floor-3,"WoodenFence");Fence(271,283,Floor-3,"WoodenFence");
    if(boss)for(int x=arenaLeftTile+9;x<arenaRightTile-8;x+=32){WallRect(x,Floor-14,3,14,"StoneSlab");Roof(x-2,Floor-15,7,"BlueDynastyShingles");}
   } else if(sceneKey=="mine") {
    for(int x=arenaLeftTile+7;x<arenaRightTile-4;x+=16){WallRect(x,Floor-15,2,15,"Wood");WallRect(x+2,Floor-15,12,2,"Wood");WallRect(x+2,Floor-13,2,2,"Wood");WallRect(x+12,Floor-13,2,2,"Wood");PlaceObject(x+7,Floor-13,1,2,"HangingLanterns");}
    WallRect(249,Floor-8,14,8,"GrayBrick");WallRect(252,Floor-7,8,6,"Glass");WallRect(249,Floor-2,14,2,"Wood");
   } else if(sceneKey=="corruption-altar") {
    RuinedColonnade("EbonstoneUnsafe","Ebonstone",12);WallRect(253,Floor-10,7,8,"PurpleStainedGlass");for(int x=251;x<263;x++)PlaceTile(x,Floor-1,brick,18,18);Roof(251,Floor-11,12,"Ebonstone");
   } else if(bossSymbol=="EyeofCthulhu") {
    WallRect(244,Floor-12,25,12,"GrayBrick");WallRect(247,Floor-10,19,9,"Glass");
    for(int x=246;x<267;x++){int dx=Math.Abs(x-256),ceiling=Floor-16+dx/2;for(int y=ceiling;y<Floor-11;y++)WallAt(x,y,"GrayBrick");if(dx<10)PlaceTile(x,ceiling,Tile("StoneSlab"),18,18);}
    WallRect(255,Floor-9,3,9,"GrayBrick");WallRect(247,Floor-5,19,1,"GrayBrick");RuinedColonnade("GrayBrick","StoneSlab",17);
   } else if(bossSymbol=="QueenSlimeBoss") {
    WallRect(246,Floor-10,21,10,"PearlstoneBrick");WallRect(249,Floor-9,15,7,"BlueStainedGlass");Roof(243,Floor-11,27,"CrystalBlock");
    for(int x=242;x<273;x+=10){WallRect(x,Floor-12,2,12,"Crystal");PlaceTile(x,Floor-13,Tile("CrystalBlock"),18,18);}
    Fence(226,242,Floor-3,"PearlwoodFence");Fence(272,288,Floor-3,"PearlwoodFence");
   } else if(bossSymbol=="SkeletronPrime") {
    WallRect(242,Floor-12,28,12,"CogWall");WallRect(245,Floor-10,22,6,"Glass");WallRect(247,Floor-16,3,5,"GrayBrick");WallRect(262,Floor-19,4,8,"GrayBrick");
    for(int x=242;x<271;x+=7)WallRect(x,Floor-12,1,12,"StoneSlab");WallRect(242,Floor-4,28,1,"StoneSlab");Roof(240,Floor-13,33,"MetalBars");
    for(int x=arenaLeftTile+12;x<arenaRightTile-10;x+=28){WallRect(x,Floor-15,2,15,"StoneSlab");WallRect(x,Floor-15,12,1,"CogWall");PlaceObject(x+6,Floor-14,1,2,"HangingLanterns");}
   } else if(bossSymbol=="Plantera") {
    RuinedColonnade("RichMaogany","RichMahogany",15);WallRect(246,Floor-11,21,11,"LivingWood");WallRect(250,Floor-9,13,7,"FlowerUnsafe");Roof(242,Floor-12,29,"RichMahogany");
    for(int x=244;x<272;x+=4){int length=2+(x%3);WallRect(x,Floor-12,1,length,"FlowerUnsafe");}
   } else if(bossSymbol=="MoonLordCore") {
    RuinedColonnade("GrayBrick","StoneSlab",24);
    WallRect(240,Floor-21,33,21,"GrayBrick");WallRect(243,Floor-19,27,17,"BlueStainedGlass");
    for(int x=243;x<271;x+=6)WallRect(x,Floor-19,1,17,"GrayBrick");
    WallRect(241,Floor-12,31,1,"GrayBrick");Roof(237,Floor-22,39,"StoneSlab");
    for(int x=arenaLeftTile+5;x<arenaRightTile-4;x+=26){WallRect(x,Floor-4,8,4,"GrayBrick");WallRect(x+1,Floor-5,6,1,"BlueStainedGlass");}
   } else if(bossSymbol=="DukeFishron") {
    for(int x=arenaLeftTile;x<arenaRightTile;x++)PlaceTile(x,Floor,Tile("WoodBlock"),18,0);
    Fence(arenaLeftTile+3,arenaRightTile-3,Floor-3,"WoodenFence");
    for(int mast=240;mast<=274;mast+=17){WallRect(mast,Floor-16,1,16,"Wood");WallRect(mast-6,Floor-15,13,1,"Wood");for(int y=Floor-14;y<Floor-5;y++)for(int x=mast+1;x<=mast+1+(Floor-5-y)/2;x++)WallAt(x,y,"Sail");}
    TimberHouse(arenaLeftTile+9,Floor,17,8,"Wood","BlueDynastyShingles");
   }
   // Staggered platforms are always four tiles apart vertically. The first can
   // be reached by an unequipped native player; no wings or hook are required.
   int platforms=Tile("Platforms");int rows=boss?3:2;
   for(int row=1;row<=rows;row++)for(int center=arenaLeftTile+8+(row%2)*8;center<arenaRightTile-5;center+=25)for(int x=center-3;x<center+5;x++)PlaceTile(x,Floor-row*4,platforms,18,0);
   if(boss&&bossSymbol!="DukeFishron")for(int x=arenaLeftTile;x<arenaRightTile;x++)for(int y=Floor;y<Floor+3;y++)PlaceTile(x,y,brick,18,18);
   Set(main,"hardMode",configuration.Stage>=2);Set(main,"dayTime",false);Set(main,"time",16200d);
  }
  int Tile(string name){return Symbol("Terraria.ID.TileID",name);}
  void WallAt(int x,int y,string name){if(x<0||y<0||x>=WorldWidth||y>=WorldHeight)return;Set(tiles.GetValue(x,y),"wall",(ushort)Symbol("Terraria.ID.WallID",name));}
  void WallRect(int x,int y,int width,int height,string name){for(int px=x;px<x+width;px++)for(int py=y;py<y+height;py++)WallAt(px,py,name);}
  void Fence(int left,int right,int y,string wall){WallRect(left,y,right-left,3,wall);}
  void Roof(int x,int y,int width,string tile){int half=width/2;for(int px=0;px<width;px++){int rise=Math.Min(px,width-1-px)/2;PlaceTile(x+px,y-rise,Tile(tile),18,18);}}
  void TimberHouse(int x,int bottom,int width,int height,string wall,string roof) {
   WallRect(x,bottom-height,width,height,wall);WallRect(x+1,bottom-height+1,width-2,height-2,wall);
   int windowWall=Symbol("Terraria.ID.WallID","Glass");for(int wx=x+3;wx<x+width-3;wx+=7)for(int dx=0;dx<4;dx++)for(int dy=0;dy<3;dy++)Set(tiles.GetValue(wx+dx,bottom-height+3+dy),"wall",(ushort)windowWall);
   WallRect(x,bottom-height,1,height,"BorealWood");WallRect(x+width-1,bottom-height,1,height,"BorealWood");WallRect(x,bottom-2,width,1,"BorealWood");
   Roof(x-2,bottom-height-1,width+4,roof);PlaceObject(x+width-4,bottom-2,2,2,"WorkBenches");PlaceObject(x+2,bottom-height+1,1,2,"HangingLanterns");
  }
  void RuinedColonnade(string wall,string tile,int height) {for(int x=arenaLeftTile+8;x<arenaRightTile-8;x+=18){int h=height-((x/18)%3)*3;WallRect(x,Floor-h,3,h,wall);WallRect(x-1,Floor-h-1,5,2,wall);if(x%3!=0)for(int dx=0;dx<7;dx++)PlaceTile(x+dx,Floor-h-2,Tile(tile),18,18);}}
  void PlaceObject(int x,int y,int width,int height,string symbol){for(int dx=0;dx<width;dx++)for(int dy=0;dy<height;dy++)PlaceTile(x+dx,y+dy,Tile(symbol),dx*18,dy*18);}
  void PlaceTile(int x,int y,int type,int frameX,int frameY){object tile=tiles.GetValue(x,y);Call(tile,"active",true);Set(tile,"type",(ushort)type);Set(tile,"frameX",(short)frameX);Set(tile,"frameY",(short)frameY);}
  string RandomEnemy() {
   string[] pool=configuration.Stage==1?new[]{"BlueSlime","Zombie","DemonEye","Skeleton","CaveBat"}:configuration.Stage==2?new[]{"PossessedArmor","ArmoredSkeleton","Wraith","SkeletonArcher"}:new[]{"PossessedArmor","ArmoredSkeleton","Wraith","ChaosElemental","SkeletonArcher"};
   return pool[encounterRandom.Next(pool.Length)];
  }
  void SpawnAvailable() {
   while(remainingToSpawn>0&&CountAlive()<maximumConcurrent) {
    int slot=-1;for(int i=0;i<npcs.Length-1;i++)if(!(bool)Get(npcs.GetValue(i),"active")&&!TrackedSlot(i)){slot=i;break;}if(slot<0)break;
    string symbol=configuration.RoomKind=="normal"?RandomEnemy():BossForStage();object entity=npcs.GetValue(slot);Call(entity,"SetDefaults",Symbol("Terraria.ID.NPCID",symbol));
    int side=spawned%2==0?1:-1;float px=((Vector2)Get(player,"position")).X+side*encounterRandom.Next(240,460);px=Math.Max((arenaLeftTile+5)*16,Math.Min((arenaRightTile-6)*16,px));
    Set(entity,"position",new Vector2(px,Floor*16-(int)Get(entity,"height")-4));Set(entity,"active",true);Set(entity,"target",0);Set(entity,"timeLeft",1800);
    encounter.Add(new EncounterEnemy{Slot=slot,Symbol=symbol,Life=(int)Get(entity,"life"),LastPosition=(Vector2)Get(entity,"position")});spawnedSymbols.Add(symbol);remainingToSpawn--;spawned++;
   }
  }
  bool TrackedSlot(int slot){foreach(EncounterEnemy enemy in encounter)if(!enemy.Defeated&&enemy.Slot==slot)return true;return false;}
  int CountAlive(){int count=0;foreach(EncounterEnemy enemy in encounter)if(!enemy.Defeated)count++;return count;}
  void WatchEnemies() {
   foreach(EncounterEnemy tracked in encounter) {
    if(tracked.Defeated)continue;object entity=npcs.GetValue(tracked.Slot);int life=(int)Get(entity,"life");
    if(life<=0){tracked.Defeated=true;defeated++;continue;}
    bool active=(bool)Get(entity,"active");Vector2 position=(Vector2)Get(entity,"position");
    // Keep live AI and health when native offscreen culling deactivates a target.
    // Resetting SetDefaults used to erase its state and teleport it beside the
    // player. Only non-finite/world-loss recovery now relocates an entity.
    if(!active){Set(entity,"active",true);Set(entity,"target",0);recovered++;}
    Vector2 velocity=(Vector2)Get(entity,"velocity");bool changed=false;
    if(!Finite(position.X)||!Finite(position.Y)){position=tracked.LastPosition;velocity=Vector2.Zero;changed=true;recovered++;}
    bool ordinary=configuration.RoomKind=="normal";
    float left=(ordinary?arenaLeftTile-5:Math.Max(2,arenaLeftTile-60))*16,right=(ordinary?arenaRightTile+5:Math.Min(WorldWidth-3,arenaRightTile+60))*16-(int)Get(entity,"width");
    if(position.X<left){position.X=left;velocity.X=Math.Max(2,Math.Abs(velocity.X));changed=true;}
    if(position.X>right){position.X=right;velocity.X=-Math.Max(2,Math.Abs(velocity.X));changed=true;}
    bool flying=(bool)Get(entity,"noTileCollide")||(bool)Get(entity,"noGravity");
    float bottom=(ordinary?(flying?Floor+18:Floor+3):Math.Min(WorldHeight-4,Floor+60))*16;
    if(position.Y>bottom){position.Y=flying?bottom:Floor*16-(int)Get(entity,"height");velocity.Y=-Math.Max(2,Math.Abs(velocity.Y));changed=true;recovered++;}
    if(position.Y<32){position.Y=32;velocity.Y=Math.Max(2,Math.Abs(velocity.Y));changed=true;}
    if(changed){Set(entity,"position",position);Set(entity,"velocity",velocity);}
    Set(entity,"timeLeft",1800);tracked.Life=(int)Get(entity,"life");tracked.LastPosition=(Vector2)Get(entity,"position");
   }
   if(CountAlive()==0&&remainingToSpawn>0)SpawnAvailable();
  }
  sealed class EncounterEnemy {internal int Slot,Life;internal string Symbol;internal bool Defeated;internal Vector2 LastPosition;}
  void InitializeTables() {
   Call(main,"Initialize_Items");Call(game.GetType("Terraria.Lang",true),"InitializeLegacyLocalization");
   Call(game.GetType("Terraria.ID.ContentSamples",true),"Initialize");
   // Do not construct Main/Game: that would start configuration, graphics and
   // platform services owned by the standalone game. Supply only native services
   // actually required by Player.Update and NPC death in our isolated process.
   object shell=FormatterServices.GetUninitializedObject(main);GC.SuppressFinalize(shell);Set(main,"instance",shell);
   Set(shell,"_achievements",New("Terraria.Achievements.AchievementManager"));
   Call(game.GetType("Terraria.Initializers.AchievementInitializer",true),"Load");
   var view=new GraphicsDevicePlaceholder(game);Set(main,"GameViewMatrix",view.Value);
   object bestiary=New("Terraria.GameContent.Bestiary.BestiaryDatabase");
   Call(New("Terraria.GameContent.Bestiary.BestiaryDatabaseNPCsPopulator"),"Populate",bestiary);Set(main,"BestiaryDB",bestiary);
   Set(main,"BestiaryTracker",New("Terraria.GameContent.Bestiary.BestiaryUnlocksTracker"));
   object drops=New("Terraria.GameContent.ItemDropRules.ItemDropDatabase");Call(drops,"Populate");Set(main,"ItemDropsDB",drops);
   Set(main,"ItemDropSolver",New("Terraria.GameContent.ItemDropRules.ItemDropResolver",drops));
   // Native headless death notifications iterate client slots. Empty, normally
   // constructed clients have no sockets and IsConnected() returns false. Do not
   // start Netplay or any listener in this local combat worker.
   Array clients=(Array)Get(game.GetType("Terraria.Netplay",true),"Clients");
   for(int i=0;i<clients.Length;i++)clients.SetValue(New("Terraria.RemoteClient"),i);
   Call(game.GetType("Terraria.GameInput.PlayerInput",true),"Initialize");
   Call(game.GetType("Terraria.Initializers.WingStatsInitializer",true),"Load");
   Call(game.GetType("Terraria.GameContent.Creative.CreativePowerManager",true),"Initialize");
   Call(game.GetType("Terraria.DataStructures.ArmorSetBonuses",true),"Initialize");Call(game.GetType("Terraria.DataStructures.ArmorSetBonuses",true),"BuildLookup");
   Call(game.GetType("Terraria.ObjectData.TileObjectData",true),"Initialize");Call(game.GetType("Terraria.Framing",true),"Initialize");
   Call(game.GetType("Terraria.Lighting",true),"Initialize");Call(game.GetType("Terraria.ID.TorchID",true),"Initialize");tablesReady=true;
  }
  // Native simulation needs camera dimensions for aiming even without Main.Draw.
  sealed class GraphicsDevicePlaceholder {
   internal readonly object Value;
   internal GraphicsDevicePlaceholder(Assembly game) {
    Type type=game.GetType("Terraria.Graphics.SpriteViewMatrix",true);
    Value=Activator.CreateInstance(type,new object[]{null});
    type.GetMethod("SetViewportOverride").Invoke(Value,new object[]{new Viewport(0,0,640,360)});
   }
  }
  internal void WriteStart(BinaryWriter writer) {writer.Write(frameSession);writer.Write(viewWidth);writer.Write(viewHeight);WriteState(writer);}
  internal void WriteAdventureStart(BinaryWriter writer){writer.Write(frameSession);writer.Write(viewWidth);writer.Write(viewHeight);WriteAdvancedState(writer);}
  internal void WriteItemIcons(BinaryReader reader,BinaryWriter writer){int count=reader.ReadInt32();if(count<1||count>12)throw new InvalidDataException("Expected one to twelve item icons.");string[] symbols=new string[count];int[] ids=new int[count];for(int i=0;i<count;i++){symbols[i]=reader.ReadString();if(symbols[i].Length>128)throw new InvalidDataException("Oversized item symbol.");ids[i]=Symbol("Terraria.ID.ItemID",symbols[i]);}Protocol.RequireEnd(reader);writer.Write(count);for(int i=0;i<count;i++){writer.Write(symbols[i]);renderer.WriteItemIcon(ids[i],writer);}}
  internal void AdventureStep(int count,byte input,float aimX,float aimY,int slot,int consumable=-1) {
   if(!advanced)throw new InvalidOperationException("Start an adventure encounter first.");
   if(slot<0||slot>=configuration.Weapons.Length)throw new InvalidDataException("Selected weapon slot is unavailable.");
   if(consumable< -1||consumable>=configuration.Consumables.Length)throw new InvalidDataException("Selected consumable is unavailable.");requestedConsumable=consumable;
   if(selectedWeapon!=slot){if(consumableSlot<0){Set(player,"itemAnimation",0);Set(player,"itemTime",0);SelectItem(slot);}selectedWeapon=slot;}
   Step(count,input,aimX,aimY);
  }
  void SelectItem(int slot){object state=Get(player,"selectedItemState");Call(state,"Select",slot);Call(state,"Update");Set(player,"selectedItemState",state);}
  bool BeginConsumable(byte input) {
   int requested=requestedConsumable;requestedConsumable=-1;
   byte pressed=(byte)(input&~previousConsumableInput&48);previousConsumableInput=(byte)(input&48);
   if(requested>=0)pressed=(byte)(configuration.Consumables[requested].Contains("Potion")?32:16);
   if(consumableSlot>=0||pressed==0)return false;
   Array inventory=(Array)Get(player,"inventory");object item=inventory.GetValue(3);
   for(int index=0;index<configuration.Consumables.Length;index++) {
    if(consumedSlots.Contains(index)||(requested>=0&&requested!=index))continue;Call(item,"SetDefaults",Symbol("Terraria.ID.ItemID",configuration.Consumables[index]));
    bool thrown=(int)Get(item,"shoot")>0&&(int)Get(item,"damage")>0&&(int)Get(item,"healLife")==0;
    if((pressed&16)!=0&&!thrown||(pressed&32)!=0&&thrown)continue;
    if((int)Get(item,"healLife")>0&&((int)Get(player,"potionDelay")>0||(int)Get(player,"statLife")>=(int)Get(player,"statLifeMax2")))continue;
    if((int)Get(item,"healMana")>0&&(int)Get(player,"statMana")>=(int)Get(player,"statManaMax2"))continue;
    Set(item,"stack",1);Set(player,"itemAnimation",0);Set(player,"itemTime",0);Set(player,"releaseUseItem",true);SelectItem(3);consumableSlot=index;consumedCurrent=false;return true;
   }
   Call(item,"TurnToAir");return false;
  }
  void TrackConsumable() {
   if(consumableSlot<0)return;object item=((Array)Get(player,"inventory")).GetValue(3);
   if(!consumedCurrent&&((int)Get(item,"stack")<=0||(int)Get(item,"type")==0)){consumedSlots.Add(consumableSlot);consumedCurrent=true;}
   if((int)Get(player,"itemAnimation")<=0){consumableSlot=-1;SelectItem(selectedWeapon);}
  }
  internal void Step(int count,byte input,float aimX,float aimY) {
   if(disposed)throw new ObjectDisposedException("NativeArena");if(faulted)throw new InvalidOperationException("Native simulation failed; end this arena and start a new one.");
   if(count<1||count>120||(input&~(advanced?127:15))!=0||!Finite(aimX)||!Finite(aimY)||Math.Abs(aimX)>WorldWidth*16||Math.Abs(aimY)>WorldHeight*16)throw new InvalidDataException("Invalid battle input.");
   try {
    for(int step=0;step<count;step++) {
     if((bool)Get(player,"dead")||(advanced?CountAlive()==0&&remainingToSpawn==0:(int)Get(npc,"life")<=0))break;
     bool consume=advanced&&BeginConsumable(input);bool useItem=consume||(consumableSlot<0&&(input&8)!=0);
     Set(triggers,"Left",(input&1)!=0);Set(triggers,"Right",(input&2)!=0);Set(triggers,"Jump",(input&4)!=0);Set(triggers,"MouseLeft",useItem);
     Set(triggers,"Down",(input&64)!=0);Set(player,"controlDown",(input&64)!=0);
     Set(player,"controlLeft",(input&1)!=0);Set(player,"controlRight",(input&2)!=0);Set(player,"controlJump",(input&4)!=0);Set(player,"controlUseItem",useItem);
     Vector2 camera=Camera();Set(main,"screenPosition",camera);Set(main,"mouseX",(int)(aimX-camera.X));Set(main,"mouseY",(int)(aimY-camera.Y));
     Set(main,"mouseLeft",useItem);Set(main,"mouseLeftRelease",!useItem);
     // We own the fixed 60 Hz scheduler, so advance the native clocks that would
     // otherwise be maintained by Main's standalone game loop.
     Set(main,"_gameUpdateCount",(uint)(tick+1));
     Set(main,"timeForVisualEffects",(double)((tick+1)%216000));Set(main,"GlobalTimeWrappedHourly",(float)(((tick+1)/60d)%3600d));
     Set(main,"UnpausedUpdateSeed",Call(game.GetType("Terraria.Utils",true),"RandomNextSeed",(ulong)Get(main,"UnpausedUpdateSeed")));
     if(advanced){if(advancedPlayerUpdate==null)advancedPlayerUpdate=NativeUpdateAdapter.Clone(game.GetType("Terraria.Player",true).GetMethod("Update",new[]{typeof(int)}),typeof(NativeArena).GetMethod("ApplyExtendedEquipment",BindingFlags.NonPublic|BindingFlags.Static));updatingArena=this;try{advancedPlayerUpdate.Invoke(null,new[]{player,(object)0});}catch(TargetInvocationException e){throw new InvalidOperationException("Native extended Player.Update failed: "+e.InnerException,e.InnerException);}finally{updatingArena=null;}}
     else Call(player,"Update",0);
     renderer.UpdatePlayerPresentation(player);
     if(advanced) {
      TrackConsumable();
      Vector2 position=(Vector2)Get(player,"position");position.X=Math.Max((arenaLeftTile+2)*16,Math.Min((arenaRightTile-3)*16,position.X));if(position.Y>(Floor+3)*16){position.Y=Floor*16-42;Set(player,"velocity",Vector2.Zero);Set(player,"fallStart",Floor);}Set(player,"position",position);
      if(tick%6==0)Set(player,"statMana",Math.Min((int)Get(player,"statManaMax2"),(int)Get(player,"statMana")+1));
      Array inventory=(Array)Get(player,"inventory");for(int ammo=54;ammo<58;ammo++)Set(inventory.GetValue(ammo),"stack",9999);
     }
     for(int i=0;i<npcs.Length;i++){object entity=npcs.GetValue(i);if((bool)Get(entity,"active"))Call(entity,"UpdateNPC",i);}
     for(int i=0;i<projectiles.Length;i++){object entity=projectiles.GetValue(i);if((bool)Get(entity,"active"))Call(entity,"Update",i);}
     Call(game.GetType("Terraria.Dust",true),"UpdateDust");
     if(advanced&&configuration.BossSymbol=="MoonLordCore"){Set(sceneMetrics,"Center",(Vector2)Get(player,"Center"));Call(game.GetType("Terraria.GameContent.Events.MoonlordDeathDrama",true),"Update",sceneState,sceneMetrics);}
     Call(game.GetType("Terraria.CombatText",true),"UpdateCombatText");
     Call(game.GetType("Terraria.PopupText",true),"UpdateItemText");
     if(advanced)WatchEnemies();
     tick++;
    }
    Render();
   } catch {faulted=true;throw;}
  }
  static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
  void ResetMoonPresentation(){Type drama=game.GetType("Terraria.GameContent.Events.MoonlordDeathDrama",true);foreach(string name in new[]{"_pieces","_explosions","_lightSources"})Call(Get(drama,name),"Clear");Set(drama,"whitening",0f);Set(drama,"requestedLight",0f);}
  static void ApplyExtendedEquipment(object nativePlayer,int index) {
   NativeArena arena=updatingArena;if(arena==null)throw new InvalidOperationException("No active equipment update.");
   foreach(object accessory in arena.extraAccessories){arena.Call(nativePlayer,"GrantPrefixBenefits",accessory);arena.Call(nativePlayer,"GrantArmorBenefits",accessory);arena.Call(nativePlayer,"ApplyEquipFunctional",3,accessory);}
   object previous=arena.Get(arena.main,"ActiveWorldFileData"),difficulty=arena.Get(arena.main,"_gameModeDifficultyOverride");
   if(previous==null)arena.Set(arena.main,"ActiveWorldFileData",arena.New("Terraria.IO.WorldFileData","SpireNext-local",false));
   try{arena.Set(arena.main,"_gameModeDifficultyOverride",(float?)3f);arena.Call(nativePlayer,"UpdateEquips",index);}finally{arena.Set(arena.main,"_gameModeDifficultyOverride",difficulty);arena.Set(arena.main,"ActiveWorldFileData",previous);}
   if(arena.configuration.ArmorAffix=="nimble")arena.Set(nativePlayer,"moveSpeed",(float)arena.Get(nativePlayer,"moveSpeed")+0.10f);
  }
  Vector2 Camera(){Vector2 p=(Vector2)Get(player,"position");return new Vector2(advanced?Math.Max(arenaLeftTile*16,Math.Min(arenaRightTile*16-viewWidth,p.X-viewWidth/2)):Math.Max(0,Math.Min(WorldWidth*16-viewWidth,p.X-viewWidth/2)),Floor*16-(viewHeight==720?590:260));}
  void Render(){Vector2 camera=Camera();byte[] bgra=renderer.RenderScene(tiles,player,npcs,projectiles,camera.X,camera.Y);frameId=exchange.WriteBgra32(viewWidth,viewHeight,bgra);}
  internal void WriteState(BinaryWriter writer) {
   Vector2 p=(Vector2)Get(player,"position"),n=(Vector2)Get(npc,"position");int active=0;
   foreach(object projectile in projectiles)if((bool)Get(projectile,"active"))active++;
   writer.Write(tick);writer.Write((int)Get(player,"statLife"));writer.Write((int)Get(player,"statLifeMax2"));writer.Write(p.X);writer.Write(p.Y);writer.Write((bool)Get(player,"dead"));
   writer.Write((int)Get(npc,"life"));writer.Write((int)Get(npc,"lifeMax"));writer.Write(n.X);writer.Write(n.Y);writer.Write((bool)Get(npc,"active"));writer.Write(active);writer.Write(frameId);
  }
  internal void WriteAdvancedState(BinaryWriter writer) {
   Vector2 p=(Vector2)Get(player,"position"),camera=Camera();int activeProjectiles=0,enemyLife=0,enemyMax=0;Vector2 target=p;bool hasTarget=false;
   foreach(object projectile in projectiles)if((bool)Get(projectile,"active"))activeProjectiles++;
   foreach(EncounterEnemy tracked in encounter)if(!tracked.Defeated){object entity=npcs.GetValue(tracked.Slot);enemyLife+=Math.Max(0,(int)Get(entity,"life"));enemyMax+=(int)Get(entity,"lifeMax");if(!hasTarget){target=(Vector2)Get(entity,"position");hasTarget=true;}}
   if(advanced&&configuration.BossSymbol=="MoonLordCore"&&CountAlive()>0) {
    // The core is shielded until both hands and the head lose their eyes. Expose
    // the real vulnerable part to the HUD/aim helper while tracking one encounter.
    int total=0,maximum=0;float nearest=Single.MaxValue;bool vulnerable=false;
    foreach(object part in npcs)if((bool)Get(part,"active")) {
     int type=(int)Get(part,"type");if(type<396||type>398)continue;
     int life=(int)Get(part,"life");float phase=((float[])Get(part,"ai"))[0];if(type==397&&phase== -2||type==396&&phase== -3||type==398&&phase==2)life=0;total+=Math.Max(0,life);maximum+=(int)Get(part,"lifeMax");
     if(!(bool)Get(part,"dontTakeDamage")&&life>1){Vector2 center=(Vector2)Get(part,"Center");float distance=Vector2.DistanceSquared(center,p);if(distance<nearest){target=center;nearest=distance;vulnerable=true;}}
    }
    if(maximum>0){enemyLife=total;enemyMax=maximum;}
    if(!vulnerable)foreach(object part in npcs)if((bool)Get(part,"active")&&(int)Get(part,"type")==396){target=(Vector2)Get(part,"Center");break;}
   }
   var state=new AdvancedBattleState{Tick=tick,Life=(int)Get(player,"statLife"),LifeMax=(int)Get(player,"statLifeMax2"),PlayerX=p.X,PlayerY=p.Y,Dead=(bool)Get(player,"dead"),EnemyLife=enemyLife,EnemyLifeMax=enemyMax,EnemyX=target.X,EnemyY=target.Y,EnemyActive=CountAlive()>0||remainingToSpawn>0,Projectiles=activeProjectiles,Frame=frameId,Alive=CountAlive(),Remaining=CountAlive()+remainingToSpawn,Waves=(int)Math.Ceiling((spawned+remainingToSpawn)/(double)maximumConcurrent),Wave=spawned==0?0:(int)Math.Ceiling(spawned/(double)maximumConcurrent),Recovered=recovered,CameraX=camera.X,CameraY=camera.Y,ArenaLeft=arenaLeftTile*16,ArenaRight=arenaRightTile*16,FloorY=Floor*16,SelectedWeapon=selectedWeapon,Title=title,ConsumedSlots=consumedSlots.ToArray(),Mana=(int)Get(player,"statMana"),MaxMana=(int)Get(player,"statManaMax2"),PotionDelay=(int)Get(player,"potionDelay"),ConsumingSlot=consumableSlot};state.Write(writer);
  }
  internal void WriteDiagnostics(BinaryWriter writer) {
   var values=new List<KeyValuePair<string,string>>();Action<string,object> add=delegate(string name,object value){values.Add(new KeyValuePair<string,string>(name,Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)));};
   add("mode",advanced?"adventure":"legacy");add("scene",sceneKey);add("spawned",spawned);add("defeated",defeated);add("remaining",remainingToSpawn+CountAlive());add("recovered",recovered);add("enemies",String.Join(",",spawnedSymbols.ToArray()));add("selectedWeapon",selectedWeapon);add("nativeSelectedWeapon",Get(player,"selectedItem"));add("accessories",advanced?configuration.Accessories.Length:0);add("nativeAccessorySlots",Math.Min(7,advanced?configuration.Accessories.Length:0));add("extraAccessoryTiming",extraAccessories.Count==0?"none":"inside-player-update-before-native-UpdateEquips");
   Array inventory=(Array)Get(player,"inventory"),armor=(Array)Get(player,"armor");int weapons=advanced?configuration.Weapons.Length:1;
   for(int i=0;i<weapons;i++){object item=inventory.GetValue(i);foreach(string field in new[]{"type","damage","crit","useTime","useAnimation","mana"})add("weapon"+i+"."+field,Get(item,field));}
   for(int i=0;i<3;i++)add("armor"+i+".defense",Get(armor.GetValue(i),"defense"));add("playerDefense",Get(player,"statDefense"));add("mana",Get(player,"statMana"));
   int groundItems=0;foreach(object item in (Array)Get(main,"item"))if((bool)Get(item,"active"))groundItems++;add("groundItems",groundItems);
   add("combatText.visible",renderer.CombatTextDrawCount);add("combatText.peak",renderer.CombatTextPeak);add("combatText.drawTotal",renderer.CombatTextDrawTotal);
   writer.Write(values.Count);foreach(var pair in values){writer.Write(pair.Key);writer.Write(pair.Value);}
  }
  int Symbol(string type,string name){FieldInfo field=game.GetType(type,true).GetField(name,BindingFlags.Public|BindingFlags.Static);if(field==null||!field.IsLiteral)throw new NotSupportedException("Native symbol missing: "+name);return Convert.ToInt32(field.GetRawConstantValue());}
  object New(string type,params object[] args){return Activator.CreateInstance(game.GetType(type,true),args);}
  void FillEntities(string name){Array values=(Array)Get(main,name);Type type=values.GetType().GetElementType();for(int i=0;i<values.Length;i++){object value=Activator.CreateInstance(type);values.SetValue(value,i);FieldInfo who=type.GetField("whoAmI",All);if(who!=null)who.SetValue(value,i);}}
  MemberInfo Member(object target,string name) {
   Type type=target as Type??target.GetType();string key=type.FullName+":"+name;MemberInfo member;
   if(!members.TryGetValue(key,out member)){member=(MemberInfo)type.GetField(name,All)??type.GetProperty(name,All);if(member==null)throw new NotSupportedException("Native member missing: "+key);members.Add(key,member);}return member;
  }
  object Get(object target,string name){MemberInfo m=Member(target,name);FieldInfo field=m as FieldInfo;return field!=null?field.GetValue(target is Type?null:target):((PropertyInfo)m).GetValue(target is Type?null:target,null);}
  void Set(object target,string name,object value){MemberInfo m=Member(target,name);FieldInfo field=m as FieldInfo;if(field!=null)field.SetValue(target is Type?null:target,value);else ((PropertyInfo)m).SetValue(target is Type?null:target,value,null);}
  object Call(object target,string name,params object[] arguments) {
   Type type=target as Type??target.GetType();string key=type.FullName+":"+name;foreach(object argument in arguments)key+=":"+(argument==null?"null":argument.GetType().FullName);
   MethodInfo method;
   if(!methods.TryGetValue(key,out method)) {
    foreach(MethodInfo candidate in type.GetMethods(All)) {
     if(candidate.Name!=name)continue;ParameterInfo[] parameters=candidate.GetParameters();if(parameters.Length<arguments.Length)continue;bool match=true;
     for(int i=0;i<arguments.Length;i++)if(arguments[i]!=null&&!parameters[i].ParameterType.IsInstanceOfType(arguments[i]))match=false;
     for(int i=arguments.Length;i<parameters.Length;i++)if(!parameters[i].IsOptional)match=false;
     if(match){method=candidate;break;}
    }
    if(method==null)throw new NotSupportedException("Native method unavailable: "+key);methods.Add(key,method);
   }
   ParameterInfo[] spec=method.GetParameters();object[] complete=new object[spec.Length];Array.Copy(arguments,complete,arguments.Length);for(int i=arguments.Length;i<spec.Length;i++)complete[i]=spec[i].DefaultValue;
   try{return method.Invoke(target is Type?null:target,complete);}catch(TargetInvocationException e){throw new InvalidOperationException("Native "+type.Name+"."+name+" failed: "+e.InnerException,e.InnerException);}
  }
  public void Dispose(){if(disposed)return;disposed=true;try{foreach(string name in new[]{"item","projectile","npc","dust","gore","combatText"})foreach(object entity in (Array)Get(main,name))if(entity!=null){if(name=="item")Call(entity,"TurnToAir");else Set(entity,"active",false);}exchange.Dispose();}finally{renderer.Dispose();}}
 }
 // Clone only in memory, preserving the installed method's control flow and exception
 // regions. One native equipment call is redirected; no Terraria file is modified.
 internal static class NativeUpdateAdapter {
  static readonly Dictionary<short,OpCode> codes=new Dictionary<short,OpCode>();
  static NativeUpdateAdapter(){foreach(FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static))if(field.FieldType==typeof(OpCode)){OpCode op=(OpCode)field.GetValue(null);codes[op.Value]=op;}}
  internal static MethodInfo Clone(MethodInfo original,MethodInfo equipmentHook) {
   MethodBody body=original.GetMethodBody();byte[] code=body.GetILAsByteArray();Module source=original.Module;
   var clone=new DynamicMethod("SpireNextNativePlayerUpdate",typeof(void),new[]{original.DeclaringType,typeof(int)},typeof(NativeUpdateAdapter).Module,true);clone.InitLocals=body.InitLocals;DynamicILInfo dynamic=clone.GetDynamicILInfo();
   var localSignature=SignatureHelper.GetLocalVarSigHelper();foreach(LocalVariableInfo local in body.LocalVariables)localSignature.AddArgument(local.LocalType,local.IsPinned);dynamic.SetLocalSignature(localSignature.GetSignature());
   for(int p=0;p<code.Length;){int position=p;short value=code[p++];if(value==0xfe)value=(short)(0xfe00|code[p++]);OpCode op=codes[value];int size=0;switch(op.OperandType){case OperandType.InlineNone:break;case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:size=1;break;case OperandType.InlineVar:size=2;break;case OperandType.InlineI8:case OperandType.InlineR:size=8;break;case OperandType.InlineSwitch:size=4+BitConverter.ToInt32(code,p)*4;break;default:size=4;break;}
    if(op.OperandType==OperandType.InlineField||op.OperandType==OperandType.InlineMethod||op.OperandType==OperandType.InlineType||op.OperandType==OperandType.InlineTok||op.OperandType==OperandType.InlineString||op.OperandType==OperandType.InlineSig){int token=BitConverter.ToInt32(code,p),replacement;if(op.OperandType==OperandType.InlineString)replacement=dynamic.GetTokenFor(source.ResolveString(token));else if(op.OperandType==OperandType.InlineSig)throw new NotSupportedException("Unsupported native calli signature at "+position);else{MemberInfo member=source.ResolveMember(token);MethodBase method=member as MethodBase;FieldInfo field=member as FieldInfo;Type type=member as Type;if(method!=null){if(method.Name=="UpdateEquips"&&method.DeclaringType==original.DeclaringType)method=equipmentHook;replacement=dynamic.GetTokenFor(method.MethodHandle,method.DeclaringType.TypeHandle);}else if(field!=null)replacement=dynamic.GetTokenFor(field.FieldHandle,field.DeclaringType.TypeHandle);else if(type!=null)replacement=dynamic.GetTokenFor(type.TypeHandle);else throw new NotSupportedException("Unsupported native token.");}Array.Copy(BitConverter.GetBytes(replacement),0,code,p,4);}p+=size;}
   if(body.ExceptionHandlingClauses.Count>0){using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){int length=4+24*body.ExceptionHandlingClauses.Count;writer.Write((byte)0x41);writer.Write((byte)length);writer.Write((byte)(length>>8));writer.Write((byte)(length>>16));foreach(ExceptionHandlingClause clause in body.ExceptionHandlingClauses){writer.Write((int)clause.Flags);writer.Write(clause.TryOffset);writer.Write(clause.TryLength);writer.Write(clause.HandlerOffset);writer.Write(clause.HandlerLength);writer.Write(clause.Flags==ExceptionHandlingClauseOptions.Clause?dynamic.GetTokenFor(clause.CatchType.TypeHandle):clause.Flags==ExceptionHandlingClauseOptions.Filter?clause.FilterOffset:0);}dynamic.SetExceptions(memory.ToArray());}}
   dynamic.SetCode(code,body.MaxStackSize);return clone;
  }
 }
}

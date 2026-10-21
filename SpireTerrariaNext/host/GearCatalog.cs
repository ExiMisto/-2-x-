using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireTerraria.Next.Host {
 public enum HeroClass { Warrior, Ranger, Mage, Summoner }
 public enum GearKind { Weapon, Armor, Accessory, Consumable }
 public enum GearRarity { Common, Uncommon, Rare }
 public enum RewardKind { Normal, Elite, Boss, Treasure, Event }
 public sealed class GearDefinition {
  public string Key {get;set;}="";
  public string Name {get;set;}="";
  public HeroClass? Hero {get;set;}
  public HeroClass[] CompatibleHeroes {get;set;}=Array.Empty<HeroClass>();
  public int Stage {get;set;}=1;
  public GearKind Kind {get;set;}
  public GearRarity Rarity {get;set;}
  public string[] NativeSymbols {get;set;}=Array.Empty<string>();
  public string Description {get;set;}="";
  public bool IsStageEntry {get;set;}
 }
 public sealed class AffixDefinition {
  public string Key {get;set;}="";
  public string Name {get;set;}="";
  public string Description {get;set;}="";
  public GearKind Kind {get;set;}
 }
 public static class GearCatalog {
  static readonly List<GearDefinition> entries=new List<GearDefinition>();
  static readonly Dictionary<string,GearDefinition> byKey=new Dictionary<string,GearDefinition>(StringComparer.Ordinal);
  public static IReadOnlyList<GearDefinition> All => entries;
  public static IReadOnlyList<AffixDefinition> Affixes {get;}=new [] {
   new AffixDefinition{Key="fierce",Name="凶猛",Kind=GearKind.Weapon,Description="伤害 +12%"},
   new AffixDefinition{Key="keen",Name="锐利",Kind=GearKind.Weapon,Description="暴击率 +8 个百分点"},
   new AffixDefinition{Key="rapid",Name="迅捷",Kind=GearKind.Weapon,Description="攻击速度 +10%"},
   new AffixDefinition{Key="efficient",Name="节能",Kind=GearKind.Weapon,Description="魔力消耗 -15%（法术武器）"},
   new AffixDefinition{Key="warded",Name="守护",Kind=GearKind.Armor,Description="防御 +6"},
   new AffixDefinition{Key="nimble",Name="轻盈",Kind=GearKind.Armor,Description="移动速度 +10%"}
  };
  static GearCatalog() {
   Weapons(HeroClass.Warrior,1,"WoodenSword|木剑|0|1;CopperBroadsword|铜阔剑|0|1;SilverBroadsword|银阔剑|0|1;GoldBroadsword|金阔剑|0|0;Spear|长矛|0|0;WoodenBoomerang|木回旋镖|0|0;EnchantedBoomerang|附魔回旋镖|1|0;EnchantedSword|附魔剑|1|0;IceBlade|冰雪刃|1|0;Starfury|星怒|1|0;BladeofGrass|草剑|1|0;BeeKeeper|养蜂人|1|0;FieryGreatsword|火山|2|0;NightsEdge|永夜刃|2|0;DarkLance|暗黑长戟|2|0;Sunfury|阳炎之怒|2|0");
   Weapons(HeroClass.Ranger,1,"WoodenBow|木弓|0|1;CopperBow|铜弓|0|1;SilverBow|银弓|0|1;GoldBow|金弓|0|0;Blowpipe|吹管|0|0;Shuriken|手里剑|0|0;Musket|火枪|1|0;TheUndertaker|夺命枪|1|0;Minishark|迷你鲨|1|0;Boomstick|三发猎枪|1|0;Revolver|左轮手枪|1|0;Sandgun|沙枪|1|0;MoltenFury|熔火之怒|2|0;BeesKnees|蜂膝弓|2|0;PhoenixBlaster|凤凰爆破枪|2|0;StarCannon|星星炮|2|0");
   Weapons(HeroClass.Mage,1,"WandofSparking|火花魔棒|0|1;AmethystStaff|紫晶法杖|0|1;TopazStaff|黄玉法杖|0|1;SapphireStaff|蓝玉法杖|0|0;EmeraldStaff|翡翠法杖|0|0;RubyStaff|红玉法杖|1|0;DiamondStaff|钻石法杖|1|0;WaterBolt|水矢|1|0;MagicMissile|魔法导弹|1|0;Vilethorn|魔刺|1|0;CrimsonRod|猩红魔杖|1|0;SpaceGun|太空枪|1|0;BeeGun|蜜蜂枪|1|0;Flamelash|烈焰火鞭|2|0;DemonScythe|恶魔锄刀|2|0;AquaScepter|海蓝权杖|2|0");
   Weapons(HeroClass.Summoner,1,"BabyBirdStaff|雀杖|0|1;AbigailsFlower|阿比盖尔的花|0|1;SlimeStaff|史莱姆法杖|0|1;BlandWhip|皮鞭|0|0;ThornWhip|荆鞭|1|0;FlinxStaff|小雪怪法杖|1|0;VampireFrogStaff|吸血鬼青蛙法杖|1|0;HornetStaff|黄蜂法杖|1|0;ImpStaff|小鬼法杖|2|0;BoneWhip|脊柱骨鞭|2|0;SlimeWhip|史莱姆鞭|0|0;CobWhip|蛛网鞭|0|0;CorruptWhip|腐化鞭|1|0;CrimsonWhip|猩红鞭|1|0;MeteorWhip|流星鞭|1|0;DD2BallistraTowerT1Popper|弩车魔杖|1|0;DD2FlameburstTowerT1Popper|爆炸烈焰魔杖|1|0;DD2ExplosiveTrapT1Popper|爆炸机关魔杖|1|0;DD2LightningAuraT1Popper|闪电光环魔杖|1|0");
   Weapons(HeroClass.Warrior,2,"CobaltSword|钴剑|0|1;PalladiumSword|钯金剑|0|1;CobaltNaginata|钴薙刀|0|1;PalladiumPike|钯金刺矛|0|1;MythrilSword|秘银剑|0|0;OrichalcumSword|山铜剑|0|0;IceSickle|冰雪镰刀|1|0;Cutlass|海盗弯刀|1|0;Frostbrand|霜印剑|1|0;BeamSword|光束剑|1|0;TitaniumSword|钛金剑|1|0;AdamantiteSword|精金剑|1|0;DaoofPow|太极连枷|2|0;ShadowFlameKnife|暗影焰刀|2|0;FetidBaghnakhs|臭虎爪|2|0;FlyingKnife|飞刀|2|0");
   Weapons(HeroClass.Ranger,2,"CobaltRepeater|钴连弩|0|1;PalladiumRepeater|钯金连弩|0|1;ClockworkAssaultRifle|发条式突击步枪|0|1;Shotgun|霰弹枪|0|1;MythrilRepeater|秘银连弩|0|0;OrichalcumRepeater|山铜连弩|0|0;AdamantiteRepeater|精金连弩|1|0;TitaniumRepeater|钛金连弩|1|0;Marrow|髓骨弓|1|0;IceBow|冰雪弓|1|0;ShadowFlameBow|暗影焰弓|1|0;DartRifle|飞镖步枪|2|0;DartPistol|飞镖手枪|2|0;DaedalusStormbow|代达罗斯风暴弓|2|0;OnyxBlaster|玛瑙爆破枪|2|0;Uzi|乌兹冲锋枪|2|0");
   Weapons(HeroClass.Mage,2,"CrystalStorm|水晶风暴|0|1;CursedFlames|诅咒焰|0|1;GoldenShower|黄金雨|0|1;CrystalSerpent|水晶蛇|0|1;SkyFracture|裂天剑|1|0;MeteorStaff|陨石法杖|1|0;FrostStaff|寒霜法杖|1|0;FlowerofFrost|寒霜之花|1|0;MagicDagger|魔法飞刀|1|0;PoisonStaff|剧毒法杖|1|0;SpiritFlame|精灵火|2|0;SoulDrain|夺命杖|2|0;ClingerStaff|爬藤怪法杖|2|0;CrystalVileShard|水晶魔刺|2|0;MedusaHead|蛇发女妖头|2|0;ShadowFlameHexDoll|暗影焰妖娃|2|0");
   Weapons(HeroClass.Summoner,2,"SpiderStaff|蜘蛛法杖|0|1;QueenSpiderStaff|蜘蛛女王法杖|0|1;FireWhip|鞭炮|0|1;CoolWhip|冷鞭|1|0;SanguineStaff|血红法杖|2|0;PirateStaff|海盗法杖|1|0;Smolstar|刃杖|2|0");
   Weapons(HeroClass.Warrior,3,"Excalibur|断钢剑|0|1;Gungnir|永恒之枪|0|1;ChlorophyteSaber|叶绿军刀|0|1;ChlorophyteClaymore|叶绿双刃刀|0|1;TrueExcalibur|真断钢剑|1|0;TrueNightsEdge|真永夜刃|1|0;DeathSickle|死神镰刀|1|0;ChlorophytePartisan|叶绿镋|1|0;Seedler|种子弯刀|1|0;TheHorsemansBlade|无头骑士剑|2|0;TerraBlade|泰拉刃|2|0;PossessedHatchet|疯狂飞斧|2|0;InfluxWaver|波涌之刃|2|0;Flairon|猪鲨链球|2|0;NorthPole|北极|2|0;ScourgeoftheCorruptor|腐化者之戳|2|0");
   Weapons(HeroClass.Ranger,3,"HallowedRepeater|神圣连弩|0|1;Megashark|巨兽鲨|0|1;ChlorophyteShotbow|叶绿连弩|0|1;SuperStarCannon|超级星星炮|0|1;GrenadeLauncher|榴弹发射器|1|0;VenusMagnum|维纳斯万能枪|1|0;TacticalShotgun|战术霰弹枪|1|0;SniperRifle|狙击步枪|1|0;RocketLauncher|火箭发射器|1|0;StakeLauncher|尖桩发射器|2|0;CandyCornRifle|玉米糖步枪|2|0;ChainGun|链式机枪|2|0;Xenopopper|外星霰弹枪|2|0;Tsunami|海啸|2|0;ElectrosphereLauncher|电圈发射器|2|0;NailGun|钉枪|2|0");
   Weapons(HeroClass.Mage,3,"RainbowRod|彩虹魔杖|0|1;MagicalHarp|魔法竖琴|0|1;VenomStaff|剧毒法杖|0|1;UnholyTrident|邪恶三叉戟|0|1;WaspGun|胡蜂枪|1|0;LeafBlower|吹叶机|1|0;ShadowbeamStaff|暗影束法杖|1|0;InfernoFork|狱火叉|1|0;SpectreStaff|幽灵法杖|1|0;BatScepter|蝙蝠权杖|2|0;Razorpine|剃刀松|2|0;BlizzardStaff|暴雪法杖|2|0;LaserMachinegun|激光机枪|2|0;RazorbladeTyphoon|利刃台风|2|0;BubbleGun|泡泡枪|2|0;StaffofEarth|大地法杖|2|0");
   Weapons(HeroClass.Summoner,3,"OpticStaff|魔眼法杖|0|1;DD2BallistraTowerT2Popper|弩车手杖|0|1;DD2FlameburstTowerT2Popper|爆炸烈焰手杖|0|1;DD2LightningAuraT2Popper|闪电光环手杖|0|1;DD2ExplosiveTrapT2Popper|爆炸机关手杖|0|1;SwordWhip|迪朗达尔|1|0;PygmyStaff|矮人法杖|1|0;RavenStaff|乌鸦法杖|1|0;DeadlySphereStaff|致命球法杖|1|0;TempestStaff|暴风雨法杖|2|0;XenoStaff|外星法杖|2|0;StormTigerStaff|沙漠虎杖|2|0;RainbowWhip|万花筒|2|0;ScytheWhip|黑暗收割|2|0;MaceWhip|晨星|2|0;FlowerWhip|花鞭|1|0;EelWhip|鳗鱼鞭|2|0;ConstellationWhip|星座鞭|2|0");
   Weapons(HeroClass.Summoner,3,"DD2BallistraTowerT3Popper|弩车法杖|2|0;DD2FlameburstTowerT3Popper|爆炸烈焰法杖|2|0;DD2ExplosiveTrapT3Popper|爆炸机关法杖|2|0;DD2LightningAuraT3Popper|闪电光环法杖|2|0");
   Armor(null,1,"wood","木套装",0,true,"WoodHelmet WoodBreastplate WoodGreaves");
   Armor(null,1,"boreal","针叶木套装",0,true,"BorealWoodHelmet BorealWoodBreastplate BorealWoodGreaves");
   Armor(null,1,"silver","银套装",0,false,"SilverHelmet SilverChainmail SilverGreaves");
   Armor(null,1,"gold","金套装",1,false,"GoldHelmet GoldChainmail GoldGreaves");
   Armor(null,1,"platinum","铂金套装",1,false,"PlatinumHelmet PlatinumChainmail PlatinumGreaves");
   Armor(HeroClass.Warrior,1,"shadow","暗影套装",1,false,"ShadowHelmet ShadowScalemail ShadowGreaves");
   Armor(HeroClass.Warrior,1,"crimson","猩红套装",1,false,"CrimsonHelmet CrimsonScalemail CrimsonGreaves");
   Armor(HeroClass.Warrior,1,"molten","熔岩套装",2,false,"MoltenHelmet MoltenBreastplate MoltenGreaves");
   Armor(HeroClass.Ranger,1,"fossil","化石套装",1,false,"FossilHelm FossilShirt FossilPants");
   Armor(HeroClass.Ranger,1,"necro","死灵套装",2,false,"NecroHelmet NecroBreastplate NecroGreaves");
   Armor(HeroClass.Mage,1,"jungle","丛林套装",1,false,"JungleHat JungleShirt JunglePants");
   Armor(HeroClass.Mage,1,"meteor","陨石套装",2,false,"MeteorHelmet MeteorSuit MeteorLeggings");
   Armor(HeroClass.Summoner,1,"bee","蜜蜂套装",1,false,"BeeHeadgear BeeBreastplate BeeGreaves");
   Armor(HeroClass.Summoner,1,"obsidian","黑曜石套装",2,false,"ObsidianHelm ObsidianShirt ObsidianPants");
   foreach(HeroClass hero in Enum.GetValues<HeroClass>()) {
    string head=hero==HeroClass.Warrior?"Helmet":hero==HeroClass.Ranger?"Mask":"Hat";
    if(hero!=HeroClass.Summoner){Armor(hero,2,"cobalt","钴套装",0,true,"Cobalt"+head+" CobaltBreastplate CobaltLeggings");}
    string palladium=hero==HeroClass.Warrior?"Mask":hero==HeroClass.Ranger?"Helmet":"Headgear";
    if(hero!=HeroClass.Summoner){Armor(hero,2,"palladium","钯金套装",0,true,"Palladium"+palladium+" PalladiumBreastplate PalladiumLeggings");}
    string mythril=hero==HeroClass.Warrior?"Helmet":hero==HeroClass.Ranger?"Hat":"Hood";
    string orichalcum=hero==HeroClass.Warrior?"Mask":hero==HeroClass.Ranger?"Helmet":"Headgear";
    string titanium=hero==HeroClass.Warrior?"Mask":hero==HeroClass.Ranger?"Helmet":"Headgear";
    if(hero!=HeroClass.Summoner){Armor(hero,2,"mythril","秘银套装",1,false,"Mythril"+mythril+" MythrilChainmail MythrilGreaves");Armor(hero,2,"orichalcum","山铜套装",1,false,"Orichalcum"+orichalcum+" OrichalcumBreastplate OrichalcumLeggings");Armor(hero,2,"titanium","钛金套装",2,false,"Titanium"+titanium+" TitaniumBreastplate TitaniumLeggings");}
    string hallowed=hero==HeroClass.Warrior?"Mask":hero==HeroClass.Ranger?"Helmet":hero==HeroClass.Mage?"Headgear":"Hood";
    Armor(hero,3,"hallowed","神圣套装",0,true,"Hallowed"+hallowed+" HallowedPlateMail HallowedGreaves");
   }
   Armor(null,2,"crystal-ninja","水晶刺客套装",1,false,"CrystalNinjaHelmet CrystalNinjaChestplate CrystalNinjaLeggings");
   Armor(HeroClass.Summoner,2,"spider","蜘蛛套装",0,true,"SpiderMask SpiderBreastplate SpiderGreaves");
   Armor(HeroClass.Summoner,2,"forbidden","禁戒套装",2,false,"AncientBattleArmorHat AncientBattleArmorShirt AncientBattleArmorPants");
   Armor(HeroClass.Warrior,3,"chlorophyte","叶绿战士套装",1,false,"ChlorophyteMask ChlorophytePlateMail ChlorophyteGreaves");
   Armor(HeroClass.Warrior,3,"turtle","海龟套装",1,false,"TurtleHelmet TurtleScaleMail TurtleLeggings");
   Armor(HeroClass.Warrior,3,"beetle","甲虫套装",2,false,"BeetleHelmet BeetleScaleMail BeetleLeggings");
   Armor(HeroClass.Ranger,3,"chlorophyte","叶绿射手套装",1,false,"ChlorophyteHelmet ChlorophytePlateMail ChlorophyteGreaves");
   Armor(HeroClass.Ranger,3,"shroomite","蘑菇套装",2,false,"ShroomiteMask ShroomiteBreastplate ShroomiteLeggings");
   Armor(HeroClass.Mage,3,"chlorophyte","叶绿法师套装",1,false,"ChlorophyteHeadgear ChlorophytePlateMail ChlorophyteGreaves");
   Armor(HeroClass.Mage,3,"spectre","幽灵套装",2,false,"SpectreMask SpectreRobe SpectrePants");
   Armor(HeroClass.Summoner,3,"tiki","提基套装",1,false,"TikiMask TikiShirt TikiPants");
   Armor(HeroClass.Summoner,3,"spooky","阴森套装",2,false,"SpookyHelmet SpookyBreastplate SpookyLeggings");
   // Tavernkeeper armor unlocks after a mechanical boss; the upgraded sets
   // belong to the post-Golem part of act three. No tower-defense room is needed.
   Armor(HeroClass.Warrior,3,"squire","侍卫套装",0,true,"SquireGreatHelm SquirePlating SquireGreaves");
   Armor(HeroClass.Warrior,3,"valhalla","英灵殿骑士套装",2,false,"SquireAltHead SquireAltShirt SquireAltPants");
   Armor(HeroClass.Ranger,3,"huntress","女猎人套装",0,true,"HuntressWig HuntressJerkin HuntressPants");
   Armor(HeroClass.Ranger,3,"red-riding","红色骑术套装",2,false,"HuntressAltHead HuntressAltShirt HuntressAltPants");
   Armor(HeroClass.Mage,3,"apprentice","学徒套装",0,true,"ApprenticeHat ApprenticeRobe ApprenticeTrousers");
   Armor(HeroClass.Mage,3,"dark-artist","暗黑艺术家套装",2,false,"ApprenticeAltHead ApprenticeAltShirt ApprenticeAltPants");
   Armor(HeroClass.Summoner,3,"monk","武僧套装",0,true,"MonkBrows MonkShirt MonkPants");
   Armor(HeroClass.Summoner,3,"shinobi","渗透忍者套装",2,false,"MonkAltHead MonkAltShirt MonkAltPants");
   foreach(string row in new[]{"RoyalGel|皇家凝胶|1","EoCShield|克苏鲁护盾|1","WormScarf|蠕虫围巾|1","BrainOfConfusion|混乱之脑|1","HiveBackpack|蜂巢背包|1","HoneyComb|蜂窝|1","BoneGlove|骨头手套|1","VolatileGelatin|挥发明胶|2","SporeSac|孢子囊|3","ShinyStone|闪亮石|3","FishronWings|猪龙鱼之翼|3","EmpressFlightBooster|翱翔徽章|3","TitanGlove|泰坦手套|2","PhilosophersStone|点金石|2","StarCloak|星星斗篷|2","CrossNecklace|十字项链|2","FleshKnuckles|血肉指虎|2","PutridScent|腐香囊|2","FastClock|快走时钟|2","TrifoldMap|三折地图|2","Bezoar|牛黄|1","Nazar|邪眼|1","CobaltShield|钴护盾|1","HermesBoots|赫尔墨斯靴|1","CloudinaBottle|云朵瓶|1","BandofRegeneration|再生手环|1"}) {
    var p=row.Split('|');Add(new GearDefinition{Key="accessory:"+p[0],Name=p[1],Stage=int.Parse(p[2]),Kind=GearKind.Accessory,Rarity=GearRarity.Rare,NativeSymbols=new[]{p[0]},Description=NativeAccessoryDescription(p[0])+" 每种只保留一件。"});
   }
   Accessory("WarriorEmblem","战士徽章",2,GearRarity.Uncommon,"血肉墙","近战伤害 +15%。",HeroClass.Warrior);
   Accessory("RangerEmblem","游侠徽章",2,GearRarity.Uncommon,"血肉墙","远程伤害 +15%。",HeroClass.Ranger);
   Accessory("SorcererEmblem","巫士徽章",2,GearRarity.Uncommon,"血肉墙","魔法伤害 +15%。",HeroClass.Mage);
   Accessory("SummonerEmblem","召唤师徽章",2,GearRarity.Uncommon,"血肉墙","召唤伤害 +15%（含鞭子、仆从与哨兵）。",HeroClass.Summoner);
   Accessory("Shackle","脚镣",1,GearRarity.Common,"僵尸","防御 +1。");
   Accessory("Aglet","疾风脚镯",1,GearRarity.Common,"地表宝箱","移动速度 +5%。");
   Accessory("AnkletoftheWind","疾风之靴",1,GearRarity.Uncommon,"丛林常春藤箱","移动速度 +10%。");
   Accessory("SharkToothNecklace","鲨牙项链",1,GearRarity.Uncommon,"血月敌怪","护甲穿透 +5，命中时忽略敌人 5 点防御。");
   Accessory("AvengerEmblem","复仇者徽章",3,GearRarity.Uncommon,"职业徽章与机械首领灵魂合成","所有伤害 +12%（近战、远程、魔法与召唤）。");
   // All four native Tavernkeeper accessories improve sentries and summon
   // damage, despite their class-themed names. Keep them in the summoner pool.
   Accessory("ApprenticeScarf","学徒围巾",1,GearRarity.Uncommon,"酒馆老板",TavernAccessoryDescription,HeroClass.Summoner);
   Accessory("SquireShield","侍卫护盾",1,GearRarity.Uncommon,"酒馆老板",TavernAccessoryDescription,HeroClass.Summoner);
   Accessory("HuntressBuckler","女猎人圆盾",1,GearRarity.Uncommon,"酒馆老板",TavernAccessoryDescription,HeroClass.Summoner);
   Accessory("MonkBelt","武僧腰带",1,GearRarity.Uncommon,"酒馆老板",TavernAccessoryDescription,HeroClass.Summoner);
   Accessory("ObsidianShield","黑曜石护盾",1,GearRarity.Uncommon,"钴护盾与黑曜石骷髅头合成","防御 +2；免疫击退与灼热物块的接触伤害。不能抵御熔岩或燃烧减益。");
   Accessory("SpectreBoots","幽灵靴",1,GearRarity.Uncommon,"奔跑靴与火箭靴合成","持续向同一方向奔跑可加速；跳跃后按住空格短暂飞行，落地后恢复飞行时间。");
   Accessory("FeralClaws","猛爪手套",1,GearRarity.Uncommon,"丛林常春藤箱","近战武器与鞭子攻击速度 +12%；按住左键可自动挥舞。",null,HeroClass.Warrior,HeroClass.Summoner);
   Get("accessory:TitanGlove").CompatibleHeroes=new[]{HeroClass.Warrior,HeroClass.Summoner};
   Accessory("ManaRegenerationBand","魔力再生手环",1,GearRarity.Uncommon,"星力手环与再生手环合成","魔力上限 +40，提高魔力再生速度；生命再生 +1/秒。",HeroClass.Mage);
   Accessory("MagicCuffs","魔法手铐",1,GearRarity.Uncommon,"魔力再生手环与脚镣合成","防御 +2，魔力上限 +40；受到攻击时恢复魔力，最多恢复至上限。",HeroClass.Mage);
   Accessory("ManaFlower","魔力花",1,GearRarity.Rare,"大自然的恩赐与魔力药水合成","魔力消耗 -8%。本模式的药水栏暂未接入自动喝药，请手动使用魔力药水。",HeroClass.Mage);
   Accessory("PygmyNecklace","矮人项链",1,GearRarity.Uncommon,"巫医","仆从数量上限 +1；不增加哨兵数量上限。",HeroClass.Summoner);
   Accessory("StarVeil","星星面纱",2,GearRarity.Rare,"十字项链与星星斗篷合成","受到可反击的攻击伤害时落下 3 颗星星（各 75 基础伤害），普通受击无敌时间延长至 2 倍。星落与星星斗篷不叠加，无敌延长与十字项链不叠加。");
   Accessory("MoonCharm","月亮护符",2,GearRarity.Uncommon,"满月狼人","夜间化为狼人：防御 +3，近战伤害与攻击速度各 +5.1%，近战暴击率 +2 个百分点，移动速度 +5%，生命再生 +0.5/秒。本模式战场固定为夜间。");
   foreach(string row in new[]{"Grenade|手榴弹|1","Dynamite|雷管|1","Beenade|蜜蜂手榴弹|1","MolotovCocktail|燃烧瓶|1","Bomb|炸弹|1","StickyGrenade|黏性手榴弹|1","BouncyGrenade|弹力手榴弹|2","PartyGirlGrenade|欢乐手榴弹|2","HealingPotion|治疗药水|1","GreaterHealingPotion|强效治疗药水|2","SuperHealingPotion|超级治疗药水|3","ManaPotion|魔力药水|1","GreaterManaPotion|强效魔力药水|2","SuperManaPotion|超级魔力药水|3"}){var p=row.Split('|');string detail=p[0].Contains("Healing")?"恢复生命，受泰拉瑞亚药水虚弱限制。":p[0].Contains("Mana")?"恢复魔力，并施加泰拉瑞亚魔力病。":"通用投掷物，不占三个武器槽。";Add(new GearDefinition{Key="consumable:"+p[0],Name=p[1],Stage=int.Parse(p[2]),Kind=GearKind.Consumable,Rarity=GearRarity.Common,NativeSymbols=new[]{p[0]},Description=detail});}
  }
  const string TavernAccessoryDescription="哨兵数量上限 +1，召唤伤害 +10%。四种酒馆饰品的这项共同加成不叠加。";
  // Effects are checked against the installed Terraria 1.4.5.8 runtime. These
  // descriptions are shared by BaseLib relic hover tips, shops and treasure.
  static string NativeAccessoryDescription(string symbol) {
   switch(symbol) {
    case "RoyalGel":return "让多数普通史莱姆不再主动攻击你，史莱姆首领不受影响。本模式仍须击败房内目标才能获胜。";
    case "EoCShield":return "防御 +2；快速双击 A 或 D 向该方向冲刺，可撞击敌人。冲刺碰撞造成 30 基础近战伤害。";
    case "WormScarf":return "受到的攻击伤害降低 17%；不能抵消中毒、燃烧等持续掉血。";
    case "BrainOfConfusion":return "受到可闪避的攻击时有 1/6 概率闪避；随后 4 秒内近战、远程与魔法暴击率 +10 个百分点，召唤伤害 +10%，期间不能再次触发该闪避。受击时还能使附近可受影响的敌人陷入困惑。";
    case "HiveBackpack":return "友方蜜蜂有 50% 概率变为更大的强化蜜蜂，提高其伤害与击退。作用于武器或蜂窝等产生的蜜蜂，本身不会召唤蜜蜂。";
    case "HoneyComb":return "受到可反击的攻击伤害时释放蜜蜂，并获得 5 秒蜂蜜增益，加快生命再生。与蜂巢背包的强化蜜蜂效果联动。";
    case "BoneGlove":return "使用有伤害的武器攻击时，朝瞄准方向额外发射交叉骨头，造成 25 基础伤害；触发间隔 1 秒。";
    case "VolatileGelatin":return "附近有可攻击的敌人时，约每 0.68 秒自动射出挥发明胶，造成 65 基础伤害；搜寻范围 640 像素。";
    case "SporeSac":return "在周围逐渐生成伤害孢子，敌人靠近后孢子追踪攻击，造成 70 基础伤害。孢子的生成受地形与当前数量限制。";
    case "ShinyStone":return "静止且不使用物品时，大幅加快生命再生；持续休息会进一步提高恢复速度，移动、使用物品或受伤会影响效果。";
    case "FishronWings":return "跳跃后按住空格飞行，也能缓降，并免疫摔落伤害；在水中能更快移动。本模式当前战场没有水域，水中加成暂不触发。";
    case "EmpressFlightBooster":return "移动速度 +7.5%，提高跳跃与飞行机动性；配合翅膀或火箭靴时可无限飞行。本身不提供飞行能力。";
    case "TitanGlove":return "近战武器击退 +100%，近战武器尺寸 +10%；具体适用武器按泰拉瑞亚原生类别判定。";
    case "PhilosophersStone":return "治疗药水造成的药水虚弱时间缩短 25%；普通 60 秒冷却缩短为 45 秒。不减少魔力药水的魔力病。";
    case "StarCloak":return "受到可反击的攻击伤害时，从上方落下 3 颗星星，每颗造成 75 基础伤害。与星星面纱的星落效果不叠加。";
    case "CrossNecklace":return "普通受击后的无敌时间延长至 2 倍，通常由约 0.67 秒延长至约 1.33 秒。与星星面纱的无敌延长效果不叠加。";
    case "FleshKnuckles":return "防御 +8，并提高敌人对你的仇恨。当前为单人战斗，仇恨增加不会提供额外减伤。";
    case "PutridScent":return "所有伤害 +5%，近战、远程与魔法暴击率 +5 个百分点，并降低敌人对你的仇恨。单人战斗仍会被敌人锁定。";
    case "FastClock":return "免疫缓慢减益，防止该减益降低移动速度；不免疫冰冻或其他减速机制。";
    case "TrifoldMap":return "免疫困惑减益，避免移动方向因该减益而颠倒。";
    case "Bezoar":return "免疫中毒减益及其持续掉血；不免疫毒液减益。";
    case "Nazar":return "免疫诅咒减益，避免因该减益而无法使用物品；不免疫诅咒狱火。";
    case "CobaltShield":return "防御 +1，并免疫受到攻击时的击退。";
    case "HermesBoots":return "持续向同一方向奔跑，未受其他加成时可加速至每秒约 22.5 个图格；转向或受阻后需要重新加速。与幽灵靴的奔跑上限不叠加。";
    case "CloudinaBottle":return "获得 1 次空中二连跳；在空中松开后再次按下空格触发，落地后恢复次数。";
    case "BandofRegeneration":return "生命再生 +1/秒；中毒、燃烧等持续掉血状态可能抑制生命再生。";
    default:throw new InvalidOperationException("Missing native accessory description: "+symbol);
   }
  }
  static void Add(GearDefinition item){if(byKey.ContainsKey(item.Key))throw new InvalidOperationException("Duplicate equipment key: "+item.Key);byKey.Add(item.Key,item);entries.Add(item);}
  static void Weapons(HeroClass hero,int stage,string rows){foreach(string row in rows.Split(';')){var p=row.Split('|');Add(new GearDefinition{Key="weapon:"+hero+":"+stage+":"+p[0],Name=p[1],Hero=hero,Stage=stage,Kind=GearKind.Weapon,Rarity=(GearRarity)int.Parse(p[2]),IsStageEntry=p[3]=="1",NativeSymbols=new[]{p[0]},Description="第 "+stage+" 幕"+HeroName(hero)+"武器；强化仅增加本件装备的基础属性。"});}}
  static void Armor(HeroClass? hero,int stage,string key,string name,int rarity,bool entry,string symbols){Add(new GearDefinition{Key="armor:"+(hero?.ToString()??"Any")+":"+stage+":"+key,Name=name,Hero=hero,Stage=stage,Kind=GearKind.Armor,Rarity=(GearRarity)rarity,IsStageEntry=entry,NativeSymbols=symbols.Split(' '),Description="整套装备，一次替换头盔、胸甲与护腿。"});}
  static void Accessory(string symbol,string name,int stage,GearRarity rarity,string source,string effect,HeroClass? hero=null,params HeroClass[] compatibleHeroes){Add(new GearDefinition{Key="accessory:"+symbol,Name=name,Hero=hero,CompatibleHeroes=compatibleHeroes,Stage=stage,Kind=GearKind.Accessory,Rarity=rarity,NativeSymbols=new[]{symbol},Description=effect+" 来源："+source+"。每种只保留一件。"});}
  public static GearDefinition Get(string key)=>byKey.TryGetValue(key,out var item)?item:throw new KeyNotFoundException("Unknown equipment: "+key);
  public static bool TryGet(string key,out GearDefinition item)=>byKey.TryGetValue(key,out item);
  public static bool IsForHero(GearDefinition gear,HeroClass hero)=>gear!=null&&(!gear.Hero.HasValue||gear.Hero==hero)&&(gear.CompatibleHeroes==null||gear.CompatibleHeroes.Length==0||gear.CompatibleHeroes.Contains(hero));
  public static IEnumerable<GearDefinition> For(HeroClass hero,int stage,GearKind? kind=null)=>entries.Where(x=>x.Stage==stage&&IsForHero(x,hero)&&(!kind.HasValue||x.Kind==kind));
  public static string HeroName(HeroClass hero)=>hero==HeroClass.Warrior?"战士":hero==HeroClass.Ranger?"射手":hero==HeroClass.Mage?"法师":"召唤师";
  public static string RarityName(GearRarity rarity)=>rarity==GearRarity.Common?"普通":rarity==GearRarity.Uncommon?"罕见":"稀有";
  public static string MaterialName(string key){var p=key.Split(':');string type=p.Length>1?p[1]:key;string name=type=="ore"?"矿锭":type=="weave"?"织物":type=="bone"?"骨片":type=="crystal"?"晶体":type=="essence"?"精华":type;return (p.Length>1?"第"+p[0]+"幕":"")+name;}
 }
}

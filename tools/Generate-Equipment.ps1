$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskIds=@{}
$taskText=Get-Content (Join-Path $taskRoot 'validation/decompiled/ItemID.cs') -Raw
foreach($m in [regex]::Matches($taskText,'public const short (\w+) = (\d+);')){$taskIds[$m.Groups[1].Value]=[int]$m.Groups[2].Value}
# Key | Chinese name | class | act | within-act unlock (0/1/2) | rarity (1/2/3) | native item symbols
# Act 2 ends before mechanical bosses. Act 3 contains post-mech, post-Plantera,
# then post-Golem/pillar equipment. Moon Lord drops and luminite armor are excluded.
# Tavernkeep sentries: T1 after the evil biome boss (act 1 mid), T2 after a
# mechanical boss (act 3 early), T3 after Golem (act 3 late). The four class
# pools follow their matching tavern armor; the native weapons deal summon damage.
$taskRows=@'
EnchantedBlade|附魔剑|Warrior|1|0|2|EnchantedSword
EnchantedBoomerang|附魔回旋镖|Warrior|1|0|1|EnchantedBoomerang
IceBladeGear|冰雪刃|Warrior|1|0|1|IceBlade
BloodBlade|血腥屠刀|Warrior|1|0|1|BloodButcherer
GrassBlade|草剑|Warrior|1|1|2|BladeofGrass
VolcanoBlade|火山|Warrior|1|1|2|FieryGreatsword
NightBlade|永夜刃|Warrior|1|2|3|NightsEdge
ShadowKnife|暗影焰刀|Warrior|2|0|2|ShadowFlameKnife
AdamantiteBlade|精金剑|Warrior|2|0|1|AdamantiteSword
IceSickleGear|冰雪镰刀|Warrior|2|0|1|IceSickle
FrostbrandGear|冰雪剑|Warrior|2|1|2|Frostbrand
FlyingKnifeGear|飞刀|Warrior|2|1|2|FlyingKnife
FetidGear|臭虎爪|Warrior|2|2|3|FetidBaghnakhs
ExcaliburGear|断钢剑|Warrior|3|0|1|Excalibur
TrueNightGear|原版永夜刃|Warrior|3|0|2|TrueNightsEdge
TerraBladeGear|泰拉刃|Warrior|3|1|3|TerraBlade
PaladinHammerGear|圣骑士锤|Warrior|3|1|2|PaladinsHammer
InfluxGear|波涌之刃|Warrior|3|2|2|InfluxWaver
SolarEruptionGear|日耀喷发剑|Warrior|3|2|3|SolarEruption
SilverBow|银弓|Ranger|1|0|1|SilverBow
FlintlockGear|燧发枪|Ranger|1|0|1|FlintlockPistol
MusketGear|火枪|Ranger|1|0|1|Musket
MiniSharkGear|迷你鲨|Ranger|1|1|2|Minishark
BeesKneesGear|蜂膝弓|Ranger|1|1|2|BeesKnees
MoltenBowGear|熔火之怒|Ranger|1|2|2|MoltenFury
PhoenixGear|凤凰爆破枪|Ranger|1|2|3|PhoenixBlaster
ClockworkGear|发条式突击步枪|Ranger|2|0|1|ClockworkAssaultRifle
AdamantiteRepeaterGear|精金连弩|Ranger|2|0|1|AdamantiteRepeater
IceBowGear|冰雪弓|Ranger|2|0|2|IceBow
OnyxGear|玛瑙爆破枪|Ranger|2|1|2|OnyxBlaster
DaedalusGear|代达罗斯风暴弓|Ranger|2|2|3|DaedalusStormbow
MegaSharkGear|巨兽鲨|Ranger|3|0|1|Megashark
ChloroBowGear|叶绿连弩|Ranger|3|0|2|ChlorophyteShotbow
TacticalGear|战术霰弹枪|Ranger|3|1|2|TacticalShotgun
TsunamiGear|海啸|Ranger|3|2|3|Tsunami
XenoGear|外星霰弹枪|Ranger|3|2|2|Xenopopper
VortexGear|星旋机枪|Ranger|3|2|3|VortexBeater
RubyStaff|红玉法杖|Mage|1|0|1|RubyStaff
WaterBoltGear|水矢|Mage|1|0|2|WaterBolt
SpaceGunGear|太空枪|Mage|1|0|1|SpaceGun
BeeGunGear|蜜蜂枪|Mage|1|1|2|BeeGun
DemonScytheGear|恶魔锄刀|Mage|1|1|2|DemonScythe
FlamelashGear|火之花法杖|Mage|1|2|3|Flamelash
CrystalSerpentGear|水晶蛇|Mage|2|0|1|CrystalSerpent
CrystalStormGear|水晶风暴|Mage|2|0|1|CrystalStorm
GoldenShowerGear|黄金雨|Mage|2|1|2|GoldenShower
SkyFractureGear|裂天剑|Mage|2|1|2|SkyFracture
MeteorStaffGear|流星法杖|Mage|2|2|3|MeteorStaff
RainbowRodGear|彩虹魔杖|Mage|3|0|1|RainbowRod
MagicalHarpGear|魔法竖琴|Mage|3|0|1|MagicalHarp
TomeWisdomGear|无限智慧巨著|Mage|3|0|2|BookStaff
MagnetSphereGear|磁球|Mage|3|1|2|MagnetSphere
SpectreStaffGear|幽魂法杖|Mage|3|1|2|SpectreStaff
TyphoonGear|利刃台风|Mage|3|2|3|RazorbladeTyphoon
LaserGear|激光机枪|Mage|3|2|2|LaserMachinegun
NebulaBlazeGear|星云烈焰|Mage|3|2|3|NebulaBlaze
LeatherWhip|皮鞭|Summoner|1|0|1|BlandWhip
FlinxStaffGear|小雪怪法杖|Summoner|1|0|1|FlinxStaff
FinchStaffGear|雀杖|Summoner|1|0|1|BabyBirdStaff
AbigailStaffGear|阿比盖尔的花|Summoner|1|0|1|AbigailsFlower
SnapthornGear|荆鞭|Summoner|1|0|2|ThornWhip
HornetStaffGear|黄蜂法杖|Summoner|1|1|2|HornetStaff
SpinalTapGear|脊柱骨鞭|Summoner|1|1|2|BoneWhip
ImpStaffGear|小鬼法杖|Summoner|1|2|3|ImpStaff
SpiderStaffGear|蜘蛛法杖|Summoner|2|0|1|SpiderStaff
FirecrackerGear|鞭炮|Summoner|2|0|1|FireWhip
CoolWhipGear|冷鞭|Summoner|2|1|2|CoolWhip
BladeStaffGear|刃杖|Summoner|2|1|2|Smolstar
SanguineStaffGear|血红法杖|Summoner|2|2|3|SanguineStaff
DurendalGear|迪朗达尔|Summoner|3|0|1|SwordWhip
OpticStaffGear|双子魔眼法杖|Summoner|3|0|1|OpticStaff
DeadlySphereGear|致命球法杖|Summoner|3|1|2|DeadlySphereStaff
MorningStarGear|晨星|Summoner|3|1|2|MaceWhip
TempestStaffGear|暴风雨法杖|Summoner|3|2|3|TempestStaff
KaleidoscopeGear|万花筒|Summoner|3|2|3|RainbowWhip
DragonStaffGear|星尘之龙法杖|Summoner|3|2|3|StardustDragonStaff
BallistaRodGear|弩车魔杖|Warrior|1|1|2|DD2BallistraTowerT1Popper
BallistaCaneGear|弩车手杖|Warrior|3|0|2|DD2BallistraTowerT2Popper
BallistaStaffGear|弩车法杖|Warrior|3|2|3|DD2BallistraTowerT3Popper
ExplosiveRodGear|爆炸机关魔杖|Ranger|1|1|2|DD2ExplosiveTrapT1Popper
ExplosiveCaneGear|爆炸机关手杖|Ranger|3|0|2|DD2ExplosiveTrapT2Popper
ExplosiveStaffGear|爆炸机关法杖|Ranger|3|2|3|DD2ExplosiveTrapT3Popper
FlameburstRodGear|烈焰爆破魔杖|Mage|1|1|2|DD2FlameburstTowerT1Popper
FlameburstCaneGear|烈焰爆破手杖|Mage|3|0|2|DD2FlameburstTowerT2Popper
FlameburstStaffGear|烈焰爆破法杖|Mage|3|2|3|DD2FlameburstTowerT3Popper
LightningRodGear|闪电光环魔杖|Summoner|1|1|2|DD2LightningAuraT1Popper
LightningCaneGear|闪电光环手杖|Summoner|3|0|2|DD2LightningAuraT2Popper
LightningStaffGear|闪电光环法杖|Summoner|3|2|3|DD2LightningAuraT3Popper
WarriorIron|铁盔甲|Warrior|1|0|1|IronHelmet,IronChainmail,IronGreaves
WarriorGold|金盔甲|Warrior|1|0|1|GoldHelmet,GoldChainmail,GoldGreaves
WarriorShadow|暗影盔甲|Warrior|1|1|2|ShadowHelmet,ShadowScalemail,ShadowGreaves
WarriorMolten|熔岩盔甲|Warrior|1|2|3|MoltenHelmet,MoltenBreastplate,MoltenGreaves
RangerFossil|化石盔甲|Ranger|1|0|1|FossilHelm,FossilShirt,FossilPants
RangerGold|金盔甲|Ranger|1|0|1|GoldHelmet,GoldChainmail,GoldGreaves
RangerNecro|死灵盔甲|Ranger|1|1|3|NecroHelmet,NecroBreastplate,NecroGreaves
MageSilver|银盔甲|Mage|1|0|1|SilverHelmet,SilverChainmail,SilverGreaves
MageJungle|丛林盔甲|Mage|1|0|2|JungleHat,JungleShirt,JunglePants
MageMeteor|陨石盔甲|Mage|1|1|3|MeteorHelmet,MeteorSuit,MeteorLeggings
SummonerFlinx|小雪怪套装|Summoner|1|0|1|WoodHelmet,FlinxFurCoat,WoodGreaves
SummonerBee|蜜蜂盔甲|Summoner|1|1|2|BeeHeadgear,BeeBreastplate,BeeGreaves
SummonerObsidian|黑曜石盔甲|Summoner|1|1|3|ObsidianHelm,ObsidianShirt,ObsidianPants
WarriorPalladium|钯金盔甲·近战|Warrior|2|0|1|PalladiumMask,PalladiumBreastplate,PalladiumLeggings
WarriorMythril|秘银盔甲·近战|Warrior|2|1|2|MythrilHelmet,MythrilChainmail,MythrilGreaves
WarriorTitanium|钛金盔甲·近战|Warrior|2|2|3|TitaniumMask,TitaniumBreastplate,TitaniumLeggings
RangerPalladium|钯金盔甲·远程|Ranger|2|0|1|PalladiumHelmet,PalladiumBreastplate,PalladiumLeggings
RangerMythril|秘银盔甲·远程|Ranger|2|1|2|MythrilHat,MythrilChainmail,MythrilGreaves
RangerTitanium|钛金盔甲·远程|Ranger|2|2|3|TitaniumHelmet,TitaniumBreastplate,TitaniumLeggings
MagePalladium|钯金盔甲·魔法|Mage|2|0|1|PalladiumHeadgear,PalladiumBreastplate,PalladiumLeggings
MageMythril|秘银盔甲·魔法|Mage|2|1|2|MythrilHood,MythrilChainmail,MythrilGreaves
MageTitanium|钛金盔甲·魔法|Mage|2|2|3|TitaniumHeadgear,TitaniumBreastplate,TitaniumLeggings
SummonerSpider|蜘蛛盔甲|Summoner|2|0|1|SpiderMask,SpiderBreastplate,SpiderGreaves
SummonerForbidden|禁戒盔甲|Summoner|2|1|3|AncientBattleArmorHat,AncientBattleArmorShirt,AncientBattleArmorPants
WarriorHallowed|神圣盔甲·近战|Warrior|3|0|1|HallowedMask,HallowedPlateMail,HallowedGreaves
WarriorTurtle|海龟盔甲|Warrior|3|0|2|TurtleHelmet,TurtleScaleMail,TurtleLeggings
WarriorBeetle|甲虫盔甲·外壳|Warrior|3|2|3|BeetleHelmet,BeetleShell,BeetleLeggings
RangerHallowed|神圣盔甲·远程|Ranger|3|0|1|HallowedHelmet,HallowedPlateMail,HallowedGreaves
RangerShroomite|蘑菇矿盔甲·子弹|Ranger|3|1|3|ShroomiteMask,ShroomiteBreastplate,ShroomiteLeggings
RangerShroomiteBow|蘑菇矿盔甲·箭矢|Ranger|3|1|3|ShroomiteHeadgear,ShroomiteBreastplate,ShroomiteLeggings
MageHallowed|神圣盔甲·魔法|Mage|3|0|1|HallowedHeadgear,HallowedPlateMail,HallowedGreaves
MageSpectre|幽灵盔甲·面具|Mage|3|1|3|SpectreMask,SpectreRobe,SpectrePants
SummonerHallowed|神圣盔甲·召唤|Summoner|3|0|1|HallowedHood,HallowedPlateMail,HallowedGreaves
SummonerTiki|提基盔甲|Summoner|3|1|2|TikiMask,TikiShirt,TikiPants
SummonerSpooky|阴森盔甲|Summoner|3|1|3|SpookyHelmet,SpookyBreastplate,SpookyLeggings
WarriorSquire|侍卫盔甲|Warrior|3|0|2|SquireGreatHelm,SquirePlating,SquireGreaves
WarriorValhalla|英灵殿骑士盔甲|Warrior|3|2|3|SquireAltHead,SquireAltShirt,SquireAltPants
RangerHuntress|女猎人盔甲|Ranger|3|0|2|HuntressWig,HuntressJerkin,HuntressPants
RangerRedRiding|红色骑术盔甲|Ranger|3|2|3|HuntressAltHead,HuntressAltShirt,HuntressAltPants
MageApprentice|学徒盔甲|Mage|3|0|2|ApprenticeHat,ApprenticeRobe,ApprenticeTrousers
MageDarkArtist|暗黑艺术家盔甲|Mage|3|2|3|ApprenticeAltHead,ApprenticeAltShirt,ApprenticeAltPants
SummonerMonk|武僧盔甲|Summoner|3|0|2|MonkBrows,MonkShirt,MonkPants
SummonerShinobi|渗透忍者盔甲|Summoner|3|2|3|MonkAltHead,MonkAltShirt,MonkAltPants
'@
$taskDefs=[Collections.Generic.List[string]]::new()
$taskModels=[Collections.Generic.List[string]]::new()
$taskCatalog=[Collections.Generic.List[object]]::new()
foreach($row in $taskRows.Split("`n")){
 $v=$row.Trim().Split('|');if($v.Count -ne 7){throw "Invalid catalog row: $row"}
 $items=@($v[6].Split(',')|ForEach-Object{if(!$taskIds.ContainsKey($_)){throw "Unknown native ItemID symbol: $_"};$taskIds[$_]})
 $taskDefs.Add(('new EquipmentDef("{0}","{1}",HeroClass.{2},{3},{4},{5},new int[]{{{6}}}),' -f $v[0],$v[1],$v[2],$v[3],$v[4],$v[5],($items -join ',')))
 $taskModels.Add(('public sealed class {0}:TerraEquipment {{public override string GearKey=>"{0}";}}' -f $v[0]))
 $taskCatalog.Add([ordered]@{key=$v[0];name=$v[1];hero=$v[2];stage=[int]$v[3];unlock=[int]$v[4];rarity=@('普通','罕见','稀有')[[int]$v[5]-1];items=$items;nativeSymbols=$v[6]})
}
$taskSource=@'
// Generated by tools/Generate-Equipment.ps1 from audited native ItemID symbols.
using System;
using System.Linq;
using System.Collections.Generic;
namespace SpireTerraria {
 public sealed class EquipmentDef {
  public readonly string Key,Name;public readonly HeroClass Hero;public readonly int Stage,Unlock,Rarity;public readonly int[] Items;
  public bool IsArmor {get{return Items.Length==3;}}
  public EquipmentDef(string k,string n,HeroClass h,int s,int u,int r,int[] items){Key=k;Name=n;Hero=h;Stage=s;Unlock=u;Rarity=r;Items=items;}
 }
 public static class EquipmentCatalog {
  public static readonly EquipmentDef[] All=new EquipmentDef[]{
DEFINITIONS
  };
  public static EquipmentDef Get(string key){return All.First(x=>x.Key==key);}
  public static int UnlockAt(int mapY){return mapY<5?0:mapY<10?1:2;}
  public static string RarityName(int rarity){return new[]{"普通","罕见","稀有"}[rarity-1];}
  public static int ForgedStat(int original,int level){if(original<0||level<0)throw new ArgumentOutOfRangeException();return (int)Math.Min(int.MaxValue/8L,original*(5L+level)/5L);}
  public static string[] Starters(HeroClass hero){switch(hero){case HeroClass.Warrior:return new[]{"EnchantedBlade","EnchantedBoomerang","WarriorIron"};case HeroClass.Ranger:return new[]{"SilverBow","FlintlockGear","RangerFossil"};case HeroClass.Mage:return new[]{"RubyStaff","WaterBoltGear","MageSilver"};default:return new[]{"LeatherWhip","FlinxStaffGear","SummonerFlinx"};}}
 }
}
'@
$taskSource=$taskSource.Replace('DEFINITIONS',($taskDefs -join "`n"))
[IO.File]::WriteAllText((Join-Path $taskRoot 'src/EquipmentCatalog.cs'),$taskSource,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $taskRoot 'src/EquipmentCards.cs'),"// Generated fixed-identity equipment models.`nnamespace SpireTerraria {`n"+($taskModels -join "`n")+"`n}",[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Force (Join-Path $taskRoot 'content')|Out-Null
[IO.File]::WriteAllText((Join-Path $taskRoot 'content/equipment.json'),($taskCatalog|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Write-Output "Generated $($taskCatalog.Count) fixed equipment definitions."

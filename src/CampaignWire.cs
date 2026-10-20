using System;
using System.IO;
using System.Collections.Generic;

namespace SpireTerraria {
 public enum HeroClass { Warrior,Ranger,Mage,Summoner }
 public enum BattleKind { Normal,Elite,Boss,OldOnesArmy }
 // Local protocol extension. Terraria's original installation is never patched.
 public sealed class BattleSetup {
  public HeroClass Class;public BattleKind Kind;public int Stage=1,MaxLife=300,MaxMana=20,DefenseBonus,Seed=1,RoundId=1;
  public float DamageBonus=.5f;public bool NewRun=true;public int CurrentLife=-1;public int[] Accessories=new int[]{54,53,49};
  public int[] WeaponIds=new int[0],WeaponLevels=new int[0],ArmorIds=new int[0],Throwables=new int[3];public int ArmorLevel;
  public float Left=6144,Floor=4096;
  public int Width {get{return Kind==BattleKind.Normal?960:Kind==BattleKind.Elite?1600:Kind==BattleKind.OldOnesArmy?3840:3200;}}
  public int Height {get{return Kind==BattleKind.Normal?640:Kind==BattleKind.OldOnesArmy?800:1408;}}
  public float Top {get{return Floor-Height;}}
  public void Validate(){
   if((int)Class<0||(int)Class>3||(int)Kind<0||(int)Kind>2||Stage<1||Stage>3)throw new InvalidDataException("Invalid campaign class, room or stage (tower defense removed)");
   if(MaxLife<1||MaxLife>100000||MaxMana<0||MaxMana>100000||DefenseBonus<0||DefenseBonus>10000||float.IsNaN(DamageBonus)||float.IsInfinity(DamageBonus)||DamageBonus<0||DamageBonus>1000)throw new InvalidDataException("Invalid campaign stats");
   if(Accessories==null||Accessories.Length>256)throw new InvalidDataException("Invalid accessory payload");
   if(WeaponIds==null||WeaponLevels==null||WeaponIds.Length>3||WeaponIds.Length!=WeaponLevels.Length||ArmorIds==null||(ArmorIds.Length!=0&&ArmorIds.Length!=3)||ArmorLevel<0||Throwables==null||Throwables.Length!=3)throw new InvalidDataException("Invalid loadout payload");
   foreach(int n in WeaponLevels)if(n<0)throw new InvalidDataException("Invalid forge level");foreach(int n in Throwables)if(n<0||n>9999)throw new InvalidDataException("Invalid throwable stock");
   if(float.IsNaN(Left)||float.IsInfinity(Left)||float.IsNaN(Floor)||float.IsInfinity(Floor)||Left<256||Left+Width>16000||Top<256||Floor>7600)throw new InvalidDataException("Invalid arena bounds");
  }
  public BattleSetup Copy(){var s=(BattleSetup)MemberwiseClone();s.Accessories=(int[])Accessories.Clone();s.WeaponIds=(int[])WeaponIds.Clone();s.WeaponLevels=(int[])WeaponLevels.Clone();s.ArmorIds=(int[])ArmorIds.Clone();s.Throwables=(int[])Throwables.Clone();return s;}
  public void Write(BinaryWriter w){Validate();w.Write(4);w.Write((int)Class);w.Write((int)Kind);w.Write(Stage);w.Write(MaxLife);w.Write(MaxMana);w.Write(DefenseBonus);w.Write(DamageBonus);w.Write(NewRun);w.Write(Seed);w.Write(RoundId);w.Write(Left);w.Write(Floor);w.Write(CurrentLife);w.Write(Accessories.Length);foreach(int id in Accessories)w.Write(id);w.Write(WeaponIds.Length);for(int i=0;i<WeaponIds.Length;i++){w.Write(WeaponIds[i]);w.Write(WeaponLevels[i]);}w.Write(ArmorIds.Length);foreach(int id in ArmorIds)w.Write(id);w.Write(ArmorLevel);foreach(int n in Throwables)w.Write(n);}
  public static BattleSetup Read(BinaryReader r){if(r.ReadInt32()!=4)throw new InvalidDataException("Host/native loadout protocol mismatch");var s=new BattleSetup{Class=(HeroClass)r.ReadInt32(),Kind=(BattleKind)r.ReadInt32(),Stage=r.ReadInt32(),MaxLife=r.ReadInt32(),MaxMana=r.ReadInt32(),DefenseBonus=r.ReadInt32(),DamageBonus=r.ReadSingle(),NewRun=r.ReadBoolean(),Seed=r.ReadInt32(),RoundId=r.ReadInt32(),Left=r.ReadSingle(),Floor=r.ReadSingle(),CurrentLife=r.ReadInt32()};int n=r.ReadInt32();if(n<0||n>256)throw new InvalidDataException("Invalid accessories");s.Accessories=new int[n];for(int i=0;i<n;i++)s.Accessories[i]=r.ReadInt32();n=r.ReadInt32();if(n<0||n>3)throw new InvalidDataException("Invalid weapon count");s.WeaponIds=new int[n];s.WeaponLevels=new int[n];for(int i=0;i<n;i++){s.WeaponIds[i]=r.ReadInt32();s.WeaponLevels[i]=r.ReadInt32();}n=r.ReadInt32();if(n!=0&&n!=3)throw new InvalidDataException("Invalid armor");s.ArmorIds=new int[n];for(int i=0;i<n;i++)s.ArmorIds[i]=r.ReadInt32();s.ArmorLevel=r.ReadInt32();for(int i=0;i<3;i++)s.Throwables[i]=r.ReadInt32();s.Validate();return s;}
 }
 public sealed class NativeEnemy {
  public int Index,Type,Life,MaxLife,Damage;public float X,Y,Width,Height;public bool Boss;public string Name="";
  public void Write(BinaryWriter w){w.Write(Index);w.Write(Type);w.Write(Life);w.Write(MaxLife);w.Write(Damage);w.Write(X);w.Write(Y);w.Write(Width);w.Write(Height);w.Write(Boss);w.Write(Name);}
  public static NativeEnemy Read(BinaryReader r){return new NativeEnemy{Index=r.ReadInt32(),Type=r.ReadInt32(),Life=r.ReadInt32(),MaxLife=r.ReadInt32(),Damage=r.ReadInt32(),X=r.ReadSingle(),Y=r.ReadSingle(),Width=r.ReadSingle(),Height=r.ReadSingle(),Boss=r.ReadBoolean(),Name=r.ReadString()};}
 }
 public sealed class BattleState {
  public bool Started,Won,Lost;public int RoundId,Tick,Kills,Damage,Wave,CrystalLife,CrystalMaxLife,AccessoryCount,Spawned,SentryCount,SentryDamage,MinionCount,LootCount;public NativeEnemy[] Enemies=new NativeEnemy[0];
  public void Write(BinaryWriter w){w.Write(Started);w.Write(Won);w.Write(Lost);w.Write(RoundId);w.Write(Tick);w.Write(Kills);w.Write(Damage);w.Write(Wave);w.Write(CrystalLife);w.Write(CrystalMaxLife);w.Write(AccessoryCount);w.Write(Spawned);w.Write(SentryCount);w.Write(SentryDamage);w.Write(MinionCount);w.Write(LootCount);w.Write(Enemies.Length);foreach(var e in Enemies)e.Write(w);}
  public static BattleState Read(BinaryReader r){var s=new BattleState{Started=r.ReadBoolean(),Won=r.ReadBoolean(),Lost=r.ReadBoolean(),RoundId=r.ReadInt32(),Tick=r.ReadInt32(),Kills=r.ReadInt32(),Damage=r.ReadInt32(),Wave=r.ReadInt32(),CrystalLife=r.ReadInt32(),CrystalMaxLife=r.ReadInt32(),AccessoryCount=r.ReadInt32(),Spawned=r.ReadInt32(),SentryCount=r.ReadInt32(),SentryDamage=r.ReadInt32(),MinionCount=r.ReadInt32(),LootCount=r.ReadInt32()};int n=r.ReadInt32();if(n<0||n>200)throw new InvalidDataException("Invalid NPC snapshot");s.Enemies=new NativeEnemy[n];for(int i=0;i<n;i++)s.Enemies[i]=NativeEnemy.Read(r);return s;}
 }
 public static class CampaignRules {
  public static string ClassName(HeroClass c){return new[]{"战士","射手","法师","召唤师"}[(int)c];}
  public static string BattleName(BattleKind k){return new[]{"普通战斗","精英战斗","Boss 战斗","远古之民"}[(int)k];}
  public static int BaseLife(HeroClass c){return new[]{300,260,240,260}[(int)c];}
  public static BattleSetup Starter(HeroClass c,BattleKind kind,int stage){return new BattleSetup{Class=c,Kind=kind,Stage=stage,MaxLife=BaseLife(c)*stage,MaxMana=new[]{20,20,160,100}[(int)c],DamageBonus=.5f,Accessories=stage==1?new[]{54,53,49}:new[]{405,53,49,1162}};}
  public static bool IsAccessoryAllowed(int id,HeroClass c){
   if(id==490)return c==HeroClass.Warrior;if(id==491||id==1321)return c==HeroClass.Ranger;if(id==489||id==1595||id==982||id==555)return c==HeroClass.Mage;if(id==2998||id==1158)return c==HeroClass.Summoner;if(id==211||id==536)return c==HeroClass.Warrior||c==HeroClass.Summoner;
   return Array.IndexOf(new[]{54,53,49,216,285,212,3212,193,935,405,898,1862,5000,1162,3809,3810,3811,3812,156,397,554,862,485},id)>=0;
  }
  public static int ClassEmblem(HeroClass c){return new[]{490,491,489,2998}[(int)c];}
  public static int[] Weapons(HeroClass c,int stage=1){
   if(stage==2)return c==HeroClass.Warrior?new[]{3054,1199}:c==HeroClass.Ranger?new[]{3788,3029}:c==HeroClass.Mage?new[]{3209,3787}:new[]{4911,2551};
   if(stage==3)return c==HeroClass.Warrior?new[]{757,1513}:c==HeroClass.Ranger?new[]{2624,533}:c==HeroClass.Mage?new[]{2622,1266}:new[]{4679,2621};
   return c==HeroClass.Warrior?new[]{989,55}:c==HeroClass.Ranger?new[]{3510,95}:c==HeroClass.Mage?new[]{743,165}:new[]{4672,5069};
  }
  public static int[] Armor(HeroClass c,int stage=1){
   if(stage==2)return c==HeroClass.Summoner?new[]{2370,2371,2372}:new[]{c==HeroClass.Warrior?1215:c==HeroClass.Ranger?1216:1217,1218,1219};
   if(stage==3)return c==HeroClass.Warrior?new[]{1316,1317,1318}:c==HeroClass.Ranger?new[]{1546,1549,1550}:c==HeroClass.Mage?new[]{2189,1504,1505}:new[]{1159,1160,1161};
   return c==HeroClass.Warrior?new[]{231,232,233}:c==HeroClass.Ranger?new[]{151,152,153}:c==HeroClass.Mage?new[]{228,229,230}:new[]{3266,3267,3268};
  }
  public static string WeaponHelp(HeroClass c,int stage){
   string[] first={"1 附魔剑   2 附魔回旋镖","1 银弓   2 燧发枪","1 红玉法杖   2 水矢","1 皮鞭   2 小雪怪法杖"};
   string[] second={"1 暗影焰刀   2 钛金剑","1 玛瑙爆破枪   2 代达罗斯风暴弓","1 水晶蛇   2 裂天剑","1 冷鞭   2 蜘蛛法杖"};
   string[] third={"1 泰拉刃   2 圣骑士锤","1 海啸   2 巨兽鲨","1 利刃台风   2 磁球","1 晨星   2 暴风雨法杖"};
   return (stage==1?first:stage==2?second:third)[(int)c]+"   3 "+new[]{"弩车","爆炸机关","烈焰爆破","闪电光环"}[(int)c]+"哨兵";
  }
  public static void Upgrade(BattleSetup s,int stat,int percent,int actualDefense=0){
   if(percent<30||percent>100)throw new ArgumentOutOfRangeException("percent");float factor=1+percent/100f;
   if(stat==0)s.DamageBonus=(1+s.DamageBonus)*factor-1;
   else if(stat==1)s.MaxLife=(int)Math.Ceiling(s.MaxLife*factor);
   else if(stat==2){if(actualDefense<=0)throw new ArgumentOutOfRangeException("actualDefense");s.DefenseBonus+=(int)Math.Ceiling(actualDefense*percent/100f);}
   else if(stat==3)s.MaxMana=(int)Math.Ceiling(s.MaxMana*factor);
   else throw new ArgumentOutOfRangeException("stat");s.Validate();
  }
  public static int[] NormalizeAccessories(int[] items,HeroClass c){
   var list=new List<int>();int bestBoot=0,rank=0;
   foreach(int id in items){if(!IsAccessoryAllowed(id,c))throw new InvalidDataException("Accessory is outside this class's tested pool: "+id);if(list.Contains(id))continue;int r=Array.IndexOf(new[]{54,405,898,1862,5000},id)+1;if(r>0){if(r>rank){rank=r;bestBoot=id;}}else list.Add(id);}
   if(bestBoot>0)list.Insert(0,bestBoot);return list.ToArray();
  }
 }
}

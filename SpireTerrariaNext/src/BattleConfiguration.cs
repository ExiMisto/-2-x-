using System;
using System.IO;

namespace SpireTerraria.Next {
 internal sealed class BattleConfiguration {
  internal int Seed,Stage=1,Life=300,LifeMax=300,ArmorLevel,EnemyCount,MaxConcurrent;
  internal string RoomKind="normal",SceneKey="forest-outpost",BossSymbol="",ArmorAffix="";
  internal string[] Weapons=new[]{"WoodenSword"},WeaponAffixes=new[]{""},Armor=new string[0],Accessories=new string[0],Consumables=new string[0];
  internal int[] WeaponLevels=new[]{0};
  internal void Validate() {
   if(Stage<1||Stage>3||(RoomKind!="normal"&&RoomKind!="elite"&&RoomKind!="boss"))throw new InvalidDataException("Unsupported adventure stage or room kind.");
   if(LifeMax<1||LifeMax>100000||Life<1||Life>LifeMax||ArmorLevel<0||ArmorLevel>1000)throw new InvalidDataException("Invalid life or armor level.");
   if(Weapons==null||Weapons.Length<1||Weapons.Length>3||WeaponLevels==null||WeaponLevels.Length!=Weapons.Length||WeaponAffixes==null||WeaponAffixes.Length!=Weapons.Length)throw new InvalidDataException("Expected one to three complete weapon entries.");
   if(Armor==null||Armor.Length>3||Accessories==null||Accessories.Length>64||Consumables==null||Consumables.Length>64||EnemyCount<0||EnemyCount>40||MaxConcurrent<0||MaxConcurrent>12)throw new InvalidDataException("Invalid equipment or encounter size.");
   Check(SceneKey,64);Check(BossSymbol,64);Check(ArmorAffix,64);foreach(string value in Weapons)Check(value,128);foreach(string value in WeaponAffixes)Check(value,64);foreach(string value in Armor)Check(value,128);foreach(string value in Accessories)Check(value,128);foreach(string value in Consumables)Check(value,128);
   for(int i=0;i<WeaponLevels.Length;i++)if(WeaponLevels[i]<0||WeaponLevels[i]>1000)throw new InvalidDataException("Invalid weapon upgrade level.");
  }
  static void Check(string value,int length){if(value==null||value.Length>length)throw new InvalidDataException("Missing or oversized battle configuration symbol.");}
  internal void Write(BinaryWriter writer) {
   Validate();writer.Write(Seed);writer.Write(Stage);writer.Write(RoomKind);writer.Write(SceneKey);writer.Write(BossSymbol);writer.Write(Life);writer.Write(LifeMax);
   writer.Write(Weapons.Length);for(int i=0;i<Weapons.Length;i++){writer.Write(Weapons[i]);writer.Write(WeaponLevels[i]);writer.Write(WeaponAffixes[i]);}
   WriteStrings(writer,Armor);writer.Write(ArmorLevel);writer.Write(ArmorAffix);WriteStrings(writer,Accessories);writer.Write(EnemyCount);writer.Write(MaxConcurrent);WriteStrings(writer,Consumables);
  }
  internal static BattleConfiguration Read(BinaryReader reader) {
   var value=new BattleConfiguration{Seed=reader.ReadInt32(),Stage=reader.ReadInt32(),RoomKind=reader.ReadString(),SceneKey=reader.ReadString(),BossSymbol=reader.ReadString(),Life=reader.ReadInt32(),LifeMax=reader.ReadInt32()};
   int count=reader.ReadInt32();if(count<1||count>3)throw new InvalidDataException("Expected one to three weapons.");value.Weapons=new string[count];value.WeaponLevels=new int[count];value.WeaponAffixes=new string[count];
   for(int i=0;i<count;i++){value.Weapons[i]=reader.ReadString();value.WeaponLevels[i]=reader.ReadInt32();value.WeaponAffixes[i]=reader.ReadString();}
   value.Armor=ReadStrings(reader,3);value.ArmorLevel=reader.ReadInt32();value.ArmorAffix=reader.ReadString();value.Accessories=ReadStrings(reader,64);value.EnemyCount=reader.ReadInt32();value.MaxConcurrent=reader.ReadInt32();value.Consumables=ReadStrings(reader,64);value.Validate();return value;
  }
  static void WriteStrings(BinaryWriter writer,string[] values){writer.Write(values.Length);foreach(string value in values)writer.Write(value);}
  static string[] ReadStrings(BinaryReader reader,int maximum){int count=reader.ReadInt32();if(count<0||count>maximum)throw new InvalidDataException("Oversized equipment list.");var values=new string[count];for(int i=0;i<count;i++)values[i]=reader.ReadString();return values;}
 }
 internal sealed class AdvancedBattleState {
  internal int Tick,Life,LifeMax,EnemyLife,EnemyLifeMax,Projectiles;
  internal float PlayerX,PlayerY,EnemyX,EnemyY;
  internal bool Dead,EnemyActive;
  internal long Frame;
  internal int Alive,Remaining,Waves,Wave,Recovered,SelectedWeapon;
  internal float CameraX,CameraY,ArenaLeft,ArenaRight,FloorY;
  internal string Title="";
  internal int Mana,MaxMana,PotionDelay,ConsumingSlot=-1;
  internal int[] ConsumedSlots=new int[0];
  internal void Write(BinaryWriter writer) {
   writer.Write(Tick);writer.Write(Life);writer.Write(LifeMax);writer.Write(PlayerX);writer.Write(PlayerY);writer.Write(Dead);writer.Write(EnemyLife);writer.Write(EnemyLifeMax);writer.Write(EnemyX);writer.Write(EnemyY);writer.Write(EnemyActive);writer.Write(Projectiles);writer.Write(Frame);
   writer.Write(Alive);writer.Write(Remaining);writer.Write(Waves);writer.Write(Wave);writer.Write(Recovered);writer.Write(CameraX);writer.Write(CameraY);writer.Write(ArenaLeft);writer.Write(ArenaRight);writer.Write(FloorY);writer.Write(SelectedWeapon);writer.Write(Title??"");writer.Write(ConsumedSlots.Length);foreach(int slot in ConsumedSlots)writer.Write(slot);
   writer.Write(Mana);writer.Write(MaxMana);writer.Write(PotionDelay);writer.Write(ConsumingSlot);
  }
  internal static AdvancedBattleState Read(BinaryReader reader) {
   var state=new AdvancedBattleState{Tick=reader.ReadInt32(),Life=reader.ReadInt32(),LifeMax=reader.ReadInt32(),PlayerX=reader.ReadSingle(),PlayerY=reader.ReadSingle(),Dead=reader.ReadBoolean(),EnemyLife=reader.ReadInt32(),EnemyLifeMax=reader.ReadInt32(),EnemyX=reader.ReadSingle(),EnemyY=reader.ReadSingle(),EnemyActive=reader.ReadBoolean(),Projectiles=reader.ReadInt32(),Frame=reader.ReadInt64(),Alive=reader.ReadInt32(),Remaining=reader.ReadInt32(),Waves=reader.ReadInt32(),Wave=reader.ReadInt32(),Recovered=reader.ReadInt32(),CameraX=reader.ReadSingle(),CameraY=reader.ReadSingle(),ArenaLeft=reader.ReadSingle(),ArenaRight=reader.ReadSingle(),FloorY=reader.ReadSingle(),SelectedWeapon=reader.ReadInt32(),Title=reader.ReadString()};int count=reader.ReadInt32();if(count<0||count>64)throw new InvalidDataException("Invalid consumed-item list.");state.ConsumedSlots=new int[count];for(int i=0;i<count;i++)state.ConsumedSlots[i]=reader.ReadInt32();
   if(reader.BaseStream.Position<reader.BaseStream.Length){state.Mana=reader.ReadInt32();state.MaxMana=reader.ReadInt32();state.PotionDelay=reader.ReadInt32();state.ConsumingSlot=reader.ReadInt32();}return state;
  }
 }
}

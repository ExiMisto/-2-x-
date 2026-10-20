using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.GameContent.Events;
using TerrariaICEY.Bridge;
using SpireTerraria;
using TMain=Terraria.Main;

static class SpireRuntime {
 internal static bool Enabled;
 internal static BattleSetup Current {get{return setup;}}
 static BattleSetup setup,pending;static BattleState state=new BattleState();static int carryLife;
 static readonly List<NPC> roots=new List<NPC>();static readonly HashSet<NPC> deadRoots=new HashSet<NPC>();
 static readonly List<Item> accessories=new List<Item>();
 static readonly MethodInfo functional=AccessTools.Method(typeof(Player),"ApplyEquipFunctional"),armor=AccessTools.Method(typeof(Player),"GrantArmorBenefits"),prefix=AccessTools.Method(typeof(Player),"GrantPrefixBenefits");
 static readonly bool tests=Array.IndexOf(Environment.GetCommandLineArgs(),"--spire-tests")>=0;
 internal static void Install(Harmony h){
  h.Patch(AccessTools.Method(typeof(Player),"ResetEffects"),postfix:new HarmonyMethod(typeof(SpireRuntime),"BaseStats"));
  h.Patch(AccessTools.Method(typeof(Player),"UpdateEquips"),prefix:new HarmonyMethod(typeof(SpireRuntime),"ExtraAccessories"),postfix:new HarmonyMethod(typeof(SpireRuntime),"CombatStats"));
  h.Patch(AccessTools.Method(typeof(NPC),"checkDead"),postfix:new HarmonyMethod(typeof(SpireRuntime),"NpcDied"));
  // Rewards are generated from the selected class's pool, rather than raw drops.
  h.Patch(AccessTools.Method(typeof(NPC),"NPCLoot"),prefix:new HarmonyMethod(typeof(SpireRuntime),"AllowLoot"));
  h.Patch(AccessTools.Method(typeof(NativeExpansion),"DrawWorld"),prefix:new HarmonyMethod(typeof(SpireScenery),"Draw"));
 }
 static bool AllowLoot(NPC __instance){return !Enabled||setup==null;}
 static void BaseStats(Player __instance){if(!Enabled||setup==null)return;__instance.statLifeMax2=setup.MaxLife;__instance.statManaMax2=setup.MaxMana;}
 static void ExtraAccessories(Player __instance){
  if(!Enabled||setup==null)return;
  // A tested list outside armor[3..9]. Apply before UpdateEquips so native
  // aggregate flags (including dd2Accessory) are resolved exactly once.
  foreach(var item in accessories){prefix.Invoke(__instance,new object[]{item});armor.Invoke(__instance,new object[]{item});functional.Invoke(__instance,new object[]{3,item});}
 }
 static void CombatStats(Player __instance){
  if(!Enabled||setup==null)return;float bonus=setup.DamageBonus;
  if(setup.Class==HeroClass.Warrior)__instance.meleeDamage*=1+bonus;
  if(setup.Class==HeroClass.Ranger)__instance.rangedDamage*=1+bonus;
  if(setup.Class==HeroClass.Mage)__instance.magicDamage*=1+bonus;
  if(setup.Class==HeroClass.Summoner){__instance.minionDamage*=1+bonus;__instance.maxMinions=Math.Max(2,__instance.maxMinions);}
  __instance.statDefense+=setup.DefenseBonus;
 }
 static void NpcDied(NPC __instance){
  if(!Enabled||setup==null||__instance.life>0||__instance.active)return;
  if(roots.Contains(__instance)&&deadRoots.Add(__instance))state.Kills++;
 }
 internal static bool Command(byte command,BinaryReader r,BinaryWriter w){
  if(command==80){var next=BattleSetup.Read(r);next.Accessories=CampaignRules.NormalizeAccessories(next.Accessories,next.Class);carryLife=next.CurrentLife>=0?next.CurrentLife:Enabled&&!next.NewRun?TMain.LocalPlayer.statLife:next.MaxLife;pending=next;Enabled=true;w.Write((byte)80);w.Flush();return true;}
  if(command==81){w.Write((byte)81);Snapshot().Write(w);w.Flush();return true;}
  if(command==82){
   if(!tests)throw new InvalidOperationException("Native fixture command is disabled");
   foreach(var n in TMain.npc)if(IsEnemy(n)&&!n.dontTakeDamage)n.StrikeNPC(1000000,0,1);
   w.Write((byte)82);w.Flush();return true;
  }
  if(command==83){if(!tests)throw new InvalidOperationException("Asset export is a build-time fixture");int count=SpireScenery.ExportAssets();w.Write((byte)83);w.Write(count);w.Flush();return true;}
  if(command==84){int kind=r.ReadInt32();var p=TMain.LocalPlayer;bool used=false;if(Enabled&&setup!=null&&!state.Won&&!state.Lost){if(kind==0&&p.potionDelay<=0&&p.statLife<p.statLifeMax2){p.statLife=Math.Min(p.statLifeMax2,p.statLife+100);p.AddBuff(BuffID.PotionSickness,3600);p.potionDelay=3600;used=true;}if(kind==1&&p.statMana<p.statManaMax2){p.statMana=Math.Min(p.statManaMax2,p.statMana+100);p.AddBuff(BuffID.ManaSickness,300);used=true;}}w.Write((byte)84);w.Write(used);w.Flush();return true;}
  if(command==85){if(!tests)throw new InvalidOperationException("Build fixture disabled");SpireScenery.ArtMode=r.ReadInt32();if(SpireScenery.ArtMode==2)foreach(var projectile in TMain.projectile)projectile.active=false;w.Write((byte)85);w.Flush();return true;}
  if(command==86){ClearRoom();w.Write((byte)86);w.Flush();return true;}
  if(command==87){if(!tests)throw new InvalidOperationException("Build fixture disabled");int at=Array.FindIndex(TMain.item,x=>!x.active);if(at<0)throw new InvalidOperationException("No fixture drop slot");TMain.item[at].inner.SetDefaults(ItemID.Gel);TMain.item[at].stack=3;TMain.item[at].position=TMain.LocalPlayer.position;w.Write((byte)87);w.Write(at);w.Flush();return true;}
  return false;
 }
 internal static void BeforeStep(EngineInput input,Player player){
  if(!Enabled)return;
  if(pending!=null){
   if(!input.FullReset)throw new InvalidOperationException("A new encounter requires a full reset");
   if(DD2Event.Ongoing)DD2Event.StopInvasion();ClearRoom();
   setup=pending;pending=null;state=new BattleState{RoundId=setup.RoundId};roots.Clear();deadRoots.Clear();accessories.Clear();
   // The worker replaces Main's scene-filter registry during initialization.
   // Original crystal destruction/victory projectiles still address these keys.
   foreach(string name in new[]{"CrystalDestructionVortex","CrystalDestructionColor","CrystalWin"})
    Terraria.Graphics.Effects.Filters.Scene[name]=new Terraria.Graphics.Effects.Filter(new Terraria.Graphics.Shaders.ScreenShaderData("Filter"+name));
   foreach(int id in setup.Accessories){var item=new Item();item.SetDefaults(id);if(!item.accessory)throw new InvalidDataException("Not an accessory: "+id);accessories.Add(item);}
   state.AccessoryCount=accessories.Count;
   World(player);SpireScenery.Prepare(setup);ApplyGear(player);player.position=new Vector2(setup.Left+setup.Width*.5f,setup.Floor-player.height);player.velocity=Vector2.Zero;player.fallStart=(int)(player.position.Y/16);
   player.statLifeMax2=setup.MaxLife;player.statManaMax2=setup.MaxMana;player.statLife=Math.Max(1,Math.Min(setup.MaxLife,carryLife));player.statMana=setup.MaxMana;
   state.Started=true;NativeExpansion.DamageDealt=0;
   int[] types=EncounterTypes();
   for(int i=0;i<types.Length;i++){
    float x=setup.Left+setup.Width*(i+1f)/(types.Length+1f);if(Math.Abs(x-player.Center.X)<180)x+=i%2==0?230:-230;x=MathHelper.Clamp(x,setup.Left+80,setup.Left+setup.Width-80);
    int at=Spawn(types[i],x,setup.Floor-(setup.Kind==BattleKind.Normal?100:240));
    if(setup.Kind==BattleKind.Elite){var n=TMain.npc[at];n.boss=true;n.lifeMax=Math.Max(200,(int)(n.lifeMax*.55f));n.life=n.lifeMax;n.damage=Math.Max(10,(int)(n.damage*.7f));}
   }
   if(setup.Class==HeroClass.Summoner)SummonMinions(player);
  }
  if(setup!=null){World(player);foreach(var item in TMain.item)if(item.active)item.TurnToAir();}
 }
 internal static void ClearRoom(){foreach(var item in TMain.item)item.TurnToAir();foreach(var p in TMain.projectile)p.active=false;foreach(var d in TMain.dust)d.active=false;foreach(var g in TMain.gore)g.active=false;}
 static int[] EncounterTypes(){
  int variant=(setup.Seed&int.MaxValue)%5;
  if(setup.Kind==BattleKind.Boss)return setup.Stage==1?new[]{(int)NPCID.SkeletronHead}:setup.Stage==2?new[]{(int)NPCID.SkeletronPrime}:new[]{(int)NPCID.DukeFishron};
  if(setup.Kind==BattleKind.Elite)return setup.Stage==1?new[]{(int)(variant%2==0?NPCID.KingSlime:NPCID.EyeofCthulhu)}:setup.Stage==2?new[]{(int)NPCID.QueenSlimeBoss}:new[]{(int)NPCID.Pumpking};
  int[][] pools=setup.Stage==1?new[]{new[]{1,3,2},new[]{21,49,21},new[]{26,111,28},new[]{42,51,42},new[]{173,3,6}}:
   setup.Stage==2?new[]{new[]{140,75,140},new[]{77,110,93},new[]{82,174,75},new[]{250,140,75},new[]{176,174,77}}:
   new[]{new[]{269,292,288},new[]{198,199,176},new[]{281,269,93},new[]{283,292,269},new[]{290,176,269}};
  return pools[variant];
 }
 static void ApplyGear(Player p){
  for(int n=0;n<50;n++)p.inventory[n].TurnToAir();for(int n=0;n<20;n++)p.armor[n].TurnToAir();
  var weapons=setup.WeaponIds.Length>0?setup.WeaponIds:CampaignRules.Weapons(setup.Class,setup.Stage);for(int n=0;n<weapons.Length;n++){p.inventory[n].SetDefaults(weapons[n]);p.inventory[n].stack=1;if(n<setup.WeaponLevels.Length)p.inventory[n].damage=EquipmentCatalog.ForgedStat(p.inventory[n].damage,setup.WeaponLevels[n]);}
  // Consumables are owned by the Spire potion bar, not a second free inventory.
  for(int n=0;n<3;n++)if(setup.Throwables[n]>0){p.inventory[n+3].SetDefaults(new[]{168,166,167}[n]);p.inventory[n+3].stack=setup.Throwables[n];}
  p.inventory[54].SetDefaults(40);p.inventory[54].stack=9999;p.inventory[55].SetDefaults(97);p.inventory[55].stack=9999;
  var equipment=setup.ArmorIds.Length==3?setup.ArmorIds:CampaignRules.Armor(setup.Class,setup.Stage);int baseDefense=0;for(int n=0;n<3;n++){p.armor[n].SetDefaults(equipment[n]);baseDefense+=p.armor[n].defense;}
  // Round the whole-set bonus once, without amplifying accessory/set-bonus defense.
  p.armor[0].defense+=EquipmentCatalog.ForgedStat(baseDefense,setup.ArmorLevel)-baseDefense;
 }
 static void SummonMinions(Player p){
  var staff=p.inventory.Take(3).LastOrDefault(x=>x.shoot>0&&x.buffType>0);if(staff==null)return;p.AddBuff(staff.buffType,3600);int damage=staff.damage;
  for(int n=0;n<2;n++)Projectile.NewProjectile(p.GetProjectileSource_Item(staff),p.Center+new Vector2(n*24,-32),Vector2.Zero,staff.shoot,damage,staff.knockBack,p.whoAmI);
 }
 static int Spawn(int type,float x,float bottom){int at=NPC.NewNPC(TMain.LocalPlayer.GetNPCSource_TileInteraction((int)x/16,(int)bottom/16),(int)x,(int)bottom,type,Target:0);if(at<0||at>=TMain.npc.Length)throw new InvalidOperationException("Native NPC capacity exhausted");var n=TMain.npc[at];n.Bottom=new Vector2(x,bottom);n.timeLeft=3600;roots.Add(n);state.Spawned++;return at;}
 internal static void World(Player p){
  if(!Enabled||setup==null)return;
  // The upstream harness boots Master mode for its own progression. A zero-
  // ascension Spire expedition uses Terraria's Classic encounter statistics.
  TMain.GameMode=0;
  TMain.dayTime=false;TMain.time=16200;TMain.hardMode=setup.Stage>=2;TMain.worldSurface=350;TMain.rockLayer=400;
  // OOA sentries must work in ordinary rooms after removing the invasion mode.
  // This is the worker's isolated player, not a saved Terraria character.
  p.downedDD2EventAnyDifficulty=true;
  p.ZoneCorrupt=p.ZoneCrimson=p.ZoneHallow=p.ZoneSnow=p.ZoneJungle=p.ZoneDungeon=false;
  NPC.downedMechBossAny=setup.Stage>=3;NPC.downedGolemBoss=setup.Stage>=3;
 }
 internal static void AfterStep(EngineInput input,Player p){
  if(!Enabled||setup==null||input.Paused||state.Won||state.Lost)return;state.Tick++;
  // Each encounter has fixed boundaries; a despawn is never a successful kill.
  foreach(var n in TMain.npc)if(IsEnemy(n))n.timeLeft=Math.Max(n.timeLeft,600);
  if(roots.Count>0){
   if(deadRoots.Count==roots.Count)state.Won=true;
   else if(roots.Any(n=>!n.active&&!deadRoots.Contains(n)))state.Lost=true;
  }
  if(p.dead||p.statLife<=0){state.Lost=true;state.Won=false;}
  // Gear changes never refill life or mana each frame.
  p.statLife=Math.Min(p.statLife,p.statLifeMax2);
 }
 static bool IsEnemy(NPC n){return n.active&&!n.friendly&&!n.townNPC&&!NativeSession.IsProxy(n)&&n.type!=NPCID.DD2LanePortal&&n.type!=NPCID.DD2EterniaCrystal;}
 static BattleState Snapshot(){
  if(!Enabled||setup==null)return state;
  state.Damage=NativeExpansion.DamageDealt;state.LootCount=TMain.item.Count(x=>x.active);var enemies=new List<NativeEnemy>();state.CrystalLife=state.CrystalMaxLife=0;
  foreach(var n in TMain.npc){
   if(n.active&&n.type==NPCID.DD2EterniaCrystal){state.CrystalLife=n.life;state.CrystalMaxLife=n.lifeMax;}
   if(IsEnemy(n))enemies.Add(new NativeEnemy{Index=n.whoAmI,Type=n.type,Life=n.life,MaxLife=n.lifeMax,Damage=n.damage,X=n.position.X,Y=n.position.Y,Width=n.width,Height=n.height,Boss=n.boss,Name=n.FullName});
  }
  state.Enemies=enemies.ToArray();state.SentryCount=state.SentryDamage=state.MinionCount=0;
  foreach(var s in TMain.projectile)if(s.active&&s.owner==0){if(s.sentry){state.SentryCount++;state.SentryDamage=Math.Max(state.SentryDamage,s.damage);}if(s.minion)state.MinionCount++;}
  return state;
 }
}

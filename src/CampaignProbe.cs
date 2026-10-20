using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using TerrariaICEY.Bridge;
using SpireTerraria;

// Exercises the real worker. Fixture strikes only finish live encounters.
class CampaignProbe {
 static readonly List<object> results=new List<object>();
 static int checks,fixtureStrikes;
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static BattleSetup Setup(HeroClass hero,BattleKind kind,int stage){
  var s=CampaignRules.Starter(hero,kind,stage);var keys=EquipmentCatalog.Starters(hero);
  s.WeaponIds=keys.Select(EquipmentCatalog.Get).Where(x=>!x.IsArmor).Select(x=>x.Items[0]).ToArray();
  s.WeaponLevels=new int[s.WeaponIds.Length];s.ArmorIds=EquipmentCatalog.Get(keys.Last()).Items.ToArray();return s;
 }
 static EngineOutput Start(NativeBridge b,BattleSetup s){b.Configure(s);var o=b.Step(Arena.CampaignInput(s,true));for(int t=0;t<3;t++)o=b.Step(Arena.CampaignInput(s,false));return o;}
 static EngineOutput Hold(NativeBridge b,BattleSetup s,int slot){EngineOutput o=null;for(int n=0;n<3;n++){var i=Arena.CampaignInput(s,false);i.Selected=slot;o=b.Step(i);}return o;}
 static string Signature(BattleState s){return string.Join(",",s.Enemies.Select(x=>x.Type).OrderBy(x=>x));}
 static void Catalog(){
  Require(EquipmentCatalog.All.Select(x=>x.Key).Distinct().Count()==EquipmentCatalog.All.Length,"equipment identities are unique");
  foreach(HeroClass c in Enum.GetValues(typeof(HeroClass)))foreach(int stage in new[]{1,2,3}){
   var pool=EquipmentCatalog.All.Where(x=>x.Hero==c&&x.Stage==stage).ToArray();
   Require(pool.Count(x=>!x.IsArmor)>=5&&pool.Any(x=>x.IsArmor),"stage/class equipment diversity "+c+" stage="+stage);
   Require(pool.Where(x=>!x.IsArmor).Select(x=>x.Rarity).Distinct().Count()==3,"common uncommon rare weapons "+c+" stage="+stage);
  }
  Require(EquipmentCatalog.ForgedStat(100,1)==120&&EquipmentCatalog.ForgedStat(100,2)==140&&EquipmentCatalog.ForgedStat(100,50)==1100,"forge adds twenty percent of original per level");
  Require(EquipmentCatalog.ForgedStat(100,int.MaxValue)>0,"extreme forge level cannot overflow negative");
  bool rejected=false;try{Setup(HeroClass.Warrior,BattleKind.OldOnesArmy,1).Validate();}catch(InvalidDataException){rejected=true;}
  Require(rejected,"removed tower defense cannot enter native protocol");
 }
 static void Classes(NativeBridge b){
  foreach(HeroClass c in Enum.GetValues(typeof(HeroClass))){
   var s=Setup(c,BattleKind.Normal,1);s.Seed=0;var o=Start(b,s);var state=b.Battle();
   Require(o.MaxLife==CampaignRules.BaseLife(c)&&o.MaxMana>=s.MaxMana,"base native life and mana class="+c);
   Require(o.InventoryIds.Take(s.WeaponIds.Length).SequenceEqual(s.WeaponIds),"actual fixed starter weapons class="+c);
   Require(state.Enemies.Length==3&&state.Spawned==3&&state.CrystalMaxLife==0,"ordinary encounter has enemies and no tower objective class="+c);
   if(c==HeroClass.Summoner)Require(state.MinionCount>=2,"equipped summon staff creates native minions");
   int maxShots=0;var initialPositions=state.Enemies.ToDictionary(n=>n.Index,n=>n.X);bool moving=false;
   for(int t=0;t<1800;t++){
    state=b.Battle();if(state.Won||state.Lost)break;var i=Arena.CampaignInput(s,false);
    var target=state.Enemies.OrderBy(n=>Math.Abs(n.X-o.X)).FirstOrDefault();
    if(target!=null){float d=target.X-o.X;float distance=c==HeroClass.Warrior?55:c==HeroClass.Summoner?115:190;i.Right=d>distance;i.Left=d< -distance;i.MouseX=target.X+target.Width*.5f;i.MouseY=target.Y+target.Height*.5f;i.Use=t%45!=44;}
    i.Jump=t%150<18;o=b.Step(i);maxShots=Math.Max(maxShots,o.Projectiles.Length);moving|=state.Enemies.Any(n=>initialPositions.ContainsKey(n.Index)&&Math.Abs(n.X-initialPositions[n.Index])>10);
   }
   state=b.Battle();Require(state.Damage>0,"real native weapon hits class="+c);Require(moving,"native enemies move class="+c);
   if(!state.Won&&!state.Lost){b.TestStrikeEnemies();fixtureStrikes++;o=b.Step(Arena.CampaignInput(s,false));state=b.Battle();}
   Require(state.Won&&!state.Lost,"victory resolves after all real enemies die class="+c);
   b.Render(Arena.CampaignRender(s,o));b.Export("campaign-"+c+".png");
   results.Add(new{test="class",hero=c.ToString(),won=state.Won,damage=state.Damage,kills=state.Kills,maxShots,life=o.Life,maxLife=o.MaxLife});
  }
 }
 static void Equipment(NativeBridge b){
  var s=Setup(HeroClass.Mage,BattleKind.Normal,1);s.WeaponIds=new[]{743,165,127};s.WeaponLevels=new int[3];
  Start(b,s);int first=Hold(b,s,0).HeldDamage,second=Hold(b,s,1).HeldDamage,third=Hold(b,s,2).HeldDamage,defense=Hold(b,s,0).Defense;
  s.WeaponLevels[0]=2;Start(b,s);int upgraded=Hold(b,s,0).HeldDamage;
  Require(Math.Abs(upgraded-first*1.4f)<3,"plus two increases only selected item damage by forty percent");
  Require(Hold(b,s,1).HeldDamage==second&&Hold(b,s,2).HeldDamage==third,"switching weapons does not leak forge bonus");
  Require(Hold(b,s,0).Defense==defense,"weapon forge does not increase armor");
  s.WeaponLevels[0]=50;Start(b,s);Require(Math.Abs(Hold(b,s,0).HeldDamage-first*11f)<20,"unlimited forge levels use additive growth");
  s.WeaponLevels[0]=0;s.ArmorLevel=2;Start(b,s);Require(Hold(b,s,0).Defense>defense&&Hold(b,s,1).HeldDamage==second,"armor forge raises defense without weapon damage");
  s.ArmorLevel=0;s.WeaponLevels[0]=2;int[] ids=(int[])s.WeaponIds.Clone();
  foreach(int stage in new[]{1,2,3}){s.Stage=stage;s.MaxLife=CampaignRules.BaseLife(s.Class)*stage;var o=Start(b,s);Require(o.MaxLife==240*stage,"stage life grows from initial life stage="+stage);Require(o.InventoryIds.Take(3).SequenceEqual(ids),"changing stage preserves equipped item identities stage="+stage);Require(Hold(b,s,0).HeldDamage==upgraded,"changing stage preserves individual forge level stage="+stage);}
  results.Add(new{test="equipment",baseDamage=first,plusTwo=upgraded,otherWeapon=second,thirdWeapon=third,baseDefense=defense});
 }
 static void Encounters(NativeBridge b){
  var unions=new List<HashSet<int>>();
  foreach(int stage in new[]{1,2,3}){
   var signatures=new HashSet<string>();var seen=new HashSet<int>();
   for(int seed=0;seed<5;seed++){var s=Setup(HeroClass.Ranger,BattleKind.Normal,stage);s.Seed=seed;Start(b,s);var state=b.Battle();Require(state.Enemies.Length==3&&state.CrystalMaxLife==0&&state.Wave==0,"stage random room has three enemies without tower defense stage="+stage+" seed="+seed);signatures.Add(Signature(state));foreach(var n in state.Enemies)seen.Add(n.Type);}
   Require(signatures.Count>=4&&seen.Count>=7,"seeded stage enemy pool varies stage="+stage);
   var repeat=Setup(HeroClass.Ranger,BattleKind.Normal,stage);repeat.Seed=3;Start(b,repeat);string a=Signature(b.Battle());Start(b,repeat);Require(Signature(b.Battle())==a,"encounter seed reproduces same composition stage="+stage);
   unions.Add(seen);results.Add(new{test="encounters",stage,compositions=signatures.ToArray(),types=seen.OrderBy(x=>x).ToArray()});
   var boss=Setup(HeroClass.Mage,BattleKind.Boss,stage);var o=Start(b,boss);Require(b.Battle().Enemies.Any(n=>n.Boss)&&b.Battle().CrystalMaxLife==0,"real stage boss without crystal stage="+stage);Require(boss.Width>repeat.Width&&boss.Height>repeat.Height,"boss arena is larger stage="+stage);if(stage==1){b.Render(Arena.CampaignRender(boss,o));b.Export("campaign-boss.png");}
   var elite=Setup(HeroClass.Mage,BattleKind.Elite,stage);Start(b,elite);Require(b.Battle().Enemies.Any(n=>n.Boss)&&b.Battle().CrystalMaxLife==0,"elite is a weak boss without tower waves stage="+stage);
  }
  Require(!unions[0].SetEquals(unions[1])&&!unions[1].SetEquals(unions[2]),"three stages have different enemy pools");
 }
 static void ConsumablesAndCleanup(NativeBridge b){
  var s=Setup(HeroClass.Warrior,BattleKind.Normal,1);s.WeaponIds=new[]{989,55,724};s.WeaponLevels=new int[3];s.Throwables=new[]{3,3,3};var o=Start(b,s);
  Require(o.InventoryIds.Take(3).SequenceEqual(s.WeaponIds)&&o.InventoryIds.Skip(3).Take(3).SequenceEqual(new[]{168,166,167}),"three universal throwables occupy separate slots after three weapons");
  for(int n=0;n<3;n++){
   int initial=o.InventoryStacks[n+3];bool projectile=false;
   for(int tick=0;tick<120;tick++){var i=Arena.CampaignInput(s,false);i.Selected=n+3;i.MouseX=s.Left+s.Width-60;i.MouseY=s.Floor-160;i.Use=o.InventoryStacks[n+3]==initial;o=b.Step(i);projectile|=o.Projectiles.Length>0;if(tick>30&&o.InventoryStacks[n+3]<initial)break;}
   Require(o.InventoryStacks[n+3]==initial-1&&projectile,"native throwable consumes one and creates projectile item="+new[]{168,166,167}[n]);
  }
  int[] remaining=o.InventoryStacks.Skip(3).Take(3).ToArray();s.Throwables=(int[])remaining.Clone();s.NewRun=false;s.RoundId++;Start(b,s);o=Hold(b,s,0);Require(o.InventoryStacks.Skip(3).Take(3).SequenceEqual(remaining),"remaining throwable stock survives room transition without refill");
  Require(b.TestDrop()>=0&&b.Battle().LootCount>0,"drop cleanup fixture creates an actual native drop");b.ClearRoom();Require(b.Battle().LootCount==0,"leaving room clears native item drops immediately");
  b.TestDrop();s.RoundId++;Start(b,s);Require(b.Battle().LootCount==0,"entering room also clears previous native drops");
  var carry=Setup(HeroClass.Warrior,BattleKind.Normal,1);carry.CurrentLife=120;var healed=Start(b,carry);Require(healed.Life==120,"room entry respects carried current life");Require(b.UsePotion(0),"native healing potion usable when injured");healed=b.Step(Arena.CampaignInput(carry,false));Require(healed.Life>=220&&healed.Life<carry.MaxLife,"native potion restores one hundred life");Require(!b.UsePotion(0),"native potion sickness prevents double healing");
  var lethal=Setup(HeroClass.Ranger,BattleKind.Normal,1);Start(b,lethal);var fatal=Arena.CampaignInput(lethal,false);fatal.Incoming=100000;b.Step(fatal);Require(b.Battle().Lost&&!b.Battle().Won,"player death cannot award victory");
  results.Add(new{test="consumables",remaining,dropCleanup=true,potionSickness=true});
 }
 static void Accessories(NativeBridge b){
  var s=Setup(HeroClass.Warrior,BattleKind.Normal,1);s.Accessories=new[]{54,53,49,216,285,212,3212,193,935,490};var equipped=Start(b,s);int defense=equipped.Defense;float damage=equipped.Modifiers.MeleeDamage;
  Require(b.Battle().AccessoryCount==10,"ten accessories work outside original slot limit");for(int t=0;t<120;t++)equipped=b.Step(Arena.CampaignInput(s,false));Require(equipped.Defense==defense&&Math.Abs(equipped.Modifiers.MeleeDamage-damage)<.001f,"accessory effects do not accumulate every frame");s.Accessories=new[]{54,53,49};var baseline=Start(b,s);Require(defense>baseline.Defense&&damage>baseline.Modifiers.MeleeDamage,"extra accessory defense and damage actually apply");
 }
 static EngineOutput PlaceSentry(NativeBridge b,BattleSetup setup,int id){
  EngineOutput output=null;for(int tick=0;tick<180;tick++){
   var input=Arena.CampaignInput(setup,false);input.Selected=0;input.MouseX=setup.Left+setup.Width*.5f+130;input.MouseY=setup.Floor-1;input.Use=b.Battle().SentryCount==0;output=b.Step(input);
   var state=b.Battle();if(state.SentryCount>0)return output;if(state.Lost||state.Won)break;
  }
  throw new Exception("Native sentry placement failed for item "+id+" outside invasion");
 }
 static void Sentries(NativeBridge b){
  // The popper is the only source of attacks in each room. No summoned minions,
  // fixture strikes or free inventory sentry can make a damage check pass.
  int[] ids={3824,3832,3818,3829,3825,3826};HeroClass[] heroes={HeroClass.Warrior,HeroClass.Ranger,HeroClass.Mage,HeroClass.Summoner,HeroClass.Warrior,HeroClass.Warrior};
  for(int at=0;at<ids.Length;at++){
   int id=ids[at],stage=at<4?1:3;var setup=Setup(heroes[at],BattleKind.Normal,stage);setup.Seed=0;setup.WeaponIds=new[]{id,CampaignRules.Weapons(heroes[at])[0]};setup.WeaponLevels=new[]{0,0};setup.ArmorIds=CampaignRules.Armor(heroes[at],stage);
   var native=Start(b,setup);int manaBefore=native.Mana,otherDamage=Hold(b,setup,1).HeldDamage;native=PlaceSentry(b,setup,id);var initial=b.Battle();int unupgraded=initial.SentryDamage;
   Require(native.InventoryIds[0]==id&&initial.SentryCount>0&&initial.MinionCount==0&&initial.CrystalMaxLife==0&&initial.Wave==0,"equipped native sentry places outside tower defense item="+id);
   Console.WriteLine("SENTRY item="+id+" manaBefore="+manaBefore+" manaAfter="+native.Mana+" heldMana="+native.HeldMana+" nativeDamage="+unupgraded+" count="+initial.SentryCount);
   results.Add(new{test="sentryPlacement",item=id,stage,manaBefore,manaAfter=native.Mana,heldMana=native.HeldMana,projectileDamage=unupgraded,sentries=initial.SentryCount});
   Require(unupgraded>0,"native placed sentry has positive actual projectile damage item="+id);
   // Native Item.SetDefaults gives these DD2 poppers mana=0. The original
   // Player only enforces Etherian Mana payment during an actual invasion.
   // Armor can raise maximum mana after the initial frame, and natural
   // regeneration can run during placement. A free sentry must not spend mana.
   Require(native.HeldMana==0&&native.Mana>=manaBefore,"outside invasion sentry retains native zero-mana placement cost item="+id);
   if(at<4){
    for(int tick=0;tick<600&&b.Battle().Damage==0;tick++){
     var input=Arena.CampaignInput(setup,false);input.Selected=1;input.Use=false;input.Jump=tick%90<28;native=b.Step(input);if(b.Battle().Lost||b.Battle().Won)break;
    }
    Require(b.Battle().Damage>0,"native sentry really attacks regular enemies item="+id);
   }
   setup.WeaponLevels[0]=2;Start(b,setup);native=PlaceSentry(b,setup,id);int upgraded=b.Battle().SentryDamage;
   Require(upgraded>unupgraded&&Math.Abs(upgraded-unupgraded*1.4f)<4,"plus two increases actual sentry source damage by forty percent item="+id);
   // Terraria 1.4.5 locks HeldItem during the thirty-tick placement animation.
   // Requesting another slot for only three ticks can still report the popper.
   var duringSwitch=Hold(b,setup,1);int duringDamage=duringSwitch.HeldDamage,duringSentry=b.Battle().SentryDamage;
   for(int tick=0;tick<Math.Max(45,duringSwitch.ItemAnimationMax+5);tick++){var input=Arena.CampaignInput(setup,false);input.Selected=1;input.Use=false;native=b.Step(input);}
   int afterSentry=b.Battle().SentryDamage;Console.WriteLine("SENTRY SWITCH item="+id+" otherBase="+otherDamage+" heldImmediate="+duringDamage+" heldSettled="+native.HeldDamage+" sourceImmediate="+upgraded+" sourceDuring="+duringSentry+" sourceSettled="+afterSentry+" selected="+native.Maintenance.Selected+" animation="+native.ItemAnimation+" summonMultiplier="+native.Modifiers.SummonDamage);
   results.Add(new{test="sentrySwitch",item=id,otherDamage,heldImmediate=duringDamage,heldSettled=native.HeldDamage,sourceImmediate=upgraded,sourceDuring=duringSentry,sourceSettled=afterSentry,selected=native.Maintenance.Selected,animation=native.ItemAnimation,summonMultiplier=native.Modifiers.SummonDamage});
   Require(native.Maintenance.Selected==1&&native.HeldDamage==otherDamage,"switching away after placement animation does not leak forge bonus to another weapon item="+id);
   Require(afterSentry==upgraded,"placed sentry keeps upgraded source damage after weapon switch item="+id);
   b.ClearRoom();Require(b.Battle().SentryCount==0&&b.Battle().SentryDamage==0,"leaving room removes sentry and its stored damage item="+id);
   setup.NewRun=false;setup.RoundId++;Start(b,setup);Require(b.Battle().SentryCount==0,"next room requires placing sentry again item="+id);
   results.Add(new{test="sentry",item=id,stage,baseDamage=unupgraded,plusTwo=upgraded,otherWeaponDamage=otherDamage});
  }
 }
 static int Main(string[] args){
  string root=args[0];try{Catalog();using(var b=new NativeBridge(root,args[1],true)){try{Classes(b);Equipment(b);Encounters(b);ConsumablesAndCleanup(b);Accessories(b);Sentries(b);}finally{File.WriteAllText(Path.Combine(root,"validation/campaign-worker.log"),b.Log);}}
   File.WriteAllText(Path.Combine(root,"validation/campaign-probe-result.json"),new JavaScriptSerializer().Serialize(new{passed=true,version="0.4",checks,fixtureStrikes,results}));return 0;
  }catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(root,"validation/campaign-probe-result.json"),new JavaScriptSerializer().Serialize(new{passed=false,version="0.4",checks,fixtureStrikes,results,failure=e.ToString()}));return 1;}
 }
}

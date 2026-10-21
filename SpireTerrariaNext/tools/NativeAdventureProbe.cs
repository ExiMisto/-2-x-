using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Xna.Framework;
using SpireTerraria.Next;

internal static class NativeAdventureProbe {
 static readonly List<string> checks=new List<string>();static readonly Dictionary<string,object> report=new Dictionary<string,object>();static string output;
 static object Get(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(target,value);}
 static void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("PASS "+label);}
 static AdvancedBattleState State(NativeArena arena){using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))arena.WriteAdvancedState(writer);memory.Position=0;using(var reader=new BinaryReader(memory))return AdvancedBattleState.Read(reader);}}
 static Dictionary<string,string> Diagnostics(NativeArena arena){using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))arena.WriteDiagnostics(writer);memory.Position=0;using(var reader=new BinaryReader(memory)){var result=new Dictionary<string,string>();int count=reader.ReadInt32();for(int i=0;i<count;i++)result.Add(reader.ReadString(),reader.ReadString());return result;}}}
 static void Image(NativeArena arena,string name){string session=(string)Get(arena,"frameSession");using(FrameExchange reader=FrameExchange.OpenReader(session)){SharedFrame frame;reader.ReadLatest(0,out frame);using(var bitmap=new System.Drawing.Bitmap(frame.Width,frame.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,frame.Width,frame.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{System.Runtime.InteropServices.Marshal.Copy(frame.Pixels,0,data.Scan0,frame.Pixels.Length);}finally{bitmap.UnlockBits(data);}bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}}}
 static void Run(TerrariaRuntime runtime,string mode){
  if(mode=="icons") {
   using(var renderer=new NativeRenderer(runtime.NativeAssembly,runtime.GameDirectory,64,64))using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)) {
    renderer.WriteItemIcon(65,writer);Check(memory.Length>20,"item icon preloader works before any combat runtime exists");
    memory.SetLength(0);renderer.WriteSceneArt(0,writer);Check(memory.Length==640*360*4+8,"character artwork works before a combat world is initialized");
   }
   return;
  }
  if(mode=="catalog-combat") {
   string file=Path.Combine(Path.GetDirectoryName(output),"adventure-test","native-categories.tsv");var failures=new List<string>();int count=0;
   foreach(string line in File.ReadAllLines(file)) {
    string[] parts=line.Split('\t');if(parts.Length<2||parts[0]!="Weapon")continue;string weapon=parts[1];count++;
    try {
     using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{weapon},Life=10000,LifeMax=10000,EnemyCount=8})) {
      var start=State(arena);object hero=Get(arena,"player");bool started=false;
      for(int i=0;i<36;i++){arena.AdventureStep(1,(byte)(i%24<20?8:0),start.PlayerX+180,start.PlayerY-25,0);started|=(int)Get(hero,"itemAnimation")>0||(int)Get(hero,"itemTime")>0||State(arena).Projectiles>0;}
      if(!started)throw new InvalidOperationException("No native attack started in 36 ticks.");
      Check(true,weapon+" starts native attack with no presentation exception");
     }
    }catch(Exception error){failures.Add(weapon+": "+error);Console.WriteLine("FAIL "+weapon+": "+error.Message);}
   }
   report["weaponsChecked"]=count;report["failures"]=failures;Check(count==192,"entire current weapon catalog was exercised");Check(failures.Count==0,"all catalog weapons start and render without native exceptions");return;
  }
  if(mode=="whip-geometry") {
   foreach(string weapon in new[]{"ThornWhip","ScytheWhip"})using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{weapon},Life=10000,LifeMax=10000,EnemyCount=8})) {
    var start=State(arena);arena.AdventureStep(1,0,start.PlayerX+180,start.PlayerY-25,0);object hero=Get(arena,"player");Array pool=(Array)Get(arena,"projectiles");var samples=new List<object>();float maximum=0;
    for(int i=0;i<36;i++) {
     arena.AdventureStep(1,8,start.PlayerX+180,start.PlayerY-25,0);
     foreach(object projectile in pool)if((bool)Get(projectile,"active")) {
      var points=new List<Vector2>();projectile.GetType().GetMethod("FillWhipControlPoints",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{projectile,points,hero,false,0});
      Vector2 heroPosition=(Vector2)Get(hero,"position");foreach(Vector2 point in points)maximum=Math.Max(maximum,Vector2.Distance(point,heroPosition));
      samples.Add(new{tick=i,position=Get(projectile,"position").ToString(),ai=Get(projectile,"ai"),scale=Get(projectile,"scale"),points=points.ConvertAll(p=>p.ToString()),hero=heroPosition.ToString(),animation=Get(hero,"itemAnimation"),animationMax=Get(hero,"itemAnimationMax")});if(i==0||i==3||i==11||i==23)Image(arena,"whip-geometry-"+weapon+"-"+i);
     }
    }
    report[weapon]=samples;report[weapon+"MaxDistance"]=maximum;Check(maximum>65&&maximum<600,weapon+" native whip extends visibly from the character");
   }
   return;
  }
  if(mode=="presentation"||mode=="presentation-fixes") {
   string[] presentationWeapons=mode=="presentation-fixes"?new[]{"LeafBlower","ScytheWhip"}:new[]{"WoodenSword","EnchantedSword","GoldBow","Musket","WaterBolt","BabyBirdStaff","BlandWhip","ThornWhip","WoodYoyo","Spear","BallOHurt","NightsEdge","TerraBlade","LaserMachinegun","DD2FlameburstTowerT1Popper"};
   foreach(string weapon in presentationWeapons) {
    using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{weapon},Armor=new[]{"WoodHelmet","WoodBreastplate","WoodGreaves"},Life=10000,LifeMax=10000,EnemyCount=8})) {
     var start=State(arena);arena.AdventureStep(1,0,start.PlayerX+180,start.PlayerY-25,0);object hero=Get(arena,"player"),renderer=Get(arena,"renderer");var rotations=new HashSet<float>();bool nearHand=false;int projectileMaximum=0,drawnLayers=0;
     for(int i=0;i<48;i++) {
      arena.AdventureStep(1,(byte)(i%32<28?8:0),start.PlayerX+180,start.PlayerY-25,0);var state=State(arena);projectileMaximum=Math.Max(projectileMaximum,state.Projectiles);drawnLayers=Math.Max(drawnLayers,(int)renderer.GetType().GetProperty("PlayerDrawCount",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(renderer,null));
      if((int)Get(hero,"itemAnimation")>0){Vector2 hand=(Vector2)Get(hero,"itemLocation"),position=(Vector2)Get(hero,"position");nearHand|=Vector2.Distance(hand,position)<110;rotations.Add((float)Get(hero,"itemRotation"));}
      if(i==3||i==11||i==23)Image(arena,"action-"+weapon+"-"+i);
     }
     Check(drawnLayers>=6,weapon+" uses native layered character and armor presentation");
     Check(nearHand,weapon+" visual hand position follows the player instead of world origin");
     if(weapon=="WoodenSword"||weapon=="EnchantedSword")Check(rotations.Count>8,weapon+" has changing native swing angles");
     if(weapon!="WoodenSword")Check(projectileMaximum>0,weapon+" creates its native attack projectile");
     report[weapon]=new{layers=drawnLayers,angles=rotations.Count,projectiles=projectileMaximum,recovered=State(arena).Recovered};
    }
   }
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=8271,Weapons=new[]{"WoodenSword"},Life=10000,LifeMax=10000,EnemyCount=10})) {
    for(int i=0;i<120;i++)arena.AdventureStep(5,0,4400,2600,0);var state=State(arena);Check(state.Tick==600,"ordinary enemies remain in the same live encounter for ten seconds");Check(state.Recovered==0,"night enemies do not repeatedly despawn or require forced relocation");Image(arena,"enemy-continuity");report["enemyContinuity"]=Diagnostics(arena);
   }
   return;
  }
  if(mode=="weapons"){
   foreach(string weapon in new[]{"EnchantedSword","GoldBow","Musket","StarCannon","Flamethrower","WaterBolt","BabyBirdStaff","BlandWhip","DD2FlameburstTowerT1Popper"}){
    using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{weapon},Life=10000,LifeMax=10000})){
     AdvancedBattleState initial=State(arena);int most=0;for(int i=0;i<18;i++){arena.AdventureStep(5,(byte)(i%5<4?8:0),initial.PlayerX+180,initial.PlayerY-25,0);most=Math.Max(most,State(arena).Projectiles);}Check(most>0,weapon+" fires a native projectile");
     if(weapon=="BabyBirdStaff"){arena.AdventureStep(120,0,initial.PlayerX+180,initial.PlayerY-25,0);Check(State(arena).Projectiles>0,"native minion persists after summoning");}
    }
   }return;
  }
  if(mode=="platforms"){
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Life=10000,LifeMax=10000})){
    AdvancedBattleState initial=State(arena);object p=Get(arena,"player");int tile=(int)(initial.ArenaLeft/16)+16;Set(p,"position",new Vector2(tile*16,initial.FloorY-42));Set(p,"velocity",Vector2.Zero);arena.AdventureStep(8,0,initial.PlayerX,initial.PlayerY,0);float highest=initial.FloorY;
    for(int i=0;i<50;i++){arena.AdventureStep(1,(byte)(i<25?4:0),initial.PlayerX,initial.PlayerY,0);highest=Math.Min(highest,State(arena).PlayerY);}
    report["jumpHighest"]=highest;report["jumpFloor"]=initial.FloorY;report["jumpEndY"]=State(arena).PlayerY;
    Check(highest<=initial.FloorY-4*16-42,"first platform is reachable by native unequipped jump");
   }return;
  }
  if(mode=="assets"){
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316})){
    object renderer=Get(arena,"renderer");MethodInfo textureMethod=renderer.GetType().GetMethod("Texture",BindingFlags.NonPublic|BindingFlags.Instance);
    using(var sheet=new System.Drawing.Bitmap(900,Math.Max(1,((90+4)/5))*110))using(var g=System.Drawing.Graphics.FromImage(sheet)){g.Clear(System.Drawing.Color.FromArgb(35,35,45));for(int i=0;i<90;i++){var texture=(Microsoft.Xna.Framework.Graphics.Texture2D)textureMethod.Invoke(renderer,new object[]{"Background_"+i});if(texture==null)continue;using(var stream=new MemoryStream()){texture.SaveAsPng(stream,texture.Width,texture.Height);stream.Position=0;using(var image=System.Drawing.Image.FromStream(stream)){int x=(i%5)*180,y=(i/5)*110;g.DrawImage(image,new System.Drawing.Rectangle(x,y,180,85));g.DrawString(i+" ("+texture.Width+"x"+texture.Height+")",System.Drawing.SystemFonts.DefaultFont,System.Drawing.Brushes.White,x,y+85);}}}sheet.Save(Path.Combine(output,"backgrounds.png"));}
   }return;
  }
  if(mode=="accessories"){
   foreach(string extra in new[]{"Shackle","AvengerEmblem","CelestialStone","ObsidianShield"}){using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Life=10000,LifeMax=10000,Accessories=new[]{"Shackle","Shackle","Shackle","Shackle","Shackle","Shackle","Shackle",extra}})){arena.AdventureStep(1,0,4500,2500,0);object p=Get(arena,"player");Check((int)Get(p,"statDefense")>=7,"all seven native accessory slots functional with "+extra);if(extra=="Shackle")Check((int)Get(p,"statDefense")==8,"eighth accessory grants its actual native defense");if(extra=="AvengerEmblem")Check((float)Get(p,"meleeDamage")>1.1f,"eighth accessory grants native damage before attack processing");if(extra=="CelestialStone")Check((int)Get(p,"statDefense")>=11&&(float)Get(p,"meleeDamage")>1.09f,"eighth accessory participates in native flag postprocessing");if(extra=="ObsidianShield")Check((bool)Get(p,"noKnockback"),"eighth accessory grants native functional immunity");}}
   return;
  }
  if(mode=="consumables"){
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{"WoodenSword"},Life=100,LifeMax=300,Consumables=new[]{"Grenade","HealingPotion","IronskinPotion"}})){
    arena.AdventureStep(1,16,4500,2500,0);arena.AdventureStep(3,16,4500,2500,0);AdvancedBattleState grenade=State(arena);Check(grenade.Projectiles>0,"Q uses native grenade outside three weapon slots");
    arena.AdventureStep(60,0,4500,2500,0);Check(State(arena).ConsumedSlots.Length==1&&State(arena).ConsumedSlots[0]==0,"grenade reports its inventory resource index once");
    arena.AdventureStep(1,32,4500,2500,0);AdvancedBattleState heal=State(arena);Check(heal.Life>100,"H uses native healing potion");
    arena.AdventureStep(60,0,4500,2500,0);Check(State(arena).ConsumedSlots.Length==2&&State(arena).ConsumedSlots[1]==1,"healing potion consumes one resource");
    arena.AdventureStep(1,32,4500,2500,0);arena.AdventureStep(60,0,4500,2500,0);Check(State(arena).ConsumedSlots.Length==3&&State(arena).ConsumedSlots[2]==2,"H can use native buff potion after healing");
   }
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{"WoodenSword"},Life=100,LifeMax=300,Consumables=new[]{"Grenade","HealingPotion","HealingPotion"}})){
    AdvancedBattleState initial=State(arena);arena.AdventureStep(1,0,initial.PlayerX+120,initial.PlayerY,0,1);arena.AdventureStep(45,0,initial.PlayerX+120,initial.PlayerY,0);
    AdvancedBattleState healed=State(arena);
    Check(healed.Life>initial.Life&&healed.ConsumedSlots.Length==1&&healed.ConsumedSlots[0]==1,"native potion-bar request consumes only selected slot 1, leaving slot 0 grenade untouched");
    Check(healed.PotionDelay>0,"healing state reports actual native potion sickness");
    arena.AdventureStep(1,0,initial.PlayerX+120,initial.PlayerY,0,2);arena.AdventureStep(30,0,initial.PlayerX+120,initial.PlayerY,0);
    AdvancedBattleState cooldown=State(arena);
    Check(cooldown.ConsumedSlots.Length==1&&cooldown.ConsumedSlots[0]==1&&cooldown.PotionDelay>0,"selected second healing bottle is retained during native potion sickness");
    report["selectedHealingSlot"]=healed.ConsumedSlots[0];report["nativeHealingCooldown"]=cooldown.PotionDelay;
   }
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{"WoodenSword"},Life=300,LifeMax=300,Consumables=new[]{"Grenade","HealingPotion"}})){
    AdvancedBattleState initial=State(arena);arena.AdventureStep(1,0,initial.PlayerX+120,initial.PlayerY,0,1);AdvancedBattleState full=State(arena);
    Check(full.Life==full.LifeMax&&full.ConsumedSlots.Length==0,"full-health selected healing bottle is not consumed");
   }
   using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{"WaterBolt"},Life=10000,LifeMax=10000,Consumables=new[]{"Grenade","ManaPotion"}})){
    AdvancedBattleState initial=State(arena);Check(initial.MaxMana>0&&initial.Mana==initial.MaxMana,"adventure state exposes native starting mana");
    AdvancedBattleState spent=initial;int mostProjectiles=0;
    for(int i=0;i<90&&(spent.Mana>=spent.MaxMana||mostProjectiles==0);i++){
     arena.AdventureStep(1,(byte)(i%30<28?8:0),initial.PlayerX+120,initial.PlayerY-180,0);spent=State(arena);mostProjectiles=Math.Max(mostProjectiles,spent.Projectiles);
    }
    report["manaAfterCasting"]=spent.Mana;report["castingProjectiles"]=mostProjectiles;report["castingTicks"]=spent.Tick;
    Check(spent.Mana<spent.MaxMana&&mostProjectiles>0,"Water Bolt spends real native mana before potion use");
    int before=spent.Mana;arena.AdventureStep(1,0,initial.PlayerX+120,initial.PlayerY-180,0,1);arena.AdventureStep(30,0,initial.PlayerX+120,initial.PlayerY-180,0);AdvancedBattleState restored=State(arena);
    Check(restored.Mana>before&&restored.Mana<=restored.MaxMana&&restored.ConsumedSlots.Length==1&&restored.ConsumedSlots[0]==1,"selected mana bottle restores native mana without consuming slot 0 grenade");
    object player=Get(arena,"player");Check(restored.Mana==(int)Get(player,"statMana")&&restored.MaxMana==(int)Get(player,"statManaMax2"),"mana HUD state matches native player fields");
    report["manaBeforePotion"]=before;report["manaAfterPotion"]=restored.Mana;report["manaMaximum"]=restored.MaxMana;
   }return;
  }
  if(mode=="boss"){
   string[] bosses={"KingSlime","EyeofCthulhu","QueenSlimeBoss","SkeletronPrime","Plantera","DukeFishron"};
   foreach(string boss in bosses){using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Stage=3,RoomKind="boss",BossSymbol=boss,Weapons=new[]{"TerraBlade"},Life=20000,LifeMax=20000})){AdvancedBattleState before=State(arena);Image(arena,boss+"-initial");for(int i=0;i<10;i++)arena.AdventureStep(60,(byte)(i%2==0?1:2),before.EnemyX,before.EnemyY,0);AdvancedBattleState after=State(arena);Check(after.Tick==600&&!after.Dead&&after.Remaining>0,boss+" native AI survives 600 ticks");Check(before.EnemyX!=after.EnemyX||before.EnemyY!=after.EnemyY,boss+" moves with native AI");Image(arena,boss+"-active");report[boss]=Diagnostics(arena);}}
   return;
  }
  using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=9316,Weapons=new[]{"EnchantedSword","WaterBolt","GoldBow"},WeaponLevels=new[]{2,1,0},WeaponAffixes=new[]{"fierce","efficient","rapid"},Armor=new[]{"GoldHelmet","GoldChainmail","GoldGreaves"},ArmorLevel=1,ArmorAffix="warded",Accessories=new[]{"BandofRegeneration","HermesBoots","CloudinaBottle","Shackle","FeralClaws","ObsidianSkull","LuckyHorseshoe","SharkToothNecklace"},EnemyCount=8,MaxConcurrent=3,Life=10000,LifeMax=10000})){
   AdvancedBattleState initial=State(arena);Check(initial.Remaining==8&&initial.Alive==3,"normal encounter starts 8 enemies in three concurrent slots");
   arena.AdventureStep(1,0,initial.EnemyX,initial.EnemyY,0);report["initial"]=Diagnostics(arena);Image(arena,"forest");
   object player=Get(arena,"player");Array npcs=(Array)Get(arena,"npcs");object npc=npcs.GetValue(0);int hp=(int)Get(npc,"life");Set(npc,"position",new Vector2(initial.ArenaLeft-1000,initial.FloorY+500));arena.AdventureStep(1,0,initial.EnemyX,initial.EnemyY,0);AdvancedBattleState recovered=State(arena);Check(recovered.Recovered==1&&(int)Get(npc,"life")==hp,"fallen NPC recovers without healing or losing combat target");
   Set(npc,"active",false);arena.AdventureStep(1,0,initial.EnemyX,initial.EnemyY,0);Check(State(arena).Remaining==8&&State(arena).Recovered==2,"despawn does not count as victory and respawns living enemy");
   arena.AdventureStep(1,8,initial.EnemyX,initial.EnemyY,1);Check(State(arena).SelectedWeapon==1,"native weapon selection changes to second slot");
   Check(State(arena).Projectiles>0,"native magic weapon spawns projectile");
   arena.AdventureStep(40,0,initial.EnemyX,initial.EnemyY,2);arena.AdventureStep(1,8,initial.EnemyX,initial.EnemyY,2);Check(State(arena).SelectedWeapon==2,"native weapon selection changes to bow slot");
   int oldRemaining=State(arena).Remaining;foreach(object enemy in npcs)if((bool)Get(enemy,"active")){Set(enemy,"life",0);Set(enemy,"active",false);}arena.AdventureStep(1,0,initial.EnemyX,initial.EnemyY,0);Check(State(arena).Remaining==oldRemaining-3&&State(arena).Alive==3,"next wave starts after all previous targets defeated");
   Check(!((bool)Get(player,"dead")),"multienemy test player remains alive");
   report["final"]=Diagnostics(arena);
  }
  foreach(string scene in new[]{"mine","corruption-altar"})using(var arena=new NativeArena(runtime,new BattleConfiguration{Seed=5811,SceneKey=scene,Weapons=new[]{"EnchantedSword"},Life=10000,LifeMax=10000})){arena.AdventureStep(2,0,4352,2648,0);Image(arena,scene);Check(Diagnostics(arena)["scene"]==scene,scene+" scene initializes native collision and render");}
 }
 [STAThread]static int Main(string[] args){bool passed=false;try{output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Run(new TerrariaRuntime(args[0]),args.Length>2?args[2]:"");passed=true;}catch(Exception e){report["failure"]=e.ToString();Console.Error.WriteLine(e);}report["passed"]=passed;report["checks"]=checks;File.WriteAllText(Path.Combine(output,"verification"+(args.Length>2?"-"+args[2]:"")+".json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(false));return passed?0:1;}
}

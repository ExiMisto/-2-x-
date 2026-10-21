using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SpireTerraria.Next.Host;

// Isolated host spies exercise the actual AdventureSession source without a
// Godot process, real save files, installed Mod changes, or a full host build.
namespace Godot {public static class GD {public static void PrintErr(object value){}}}
namespace HarmonyLib {
 public sealed class HarmonyMethod {internal MethodInfo Method;public HarmonyMethod(Type type,string name){Method=type.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);}}
 public static class AccessTools {public static MethodInfo Method(Type type,string name)=>type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);}
 public sealed class Harmony {
  static readonly Dictionary<string,(HarmonyMethod Prefix,HarmonyMethod Postfix)> patches=new();
  public void Patch(MethodInfo target,HarmonyMethod prefix=null,HarmonyMethod postfix=null){if(target==null)throw new Exception("Missing host setup hook");patches[target.Name]=(prefix,postfix);}
  internal static void Invoke(string name,bool postfix,params object[] args){if(patches.TryGetValue(name,out var pair)){var method=(postfix?pair.Postfix:pair.Prefix)?.Method;if(method!=null)method.Invoke(null,method.GetParameters().Length==0?null:args);}}
  internal static int Count=>patches.Count;
 }
}
namespace MegaCrit.Sts2.Core.Combat {}
namespace MegaCrit.Sts2.Core.Entities.Creatures {
 public sealed class Creature {public int MaxHp=300,CurrentHp=189,Writes;public void SetMaxHpInternal(int hp){MaxHp=hp;Writes++;}public void SetCurrentHpInternal(int hp){CurrentHp=hp;Writes++;}}
}
namespace MegaCrit.Sts2.Core.Rooms {public enum RoomType {Monster,Elite,Boss}public sealed class ProbeRoom {public RoomType RoomType;}}
namespace MegaCrit.Sts2.Core.Nodes.CommonUi {
 public static class NErrorPopup {public static string Create(string title,string body,bool report)=>title+"\n"+body;}
 public sealed class NModalContainer {public static NModalContainer Instance=new();public readonly List<string> Messages=new();public void Add(string message,bool showBackstop=true){Messages.Add(message);}}
}
namespace MegaCrit.Sts2.Core.Saves {public sealed class SaveManager {public static SaveManager Instance=new();public int CurrentProfileId=17;}}
namespace MegaCrit.Sts2.Core.Runs {
 public sealed class ProbeRng {public string StringSeed="RESTORE-PROBE";}
 public sealed class ProbePlayer {
  public AdventureCharacter Character=new();public MegaCrit.Sts2.Core.Entities.Creatures.Creature Creature=new();
  public List<string> Deck=new(){"native-owned-weapon+7","native-owned-armor+4"},Relics=new(){"native-owned-accessory"},Potions=new(){"native-owned-potion"};
 }
 public sealed class RunState {public List<ProbePlayer> Players=new(){new()};public ProbeRng Rng=new();public int CurrentActIndex,CurrentRoomCount;public object CurrentMapCoord;public MegaCrit.Sts2.Core.Rooms.ProbeRoom CurrentRoom;}
 public sealed class RunManager {
  public static RunManager Instance=new();RunState State {get;set;}long _startTime=1790000000;
  public void SetUpNewSingleplayer(RunState state){HarmonyLib.Harmony.Invoke(nameof(SetUpNewSingleplayer),false,state);State=state;}
  public void SetUpSavedSingleplayer(RunState state){HarmonyLib.Harmony.Invoke(nameof(SetUpSavedSingleplayer),false,state);State=state;}
  public void SetUpSavedMultiplayer(RunState state){HarmonyLib.Harmony.Invoke(nameof(SetUpSavedMultiplayer),false,state);State=state;}
  public void SetUpReplay(RunState state){HarmonyLib.Harmony.Invoke(nameof(SetUpReplay),false,state);State=state;}
  public void SetUpTest(RunState state){HarmonyLib.Harmony.Invoke(nameof(SetUpTest),false,state);State=state;}
  public void CleanUp(){State=null;HarmonyLib.Harmony.Invoke(nameof(CleanUp),true);}
  public void AttachWithoutSetup(RunState state){State=state;}
 }
}
namespace SpireTerraria.Next.Host {
 public sealed class AdventureCharacter {public string Id="TerraWarrior";public HeroClass Hero=HeroClass.Warrior;}
 internal static class HostIntegration {internal static string ModDirectory;internal static object Active;}
 internal static class SpireSelfTest {internal static bool Enabled;internal static bool AdventureEnabled=true;}
 internal static class AdventureInventoryModels {
  internal static int Syncs;
  internal static void Sync(MegaCrit.Sts2.Core.Runs.ProbePlayer player){Syncs++;player.Deck.Clear();player.Relics.Clear();player.Potions.Clear();}
 }
 public static class AdventureSessionRestoreTests {
  static readonly List<string> checks=new();
  static string PathForRun()=>(string)typeof(AdventureSession).GetMethod("StatePath",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
  static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);checks.Add(name);}
  static MegaCrit.Sts2.Core.Runs.RunState Resume(){var run=new MegaCrit.Sts2.Core.Runs.RunState();MegaCrit.Sts2.Core.Runs.RunManager.Instance.SetUpSavedSingleplayer(run);return run;}
  static string Inventory(MegaCrit.Sts2.Core.Runs.RunState run){var p=run.Players[0];return string.Join("|",p.Deck.Concat(p.Relics).Concat(p.Potions));}
  static void Refuses(MegaCrit.Sts2.Core.Runs.RunState run,string path,string original,string name) {
   int syncs=AdventureInventoryModels.Syncs,writes=run.Players[0].Creature.Writes;string inventory=Inventory(run);bool rejected=false;
   try{AdventureSession.EnsureRun();}catch(InvalidDataException error){rejected=error.Message.Contains(path)&&error.Message.Contains("未重建起始装备");}
   AdventureSession.Save();
   Check(rejected&&AdventureSession.Current==null&&AdventureInventoryModels.Syncs==syncs&&run.Players[0].Creature.Writes==writes&&Inventory(run)==inventory&&(original==null?!File.Exists(path):File.ReadAllText(path)==original)&&!File.Exists(path+".tmp"),name);
  }
  public static string Run(string directory) {
   if(Directory.Exists(directory))throw new InvalidOperationException("Probe requires a new isolated directory.");
   Directory.CreateDirectory(directory);HostIntegration.ModDirectory=directory;AdventureSession.Install(new HarmonyLib.Harmony());
   Check(HarmonyLib.Harmony.Count==6,"Explicit new/load/test/replay/cleanup hooks installed");
   var fresh=new MegaCrit.Sts2.Core.Runs.RunState();MegaCrit.Sts2.Core.Runs.RunManager.Instance.SetUpNewSingleplayer(fresh);
   var model=AdventureSession.EnsureRun();string path=PathForRun();
   Check(model!=null&&model.Weapons.Count>0&&File.Exists(path)&&fresh.Players[0].Creature.CurrentHp==300,"Only explicit fresh singleplayer setup creates starter equipment");
   model.Weapons[0].Level=7;model.Materials["ore"]=19;model.Life=123;AdventureSession.Save();string good=File.ReadAllText(path);
   var saved=Resume();saved.Players[0].Creature.CurrentHp=123;var restored=AdventureSession.EnsureRun();
   Check(restored.Weapons[0].Level==7&&restored.Materials["ore"]==19&&restored.Life==123,"Matching continuation restores upgrades materials and HP");
   File.Delete(path);var missing=Resume();int notices=MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance.Messages.Count;
   Refuses(missing,path,null,"Missing continuation sidecar preserves native inventory HP and files");
   Refuses(missing,path,null,"Repeated missing-sidecar access cannot create a starter save");
   Check(MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance.Messages.Count==notices+1,"Recovery popup appears once per affected run");
   File.WriteAllText(path,good);Check(AdventureSession.EnsureRun().Weapons[0].Level==7,"Externally restored sidecar can recover without changing native save");
   void Broken(Action<AdventureSession.SessionSave> mutate,string label) {
    var data=JsonSerializer.Deserialize<AdventureSession.SessionSave>(good);mutate(data);string text=JsonSerializer.Serialize(data);File.WriteAllText(path,text);Refuses(Resume(),path,text,label);
   }
   Broken(data=>data.RunIdentity="different-run","Mismatched run identity never resets or overwrites equipment");
   Broken(data=>data.Version=99,"Unsupported envelope version never resets or overwrites equipment");
   File.WriteAllText(path,"{broken-json");Refuses(Resume(),path,"{broken-json","Malformed JSON remains untouched");
   Broken(data=>data.ModelJson="null","Invalid equipment model remains untouched");
   Broken(data=>{var item=AdventureRun.Deserialize(data.ModelJson);item.Hero=HeroClass.Mage;data.ModelJson=item.Serialize();},"Mismatched hero remains untouched");
   Broken(data=>{var item=AdventureRun.Deserialize(data.ModelJson);item.Seed++;data.ModelJson=item.Serialize();},"Mismatched model seed remains untouched");
   Broken(data=>{var item=AdventureRun.Deserialize(data.ModelJson);item.EnterStage(3);data.ModelJson=item.Serialize();},"Future-stage model remains untouched");
   File.Delete(path);var unknown=new MegaCrit.Sts2.Core.Runs.RunState();MegaCrit.Sts2.Core.Runs.RunManager.Instance.AttachWithoutSetup(unknown);Refuses(unknown,path,null,"Unknown setup cannot impersonate a fresh run");
   File.WriteAllText(path,good);Resume();AdventureSession.EnsureRun();string prior=File.ReadAllText(path);int priorSyncs=AdventureInventoryModels.Syncs;var other=new MegaCrit.Sts2.Core.Runs.RunState();string otherInventory=Inventory(other);MegaCrit.Sts2.Core.Runs.RunManager.Instance.AttachWithoutSetup(other);AdventureSession.Save();
   Check(AdventureInventoryModels.Syncs==priorSyncs&&Inventory(other)==otherInventory&&File.ReadAllText(path)==prior,"Detached session cannot sync or write into another host run");
   MegaCrit.Sts2.Core.Runs.RunManager.Instance.CleanUp();Check(AdventureSession.Current==null&&AdventureSession.EnsureRun()==null,"Cleanup clears current state and missing host cannot reuse it");
   File.Delete(path);SpireSelfTest.Enabled=true;Refuses(Resume(),path,null,"Self-test flag cannot bypass continuation protection");
   var testFresh=new MegaCrit.Sts2.Core.Runs.RunState();MegaCrit.Sts2.Core.Runs.RunManager.Instance.SetUpNewSingleplayer(testFresh);Check(AdventureSession.EnsureRun()!=null&&!File.Exists(path),"Explicit fresh self-test remains supported without persistent writes");
   return JsonSerializer.Serialize(new{passed=true,checks=checks.Count,results=checks},new JsonSerializerOptions{WriteIndented=true});
  }
 }
}

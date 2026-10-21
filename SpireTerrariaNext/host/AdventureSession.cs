using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireTerraria.Next;

namespace SpireTerraria.Next.Host {
 internal static class AdventureSession {
  internal static AdventureRun Current {get;private set;}
  internal static bool ClassChosen {get;private set;}
  internal static bool IsLegacyTest {get{return SpireSelfTest.Enabled&&!SpireSelfTest.AdventureEnabled;}}
  static RunState attachedRun;
  static RunState newRun,notifiedRun;
  static bool installed;
  static string identity="";
  static readonly Dictionary<string,string> verifiedBosses=new Dictionary<string,string>();
  static readonly PropertyInfo stateProperty=typeof(RunManager).GetProperty("State",BindingFlags.Instance|BindingFlags.NonPublic);
  internal static void Install(Harmony harmony) {
   if(installed)return;installed=true;
   // A newly selected character and a resumed character can have identical starter
   // models. Only the host's explicit new-run entry point authorizes creation.
   harmony.Patch(AccessTools.Method(typeof(RunManager),"SetUpNewSingleplayer"),prefix:new HarmonyMethod(typeof(AdventureSession),nameof(OnNewRun)));
   foreach(string name in new[]{"SetUpSavedSingleplayer","SetUpSavedMultiplayer","SetUpReplay","SetUpTest"})harmony.Patch(AccessTools.Method(typeof(RunManager),name),prefix:new HarmonyMethod(typeof(AdventureSession),nameof(OnExistingRun)));
   harmony.Patch(AccessTools.Method(typeof(RunManager),"CleanUp"),postfix:new HarmonyMethod(typeof(AdventureSession),nameof(Reset)));
  }
  static void OnNewRun(RunState state){Reset();newRun=state;}
  static void OnExistingRun(){Reset();}
  static void Reset(){Current=null;ClassChosen=false;attachedRun=null;newRun=null;notifiedRun=null;identity="";verifiedBosses.Clear();}
  internal static RunState HostRun {get{return stateProperty?.GetValue(RunManager.Instance) as RunState;}}
  internal static string NodeKey {
   get {var run=HostRun;return run==null?"unattached":$"act:{run.CurrentActIndex+1}/map:{run.CurrentMapCoord}/room:{run.CurrentRoomCount}/{run.CurrentRoom?.RoomType}";}
  }
  internal static string RoomKind {
   get {RoomType? type=HostRun?.CurrentRoom?.RoomType;return type==RoomType.Elite?"elite":type==RoomType.Boss?"boss":"normal";}
  }
  internal static string BossSymbol {
   get {
    if(RoomKind=="normal")return "";
    int stage=Current?.Stage??Math.Clamp((HostRun?.CurrentActIndex??0)+1,1,3);
    string[][] elites={new[]{"KingSlime"},new[]{"QueenSlimeBoss"},new[]{"Plantera"}};
    string[][] bosses={new[]{"EyeofCthulhu"},new[]{"SkeletronPrime"},new[]{"DukeFishron"}};
    string[] pool=(RoomKind=="elite"?elites:bosses)[stage-1];return pool[(int)(StableHash(NodeKey)%((uint)pool.Length))];
   }
  }
  internal static string VerifiedBossSymbol {get{string value;return verifiedBosses.TryGetValue(NodeKey,out value)?value:"";}}
  internal static AdventureRun EnsureRun() {
   RunState run=HostRun;if(run==null)return null;
   if(run.Players.FirstOrDefault()?.Character is not AdventureCharacter character)return null;
   if(ReferenceEquals(attachedRun,run)&&Current!=null){UpdateStageAndHealth(run);return Current;}
   attachedRun=run;
   object started=typeof(RunManager).GetField("_startTime",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(RunManager.Instance);
   identity=$"{run.Rng.StringSeed}/{started}/{run.Players.FirstOrDefault()?.Character.Id}/{SaveManager.Instance.CurrentProfileId}";
   Current=null;ClassChosen=false;verifiedBosses.Clear();
   string path=StatePath();
   if(!SpireSelfTest.Enabled) {
    if(File.Exists(path))try {
     var data=JsonSerializer.Deserialize<SessionSave>(File.ReadAllText(path));
     if(data==null||data.Version!=1)throw new InvalidDataException("装备存档版本不兼容");
     if(data.RunIdentity!=identity)throw new InvalidDataException("装备存档与当前远征身份不匹配");
     var restored=AdventureRun.Deserialize(data.ModelJson);
     if(restored.Hero!=character.Hero||restored.Seed!=unchecked((long)StableHash64(run.Rng.StringSeed))||restored.Stage>Math.Clamp(run.CurrentActIndex+1,1,3))throw new InvalidDataException("装备数据与当前角色、种子或幕数不匹配");
     Current=restored;ClassChosen=data.ClassChosen;foreach(var pair in data.VerifiedBosses??new Dictionary<string,string>())verifiedBosses[pair.Key]=pair.Value;
    }catch(Exception error){throw RestoreError(run,path,"无法读取匹配的装备存档",error);}
   }
   if(Current==null) {
    if(!ReferenceEquals(newRun,run))throw RestoreError(run,path,"续局缺少泰拉远征装备存档");
    Current=AdventureRun.Create(unchecked((long)StableHash64(run.Rng.StringSeed)),character.Hero);PushHostHealth();
   }
   ClassChosen=true;
   UpdateStageAndHealth(run);Save();return Current;
  }
  static InvalidDataException RestoreError(RunState run,string path,string reason,Exception inner=null) {
   Current=null;ClassChosen=false;verifiedBosses.Clear();
   string message=reason+"。已停止接管，保留尖塔现有牌组、饰品和药水，未重建起始装备。\n请退出本局，将对应装备存档恢复到以下位置，再重启游戏继续：\n"+path;
   GD.PrintErr("[SpireTerrariaNext] "+message+(inner==null?"":"\n"+inner.Message));
   if(!ReferenceEquals(notifiedRun,run)&&NModalContainer.Instance!=null)try {NModalContainer.Instance.Add(NErrorPopup.Create("泰拉远征存档需要恢复",message,false));notifiedRun=run;}catch(Exception error){GD.PrintErr("[SpireTerrariaNext] restore notice: "+error.Message);}
   return new InvalidDataException(message,inner);
  }
  static void UpdateStageAndHealth(RunState run) {
   int stage=Math.Clamp(run.CurrentActIndex+1,1,3);
   if(Current.Stage!=stage){Current.EnterStage(stage);PushHostHealth();}
   else if(HostIntegration.Active==null){var creature=run.Players.FirstOrDefault()?.Creature;if(creature!=null&&creature.MaxHp==Current.LifeMax)Current.Life=Math.Clamp(creature.CurrentHp,0,Current.LifeMax);}
  }
  internal static void ChooseHero(HeroClass hero) {
   EnsureRun();if(ClassChosen)return;
   // The host's opening event can precede the first fight. Keep its resources,
   // claims and HP when replacing only the provisional class starter loadout.
   var starter=AdventureRun.Create(Current.Seed,hero);
   foreach(var gear in starter.Weapons.Append(starter.Armor))gear.InstanceId="gear-"+(++Current.InstanceCounter);
   Current.Hero=hero;Current.Weapons=starter.Weapons;Current.Armor=starter.Armor;
   ClassChosen=true;PushHostHealth();Save();
  }
  internal static void PushHostHealth() {
   var creature=HostRun?.Players.FirstOrDefault()?.Creature;if(creature==null||Current==null)return;
   creature.SetMaxHpInternal(Current.LifeMax);creature.SetCurrentHpInternal(Math.Clamp(Current.Life,0,Current.LifeMax));
  }
  internal static void Save() {
   if(Current==null||!ReferenceEquals(attachedRun,HostRun))return;
   var player=HostRun?.Players.FirstOrDefault();if(player?.Character is not AdventureCharacter)return;
   AdventureInventoryModels.Sync(player);
   if(SpireSelfTest.Enabled)return;
   var data=new SessionSave{Version=1,RunIdentity=identity,ClassChosen=ClassChosen,ModelJson=Current.Serialize(),VerifiedBosses=new Dictionary<string,string>(verifiedBosses)};
   string path=StatePath();Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp";
   File.WriteAllText(temporary,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);
  }
  static string StatePath(){return Path.Combine(HostIntegration.ModDirectory,"state",$"profile-{SaveManager.Instance.CurrentProfileId}",StableHash64(identity).ToString("x16")+".json");}
  internal static BattleConfiguration BuildConfiguration() {
   EnsureRun();if(Current==null)throw new InvalidOperationException("没有可接管的单人远征。");
   var weapons=Current.Weapons.Take(3).ToArray();if(weapons.Length==0)throw new InvalidOperationException("当前职业没有武器。");
   string[] scenes={"forest-outpost","mine","corruption-altar"};
   var config=new BattleConfiguration {
    Seed=unchecked((int)StableHash($"{Current.Seed}/{NodeKey}")),Stage=Current.Stage,RoomKind=RoomKind,BossSymbol=BossSymbol,
    SceneKey=RoomKind=="normal"?scenes[(int)(StableHash(NodeKey)%3)]:"boss-"+BossSymbol,
    Life=Math.Max(1,Current.Life),LifeMax=Current.LifeMax,
    Weapons=weapons.Select(w=>GearCatalog.Get(w.DefinitionKey).NativeSymbols[0]).ToArray(),WeaponLevels=weapons.Select(w=>w.Level).ToArray(),WeaponAffixes=weapons.Select(w=>w.Affix??"").ToArray(),
    Armor=Current.Armor==null?Array.Empty<string>():GearCatalog.Get(Current.Armor.DefinitionKey).NativeSymbols,
    ArmorLevel=Current.Armor?.Level??0,ArmorAffix=Current.Armor?.Affix??"",
    Accessories=Current.Accessories.SelectMany(a=>GearCatalog.Get(a.DefinitionKey).NativeSymbols).Distinct(StringComparer.Ordinal).ToArray(),
    Consumables=Current.Consumables.Take(64).Select(c=>GearCatalog.Get(c.DefinitionKey).NativeSymbols[0]).ToArray(),
    EnemyCount=0,MaxConcurrent=0
   };
   config.Validate();return config;
  }
  internal static void FinishBattle(BattleConfiguration configuration,AdvancedBattleState state,bool won,IEnumerable<string> consumedInstanceIds) {
   if(Current==null)return;Current.Life=Math.Clamp(state.Life,0,Current.LifeMax);
   var consumed=new HashSet<string>(consumedInstanceIds??Array.Empty<string>(),StringComparer.Ordinal);Current.Consumables.RemoveAll(item=>consumed.Contains(item.InstanceId));
   if(won&&!string.IsNullOrEmpty(configuration.BossSymbol)&&state.Remaining==0&&state.Alive==0)verifiedBosses[NodeKey]=configuration.BossSymbol;
   PushHostHealth();Save();
  }
  internal static uint StableHash(string value){uint hash=2166136261;foreach(char c in value??""){hash^=c;hash*=16777619;}return hash;}
  internal static ulong StableHash64(string value){ulong hash=14695981039346656037;foreach(char c in value??""){hash^=c;hash*=1099511628211;}return hash;}
  internal sealed class SessionSave {
   public int Version {get;set;}
   public string RunIdentity {get;set;}="";
   public bool ClassChosen {get;set;}
   public string ModelJson {get;set;}="";
   public Dictionary<string,string> VerifiedBosses {get;set;}=new Dictionary<string,string>();
  }
 }
}

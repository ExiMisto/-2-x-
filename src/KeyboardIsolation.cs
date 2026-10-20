using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace SpireTerraria {
 // Terraria polls physical keys. Spire uses both InputMap and NInputManager's
 // separate key-to-action translator, so both paths must be isolated.
 public static class KeyboardIsolation {
  static readonly Dictionary<StringName,List<InputEvent>> saved=new Dictionary<StringName,List<InputEvent>>();
  static bool active;
  public static int BlockedEvents {get;private set;}
  public static int SavedBindings {get;private set;}
  public static int Install(Harmony harmony) {
   int count=0;
   var names=new HashSet<string>{"_Input","_ShortcutInput","_UnhandledInput","_UnhandledKeyInput"};
   foreach(var type in typeof(NGame).Assembly.GetTypes().Where(t=>typeof(Node).IsAssignableFrom(t)))
    foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)) {
     var args=method.GetParameters();
     if(names.Contains(method.Name)&&!method.IsAbstract&&args.Length==1&&args[0].ParameterType==typeof(InputEvent)) {
      harmony.Patch(method,prefix:new HarmonyMethod(typeof(KeyboardIsolation),nameof(AllowSpireInput)));count++;
     }
    }
   return count;
  }
  public static bool AllowSpireInput(InputEvent __0) {
   if(!Plugin.Realtime||!(__0 is InputEventKey||__0 is InputEventAction))return true;
   BlockedEvents++;return false;
  }
  public static void Enter() {
   if(active)return;active=true;BlockedEvents=SavedBindings=0;saved.Clear();
   // Do not change SettingsSave or the user's remapped controls.
   foreach(var action in InputMap.GetActions()) {
    var keys=InputMap.ActionGetEvents(action).Where(e=>e is InputEventKey).ToList();
    if(keys.Count==0)continue;saved.Add(action,keys);SavedBindings+=keys.Count;
    Input.ActionRelease(action);
    foreach(var key in keys)InputMap.ActionEraseEvent(action,key);
   }
  }
  public static int RemainingKeyBindings() {
   return InputMap.GetActions().Sum(a=>InputMap.ActionGetEvents(a).Count(e=>e is InputEventKey));
  }
  public static bool Restore() {
   if(!active)return true;bool ok=true;
   foreach(var pair in saved) {
    if(!InputMap.HasAction(pair.Key)){ok=false;continue;}
    Input.ActionRelease(pair.Key);
    foreach(var key in pair.Value) {
     if(!InputMap.ActionHasEvent(pair.Key,key))InputMap.ActionAddEvent(pair.Key,key);
     ok&=InputMap.ActionHasEvent(pair.Key,key);
    }
   }
   saved.Clear();active=false;return ok;
  }
 }
}

using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace SpireTerraria.Next.Host {
 [ModInitializer("Initialize")]
 public static class EntryProbe {
  public static void Initialize() {
   GD.Print("[SpireTerrariaNextProbe] Independent native battle probe loaded.");
   HostIntegration.Install();
  }
 }
}

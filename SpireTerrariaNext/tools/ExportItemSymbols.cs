using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SpireTerraria.Next;

internal static class ExportItemSymbols {
 static int Main(string[] args) {
  try {
   var runtime=new TerrariaRuntime(args[0]);
   var names=runtime.NativeAssembly.GetType("Terraria.ID.ItemID",true)
    .GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.IsLiteral)
    .Select(f=>f.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
   if(names.Length==0)throw new InvalidDataException("The installed game has no ItemID constants.");
   File.WriteAllLines(args[1],names);Console.WriteLine("Exported local ItemID symbols: "+names.Length);return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

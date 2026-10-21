using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SpireTerraria.Next;

internal static class CatalogNativeValidation {
 static int Main(string[] args) {
  try {
   var runtime=new TerrariaRuntime(args[0]);runtime.EnsureItemRuntime();var game=runtime.NativeAssembly;
   var ids=game.GetType("Terraria.ID.ItemID",true);var itemType=game.GetType("Terraria.Item",true);
   var defaults=itemType.GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(x=>x.Name=="SetDefaults"&&x.GetParameters().Length>0&&x.GetParameters()[0].ParameterType==typeof(int)&&x.GetParameters().Skip(1).All(p=>p.IsOptional)).OrderBy(x=>x.GetParameters().Length).First();
   int count=0,failures=0;var report=new System.Collections.Generic.List<string>();
   foreach(string line in File.ReadAllLines(args[1])) {
    var p=line.Split('\t');string kind=p[0],symbol=p[1];int slot=int.Parse(p[2]);int id=Convert.ToInt32(ids.GetField(symbol).GetRawConstantValue());
    object item=Activator.CreateInstance(itemType);var spec=defaults.GetParameters();var values=new object[spec.Length];values[0]=id;for(int i=1;i<values.Length;i++)values[i]=spec[i].DefaultValue;defaults.Invoke(item,values);
    int damage=(int)itemType.GetField("damage").GetValue(item),defense=(int)itemType.GetField("defense").GetValue(item),ammo=(int)itemType.GetField("useAmmo").GetValue(item);bool accessory=(bool)itemType.GetField("accessory").GetValue(item);
    bool valid=kind=="Accessory"?accessory:kind=="Weapon"?damage>0:kind=="Armor"?defense>=0&&(int)itemType.GetField(new[]{"headSlot","bodySlot","legSlot"}[slot]).GetValue(item)>0:true;
    if(!valid)failures++;count++;report.Add(kind+"\t"+symbol+"\t"+id+"\t"+damage+"\t"+defense+"\t"+accessory+"\t"+ammo+"\t"+(valid?"PASS":"FAIL"));
   }
   File.WriteAllLines(args[2],report.ToArray());Console.WriteLine("Native equipment defaults and categories checked: "+count+"; failures: "+failures);return failures==0?0:1;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SpireTerraria.Next;

internal static class VerifyBossSources {
 static int Main(string[] args) {
  try {
   var runtime=new TerrariaRuntime(args[0]);runtime.EnsureItemRuntime();var game=runtime.NativeAssembly;var main=game.GetType("Terraria.Main",true);
   main.GetField("rand").SetValue(null,Activator.CreateInstance(game.GetType("Terraria.Utilities.UnifiedRandom"),new object[]{123}));
   foreach(string name in new[]{"Initialize_TileAndNPCData1","Initialize_TileAndNPCData2"})main.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
   var players=(Array)main.GetField("player").GetValue(null);for(int i=0;i<players.Length;i++)players.SetValue(Activator.CreateInstance(game.GetType("Terraria.Player",true)),i);main.GetField("myPlayer").SetValue(null,0);
   var dbType=game.GetType("Terraria.GameContent.ItemDropRules.ItemDropDatabase",true);object db=Activator.CreateInstance(dbType);dbType.GetMethod("Populate").Invoke(db,null);
   var npcIds=game.GetType("Terraria.ID.NPCID",true);var itemIds=game.GetType("Terraria.ID.ItemID",true);var itemType=game.GetType("Terraria.Item",true);
   var names=new Dictionary<int,string>();foreach(var field in itemIds.GetFields(BindingFlags.Public|BindingFlags.Static))if(field.IsLiteral&&field.FieldType==typeof(short)||field.IsLiteral&&field.FieldType==typeof(int)){int id=Convert.ToInt32(field.GetRawConstantValue());if(!names.ContainsKey(id))names.Add(id,field.Name);}
   var infoType=game.GetType("Terraria.GameContent.ItemDropRules.DropRateInfo",true);var feedType=game.GetType("Terraria.GameContent.ItemDropRules.DropRateInfoChainFeed",true);var reportType=typeof(List<>).MakeGenericType(infoType);
   var ruleType=game.GetType("Terraria.GameContent.ItemDropRules.IItemDropRule",true);var reportMethod=ruleType.GetMethod("ReportDroprates");
   var defaults=itemType.GetMethods().Where(x=>x.Name=="SetDefaults"&&x.GetParameters().Length>0&&x.GetParameters()[0].ParameterType==typeof(int)&&x.GetParameters().Skip(1).All(p=>p.IsOptional)).OrderBy(x=>x.GetParameters().Length).First();
   var output=new List<string>();
   foreach(string row in new[]{"KingSlime|KingSlimeBossBag","EyeofCthulhu|EyeOfCthulhuBossBag","QueenSlimeBoss|QueenSlimeBossBag","SkeletronPrime|SkeletronPrimeBossBag","Plantera|PlanteraBossBag","DukeFishron|FishronBossBag"}) {
    var parts=row.Split('|');int npcId=Convert.ToInt32(npcIds.GetField(parts[0]).GetRawConstantValue()),bagId=Convert.ToInt32(itemIds.GetField(parts[1]).GetRawConstantValue());
    object report=Activator.CreateInstance(reportType);object feed=Activator.CreateInstance(feedType,new object[]{1f});var rules=(IEnumerable)dbType.GetMethod("GetRulesForNPCID").Invoke(db,new object[]{npcId,false});int roots=0;
    foreach(object rule in rules){roots++;reportMethod.Invoke(rule,new object[]{report,feed});}
    bool bagPresent=false;int reports=0;
    foreach(object info in (IEnumerable)report){reports++;int id=(int)infoType.GetField("itemId").GetValue(info);if(id==bagId)bagPresent=true;var conditions=(IEnumerable)infoType.GetField("conditions").GetValue(info);var conditionNames=new List<string>();if(conditions!=null)foreach(object c in conditions)conditionNames.Add(c.GetType().Name);
     output.Add(parts[0]+"\t"+npcId+"\t"+(names.ContainsKey(id)?names[id]:id.ToString())+"\t"+id+"\t"+infoType.GetField("dropRate").GetValue(info)+"\t"+string.Join(",",conditionNames.ToArray()));
    }
    if(!bagPresent)throw new Exception("Actual NPC rules did not report expected expert bag: "+parts[0]);
    Console.WriteLine(parts[0]+" NPC="+npcId+" roots="+roots+" reported="+reports+" verifiedBag="+parts[1]+"("+bagId+")");
   }
   Directory.CreateDirectory(args[1]);File.WriteAllLines(Path.Combine(args[1],"npc-rules.tsv"),output.ToArray());
   // Export all genuine accessory IDs for the separate OpenBossBag IL branch audit.
   var accessories=new List<string>();var skipped=new List<string>();foreach(var entry in names){if(entry.Key<=0)continue;try{object item=Activator.CreateInstance(itemType);var spec=defaults.GetParameters();var values=new object[spec.Length];values[0]=entry.Key;for(int i=1;i<values.Length;i++)values[i]=spec[i].DefaultValue;defaults.Invoke(item,values);if((bool)itemType.GetField("accessory").GetValue(item))accessories.Add(entry.Key+"\t"+entry.Value);}catch(TargetInvocationException error){skipped.Add(entry.Key+"\t"+entry.Value+"\t"+error.InnerException.GetType().Name);}}
   File.WriteAllLines(Path.Combine(args[1],"uninitialized-native-ids.tsv"),skipped.ToArray());Console.WriteLine("Native accessory fields recorded="+accessories.Count+"; unavailable unrelated defaults="+skipped.Count);
   File.WriteAllLines(Path.Combine(args[1],"all-native-accessories.tsv"),accessories.ToArray());
   game.GetType("Terraria.Lang",true).GetMethod("InitializeLegacyLocalization").Invoke(null,null);
   game.GetType("Terraria.ID.ContentSamples",true).GetMethod("Initialize").Invoke(null,null);
   var recipeType=game.GetType("Terraria.Recipe",true);var initialRecipes=(Array)main.GetField("recipe").GetValue(null);for(int i=0;i<initialRecipes.Length;i++)if(initialRecipes.GetValue(i)==null)initialRecipes.SetValue(Activator.CreateInstance(recipeType),i);recipeType.GetMethod("SetupRecipeGroups").Invoke(null,null);recipeType.GetMethod("SetupRecipes").Invoke(null,null);
   var recipeLines=new List<string>();var recipes=(Array)main.GetField("recipe").GetValue(null);int recipeCount=(int)recipeType.GetField("numRecipes").GetValue(null);
   for(int i=0;i<recipeCount;i++){object recipe=recipes.GetValue(i),created=recipeType.GetField("createItem").GetValue(recipe);int id=(int)itemType.GetField("type").GetValue(created),tile=(int)recipeType.GetField("requiredTile").GetValue(recipe);var materials=new List<string>();foreach(object input in (Array)recipeType.GetField("requiredItem").GetValue(recipe)){int inputId=(int)itemType.GetField("type").GetValue(input);if(inputId<=0)continue;materials.Add((names.ContainsKey(inputId)?names[inputId]:inputId.ToString())+":"+itemType.GetField("stack").GetValue(input));}recipeLines.Add((names.ContainsKey(id)?names[id]:id.ToString())+"\t"+tile+"\t"+string.Join(",",materials.ToArray()));}
   File.WriteAllLines(Path.Combine(args[1],"native-recipes.tsv"),recipeLines.ToArray());Console.WriteLine("Native recipes recorded="+recipeCount);return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

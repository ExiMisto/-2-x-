using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace SpireTerraria.Next {
 internal sealed class TerrariaRuntime {
  readonly string directory;
  readonly Assembly game;
  readonly Dictionary<string,Assembly> dependencies=new Dictionary<string,Assembly>(StringComparer.OrdinalIgnoreCase);
  bool itemRuntimeReady;
  internal Assembly NativeAssembly {get{return game;}}
  internal string GameDirectory {get{return directory;}}
  internal TerrariaRuntime(string path){
   directory=Path.GetFullPath(path);
   string executable=Path.Combine(directory,"Terraria.exe");
   if(!File.Exists(executable))throw new FileNotFoundException("Terraria.exe was not found in the configured local game directory.");
   game=Assembly.LoadFrom(executable);
   AppDomain.CurrentDomain.AssemblyResolve+=ResolveLocalDependency;
  }
  Assembly ResolveLocalDependency(object sender,ResolveEventArgs args){
   string name=new AssemblyName(args.Name).Name;
   if(name.IndexOfAny(new[]{'/', '\\', ':'})>=0)return null;
   Assembly existing;if(dependencies.TryGetValue(name,out existing))return existing;
   string file=name+".dll",local=Path.Combine(directory,file);
   if(File.Exists(local)){existing=Assembly.LoadFrom(local);dependencies[name]=existing;return existing;}
   foreach(string resource in game.GetManifestResourceNames()){
    if(!resource.EndsWith("."+file,StringComparison.OrdinalIgnoreCase)&&!resource.Equals(file,StringComparison.OrdinalIgnoreCase))continue;
    using(Stream input=game.GetManifestResourceStream(resource))using(var buffer=new MemoryStream()){
     input.CopyTo(buffer);byte[] bytes=buffer.ToArray();
     if(bytes.Length<2||bytes[0]!='M'||bytes[1]!='Z'){
      using(var compressed=new DeflateStream(new MemoryStream(bytes),CompressionMode.Decompress))using(var output=new MemoryStream()){compressed.CopyTo(output);bytes=output.ToArray();}
     }
     existing=Assembly.Load(bytes);dependencies[name]=existing;return existing;
    }
   }
   return null;
  }
  internal void Inspect(BinaryWriter writer){
   writer.Write(game.GetName().Name);
   writer.Write(game.GetName().Version.ToString());
   writer.Write(FileVersionInfo.GetVersionInfo(game.Location).FileVersion??"");
   writer.Write(IntPtr.Size*8);
   foreach(string typeName in new[]{"Terraria.Main","Terraria.Player","Terraria.Item","Terraria.NPC","Terraria.Projectile","Terraria.ID.ItemID"}){
    Type t=game.GetType(typeName,true);writer.Write(typeName);writer.Write(t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Length);
   }
  }
  internal void ItemIds(BinaryReader reader,BinaryWriter writer){
   int count=reader.ReadInt32();if(count<1||count>128)throw new InvalidDataException("Expected between 1 and 128 item symbols.");
   string[] names=new string[count];for(int i=0;i<count;i++){names[i]=reader.ReadString();if(names[i].Length>128)throw new InvalidDataException("Item symbol too long.");}
   Protocol.RequireEnd(reader);
   Type ids=game.GetType("Terraria.ID.ItemID",true);
   var values=new List<KeyValuePair<string,int>>();
   foreach(string name in names){FieldInfo field=ids.GetField(name,BindingFlags.Public|BindingFlags.Static);if(field==null||!field.IsLiteral)throw new InvalidDataException("Unknown native item symbol: "+name);values.Add(new KeyValuePair<string,int>(name,Convert.ToInt32(field.GetRawConstantValue())));}
   writer.Write(values.Count);foreach(var pair in values){writer.Write(pair.Key);writer.Write(pair.Value);}
  }
  internal void ItemStats(BinaryReader reader,BinaryWriter writer){
   string symbol=reader.ReadString();if(symbol.Length>128)throw new InvalidDataException("Item symbol too long.");Protocol.RequireEnd(reader);
   Type ids=game.GetType("Terraria.ID.ItemID",true),itemType=game.GetType("Terraria.Item",true);
   EnsureItemRuntime();
   FieldInfo idField=ids.GetField(symbol,BindingFlags.Public|BindingFlags.Static);
   if(idField==null||!idField.IsLiteral)throw new InvalidDataException("Unknown native item symbol: "+symbol);
   int id=Convert.ToInt32(idField.GetRawConstantValue());
   MethodInfo setDefaults=null;
   foreach(MethodInfo method in itemType.GetMethods(BindingFlags.Public|BindingFlags.Instance)){
    if(method.Name!="SetDefaults")continue;ParameterInfo[] parameters=method.GetParameters();if(parameters.Length==0||parameters[0].ParameterType!=typeof(int))continue;
    bool optional=true;for(int i=1;i<parameters.Length;i++)if(!parameters[i].IsOptional)optional=false;
    if(optional&&(setDefaults==null||parameters.Length<setDefaults.GetParameters().Length))setDefaults=method;
   }
   if(setDefaults==null)throw new NotSupportedException("Native Item.SetDefaults signature is not supported.");
   object item=Activator.CreateInstance(itemType);ParameterInfo[] spec=setDefaults.GetParameters();object[] arguments=new object[spec.Length];arguments[0]=id;for(int i=1;i<arguments.Length;i++)arguments[i]=spec[i].DefaultValue;
   try{setDefaults.Invoke(item,arguments);}catch(TargetInvocationException e){throw new InvalidOperationException("Native item initialization failed: "+e.InnerException,e.InnerException);}
   writer.Write(symbol);writer.Write(id);
   foreach(string fieldName in new[]{"damage","defense","useTime","useAnimation","mana","rare"}){FieldInfo f=itemType.GetField(fieldName,BindingFlags.Public|BindingFlags.Instance);if(f==null)throw new NotSupportedException("Native item field unavailable: "+fieldName);writer.Write(Convert.ToInt32(f.GetValue(item)));}
  }
  internal void EnsureItemRuntime(){
   if(itemRuntimeReady)return;
   string state=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"..","native-state"));
   Directory.CreateDirectory(state);
   Type program=game.GetType("Terraria.Program",true);
   program.GetField("SavePath",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,state);
   RuntimeHelpers.RunClassConstructor(game.GetType("Terraria.Main",true).TypeHandle);
   RuntimeHelpers.RunClassConstructor(game.GetType("Terraria.ID.ItemID",true).TypeHandle);
   RuntimeHelpers.RunClassConstructor(game.GetType("Terraria.ID.ItemID+Sets",true).TypeHandle);
   itemRuntimeReady=true;
  }
 }
}

param([string]$Pattern='ModInitializerAttribute|CombatManager|NCombatRoom|CombatRoom|RunManager|NGame|CreatureCmd|NMainMenu|ModManifest',[string]$AssemblyPath='D:/steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll')
$ErrorActionPreference='Stop'
$taskGame='D:/steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
Add-Type -TypeDefinition 'public static class SpireInspectorResolver { public static void Install() { System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (c,n) => { var p=System.IO.Path.Combine(@"D:/steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64",n.Name+".dll"); return System.IO.File.Exists(p) ? c.LoadFromAssemblyPath(p) : null; }; } }'
[SpireInspectorResolver]::Install()
$a=[Reflection.Assembly]::LoadFrom($AssemblyPath)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$types=$a.GetTypes() | Where-Object {$_.FullName -match $Pattern -and $_.FullName -notmatch '\+|PropertyName|MethodName|SignalName'}
foreach($t in $types){
 'TYPE '+$t.FullName+' BASE '+$t.BaseType.FullName
 foreach($c in $t.GetConstructors($flags)){'CTOR '+$c.ToString(); foreach($p in $c.GetParameters()){'PARAM '+$p.Name+' '+$p.ParameterType+' optional='+$p.IsOptional+' default='+$p.DefaultValue}}
 foreach($p in $t.GetProperties($flags)){'PROPERTY '+$p.ToString()+' GET '+$(if($p.GetMethod){$p.GetMethod.Attributes.ToString()}else{'none'})+' SET '+$(if($p.SetMethod){$p.SetMethod.Attributes.ToString()}else{'none'})}
 foreach($f in $t.GetFields($flags)){'FIELD '+$f.ToString()}
 foreach($e in $t.GetEvents($flags)){'EVENT '+$e.ToString()}
 foreach($m in $t.GetMethods($flags) | Where-Object {$_.Name -notmatch '^(get_|set_|add_|remove_|GetGodot|InvokeGodot|HasGodot|SetGodot|SaveGodot|RestoreGodot)'}){ 'METHOD '+$m.Attributes.ToString()+' '+$m.ToString(); foreach($p in $m.GetParameters()){'PARAM '+$p.Name+' '+$p.ParameterType+' optional='+$p.IsOptional+' default='+$p.DefaultValue} }
}

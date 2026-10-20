param([string]$GameDir='D:\steam\steamapps\common\Slay the Spire 2',[switch]$Install,[string]$UpstreamDir,[string]$RoslynDir,[string]$BaseLibPath)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalPaths.ps1')
$reference=Get-SpireUpstreamSource $UpstreamDir
$roslyn=Get-SpireRoslynDirectory $RoslynDir
Add-Type -Path (Join-Path $roslyn 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $roslyn 'Microsoft.CodeAnalysis.CSharp.dll')
$taskSources=@('Plugin.cs','NativeBridge.cs','KeyboardIsolation.cs','CampaignWire.cs','EquipmentCatalog.cs','EquipmentCards.cs','TerraModels.cs','TerraIntegration.cs','TerraEconomy.cs','TerraEvents.cs','JourneyTest.cs','ShowcaseCapture.cs' | ForEach-Object {Join-Path $taskRoot "src/$_"})
foreach($name in @('EngineWire.cs','RenderWire.cs','MaintenanceWire.cs','PermanentStats.cs')){$taskSources+=Join-Path $reference "src/$name"}
$taskTrees=[Microsoft.CodeAnalysis.SyntaxTree[]]@($taskSources | ForEach-Object {
 [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_),[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default,$_,[Text.Encoding]::UTF8)
})
$taskReferences=[System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$taskReferences.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Get-SpireBaseLibPath $GameDir $BaseLibPath)))
foreach($file in Get-ChildItem -LiteralPath (Join-Path $GameDir 'data_sts2_windows_x86_64') -Filter '*.dll'){
 try{[Reflection.AssemblyName]::GetAssemblyName($file.FullName)|Out-Null;$taskReferences.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName))}catch{}
}
$taskOptions=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithAllowUnsafe($true).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$taskCompilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SpireTerraria',$taskTrees,$taskReferences,$taskOptions)
$taskOutput=Join-Path $taskRoot 'SpireTerraria.dll'
$taskStream=[IO.File]::Create($taskOutput)
try{$taskResult=$taskCompilation.Emit($taskStream)}finally{$taskStream.Dispose()}
$taskResult.Diagnostics|Where-Object {$_.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning}|ForEach-Object {$_.ToString()}
if(!$taskResult.Success){throw 'Host compilation failed'}
Write-Output "Built $taskOutput"
if($Install){
 $destination=Join-Path $GameDir 'mods/SpireTerraria';New-Item -ItemType Directory -Force -Path $destination|Out-Null
 Copy-Item -LiteralPath $taskOutput -Destination $destination -Force
 foreach($folder in @('runtime','assets','content')){Copy-Item -LiteralPath (Join-Path $taskRoot $folder) -Destination $destination -Recurse -Force}
 Copy-Item -LiteralPath (Join-Path $taskRoot 'SpireTerraria.json') -Destination $destination -Force
 Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY.md') -Destination $destination -Force
 Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $destination -Force
 Write-Output "Installed $destination"
}

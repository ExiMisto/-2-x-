param(
 [Parameter(Mandatory=$true)][string]$GameDirectory,
 [Parameter(Mandatory=$true)][string]$OutputPath,
 [string]$BaseLibDirectory=''
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskManaged=Join-Path $GameDirectory 'data_sts2_windows_x86_64'
if(!(Test-Path -LiteralPath (Join-Path $taskManaged 'sts2.dll'))){throw 'The installed Slay the Spire 2 managed runtime was not found.'}
if(!$BaseLibDirectory) {
 $taskBaseLibCandidates=@((Join-Path $GameDirectory 'mods/BaseLib'),([IO.Path]::GetFullPath((Join-Path $GameDirectory '../../workshop/content/2868840/3737335127/BaseLib'))))
 $BaseLibDirectory=$taskBaseLibCandidates | Where-Object {Test-Path -LiteralPath (Join-Path $_ 'BaseLib.dll')} | Select-Object -First 1
 if(!$BaseLibDirectory){throw 'Install BaseLib or pass -BaseLibDirectory with its extracted directory.'}
}
$taskBaseLibPath=Join-Path $BaseLibDirectory 'BaseLib.dll'
$taskBaseLibManifestPath=Join-Path $BaseLibDirectory 'BaseLib.json'
if(!(Test-Path -LiteralPath $taskBaseLibPath) -or !(Test-Path -LiteralPath $taskBaseLibManifestPath)){throw 'BaseLib is required. Subscribe to BaseLib in the Steam Workshop or pass -BaseLibDirectory.'}
$taskBaseLibManifest=Get-Content -LiteralPath $taskBaseLibManifestPath -Raw | ConvertFrom-Json
if($taskBaseLibManifest.id -ne 'BaseLib' -or [version]($taskBaseLibManifest.version.TrimStart('v')) -lt [version]'3.4.7'){throw 'BaseLib v3.4.7 or newer is required.'}
foreach($taskCompiler in @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')) {
 $taskCompilerPath=Join-Path $PSHOME $taskCompiler
 if(!(Test-Path -LiteralPath $taskCompilerPath)){throw 'Run this script with the bundled PowerShell runtime containing Roslyn.'}
 [void][Reflection.Assembly]::LoadFrom($taskCompilerPath)
}
$taskSources=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'host') -Filter '*.cs' -File | Sort-Object FullName | ForEach-Object FullName)
$taskSources+=@(Join-Path $taskRoot 'src/Protocol.cs'; Join-Path $taskRoot 'src/FrameExchange.cs'; Join-Path $taskRoot 'src/BattleConfiguration.cs')
$taskTrees=[Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$taskParse=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Latest)
foreach($taskSource in $taskSources) {
 $taskText=[IO.File]::ReadAllText($taskSource)
 $taskTree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($taskText,$taskParse,$taskSource,[Text.Encoding]::UTF8,[Threading.CancellationToken]::None)
 $taskTrees.Add($taskTree)
}
# Use the game's actual .NET 9 runtime instead of the compiler host's framework.
# BaseLib provides the native character/item model API. No old Mod or bridge
# assembly is referenced or copied, and BaseLib remains a Workshop dependency.
$taskReferences=[Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$taskReferencePaths=[Collections.Generic.List[string]]::new()
foreach($taskDll in (Get-ChildItem -LiteralPath $taskManaged -Filter '*.dll' -File | Sort-Object Name)) {
 try {[void][Reflection.AssemblyName]::GetAssemblyName($taskDll.FullName)} catch {continue}
 $taskReference=[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($taskDll.FullName,[Microsoft.CodeAnalysis.MetadataReferenceProperties]::Assembly,$null)
 $taskReferences.Add($taskReference)
 $taskReferencePaths.Add($taskDll.FullName)
}
$taskReferences.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($taskBaseLibPath,[Microsoft.CodeAnalysis.MetadataReferenceProperties]::Assembly,$null))
$taskReferencePaths.Add([IO.Path]::GetFullPath($taskBaseLibPath))
$taskOptions=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$taskOptions=$taskOptions.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithPlatform([Microsoft.CodeAnalysis.Platform]::AnyCpu).WithDeterministic($true).WithWarningLevel(4)
$taskCompilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SpireTerrariaNextProbe',$taskTrees,$taskReferences,$taskOptions)
$taskOutputParent=Split-Path $OutputPath -Parent
New-Item -ItemType Directory -Path $taskOutputParent -Force | Out-Null
$taskStream=[IO.File]::Create($OutputPath)
try {$taskEmit=$taskCompilation.Emit($taskStream)} finally {$taskStream.Dispose()}
foreach($taskDiagnostic in $taskEmit.Diagnostics) {if($taskDiagnostic.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden){Write-Host $taskDiagnostic.ToString()}}
if(!$taskEmit.Success){throw 'Independent Spire host compilation failed.'}
$taskBuildReport=[ordered]@{
 assembly='SpireTerrariaNextProbe'
 target='net9.0'
 baselib_version=$taskBaseLibManifest.version
 compiler=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation].Assembly.GetName().Version.ToString()
 gameRuntime=(Get-Content -LiteralPath (Join-Path $taskManaged 'sts2.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
 sources=$taskSources
 references=$taskReferencePaths.ToArray()
 output=[IO.Path]::GetFullPath($OutputPath)
 sha256=(Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
}
$taskBuildReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRoot '.local/spire-build.json') -Encoding utf8
Write-Output "Compiled independent Spire host: $OutputPath"

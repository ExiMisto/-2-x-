function Get-SpireUpstreamSource([string]$UpstreamDir) {
 $taskProjectRoot=Split-Path $PSScriptRoot -Parent
 $taskCandidates=@($UpstreamDir,$env:SPIRETERRARIA_UPSTREAM_DIR,(Join-Path $taskProjectRoot '.local/TerrariaICEY'),(Join-Path $taskProjectRoot '../../_research/TerrariaICEY'))
 foreach($taskCandidate in $taskCandidates){
  if(!$taskCandidate){continue}
  foreach($taskSource in @($taskCandidate,(Join-Path $taskCandidate 'source'))){
   if((Test-Path -LiteralPath (Join-Path $taskSource 'src/EngineWire.cs')) -and (Test-Path -LiteralPath (Join-Path $taskSource 'BuildEngine.ps1'))){return (Resolve-Path -LiteralPath $taskSource).Path}
  }
  if($UpstreamDir -and $taskCandidate -eq $UpstreamDir){throw 'UpstreamDir must contain the pinned TerrariaICEY checkout or its source directory.'}
 }
 throw 'TerrariaICEY source is required separately. Supply -UpstreamDir or SPIRETERRARIA_UPSTREAM_DIR; see docs/BUILDING.md and upstream.json.'
}
function Get-SpireRoslynDirectory([string]$RoslynDir) {
 foreach($taskCandidate in @($RoslynDir,$env:SPIRETERRARIA_ROSLYN_DIR,$PSHOME,(Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell'))){
  if($taskCandidate -and (Test-Path -LiteralPath (Join-Path $taskCandidate 'Microsoft.CodeAnalysis.CSharp.dll')) -and (Test-Path -LiteralPath (Join-Path $taskCandidate 'Microsoft.CodeAnalysis.dll'))){return (Resolve-Path -LiteralPath $taskCandidate).Path}
 }
 throw 'Roslyn assemblies not found. Supply -RoslynDir pointing to Microsoft.CodeAnalysis.dll and Microsoft.CodeAnalysis.CSharp.dll.'
}
function Get-SpireBaseLibPath([string]$GameDir,[string]$BaseLibPath) {
 foreach($taskCandidate in @($BaseLibPath,(Join-Path $GameDir 'mods/BaseLib/BaseLib.dll'),(Join-Path $GameDir '../../workshop/content/2868840/3737335127/BaseLib/BaseLib.dll'))){
  if($taskCandidate -and (Test-Path -LiteralPath $taskCandidate)){return (Resolve-Path -LiteralPath $taskCandidate).Path}
 }
 throw 'BaseLib.dll not found. Supply -BaseLibPath or install BaseLib 3.4.7 in this Steam library.'
}

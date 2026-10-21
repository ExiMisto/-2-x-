param(
 [string]$TerrariaDir=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
 [string]$CscPath=(Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'),
 [switch]$CompileOnly
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot '.local/battle-test'
if(![IO.Path]::IsPathRooted($CscPath)){throw 'CscPath must be an absolute system .NET Framework compiler path.'}
if(!(Test-Path -LiteralPath $CscPath -PathType Leaf)){throw 'System .NET Framework CSC is required.'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
if(!$CompileOnly){& (Join-Path $PSScriptRoot 'Build.ps1')}
$taskProbe=Join-Path $taskOutput 'SpireNext.BattleProbe.x64.exe'
$taskSources=@('Protocol.cs','BridgeClient.cs','FrameExchange.cs','BattleProbe.cs') | ForEach-Object {Join-Path $taskRoot ('src/'+$_)}
$taskArguments=@('/nologo','/optimize+','/warn:4','/warnaserror+','/langversion:5','/platform:x64','/target:exe','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/out:'+$taskProbe))+$taskSources
& $CscPath @taskArguments
if($LASTEXITCODE -ne 0){throw 'M1 battle acceptance probe compilation failed.'}
if($CompileOnly){'Compiled the M1 x64 probe only; no worker or GUI was launched.';return}
$taskProtected=@((Join-Path $TerrariaDir 'Terraria.exe'),(Join-Path $TerrariaDir '../Slay the Spire 2/mods/SpireTerraria/SpireTerraria.dll'))
$taskHashes=@{}
foreach($taskFile in $taskProtected){if(Test-Path -LiteralPath $taskFile){$taskHashes[$taskFile]=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash}}
$taskReport=Join-Path $taskOutput 'verification.json'
try{
 & $taskProbe (Join-Path $taskRoot '.local/bin/SpireNext.Worker.exe') $TerrariaDir $taskReport
 if($LASTEXITCODE -ne 0){throw ('M1 battle acceptance failed: '+$taskReport)}
}finally{
 foreach($taskFile in $taskHashes.Keys){if((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskHashes[$taskFile]){throw ('Existing game or installed Mod changed: '+$taskFile)}}
}
'M1 native battle checks passed. Shared-memory PNG previews and report: .local/battle-test/'

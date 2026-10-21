param([string]$TerrariaDir=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskBin=Join-Path $taskRoot '.local/bin'
$taskOriginalFiles=@((Join-Path $TerrariaDir 'Terraria.exe'),(Join-Path $TerrariaDir '../Slay the Spire 2/mods/SpireTerraria/SpireTerraria.dll'))
$taskHashes=@{}
foreach($taskFile in $taskOriginalFiles){if(Test-Path -LiteralPath $taskFile){$taskHashes[$taskFile]=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash}}
& (Join-Path $PSScriptRoot 'Build.ps1')
& (Join-Path $taskBin 'SpireNext.Probe.exe') (Join-Path $taskBin 'SpireNext.Worker.exe') $TerrariaDir (Join-Path $taskRoot '.local/verification.json')
if($LASTEXITCODE -ne 0){throw 'Independent core verification failed; see .local/verification.json.'}
foreach($taskFile in $taskHashes.Keys){if((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskHashes[$taskFile]){throw ('Existing file changed: '+$taskFile)}}
'Existing game and installed Mod hashes unchanged.'

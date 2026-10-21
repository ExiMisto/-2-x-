param(
 [ValidateSet('menu','smoke','systems','classes','late','effects','moon','showcase-a','showcase-b','showcase-c')][string]$Scope='smoke',
 [ValidatePattern('^[a-zA-Z0-9_-]{1,48}$')][string]$Take='take01',
 [string]$GameDirectory='D:/steam/steamapps/common/Slay the Spire 2',
 [string]$FFmpeg='C:/Users/ExiMisto/AppData/Local/oopz/ffmpeg.exe',
 [ValidateRange(30,300)][int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskProduction=Join-Path $taskRoot 'pv_new_era'
$taskReportDirectory=Join-Path $taskProduction ('footage/reports/'+$Take)
$taskUtf8=[Text.UTF8Encoding]::new($false)
if(@(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue).Count){throw 'Save and close the game before a memory-only PV fixture is launched.'}
if(!(Test-Path -LiteralPath $FFmpeg -PathType Leaf)){throw 'The configured FFmpeg was not found.'}
if(Test-Path -LiteralPath $taskReportDirectory){throw 'A PV take already exists; choose a new Take to preserve its provenance.'}
New-Item -ItemType Directory -Path $taskReportDirectory -Force | Out-Null
function Write-PVJson([string]$Path,[object]$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 50),$taskUtf8)}
function Get-SaveSnapshot {
 $taskPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $taskSaveRoot=Join-Path $env:APPDATA 'SlayTheSpire2/steam'
 foreach($taskScanRoot in @($taskSaveRoot,(Join-Path $GameDirectory 'mods/SpireTerraria/state'),(Join-Path $GameDirectory 'mods/SpireTerrariaNextProbe/state'))){
  if(Test-Path -LiteralPath $taskScanRoot -PathType Container){foreach($taskFile in (Get-ChildItem -LiteralPath $taskScanRoot -File -Recurse)){if($taskFile.Name -ne 'settings.save'){[void]$taskPaths.Add($taskFile.FullName)}}}
 }
 $taskPrior=Join-Path $taskRoot '.local/damage-feedback/saves-before.json'
 if(Test-Path -LiteralPath $taskPrior){foreach($taskEntry in (Get-Content -LiteralPath $taskPrior -Raw|ConvertFrom-Json)){[void]$taskPaths.Add([string]$taskEntry.path)}}
 foreach($taskPath in ($taskPaths|Sort-Object)){
  if(Test-Path -LiteralPath $taskPath -PathType Leaf){[pscustomobject]@{path=$taskPath;hash=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash}}
  else{[pscustomobject]@{path=$taskPath;hash=$null}}
 }
}
$taskBefore=@(Get-SaveSnapshot)
Write-PVJson (Join-Path $taskReportDirectory 'saves-before.json') $taskBefore
$taskNames=@('SPIRE_NEXT_PV','SPIRE_PV_SCOPE','SPIRE_PV_TAKE','SPIRE_PV_DIRECTORY','SPIRE_PV_FFMPEG')
$taskOriginal=@{}
foreach($taskName in $taskNames){$taskOriginal[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
$taskFailed=$null
$taskStarted=[DateTime]::UtcNow
try{
 [Environment]::SetEnvironmentVariable('SPIRE_NEXT_PV','1','Process')
 [Environment]::SetEnvironmentVariable('SPIRE_PV_SCOPE',$Scope,'Process')
 [Environment]::SetEnvironmentVariable('SPIRE_PV_TAKE',$Take,'Process')
 [Environment]::SetEnvironmentVariable('SPIRE_PV_DIRECTORY',$taskProduction,'Process')
 [Environment]::SetEnvironmentVariable('SPIRE_PV_FFMPEG',$FFmpeg,'Process')
 & (Join-Path $PSScriptRoot 'Test-Spire.ps1') -GameDirectory $GameDirectory -Adventure -TimeoutSeconds $TimeoutSeconds
}catch{$taskFailed=$_.Exception.Message}
finally{
 foreach($taskName in $taskNames){[Environment]::SetEnvironmentVariable($taskName,$taskOriginal[$taskName],'Process')}
 foreach($taskName in @('report.json','launcher-report.json','restore-state.json','progress.json','game.log')){
  $taskSource=Join-Path $taskRoot ('.local/spire-test/'+$taskName)
  if(Test-Path -LiteralPath $taskSource -PathType Leaf){Copy-Item -LiteralPath $taskSource -Destination (Join-Path $taskReportDirectory $taskName)}
 }
 $taskAfter=@(Get-SaveSnapshot)
 Write-PVJson (Join-Path $taskReportDirectory 'saves-after.json') $taskAfter
 $taskBeforeMap=@{};foreach($taskEntry in $taskBefore){$taskBeforeMap[$taskEntry.path]=$taskEntry.hash}
 $taskAfterMap=@{};foreach($taskEntry in $taskAfter){$taskAfterMap[$taskEntry.path]=$taskEntry.hash}
 $taskChanges=@(foreach($taskEntry in $taskBefore){if(!$taskAfterMap.ContainsKey($taskEntry.path)-or $taskAfterMap[$taskEntry.path] -ne $taskEntry.hash){$taskEntry.path}})
 $taskNew=@(foreach($taskEntry in $taskAfter){if(!$taskBeforeMap.ContainsKey($taskEntry.path)){$taskEntry.path}})
 $taskLauncherPath=Join-Path $taskReportDirectory 'launcher-report.json'
 $taskLauncher=if(Test-Path -LiteralPath $taskLauncherPath){Get-Content -LiteralPath $taskLauncherPath -Raw|ConvertFrom-Json}else{$null}
 $taskIntegrity=[ordered]@{scope=$Scope;take=$Take;started_utc=$taskStarted.ToString('o');finished_utc=[DateTime]::UtcNow.ToString('o');save_files=$taskBefore.Count;changed_or_missing=$taskChanges;new_save_files=$taskNew;settings_restored=($taskLauncher -and $taskLauncher.settings_restored);live_game_processes=@(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue|ForEach-Object Id);error=$taskFailed}
 Write-PVJson (Join-Path $taskReportDirectory 'integrity.json') $taskIntegrity
 if($taskChanges.Count -or $taskNew.Count){$taskFailed='A real save changed during the fixture. Inspect integrity.json before any further recording.'}
 if(!$taskIntegrity.settings_restored){$taskFailed='Native test launcher did not confirm restoration. Inspect its restore-state.json.'}
}
if($taskFailed){throw $taskFailed}
Write-Output ('PV recording complete; save files unchanged: '+$taskBefore.Count+'; native Mod switches restored. Reports: '+$taskReportDirectory)

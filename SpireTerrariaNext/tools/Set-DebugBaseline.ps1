param(
 [string]$GameDirectory='D:/steam/steamapps/common/Slay the Spire 2',
 [switch]$Activate
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskModId='SpireTerrariaNextProbe'
$taskMod=Join-Path $GameDirectory ('mods/'+$taskModId)
$taskManifest=Get-Content -LiteralPath (Join-Path $taskMod ($taskModId+'.json')) -Raw | ConvertFrom-Json
$taskBaseLibPath=[IO.Path]::GetFullPath((Join-Path $GameDirectory '../../workshop/content/2868840/3737335127/BaseLib/BaseLib.json'))
$taskBaseLib=Get-Content -LiteralPath $taskBaseLibPath -Raw | ConvertFrom-Json
if($taskManifest.id -ne $taskModId -or @($taskManifest.dependencies | Where-Object id -eq 'BaseLib').Count -ne 1){throw 'The installed independent Mod must declare its BaseLib dependency.'}
if([version]($taskBaseLib.version.TrimStart('v')) -lt [version]'3.4.7'){throw 'BaseLib 3.4.7 or newer is required.'}
foreach($taskFile in @((Join-Path $taskMod ($taskModId+'.dll')),(Join-Path $taskMod 'worker/SpireNext.Worker.exe'))){if(!(Test-Path -LiteralPath $taskFile -PathType Leaf)){throw "Missing installed Mod file: $taskFile"}}
$taskSettingsRoot=Join-Path $env:APPDATA 'SlayTheSpire2/steam'
$taskSettingsFiles=@(Get-ChildItem -LiteralPath $taskSettingsRoot -Directory | ForEach-Object {
 $taskCandidate=Join-Path $_.FullName 'settings.save'
 if(Test-Path -LiteralPath $taskCandidate -PathType Leaf){Get-Item -LiteralPath $taskCandidate}
})
if($taskSettingsFiles.Count -ne 1){throw 'Expected exactly one Steam settings file; nothing has been changed.'}
$taskSettingsPath=$taskSettingsFiles[0].FullName
$taskSettings=Get-Content -LiteralPath $taskSettingsPath -Raw | ConvertFrom-Json
if(!$taskSettings.mod_settings -or !$taskSettings.mod_settings.PSObject.Properties['mod_list']){throw 'Unexpected Mod settings schema; nothing has been changed.'}
if($Activate) {
 if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue){throw 'Close Slay the Spire 2 before selecting the restored debug baseline.'}
 $taskBackupDirectory=Join-Path $taskRoot ('.local/debug-baseline/'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
 New-Item -ItemType Directory -Path $taskBackupDirectory | Out-Null
 Copy-Item -LiteralPath $taskSettingsPath -Destination (Join-Path $taskBackupDirectory 'settings-before.save')
 $taskItems=@($taskSettings.mod_settings.mod_list)
 foreach($taskItem in $taskItems){$taskItem.is_enabled=($taskItem.id -eq 'BaseLib' -and $taskItem.source -eq 'steam_workshop') -or ($taskItem.id -eq $taskModId -and $taskItem.source -eq 'mods_directory')}
 foreach($taskRequired in @([pscustomobject]@{id='BaseLib';source='steam_workshop'},[pscustomobject]@{id=$taskModId;source='mods_directory'})){
  if(@($taskItems | Where-Object {$_.id -eq $taskRequired.id -and $_.source -eq $taskRequired.source}).Count -eq 0){$taskItems+=[pscustomobject]@{id=$taskRequired.id;source=$taskRequired.source;is_enabled=$true}}
 }
 $taskSettings.mod_settings.mod_list=$taskItems
 $taskSettings.mod_settings.mods_enabled=$true
 $taskTemporary=$taskSettingsPath+'.spire-baseline-'+[Guid]::NewGuid().ToString('N')+'.tmp'
 try {
  [IO.File]::WriteAllText($taskTemporary,($taskSettings | ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
  [IO.File]::Move($taskTemporary,$taskSettingsPath,$true)
 } finally {if([IO.File]::Exists($taskTemporary)){[IO.File]::Delete($taskTemporary)}}
 $taskReceipt=[ordered]@{created=(Get-Date).ToString('o');mod=$taskModId;version=$taskManifest.version;bridge='independent';baselib=$taskBaseLib.version;enabled=@('BaseLib',$taskModId);host_sha256=(Get-FileHash -LiteralPath (Join-Path $taskMod ($taskModId+'.dll'))).Hash;worker_sha256=(Get-FileHash -LiteralPath (Join-Path $taskMod 'worker/SpireNext.Worker.exe')).Hash}
 [IO.File]::WriteAllText((Join-Path $taskBackupDirectory 'baseline.json'),($taskReceipt | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
}
[pscustomobject]@{activated=[bool]$Activate;mod=$taskModId;version=$taskManifest.version;baselib=$taskBaseLib.version;enabled=@($taskSettings.mod_settings.mod_list | Where-Object is_enabled | Select-Object -ExpandProperty id)} | ConvertTo-Json -Depth 4

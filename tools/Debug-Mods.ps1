param([ValidateSet('Isolate','Restore')][string]$Mode='Isolate',[string]$SettingsPath)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskStateFile=Join-Path $taskRoot '.local/mod-isolation.json'
if(!$SettingsPath -and $Mode -eq 'Restore' -and (Test-Path -LiteralPath $taskStateFile)){
 $SettingsPath=(Get-Content -LiteralPath $taskStateFile -Raw | ConvertFrom-Json).settings
}
if(!$SettingsPath){
 $taskCandidates=@(Get-ChildItem -LiteralPath (Join-Path $env:APPDATA 'SlayTheSpire2/steam') -Directory | ForEach-Object {Join-Path $_.FullName 'settings.save'} | Where-Object {Test-Path -LiteralPath $_})
 if($taskCandidates.Count -ne 1){throw 'Supply -SettingsPath for the desired Steam profile settings.save.'}
 $SettingsPath=$taskCandidates[0]
}
$taskSettings=(Resolve-Path -LiteralPath $SettingsPath).Path
if(Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue){throw 'Close Slay the Spire 2 before changing mod settings.'}
$taskSettingsObject=Get-Content -LiteralPath $taskSettings -Raw | ConvertFrom-Json
if(!$taskSettingsObject.mod_settings.mod_list){throw 'Expected mod settings were not found.'}
if($Mode -eq 'Isolate'){
 if(Test-Path -LiteralPath $taskStateFile){
  $taskState=Get-Content -LiteralPath $taskStateFile -Raw | ConvertFrom-Json
  if($taskState.active){throw 'An isolation backup is already active; restore it before making another.'}
 }
 $taskBackup=Join-Path $taskRoot ('.local/mod-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
 Copy-Item -LiteralPath $taskSettings -Destination (Join-Path $taskBackup 'settings.save')
 if(Test-Path -LiteralPath ($taskSettings+'.backup')){Copy-Item -LiteralPath ($taskSettings+'.backup') -Destination (Join-Path $taskBackup 'settings.save.backup')}
 $taskState=[pscustomobject]@{active=$true;settings=$taskSettings;backup=$taskBackup;created=(Get-Date).ToString('o');mods_enabled=$taskSettingsObject.mod_settings.mods_enabled;mods=@($taskSettingsObject.mod_settings.mod_list | ForEach-Object{[pscustomobject]@{id=$_.id;source=$_.source;is_enabled=$_.is_enabled}})}
 # Write recovery metadata before touching the settings file.
 $taskState | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $taskStateFile -Encoding utf8
 foreach($taskMod in $taskSettingsObject.mod_settings.mod_list){$taskMod.is_enabled=($taskMod.id -eq 'SpireTerraria' -and $taskMod.source -eq 'mods_directory') -or ($taskMod.id -eq 'BaseLib' -and $taskMod.source -eq 'steam_workshop')}
 $taskSettingsObject.mod_settings.mods_enabled=$true
}else{
 if(!(Test-Path -LiteralPath $taskStateFile)){throw 'No isolation backup exists.'}
 $taskState=Get-Content -LiteralPath $taskStateFile -Raw | ConvertFrom-Json
 if(!$taskState.active){throw 'Mod settings have already been restored.'}
 if($taskState.settings -ne $taskSettings){throw 'Settings path differs from the backup.'}
 foreach($taskMod in $taskSettingsObject.mod_settings.mod_list){
  $taskOriginal=@($taskState.mods | Where-Object{$_.id -eq $taskMod.id -and $_.source -eq $taskMod.source})
  if($taskOriginal.Count -eq 1){$taskMod.is_enabled=$taskOriginal[0].is_enabled}
 }
 $taskSettingsObject.mod_settings.mods_enabled=$taskState.mods_enabled
}
$taskSettingsJson=$taskSettingsObject | ConvertTo-Json -Depth 30
# Atomic replacement in the same directory; keeps unrelated preferences current.
$taskTemporary=$taskSettings+'.spire-terraria.tmp'
[IO.File]::WriteAllText($taskTemporary,$taskSettingsJson,[Text.UTF8Encoding]::new($false))
[IO.File]::Move($taskTemporary,$taskSettings,$true)
if($Mode -eq 'Restore'){$taskState.active=$false;$taskState | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $taskStateFile -Encoding utf8}
[pscustomobject]@{mode=$Mode;enabled=@($taskSettingsObject.mod_settings.mod_list | Where-Object is_enabled | Select-Object -ExpandProperty id);backup=$taskState.backup} | ConvertTo-Json -Depth 5

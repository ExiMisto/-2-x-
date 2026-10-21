param(
 [string]$GameDirectory='D:/steam/steamapps/common/Slay the Spire 2',
 [string]$BaseLibDirectory='D:/steam/steamapps/workshop/content/2868840/3737335127/BaseLib',
 [ValidateRange(10,300)][int]$TimeoutSeconds=120,
 [switch]$Adventure,
 [switch]$VictoryRegression,
 [switch]$RestoreOnly
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskTestDirectory=Join-Path $taskRoot '.local/spire-test'
$taskStatePath=Join-Path $taskTestDirectory 'restore-state.json'
$taskBackupPath=Join-Path $taskTestDirectory 'settings-before.save'
$taskLogPath=Join-Path $taskTestDirectory 'game.log'
$taskLauncherReport=Join-Path $taskTestDirectory 'launcher-report.json'
$taskNativeReportPath=Join-Path $taskTestDirectory 'report.json'
$taskProgressPath=Join-Path $taskTestDirectory 'progress.json'
$taskModId='SpireTerrariaNextProbe'
$taskUtf8=[Text.UTF8Encoding]::new($false)

function Assert-SpireClosed {
 if(@(Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue).Count -gt 0) {
  throw 'Close Slay the Spire 2 before changing its temporary test Mod switches.'
 }
}

function Write-TestJson([string]$Path,[object]$Value) {
 [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 100),$taskUtf8)
}

function Write-SettingsAtomic([string]$Path,[object]$Value) {
 # The approved launcher command must have write access to the settings directory.
 # Keep the original file in place until the complete replacement is ready.
 $taskTemporary=$Path+'.spire-next-'+[Guid]::NewGuid().ToString('N')+'.tmp'
 try {
  [IO.File]::WriteAllText($taskTemporary,($Value | ConvertTo-Json -Depth 100),$taskUtf8)
  [IO.File]::Move($taskTemporary,$Path,$true)
 } finally {
  if([IO.File]::Exists($taskTemporary)){[IO.File]::Delete($taskTemporary)}
 }
}

function Restore-ModSwitches([object]$State,[string]$SettingsPath) {
 Assert-SpireClosed
 if($State.settings_path -ne $SettingsPath){throw 'Recovery state does not match the uniquely detected settings file.'}
 if(!(Test-Path -LiteralPath $SettingsPath)){throw 'Settings are missing; preserve the backup and recover them manually.'}
 $taskCurrent=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
 if(!$taskCurrent.mod_settings -or !$taskCurrent.mod_settings.PSObject.Properties['mod_list']) {
  throw 'Current settings schema changed; the original backup is preserved for recovery.'
 }
 $taskCurrentItems=@($taskCurrent.mod_settings.mod_list)
 foreach($taskItem in $taskCurrentItems) {
  $taskOriginal=@($State.mod_list | Where-Object {$_.id -eq $taskItem.id -and $_.source -eq $taskItem.source})
  $taskEnabled=$false
  if($taskOriginal.Count -gt 0){$taskEnabled=[bool]$taskOriginal[0].is_enabled}
  $taskItem | Add-Member -NotePropertyName 'is_enabled' -NotePropertyValue $taskEnabled -Force
 }
 foreach($taskOriginal in @($State.mod_list)) {
  if(@($taskCurrentItems | Where-Object {$_.id -eq $taskOriginal.id -and $_.source -eq $taskOriginal.source}).Count -eq 0) {
   $taskCurrentItems+=([pscustomobject]@{id=$taskOriginal.id;is_enabled=[bool]$taskOriginal.is_enabled;source=$taskOriginal.source})
  }
 }
 $taskCurrent.mod_settings.mod_list=@($taskCurrentItems)
 $taskCurrent.mod_settings.mods_enabled=[bool]$State.mods_enabled
 Write-SettingsAtomic $SettingsPath $taskCurrent
 $State.status='restored'
 $State | Add-Member -NotePropertyName 'restored_utc' -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
 Write-TestJson $taskStatePath $State
}

Assert-SpireClosed
$taskSettingsRoot=Join-Path $env:APPDATA 'SlayTheSpire2/steam'
if(!(Test-Path -LiteralPath $taskSettingsRoot)){throw 'No Slay the Spire 2 Steam settings directory was found.'}
$taskSettingsFiles=@(Get-ChildItem -LiteralPath $taskSettingsRoot -Directory | ForEach-Object {
 $taskCandidate=Join-Path $_.FullName 'settings.save'
 if(Test-Path -LiteralPath $taskCandidate -PathType Leaf){Get-Item -LiteralPath $taskCandidate}
})
if($taskSettingsFiles.Count -ne 1){throw 'Expected exactly one Steam settings.save; no settings have been changed.'}
$taskSettingsPath=$taskSettingsFiles[0].FullName
New-Item -ItemType Directory -Path $taskTestDirectory -Force | Out-Null
$taskPreviousState=$null
if(Test-Path -LiteralPath $taskStatePath){$taskPreviousState=Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json}
if($RestoreOnly) {
 if(!$taskPreviousState){throw 'There is no recorded test state to restore.'}
 Restore-ModSwitches $taskPreviousState $taskSettingsPath
 Write-Output "Original Mod switches restored. Backup retained: $taskBackupPath"
 return
}
if($taskPreviousState -and $taskPreviousState.status -ne 'restored') {
 throw 'A prior test has pending recovery. Run tools/Test-Spire.ps1 -RestoreOnly before starting another test.'
}
$taskExe=Join-Path $GameDirectory 'SlayTheSpire2.exe'
$taskManifestPath=Join-Path $GameDirectory ('mods/'+$taskModId+'/'+$taskModId+'.json')
$taskHostPath=Join-Path $GameDirectory ('mods/'+$taskModId+'/'+$taskModId+'.dll')
if(!(Test-Path -LiteralPath $taskExe)){throw 'SlayTheSpire2.exe was not found.'}
if(!(Test-Path -LiteralPath $taskManifestPath) -or !(Test-Path -LiteralPath $taskHostPath)){throw 'Install the independent test Mod with tools/Build-Spire.ps1 -Install first.'}
$taskManifest=Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
if($taskManifest.id -ne $taskModId -or !$taskManifest.has_dll){throw 'The installed probe manifest is invalid.'}
$taskBaseLibManifestPath=Join-Path $BaseLibDirectory 'BaseLib.json'
if(!(Test-Path -LiteralPath $taskBaseLibManifestPath) -or !(Test-Path -LiteralPath (Join-Path $BaseLibDirectory 'BaseLib.dll'))){throw 'The isolated test requires the installed BaseLib Workshop dependency.'}
$taskBaseLibManifest=Get-Content -LiteralPath $taskBaseLibManifestPath -Raw | ConvertFrom-Json
if($taskBaseLibManifest.id -ne 'BaseLib' -or [version]($taskBaseLibManifest.version.TrimStart('v')) -lt [version]'3.4.7'){throw 'The test requires BaseLib v3.4.7 or newer.'}
if(@($taskManifest.dependencies | Where-Object {$_.id -eq 'BaseLib'}).Count -ne 1){throw 'Rebuild the Mod: its manifest must declare the BaseLib dependency.'}
$taskSettings=Get-Content -LiteralPath $taskSettingsPath -Raw | ConvertFrom-Json
if(!$taskSettings.mod_settings -or !$taskSettings.mod_settings.PSObject.Properties['mod_list'] -or !$taskSettings.mod_settings.PSObject.Properties['mods_enabled']) {
 throw 'Unexpected settings schema. No settings have been changed.'
}
# Archive only these known outputs, so a previous successful run cannot satisfy
# this run's report validation or trigger an old initializer error.
$taskRunId=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$taskArchiveDirectory=Join-Path $taskTestDirectory ('previous-runs/'+$taskRunId)
foreach($taskPriorOutput in @($taskNativeReportPath,$taskProgressPath,$taskLogPath)) {
 if(Test-Path -LiteralPath $taskPriorOutput -PathType Leaf) {
  New-Item -ItemType Directory -Path $taskArchiveDirectory -Force | Out-Null
  [IO.File]::Move($taskPriorOutput,(Join-Path $taskArchiveDirectory ([IO.Path]::GetFileName($taskPriorOutput))))
 }
}
foreach($taskPriorImage in @(Get-ChildItem -LiteralPath $taskTestDirectory -Filter '*.png' -File)) {
 New-Item -ItemType Directory -Path $taskArchiveDirectory -Force | Out-Null
 [IO.File]::Move($taskPriorImage.FullName,(Join-Path $taskArchiveDirectory $taskPriorImage.Name))
}
Copy-Item -LiteralPath $taskSettingsPath -Destination $taskBackupPath -Force
$taskOriginalItems=@($taskSettings.mod_settings.mod_list | ForEach-Object {
 [pscustomobject]@{id=$_.id;is_enabled=[bool]$_.is_enabled;source=$_.source}
})
$taskState=[pscustomobject][ordered]@{
 status='prepared'
 created_utc=[DateTime]::UtcNow.ToString('o')
 settings_path=$taskSettingsPath
 backup_path=$taskBackupPath
 backup_sha256=(Get-FileHash -LiteralPath $taskBackupPath -Algorithm SHA256).Hash
 mods_enabled=[bool]$taskSettings.mod_settings.mods_enabled
 mod_list=$taskOriginalItems
}
# Persist rollback information before writing anything outside the project.
Write-TestJson $taskStatePath $taskState
$taskProcess=$null
$taskExitCode=$null
$taskTimedOut=$false
$taskFailure=$null
$taskRestored=$false
$taskNativeReportPassed=$false
$taskStartedUtc=$null
$taskWatch=[Diagnostics.Stopwatch]::StartNew()
try {
 Assert-SpireClosed
 $taskItems=@($taskSettings.mod_settings.mod_list)
 foreach($taskItem in $taskItems){$taskItem.is_enabled=$false}
 $taskProbeEntries=@($taskItems | Where-Object {$_.id -eq $taskModId -and $_.source -eq 'mods_directory'})
 if($taskProbeEntries.Count -eq 0) {
  $taskItems+=([pscustomobject]@{id=$taskModId;is_enabled=$true;source='mods_directory'})
 } else {
  foreach($taskItem in $taskProbeEntries){$taskItem.is_enabled=$true}
 }
 # BaseLib is the only enabled prerequisite. The classic Mod and every other
 # gameplay Mod stay disabled for this run; finally restores every prior flag.
 $taskBaseLibEntries=@($taskItems | Where-Object {$_.id -eq 'BaseLib' -and $_.source -eq 'steam_workshop'})
 if($taskBaseLibEntries.Count -eq 0) {
  $taskItems+=([pscustomobject]@{id='BaseLib';is_enabled=$true;source='steam_workshop'})
 } else {
  foreach($taskItem in $taskBaseLibEntries){$taskItem.is_enabled=$true}
 }
 $taskSettings.mod_settings.mod_list=@($taskItems)
 $taskSettings.mod_settings.mods_enabled=$true
 Write-SettingsAtomic $taskSettingsPath $taskSettings
 $taskState.status='running'
 Write-TestJson $taskStatePath $taskState
 # A packaged Godot executable locates its adjacent PCK from this working directory.
 # Game arguments follow -- so Godot does not consume the self-test switch.
 $taskTestSwitch=if($VictoryRegression){'--spire-next-victory-regression'}elseif($Adventure){'--spire-next-adventuretest'}else{'--spire-next-selftest'}
 $taskArguments=@('--log-file',('"'+$taskLogPath+'"'),'--',$taskTestSwitch)
 # Steam normally supplies these to the game. Set them only for this child
 # process when launching the installed executable from the test harness.
 $taskStartedUtc=[DateTime]::UtcNow
 $taskProcess=Start-Process -FilePath $taskExe -WorkingDirectory $GameDirectory -ArgumentList $taskArguments -Environment @{SteamAppId='2868840';SteamGameId='2868840'} -WindowStyle Hidden -PassThru
 Write-Output "Spire native battle self-test started (PID $($taskProcess.Id)); limit $TimeoutSeconds seconds."
 $taskWatch.Restart()
 while(!$taskProcess.WaitForExit(250)) {
  $taskLiveLog=$null
  if(Test-Path -LiteralPath $taskLogPath -PathType Leaf) {
   try {
    $taskLogStream=[IO.FileStream]::new($taskLogPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $taskLogReader=[IO.StreamReader]::new($taskLogStream)
    try {$taskLiveLog=$taskLogReader.ReadToEnd()} finally {$taskLogReader.Dispose()}
   } catch [IO.IOException] { }
  }
  if($taskLiveLog -and $taskLiveLog.Contains('Exception thrown when calling mod initializer of type SpireTerraria.Next.Host.EntryProbe')) {
   throw 'Independent Mod initializer failed. See this run''s game.log for the underlying exception.'
  }
  if($taskWatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
   $taskTimedOut=$true
   throw "Spire native battle self-test exceeded $TimeoutSeconds seconds."
  }
 }
 $taskExitCode=$taskProcess.ExitCode
 if($taskExitCode -ne 0){throw "Spire native battle self-test exited with code $taskExitCode."}
 if(!(Test-Path -LiteralPath $taskNativeReportPath -PathType Leaf)){throw 'Game exited without producing a new native self-test report.'}
 if((Get-Item -LiteralPath $taskNativeReportPath).LastWriteTimeUtc -lt $taskStartedUtc.AddSeconds(-2)){throw 'The native self-test report predates this game launch.'}
 $taskNativeReport=Get-Content -LiteralPath $taskNativeReportPath -Raw | ConvertFrom-Json
 if($taskNativeReport.passed -isnot [bool] -or !$taskNativeReport.passed) {
  $taskNativeError='Native self-test report did not pass.'
  if($taskNativeReport.error){$taskNativeError+=' '+[string]$taskNativeReport.error}
  throw $taskNativeError
 }
 $taskNativeReportPassed=$true
} catch {
 $taskFailure=$_.Exception.Message
} finally {
 if($taskProcess) {
  try {
   if(!$taskProcess.HasExited) {
    [void]$taskProcess.CloseMainWindow()
    if(!$taskProcess.WaitForExit(3000)){$taskProcess.Kill();[void]$taskProcess.WaitForExit(5000)}
   }
   if($taskProcess.HasExited){$taskExitCode=$taskProcess.ExitCode}
  } catch {
   if(!$taskFailure){$taskFailure=$_.Exception.Message}
  }
 }
 try {
  Restore-ModSwitches $taskState $taskSettingsPath
  $taskRestored=$true
 } catch {
  $taskState.status='restore_failed'
  $taskState | Add-Member -NotePropertyName 'restore_error' -NotePropertyValue $_.Exception.Message -Force
  Write-TestJson $taskStatePath $taskState
  $taskRecoveryMessage='Settings recovery is pending. Close the game and run tools/Test-Spire.ps1 -RestoreOnly; the original backup is preserved.'
  if($taskFailure){$taskFailure+=' '+$taskRecoveryMessage}else{$taskFailure=$taskRecoveryMessage}
 }
 if($taskProcess){$taskProcess.Dispose()}
 $taskWatch.Stop()
 $taskReport=[ordered]@{
  success=(!$taskFailure)
  exit_code=$taskExitCode
  timed_out=$taskTimedOut
  settings_restored=$taskRestored
  native_report_passed=$taskNativeReportPassed
  enabled_test_mods=@('BaseLib',$taskModId)
  elapsed_seconds=[Math]::Round($taskWatch.Elapsed.TotalSeconds,2)
  error=$taskFailure
  game_log=$taskLogPath
  native_report=$taskNativeReportPath
  previous_outputs=$taskArchiveDirectory
  settings_backup=$taskBackupPath
  restore_state=$taskStatePath
 }
 Write-TestJson $taskLauncherReport $taskReport
}
Write-Output "Launcher report: $taskLauncherReport"
Write-Output "Settings restored: $taskRestored; game exit code: $taskExitCode"
if($taskFailure){throw $taskFailure}

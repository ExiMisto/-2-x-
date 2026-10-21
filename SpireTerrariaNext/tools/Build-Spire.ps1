param(
 [string]$GameDirectory='',
 [string]$BaseLibDirectory='',
 [switch]$Install,
 [switch]$SkipWorkerBuild
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskModId='SpireTerrariaNextProbe'
$taskStage=Join-Path $taskRoot ('.local/spire_mod/'+$taskModId)
if(!$GameDirectory){$GameDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Slay the Spire 2'))}
if(!(Test-Path -LiteralPath (Join-Path $GameDirectory 'data_sts2_windows_x86_64/sts2.dll'))){throw 'Pass -GameDirectory with the installed Slay the Spire 2 directory.'}
if($PSVersionTable.PSVersion.Major -lt 7){throw 'Run Build-Spire.ps1 with PowerShell 7 (pwsh), not Windows PowerShell 5.'}
$taskPwsh=Join-Path $PSHOME 'pwsh.exe'
if(!(Test-Path -LiteralPath $taskPwsh)){throw 'PowerShell 7 with its bundled Roslyn compiler is required.'}
New-Item -ItemType Directory -Path $taskStage -Force | Out-Null
if(!$SkipWorkerBuild){& (Join-Path $PSScriptRoot 'Build.ps1')}
& $taskPwsh -NoProfile -File (Join-Path $PSScriptRoot 'Compile-Spire.ps1') -GameDirectory $GameDirectory -BaseLibDirectory $BaseLibDirectory -OutputPath (Join-Path $taskStage ($taskModId+'.dll'))
if($LASTEXITCODE -ne 0){throw 'Spire host compilation failed.'}
$taskManifest=[ordered]@{
 id=$taskModId
 name='泰拉瑞亚 × 杀戮尖塔 2'
 author='ExiMisto'
 description='四职业泰拉远征：即时战斗、尖塔路线、装备奖励、饰品药水、材料锻造与词条。需要本机安装泰拉瑞亚及 BaseLib。'
 version='0.2.0'
 has_pck=$false
 has_dll=$true
 dependencies=@([ordered]@{id='BaseLib';min_version='3.4.7'})
 affects_gameplay=$true
}
$taskManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskStage ($taskModId+'.json')) -Encoding utf8
$taskWorker=Join-Path $taskRoot '.local/bin/SpireNext.Worker.exe'
if(!(Test-Path -LiteralPath $taskWorker)){throw 'Build the independent worker before installing the host.'}
$taskWorkerStage=Join-Path $taskStage 'worker'
New-Item -ItemType Directory -Path $taskWorkerStage -Force | Out-Null
Copy-Item -LiteralPath $taskWorker -Destination (Join-Path $taskWorkerStage 'SpireNext.Worker.exe') -Force
$taskObsoleteWorker=Join-Path $taskStage 'SpireNext.Worker.exe'
if(Test-Path -LiteralPath $taskObsoleteWorker -PathType Leaf){Remove-Item -LiteralPath $taskObsoleteWorker}
$taskTarget=$taskStage
if($Install) {
 $taskMods=[IO.Path]::GetFullPath((Join-Path $GameDirectory 'mods'))
 $taskTarget=[IO.Path]::GetFullPath((Join-Path $taskMods $taskModId))
 if([IO.Path]::GetDirectoryName($taskTarget).TrimEnd('\') -ne $taskMods.TrimEnd('\')){throw 'Unexpected Mod install target.'}
 New-Item -ItemType Directory -Path $taskTarget -Force | Out-Null
 foreach($taskName in @(($taskModId+'.dll'),($taskModId+'.json'))) {
  Copy-Item -LiteralPath (Join-Path $taskStage $taskName) -Destination (Join-Path $taskTarget $taskName) -Force
 }
 $taskWorkerTarget=Join-Path $taskTarget 'worker'
 New-Item -ItemType Directory -Path $taskWorkerTarget -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $taskWorkerStage 'SpireNext.Worker.exe') -Destination (Join-Path $taskWorkerTarget 'SpireNext.Worker.exe') -Force
 $taskObsoleteWorker=Join-Path $taskTarget 'SpireNext.Worker.exe'
 if(Test-Path -LiteralPath $taskObsoleteWorker -PathType Leaf){Remove-Item -LiteralPath $taskObsoleteWorker}
}
Write-Output "Terraria Spire Mod ready: $taskTarget (BaseLib >= 3.4.7)"

param([string]$TerrariaDir='D:\steam\steamapps\common\Terraria',[string]$UpstreamDir)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'LocalPaths.ps1')
$taskReference=Get-SpireUpstreamSource $UpstreamDir
New-Item -ItemType Directory -Force -Path (Join-Path $taskRoot 'validation') | Out-Null
$taskFiles=@('EngineWire.cs','MaintenanceWire.cs','PermanentStats.cs','RenderWire.cs')|ForEach-Object{[IO.Path]::Combine($taskReference,'src',$_)}
$taskArguments=@('/nologo','/unsafe+','/optimize+','/target:exe','/reference:System.Web.Extensions.dll',('/out:'+ [IO.Path]::Combine($taskRoot,'CampaignProbe.exe')),[IO.Path]::Combine($taskRoot,'src','NativeBridge.cs'),[IO.Path]::Combine($taskRoot,'src','CampaignWire.cs'),[IO.Path]::Combine($taskRoot,'src','EquipmentCatalog.cs'),[IO.Path]::Combine($taskRoot,'src','CampaignProbe.cs'))+$taskFiles
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" @taskArguments
if($LASTEXITCODE -ne 0){throw 'Campaign probe compilation failed'}
& (Join-Path $taskRoot 'CampaignProbe.exe') $taskRoot $TerrariaDir 2>&1 | Tee-Object -FilePath (Join-Path $taskRoot 'validation/campaign-probe.log')
if($LASTEXITCODE -ne 0){throw 'Campaign verification failed'}

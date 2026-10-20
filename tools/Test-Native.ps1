param([string]$TerrariaDir='D:\steam\steamapps\common\Terraria',[string]$UpstreamDir)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'LocalPaths.ps1')
$reference=Get-SpireUpstreamSource $UpstreamDir
New-Item -ItemType Directory -Force -Path (Join-Path $taskRoot 'validation') | Out-Null
$taskFiles=@('EngineWire.cs','MaintenanceWire.cs','PermanentStats.cs','RenderWire.cs')|ForEach-Object{[IO.Path]::Combine($reference,'src',$_)}
$taskFiles+=Join-Path $taskRoot 'src/CampaignWire.cs'
$taskArguments=@('/nologo','/unsafe+','/optimize+','/target:exe',('/out:'+ [IO.Path]::Combine($taskRoot,'NativeProbe.exe')),[IO.Path]::Combine($taskRoot,'src','NativeBridge.cs'),[IO.Path]::Combine($taskRoot,'src','NativeProbe.cs'))+$taskFiles
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" @taskArguments
if($LASTEXITCODE -ne 0){throw 'Native probe compilation failed'}
$taskOutput=& (Join-Path $taskRoot 'NativeProbe.exe') $taskRoot $TerrariaDir 2>&1
$taskExit=$LASTEXITCODE
$taskOutput|Set-Content -LiteralPath (Join-Path $taskRoot 'validation/native-probe.log') -Encoding utf8
$taskOutput
if($taskExit -ne 0){throw 'Native simulation / render verification failed'}

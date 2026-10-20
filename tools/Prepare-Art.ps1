param([string]$TerrariaDir='D:\steam\steamapps\common\Terraria',[string]$UpstreamDir)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'LocalPaths.ps1')
$taskReference=Get-SpireUpstreamSource $UpstreamDir
$taskFiles=@('EngineWire.cs','MaintenanceWire.cs','PermanentStats.cs','RenderWire.cs')|ForEach-Object{[IO.Path]::Combine($taskReference,'src',$_)}
$taskArguments=@('/nologo','/unsafe+','/optimize+','/target:exe',('/out:'+ [IO.Path]::Combine($taskRoot,'SceneryProbe.exe')),[IO.Path]::Combine($taskRoot,'src','NativeBridge.cs'),[IO.Path]::Combine($taskRoot,'src','CampaignWire.cs'),[IO.Path]::Combine($taskRoot,'src','EquipmentCatalog.cs'),[IO.Path]::Combine($taskRoot,'src','SceneryProbe.cs'))+$taskFiles
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" @taskArguments
if($LASTEXITCODE -ne 0){throw 'Scenery probe compilation failed'}
& (Join-Path $taskRoot 'SceneryProbe.exe') $taskRoot $TerrariaDir
if($LASTEXITCODE -ne 0){throw 'Scenery probe failed'}
$taskUi=Join-Path $taskRoot 'assets/ui'
New-Item -ItemType Directory -Force -Path $taskUi | Out-Null
Copy-Item -Path (Join-Path $taskRoot 'saves/ui-export/*.png') -Destination $taskUi -Force
foreach($taskClass in @('Warrior','Ranger','Mage','Summoner')){Copy-Item -LiteralPath (Join-Path $taskRoot "saves/preview-$taskClass.png") -Destination $taskUi -Force}
Add-Type -AssemblyName System.Drawing
foreach($taskClass in @('Warrior','Ranger','Mage','Summoner')){
 $taskBitmap=[Drawing.Bitmap]::new((Join-Path $taskRoot "saves/portrait-$taskClass.png"))
 # The player is centered at x=490; exclude other projectile layers from the portrait.
 $taskCrop=$taskBitmap.Clone([Drawing.Rectangle]::new(464,342,54,80),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $taskCrop.Save((Join-Path $taskUi "portrait-$taskClass.png"))
 $taskCrop.Dispose()
 $taskBitmap.Dispose()
}

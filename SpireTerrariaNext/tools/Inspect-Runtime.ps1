param([ValidateSet('metadata','il','texture','scene','data','members')][string]$Mode='metadata',[string]$Type='Terraria.Main',[string]$Method='Initialize',[string]$TerrariaDir='D:/steam/steamapps/common/Terraria')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOut=Join-Path $taskRoot '.local/bootstrap-research'
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskGac=Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_32'
$taskRefs=@('System.Windows.Forms.dll','System.Drawing.dll')
foreach($taskName in @('Microsoft.Xna.Framework','Microsoft.Xna.Framework.Graphics','Microsoft.Xna.Framework.Game')) {
 $taskAssembly=Get-ChildItem -LiteralPath (Join-Path $taskGac $taskName) -Recurse -Filter ($taskName+'.dll') -File | Select-Object -First 1
 if(!$taskAssembly){throw "Missing installed XNA assembly: $taskName"}
 $taskRefs+=$taskAssembly.FullName
}
$taskArgs=@('/nologo','/target:exe','/platform:x86','/optimize+','/warn:4',('/out:'+(Join-Path $taskOut 'SpireNext.RuntimeInspector.exe')))
foreach($taskRef in $taskRefs){$taskArgs+=('/reference:'+$taskRef)}
$taskArgs+=(Join-Path $PSScriptRoot 'RuntimeInspector.cs')
$taskArgs+=(Join-Path $taskRoot 'src/NativeRenderer.cs')
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') @taskArgs
if($LASTEXITCODE -ne 0){throw 'Runtime inspector build failed.'}
$taskRunArgs=@($TerrariaDir,$taskOut,$Mode)
if($Mode -eq 'il'){$taskRunArgs+=@($Type,$Method)}
if($Mode -eq 'members'){$taskRunArgs+=@($Type)}
& (Join-Path $taskOut 'SpireNext.RuntimeInspector.exe') @taskRunArgs
if($LASTEXITCODE -ne 0){throw 'Runtime inspector failed.'}

param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot '.local/damage-feedback/native'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskGac=Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_32'
$taskArgs=@('/nologo','/optimize+','/langversion:5','/warn:4','/warnaserror+','/platform:x86','/target:exe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $taskOutput 'NativeCombatTextProbe.exe')))
foreach($taskName in @('Microsoft.Xna.Framework','Microsoft.Xna.Framework.Graphics','Microsoft.Xna.Framework.Game')) {
 $taskAssembly=Get-ChildItem -LiteralPath (Join-Path $taskGac $taskName) -Recurse -Filter ($taskName+'.dll') -File | Select-Object -First 1
 $taskArgs+=('/reference:'+$taskAssembly.FullName)
}
foreach($taskName in @('Protocol','TerrariaRuntime','FrameExchange','BattleConfiguration','NativeRenderer','NativeArena')) {$taskArgs+=(Join-Path $taskRoot ('src/'+$taskName+'.cs'))}
$taskArgs+=(Join-Path $PSScriptRoot 'NativeCombatTextProbe.cs')
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') @taskArgs
if($LASTEXITCODE -ne 0){throw 'Combat text probe compilation failed.'}
if(!$CompileOnly){
 & (Join-Path $taskOutput 'NativeCombatTextProbe.exe') (Split-Path $taskRoot -Parent) $taskOutput
 if($LASTEXITCODE -ne 0){throw 'Native combat text probe failed. See .local/damage-feedback/native/combat-text-verification.json.'}
}

param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskBin=Join-Path $taskRoot '.local/bin'
New-Item -ItemType Directory -Path $taskBin -Force | Out-Null
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
if(!(Test-Path -LiteralPath $taskCsc)){throw '.NET Framework x86 CSC is required.'}
$taskCommon=@('/nologo','/optimize+','/warn:4','/warnaserror+','/target:exe')
$taskGac=Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_32'
$taskRefs=@('System.Windows.Forms.dll','System.Drawing.dll')
foreach($taskName in @('Microsoft.Xna.Framework','Microsoft.Xna.Framework.Graphics','Microsoft.Xna.Framework.Game')) {
 $taskAssembly=Get-ChildItem -LiteralPath (Join-Path $taskGac $taskName) -Recurse -Filter ($taskName+'.dll') -File | Select-Object -First 1
 if(!$taskAssembly){throw "Missing installed XNA assembly: $taskName"}
 $taskRefs+=$taskAssembly.FullName
}
$taskWorkerArguments=$taskCommon+@('/platform:x86',('/out:'+(Join-Path $taskBin 'SpireNext.Worker.exe')))
foreach($taskRef in $taskRefs){$taskWorkerArguments+=('/reference:'+$taskRef)}
foreach($taskSource in @('Protocol','TerrariaRuntime','FrameExchange','BattleConfiguration','NativeRenderer','NativeArena','Worker')){$taskWorkerArguments+=(Join-Path $taskRoot ('src/'+$taskSource+'.cs'))}
& $taskCsc @taskWorkerArguments
if($LASTEXITCODE -ne 0){throw 'Worker compilation failed.'}
$taskProbeArguments=$taskCommon+@('/platform:anycpu','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $taskBin 'SpireNext.Probe.exe')),(Join-Path $taskRoot 'src/Protocol.cs'),(Join-Path $taskRoot 'src/BridgeClient.cs'),(Join-Path $taskRoot 'src/Probe.cs'))
& $taskCsc @taskProbeArguments
if($LASTEXITCODE -ne 0){throw 'Probe compilation failed.'}
$taskFixtureArguments=$taskCommon+@('/platform:anycpu',('/out:'+(Join-Path $taskBin 'SpireNext.UnresponsiveFixture.exe')),(Join-Path $taskRoot 'tools/fixtures/UnresponsiveWorker.cs'))
& $taskCsc @taskFixtureArguments
if($LASTEXITCODE -ne 0){throw 'Timeout fixture compilation failed.'}
'Built independent worker and probe.'

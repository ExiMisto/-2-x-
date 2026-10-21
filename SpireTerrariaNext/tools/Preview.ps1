param(
 [switch]$SmokeTest,
 [switch]$BuildOnly,
 [string]$TerrariaDir='D:/steam/steamapps/common/Terraria',
 [ValidateSet('EnchantedSword','WoodenSword','WaterBolt')][string]$Weapon='EnchantedSword'
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskBin=Join-Path $taskRoot '.local/bin'
New-Item -ItemType Directory -Path $taskBin -Force | Out-Null
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(!(Test-Path -LiteralPath $taskCsc)){throw '.NET Framework x64 CSC is required.'}
$taskPreview=Join-Path $taskBin 'SpireNext.Preview.exe'
$taskArguments=@('/nologo','/optimize+','/warn:4','/warnaserror+','/target:winexe','/platform:x64','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',('/out:'+$taskPreview))
foreach($taskSource in @('Protocol','BridgeClient','FrameExchange','BattlePreview')){$taskArguments+=(Join-Path $taskRoot ('src/'+$taskSource+'.cs'))}
& $taskCsc @taskArguments
if($LASTEXITCODE -ne 0){throw 'Preview compilation failed.'}
if($BuildOnly){[pscustomobject]@{built=$true;path=$taskPreview;bits=64} | ConvertTo-Json -Compress;return}
$taskWorker=Join-Path $taskBin 'SpireNext.Worker.exe'
if(!(Test-Path -LiteralPath $taskWorker)){throw 'Build the worker first with tools/Build.ps1.'}
foreach($taskPath in @($taskRoot,$taskWorker,$TerrariaDir)){if($taskPath.Contains('"')){throw 'Paths containing quotes are unsupported.'}}
$taskRunArguments=@('--root',('"'+$taskRoot+'"'),'--worker',('"'+$taskWorker+'"'),'--game',('"'+$TerrariaDir+'"'),'--weapon',$Weapon)
if($SmokeTest){$taskRunArguments+='--smoke'}
$taskWindowStyle=if($SmokeTest){'Hidden'}else{'Normal'}
$taskProcess=Start-Process -FilePath $taskPreview -ArgumentList $taskRunArguments -WindowStyle $taskWindowStyle -Wait -PassThru
if($SmokeTest){
 $taskReport=Join-Path $taskRoot '.local/preview-test/result.json'
 if(!(Test-Path -LiteralPath $taskReport)){throw 'Preview exited without a smoke-test report.'}
 $taskResult=Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
 $taskResult | ConvertTo-Json -Depth 5
 if($taskProcess.ExitCode -ne 0 -or !$taskResult.passed){throw 'Preview smoke test failed; see .local/preview-test/result.json.'}
}elseif($taskProcess.ExitCode -ne 0){throw ('Preview exited with code '+$taskProcess.ExitCode)}

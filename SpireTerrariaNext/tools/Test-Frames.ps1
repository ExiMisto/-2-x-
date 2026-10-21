param(
 [string]$CscPath=(Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe')
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot '.local/frame-test'
if(![IO.Path]::IsPathRooted($CscPath)){throw 'CscPath must be an absolute path to the system .NET Framework C# compiler.'}
if(!(Test-Path -LiteralPath $CscPath -PathType Leaf)){throw 'System .NET Framework CSC is required.'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskSources=@((Join-Path $taskRoot 'src/FrameExchange.cs'),(Join-Path $taskRoot 'src/FrameProbe.cs'))
$taskCommon=@('/nologo','/optimize+','/warn:4','/warnaserror+','/langversion:5','/target:exe','/reference:System.Core.dll','/reference:System.Web.Extensions.dll')
$taskWriter=Join-Path $taskOutput 'SpireNext.FrameWriter.x86.exe'
$taskReader=Join-Path $taskOutput 'SpireNext.FrameProbe.x64.exe'
& $CscPath @($taskCommon+@('/platform:x86',('/out:'+$taskWriter))+$taskSources)
if($LASTEXITCODE -ne 0){throw 'Synthetic x86 frame writer compilation failed.'}
& $CscPath @($taskCommon+@('/platform:x64',('/out:'+$taskReader))+$taskSources)
if($LASTEXITCODE -ne 0){throw 'Synthetic x64 frame reader compilation failed.'}
$taskReport=Join-Path $taskOutput 'verification.json'
& $taskReader test $taskWriter $taskReport
if($LASTEXITCODE -ne 0){throw ('Synthetic frame transport checks failed: '+$taskReport)}
'Only synthetic BGRA32 data was tested; native game rendering was not exercised.'

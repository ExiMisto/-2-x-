param([string]$Mode='', [switch]$CompileOnly, [string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot '.local/native-pv-test'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskGac=Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_32'
$taskArgs=@('/nologo','/optimize+','/langversion:5','/warn:4','/warnaserror+','/platform:x86','/target:exe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $taskOutput 'NativePvProbe.exe')))
foreach($taskName in @('Microsoft.Xna.Framework','Microsoft.Xna.Framework.Graphics','Microsoft.Xna.Framework.Game')) {$taskAssembly=Get-ChildItem -LiteralPath (Join-Path $taskGac $taskName) -Recurse -Filter ($taskName+'.dll') -File | Select-Object -First 1; $taskArgs+=('/reference:'+$taskAssembly.FullName)}
foreach($taskName in @('Protocol','TerrariaRuntime','FrameExchange','BattleConfiguration','NativeRenderer','NativeArena')) {$taskArgs+=(Join-Path $taskRoot ('src/'+$taskName+'.cs'))}
$taskArgs+=(Join-Path $PSScriptRoot 'NativePvProbe.cs')
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') @taskArgs
if($LASTEXITCODE -ne 0){throw 'Adventure probe compilation failed.'}
if(!$CompileOnly){& (Join-Path $taskOutput 'NativePvProbe.exe') (Split-Path $taskRoot -Parent) $taskOutput $Mode; if($LASTEXITCODE -ne 0){throw 'Native adventure probe failed. See .local/native-pv-test/verification.json.'}}

param([string]$TerrariaDirectory='D:/steam/steamapps/common/Terraria')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOut=Join-Path $taskRoot '.local/drop-validation'
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskProgram=Join-Path $taskOut 'VerifyBossSources.exe'
& $taskCsc /nologo /target:exe /platform:x86 /optimize+ /reference:System.Core.dll "/out:$taskProgram" (Join-Path $taskRoot 'src/TerrariaRuntime.cs') (Join-Path $taskRoot 'src/Protocol.cs') (Join-Path $PSScriptRoot 'VerifyBossSources.cs')
if($LASTEXITCODE -ne 0){throw 'Boss source validator compilation failed.'}
& $taskProgram $TerrariaDirectory $taskOut
if($LASTEXITCODE -ne 0){throw 'Actual NPC rule source verification failed.'}

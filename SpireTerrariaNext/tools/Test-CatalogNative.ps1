param([string]$TerrariaDirectory='D:/steam/steamapps/common/Terraria')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOut=Join-Path $taskRoot '.local/adventure-test'
$taskInput=Join-Path $taskOut 'native-categories.tsv'
if(!(Test-Path -LiteralPath $taskInput)){throw 'Run Test-Adventure.ps1 first to export the current catalog.'}
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskProgram=Join-Path $taskOut 'CatalogNativeValidation.exe'
& $taskCsc /nologo /target:exe /platform:x86 /optimize+ /reference:System.Core.dll "/out:$taskProgram" (Join-Path $taskRoot 'src/TerrariaRuntime.cs') (Join-Path $taskRoot 'src/Protocol.cs') (Join-Path $PSScriptRoot 'CatalogNativeValidation.cs')
if($LASTEXITCODE -ne 0){throw 'Native catalog validator compilation failed.'}
& $taskProgram $TerrariaDirectory $taskInput (Join-Path $taskOut 'native-categories-report.tsv')
if($LASTEXITCODE -ne 0){throw 'Native catalog defaults or categories failed verification.'}

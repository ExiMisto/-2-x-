param(
 [Parameter(Mandatory=$true)][string]$TerrariaDirectory,
 [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath (Join-Path $TerrariaDirectory 'Terraria.exe'))){throw 'Pass -TerrariaDirectory with the local Terraria installation.'}
$taskOut=Join-Path $taskRoot '.local/item-symbol-export'
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent) -Force | Out-Null
$taskProgram=Join-Path $taskOut 'ExportItemSymbols.exe'
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $taskCsc /nologo /target:exe /platform:x86 /optimize+ /reference:System.Core.dll "/out:$taskProgram" (Join-Path $taskRoot 'src/TerrariaRuntime.cs') (Join-Path $taskRoot 'src/Protocol.cs') (Join-Path $PSScriptRoot 'ExportItemSymbols.cs')
if($LASTEXITCODE -ne 0){throw 'Local ItemID symbol exporter compilation failed.'}
& $taskProgram $TerrariaDirectory $OutputPath
if($LASTEXITCODE -ne 0){throw 'Local ItemID symbol export failed.'}

param([string]$TerrariaSymbolsPath='', [string]$TerrariaDirectory=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
if(!$TerrariaSymbolsPath){$TerrariaSymbolsPath=Join-Path $taskRoot '.local/item-symbols.txt'}
if(!(Test-Path -LiteralPath $TerrariaSymbolsPath)){
 & (Join-Path $PSScriptRoot 'Export-ItemSymbols.ps1') -TerrariaDirectory $TerrariaDirectory -OutputPath $TerrariaSymbolsPath
}
Add-Type -Path @((Join-Path $taskRoot 'host/GearCatalog.cs'),(Join-Path $taskRoot 'host/AdventureModel.cs'),(Join-Path $PSScriptRoot 'AdventureModelTests.cs'))
$taskReport=[SpireTerraria.Next.Host.AdventureModelTests]::Run($TerrariaSymbolsPath)
$taskOut=Join-Path $taskRoot '.local/adventure-test'
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskReport | Set-Content -LiteralPath (Join-Path $taskOut 'report.json') -Encoding utf8
[SpireTerraria.Next.Host.GearCatalog]::All.NativeSymbols | Sort-Object -Unique | Set-Content -LiteralPath (Join-Path $taskOut 'native-symbols.txt') -Encoding utf8
$taskNativeEntries=foreach($taskDefinition in [SpireTerraria.Next.Host.GearCatalog]::All){for($taskIndex=0;$taskIndex -lt $taskDefinition.NativeSymbols.Length;$taskIndex++){"$($taskDefinition.Kind)`t$($taskDefinition.NativeSymbols[$taskIndex])`t$taskIndex"}}
$taskNativeEntries | Sort-Object -Unique | Set-Content -LiteralPath (Join-Path $taskOut 'native-categories.tsv') -Encoding utf8
Write-Output $taskReport

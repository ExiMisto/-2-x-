param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskFiles=@('host/GearCatalog.cs','host/AdventureModel.cs','host/AdventureSession.cs','src/BattleConfiguration.cs','tools/AdventureSessionRestoreTests.cs') | ForEach-Object {Join-Path $taskRoot $_}
Add-Type -Path $taskFiles -IgnoreWarnings -WarningAction SilentlyContinue
$taskProbeDirectory=Join-Path $taskRoot ('.local/save-integrity/session-restore-'+[Guid]::NewGuid().ToString('N'))
$taskReport=[SpireTerraria.Next.Host.AdventureSessionRestoreTests]::Run($taskProbeDirectory)
$taskReport | Set-Content -LiteralPath (Join-Path $taskProbeDirectory 'report.json') -Encoding utf8
Write-Output $taskReport

param([string]$TerrariaDir='D:\steam\steamapps\common\Terraria',[string]$UpstreamDir)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalPaths.ps1')
$taskReferenceRoot=Get-SpireUpstreamSource $UpstreamDir
$taskGenerated=Join-Path $taskRoot 'native/generated'
New-Item -ItemType Directory -Force -Path $taskGenerated | Out-Null
$taskSession=Get-Content -LiteralPath (Join-Path $taskReferenceRoot 'tools/engine/NativeSession.cs') -Raw
$taskSession=$taskSession.Replace('NativeWorldRules.Install(h);','NativeWorldRules.Install(h);SpireRuntime.Install(h);')
$taskSession=$taskSession.Replace('if(command==47){','if(SpireRuntime.Command(command,reader,writer))continue;'+[Environment]::NewLine+'     if(command==47){')
$taskSession=$taskSession.Replace('TMain.sittingManager.ClearPlayerAnchors();','SpireRuntime.BeforeStep(input,player);'+[Environment]::NewLine+'  TMain.sittingManager.ClearPlayerAnchors();')
$taskSession=$taskSession.Replace('NativePerf.StepPhase(ref stepStamp,4);NativeSmartCursor.End();','SpireRuntime.AfterStep(input,player);NativePerf.StepPhase(ref stepStamp,4);NativeSmartCursor.End();')
$taskExpansion=Get-Content -LiteralPath (Join-Path $taskReferenceRoot 'tools/engine/NativeExpansion.cs') -Raw
$taskExpansion=$taskExpansion.Replace('TickConstruction(p);TickEcology();if(force||BossCount>0)','TickConstruction(p);TickEcology();if(SpireRuntime.Enabled){SpireRuntime.World(p);return;}if(force||BossCount>0)')
$taskExpansion=$taskExpansion.Replace('if(BossCount==0||constructionTick%60!=0)return;','if(SpireRuntime.Enabled||BossCount==0||constructionTick%60!=0)return;')
[IO.File]::WriteAllText((Join-Path $taskGenerated 'NativeSession.cs'),$taskSession)
[IO.File]::WriteAllText((Join-Path $taskGenerated 'NativeExpansion.cs'),$taskExpansion)
if(($taskSession | Select-String -Pattern 'SpireRuntime\.' -AllMatches).Matches.Count -ne 4){throw 'Native session extension anchors changed'}
if(($taskExpansion | Select-String -Pattern 'SpireRuntime\.' -AllMatches).Matches.Count -ne 3){throw 'Native arena extension anchors changed'}
# Compile an overlay of the pinned upstream source. Keep the clone untouched.
$taskBuild=Get-Content -LiteralPath (Join-Path $taskReferenceRoot 'BuildEngine.ps1') -Raw
$taskBuild=$taskBuild.Replace("Join-Path `$PSScriptRoot '.local\native-runtime'","Join-Path `$taskRoot 'runtime'")
$taskBuild=$taskBuild.Replace("(Join-Path `$PSScriptRoot 'tools\engine\NativeSession.cs')","(Join-Path `$taskGenerated 'NativeSession.cs')")
$taskBuild=$taskBuild.Replace("(Join-Path `$PSScriptRoot 'tools\engine\NativeExpansion.cs')","(Join-Path `$taskGenerated 'NativeExpansion.cs')")
$taskBuild=$taskBuild.Replace('& $compiler @arguments',"`$arguments+=(Join-Path `$taskRoot 'native/SpireRuntime.cs')`n`$arguments+=(Join-Path `$taskRoot 'native/SpireScenery.cs')`n`$arguments+=(Join-Path `$taskRoot 'src/CampaignWire.cs')`n`$arguments+=(Join-Path `$taskRoot 'src/EquipmentCatalog.cs')`n& `$compiler @arguments")
$taskBuild=$taskBuild.Replace('$PSScriptRoot','$taskReferenceRoot')
& ([scriptblock]::Create($taskBuild)) -TerrariaDir $TerrariaDir
if($LASTEXITCODE -ne 0){throw 'Extended native worker compilation failed'}
Copy-Item -LiteralPath (Join-Path $taskReferenceRoot '../LICENSE.md') -Destination (Join-Path $taskRoot 'runtime/TerrariaICEY-LICENSE.md') -Force
Copy-Item -LiteralPath (Join-Path $taskReferenceRoot '../THIRD-PARTY-NOTICES.md') -Destination (Join-Path $taskRoot 'runtime/TerrariaICEY-THIRD-PARTY-NOTICES.md') -Force

param([string]$TerrariaDirectory='D:/steam/steamapps/common/Terraria',[switch]$CompileOnly)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path $taskRoot '.local/catalog-recovery-native'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Add-Type -Path (Join-Path $taskRoot 'host/GearCatalog.cs')
$taskArmorKeys=@('squire','valhalla','huntress','red-riding','apprentice','dark-artist','monk','shinobi')
$taskAccessorySymbols=@('WarriorEmblem','RangerEmblem','SorcererEmblem','SummonerEmblem','Shackle','Aglet','AnkletoftheWind','SharkToothNecklace','AvengerEmblem','ApprenticeScarf','SquireShield','HuntressBuckler','MonkBelt','ObsidianShield','SpectreBoots','FeralClaws','ManaRegenerationBand','MagicCuffs','ManaFlower','PygmyNecklace','StarVeil','MoonCharm')
$taskSentrySymbols=@('DD2BallistraTowerT3Popper','DD2FlameburstTowerT3Popper','DD2ExplosiveTrapT3Popper','DD2LightningAuraT3Popper')
$taskRecovered=@([SpireTerraria.Next.Host.GearCatalog]::All | Where-Object {($_.Kind -eq 'Armor' -and $taskArmorKeys -contains $_.Key.Split(':')[-1]) -or ($_.Kind -eq 'Accessory' -and $taskAccessorySymbols -contains $_.NativeSymbols[0]) -or ($_.Kind -eq 'Weapon' -and $taskSentrySymbols -contains $_.NativeSymbols[0])})
if($taskRecovered.Count -ne 34){throw "Expected exactly 34 recovered definitions, found $($taskRecovered.Count)."}
$taskLines=foreach($taskGear in $taskRecovered){@($taskGear.Key,$taskGear.Kind,$taskGear.Stage,($taskGear.NativeSymbols -join ' ')) -join "`t"}
$taskInput=Join-Path $taskOutput 'recovered-content.tsv'
[IO.File]::WriteAllLines($taskInput,$taskLines,[Text.UTF8Encoding]::new($false))
$taskGac=Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_32'
$taskArgs=@('/nologo','/optimize+','/langversion:5','/warn:4','/warnaserror+','/platform:x86','/target:exe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $taskOutput 'NativeRecoveryContentProbe.exe')))
foreach($taskName in @('Microsoft.Xna.Framework','Microsoft.Xna.Framework.Graphics','Microsoft.Xna.Framework.Game')) {
 $taskAssembly=Get-ChildItem -LiteralPath (Join-Path $taskGac $taskName) -Recurse -Filter ($taskName+'.dll') -File | Select-Object -First 1
 if(!$taskAssembly){throw "Missing installed XNA assembly: $taskName"}
 $taskArgs+=('/reference:'+$taskAssembly.FullName)
}
foreach($taskName in @('Protocol','TerrariaRuntime','FrameExchange','BattleConfiguration','NativeRenderer','NativeArena')) {$taskArgs+=(Join-Path $taskRoot ('src/'+$taskName+'.cs'))}
$taskArgs+=(Join-Path $PSScriptRoot 'NativeRecoveryContentProbe.cs')
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') @taskArgs
if($LASTEXITCODE -ne 0){throw 'Recovery content probe compilation failed.'}
if(!$CompileOnly) {
 & (Join-Path $taskOutput 'NativeRecoveryContentProbe.exe') $TerrariaDirectory $taskOutput $taskInput
 if($LASTEXITCODE -ne 0){throw "Recovered content native battle/render verification failed. See $taskOutput/verification.json."}
}

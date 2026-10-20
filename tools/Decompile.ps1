param([Parameter(Mandatory=$true)][string[]]$Types,[string]$AssemblyPath='D:\steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'decompiler/lib/netstandard2.0/ICSharpCode.Decompiler.dll')
$assembly=$AssemblyPath
$settings=[ICSharpCode.Decompiler.DecompilerSettings]::new()
$settings.ThrowOnAssemblyResolveErrors=$false
$decompiler=[ICSharpCode.Decompiler.CSharp.CSharpDecompiler]::new($assembly,$settings)
$destination=Join-Path $PSScriptRoot '../validation/decompiled'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach($type in $Types){
    $text=$decompiler.DecompileTypeAsString([ICSharpCode.Decompiler.TypeSystem.FullTypeName]::new($type))
    $path=Join-Path $destination ($type.Split('.')[-1]+'.cs')
    [IO.File]::WriteAllText($path,$text)
    Write-Output $path
}

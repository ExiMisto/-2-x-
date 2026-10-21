# 构建新版 Mod

本文适用于 `SpireTerrariaNext/`。仓库根目录的旧版 `tools/` 使用的是另一套桥接，不能混用。

## 前置

- Windows x64；当前验证为《杀戮尖塔 2》v0.111.0、Terraria 1.4.5.8。
- 两款游戏均已安装，且在同一 Steam 库的 `common` 目录下。运行时从尖塔安装目录定位同级 `Terraria/`。
- BaseLib v3.4.7 或更新版本，单独从其创意工坊安装。
- PowerShell 7（`pwsh`，需要随附的 `Microsoft.CodeAnalysis.dll`、`Microsoft.CodeAnalysis.CSharp.dll`）。不要使用 Windows PowerShell 5。
- .NET Framework 4 和 XNA Framework 4 的 32 位运行库；worker 使用 Windows 的 `Microsoft.NET/Framework/v4.0.30319/csc.exe` 编译。

## 编译

在新版源码目录运行，替换示例路径：

```powershell
cd SpireTerrariaNext
pwsh -NoProfile -File tools/Generate-InventoryModels.ps1
pwsh -NoProfile -File tools/Build-Spire.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Slay the Spire 2'
```

BaseLib 自动查找游戏的 `mods/BaseLib` 以及同一 Steam 库的 `workshop/content/2868840/3737335127/BaseLib`。其他位置请显式指定：

```powershell
pwsh -NoProfile -File tools/Build-Spire.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Slay the Spire 2' -BaseLibDirectory 'D:/path/to/BaseLib'
```

输出目录为 `.local/spire_mod/SpireTerrariaNextProbe/`，包含宿主 DLL、Mod 清单和 `worker/SpireNext.Worker.exe`。不复制 Terraria、尖塔、BaseLib、XNA 或提取的游戏素材。

## 安装与更新

先保存并退出尖塔，再为上述构建命令追加 `-Install`，或将输出中的整个 `SpireTerrariaNextProbe` 文件夹复制到尖塔 `mods/`。只启用 BaseLib 和新版融合 Mod，关闭旧版 SpireTerraria，重启后选择泰拉职业开启测试新局。

现有新版安装目录的 `state/` 包含独立远征装备状态，更新时保留。不要用旧版装备状态覆盖新版，也不要为了更新删除玩家的原版存档。完整退出后继续远征尚未全面验收。

## 回归

```powershell
pwsh -NoProfile -File tools/Test-Adventure.ps1 -TerrariaDirectory 'D:/SteamLibrary/steamapps/common/Terraria'
pwsh -NoProfile -File tools/Test-SessionRestore.ps1
pwsh -NoProfile -File tools/Test-CatalogNative.ps1 -TerrariaDirectory 'D:/SteamLibrary/steamapps/common/Terraria'
```

模型和序列化检查不启动尖塔。首次模型检查会从本机 Terraria 导出 ItemID 常量名称到被忽略的 `.local/`，不读取玩家存档。原生分类检查需要本机 Terraria。`Test-Spire.ps1`、原生战斗和图形探针会运行实际游戏或访问图形设备，需先关闭游戏并阅读 [试玩说明](SPIRE-PROBE.md)；它们不是构建或安装的必经步骤。

调试脚本中的默认安装路径记录了开发环境。换机器运行这些检查时，使用各脚本的 `-GameDirectory`、`-BaseLibDirectory` 或 `-TerrariaDirectory` 参数；宣传片专用脚本还需显式指定 FFmpeg。正常游玩不需要 FFmpeg、AE 或 MuseScore。

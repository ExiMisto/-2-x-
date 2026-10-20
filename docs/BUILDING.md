# 本地构建

需要 Windows、PowerShell 7、.NET Framework 4 CSC、XNA 4 运行时、本地 Terraria 1.4.5.8、Slay the Spire 2 v0.111.0 与 BaseLib v3.4.7。宿主编译通过 Roslyn 完成，不依赖 .NET SDK。

## 外部桥接源码

单独检出 TerrariaICEY，固定在 `upstream.json` 的提交。阅读并遵守其许可，再运行上游 `Ensure-Dependencies.ps1` 准备固定版本的本地依赖。不要把上游源码、游戏程序集或提取素材提交到本仓库。

```powershell
git clone https://github.com/Hatsunetrump/TerrariaICEY.git C:/dev/TerrariaICEY
git -C C:/dev/TerrariaICEY checkout 70bc8822f4bd1fec54e007bc9a156c1c39d71ead
pwsh -File C:/dev/TerrariaICEY/Ensure-Dependencies.ps1
```

后续脚本接受 `-UpstreamDir C:/dev/TerrariaICEY`，也可设置进程环境变量 `SPIRETERRARIA_UPSTREAM_DIR`。默认还会寻找项目内 `.local/TerrariaICEY` 与旧本地工作区的相对路径。

## 构建、导出素材与安装

在项目根目录运行，先关闭尖塔：

```powershell
pwsh -File tools/Build-Native.ps1 -TerrariaDir 'D:/steam/steamapps/common/Terraria' -UpstreamDir C:/dev/TerrariaICEY
pwsh -File tools/Prepare-Art.ps1 -TerrariaDir 'D:/steam/steamapps/common/Terraria' -UpstreamDir C:/dev/TerrariaICEY
pwsh -File tools/Build.ps1 -GameDir 'D:/steam/steamapps/common/Slay the Spire 2' -UpstreamDir C:/dev/TerrariaICEY -Install
```

`Build-Native.ps1` 生成 `runtime/` 与 `native/generated/`，`Prepare-Art.ps1` 从本机游戏导出 `assets/`。`Build.ps1 -Install` 将宿主 DLL、manifest、上述运行文件与内容目录复制到游戏的 `mods/SpireTerraria`。

Roslyn 默认从 PowerShell 的程序集目录查找；找不到时提供 `-RoslynDir`，指向同时包含 `Microsoft.CodeAnalysis.dll` 与 `Microsoft.CodeAnalysis.CSharp.dll` 的目录。也支持 `SPIRETERRARIA_ROSLYN_DIR`。BaseLib 默认在当前 Steam 库的 Workshop 目录或 `mods/BaseLib` 寻找，可用 `-BaseLibPath` 显式指定。

## 开发工具

`tools/Test-Campaign.ps1` 接受同样的 `-TerrariaDir` 和 `-UpstreamDir`，运行原生图形设备回归。宿主主菜单可通过创建空的 `journey.request` / `economy.request` 触发集成检查，结果会在 Mod 目录生成。

`Generate-Equipment.ps1` 是装备配置生成器；已生成的 `src/EquipmentCatalog.cs`、`src/EquipmentCards.cs` 与 `content/equipment.json` 已纳入源码，因此正常构建无需重新生成。重新生成时需要先从本机 Terraria 的 `Terraria.ID.ItemID` 导出常量到 `validation/decompiled/ItemID.cs`。`Decompile.ps1` 为可选研究工具，需要自行在 `tools/decompiler/lib/netstandard2.0/` 安装兼容的 ICSharpCode.Decompiler；该目录不上传。

`Debug-Mods.ps1` 用于临时只启用 BaseLib 与本 Mod，支持 `-SettingsPath` 指定自己的 Steam 配置。切换前会在 `.local/` 保存恢复状态，关闭游戏后用 `-Mode Restore` 恢复；备份不上传。

`ShowcaseCapture.cs` 是可选的游戏内 viewport 录制入口，仅检测到显式 `showcase.request` 时触发，不录桌面。使用时在请求选项中指定本机 FFmpeg 路径。

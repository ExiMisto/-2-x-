# 泰拉瑞亚 × 杀戮尖塔 2

在《杀戮尖塔2》的原生选角页选择泰拉职业，沿尖塔地图探索，在房间内实时战斗，再通过原生奖励、商店、宝箱和火堆构筑装备。当前开发版保留新独立战斗接口，并接回 BaseLib 的角色、装备牌组、饰品栏与药水栏；旧版源码单独保留作对照。

## 当前玩法

- 原生选角页提供战士、射手、法师、召唤师。目录共 307 项：192 把独立武器、53 套防具配置、48 件饰品、14 种消耗品，使用 383 个原生物品符号。补回酒馆三阶哨兵、八套远古军团护甲和遗漏饰品，保留职业与阶段筛选。
- 四职业分别以附魔剑、金弓、水矢、雀杖加皮鞭开局，均配木套装、手榴弹、赫尔墨斯靴、云朵瓶、再生手环、治疗药水与魔力药水。武器、防具进入真实装备牌组；饰品和药水使用尖塔原生栏位与提示。
- 每次战斗在两件装备和一个随机材料包之间选择。已有装备及近期出现的装备降低再出现权重。三个武器槽满后选择替换；护甲以整套装备，投掷物不占武器槽。
- 火堆主行动为休息、单件强化或材料锻造。锻造先显示材料成本与产物范围，支付后随机出现三件装备供选择；可提前制作下一幕的入门装备。每个火堆另有三次免费重铸，可采用新词条或保留旧词条。
- 强化按单件基础值加算：+1 = +20%、+2 = +40%，替换装备不继承。三幕生命上限为 300 / 600 / 900，每进入下一幕增加初始值 300。
- 精英和 Boss 饰品来自实际击败敌人的原版来源，包含专家宝藏袋；没有专属饰品或已经拥有时给材料。宝箱使用尖塔原生饰品拾取界面和获得动画，只发泰拉饰品，不追加原版遗物或战斗奖励页。
- 商店卡牌位卖装备，白色卡牌位卖通用投掷物，遗物位卖饰品，药水位卖泰拉药水。事件按原房间身份映射到九种普通主题、九种远古主题，提供不同的装备、饰品、材料交换、单件强化与词条选择，部分需要金币或当前生命作为代价。
- 普通战斗分波刷出当前阶段敌人；六种精英／Boss 使用独立建筑场景。普通场地较小，Boss 场地较大；连续地基、低平台和保留生命的越界回收防止掉落卡关。没有塔防目标，远古军团哨兵仍可作为普通武器使用。
- 战斗显示泰拉原生伤害跳字，沿用实际伤害、暴击字体、玩家受伤颜色与持续伤害文字，并随原生生命周期飘动、消失。饰品说明按本机版本列出具体效果和触发条件，在尖塔原生饰品栏、商店、宝箱与敌人掉落提示中显示。

## 尖塔内试玩

安装目录：`Slay the Spire 2/mods/SpireTerrariaNextProbe/`。

只启用 **BaseLib v3.4.7 或更新版本** 与 **泰拉瑞亚 × 杀戮尖塔 2**，关闭旧版 **SpireTerraria** 及其他 Mod 后重启，从新的单人远征选择泰拉职业。Mod 内部 ID 仍为 `SpireTerrariaNextProbe`，便于沿用安装目录。泰拉瑞亚须位于尖塔的同级目录，本机需有 XNA 4 和 .NET Framework 4。当前适配版本：Terraria 1.4.5.8、尖塔 v0.111.0。

A / D 移动，空格跳跃，S 下穿平台，鼠标左键攻击。1～3 切武器，4～6 选择通用投掷物后左键投掷，Q 快捷投掷，H 治疗药水，J 魔力药水，P 暂停；药水也可在原生栏位点击后选择使用。战斗保留尖塔顶栏、饰品和药水栏，暂停尖塔卡牌战斗及其快捷键处理；打开地图等界面或失焦时暂停。右上角 ↩ 用于调试回到本房间卡牌战斗。

详见 [试玩与限制](docs/SPIRE-PROBE.md)。

## 构建与验证

使用 Windows、PowerShell 7（含 Roslyn）、.NET Framework 4 和 XNA 4。工作进程使用 Windows 的 x86 CSC，宿主引用本机尖塔的 .NET 9 程序集与 BaseLib。没有额外打包游戏或前置库。

~~~powershell
$taskPwsh='pwsh'
& $taskPwsh -NoProfile -File tools/Generate-InventoryModels.ps1
& $taskPwsh -NoProfile -File tools/Build-Spire.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Slay the Spire 2' -Install
& $taskPwsh -NoProfile -File tools/Test-Adventure.ps1
& $taskPwsh -NoProfile -File tools/Test-CatalogNative.ps1
& $taskPwsh -NoProfile -File tools/Test-NativeAdventure.ps1
& $taskPwsh -NoProfile -File tools/Test-NativeWeaponSwitch.ps1
& $taskPwsh -NoProfile -File tools/Test-NativeCombatText.ps1
& $taskPwsh -NoProfile -File tools/Test-NativeRecoveryContent.ps1
& $taskPwsh -NoProfile -File tools/Test-SessionRestore.ps1
& $taskPwsh -NoProfile -File tools/Test-Spire.ps1 -Adventure -TimeoutSeconds 240
& $taskPwsh -NoProfile -File tools/Test-Spire.ps1 -VictoryRegression -TimeoutSeconds 240
~~~

改动装备目录后先运行 `Generate-InventoryModels.ps1`，再构建宿主。BaseLib 仅作为编译与运行前置，不复制到融合 Mod 内。

上面的游戏路径是示例，请替换为实际安装路径。构建脚本会查找游戏 `mods/BaseLib` 或同一 Steam 库的 BaseLib 创意工坊目录；找不到时另传 `-BaseLibDirectory '实际的 BaseLib 目录'`。不加 `-Install` 时只在 `.local/spire_mod/SpireTerrariaNextProbe/` 生成 Mod。完整步骤见 [构建指南](docs/BUILDING.md)。

尖塔自测只启用 BaseLib 与新版 Mod，使用游戏原生内存存档，并在结束后恢复原有 Mod 开关。图形自测需要启动游戏及写入 AppData 设置的权限。导出画面来自游戏自身 viewport／渲染目标，不截取桌面或另一块屏幕。输出保存在被忽略的 .local/；不加载泰拉玩家角色或世界。

## 已验证与待完成

2026-10-21 最终恢复版通过 2448 项模型断言、383 个本机原生物品分类、192 把武器的短时攻击启动与绘制检查，以及 56 个补回装备的效果与绘制样本。已修复武器手持帧为空、挥舞动作不更新、怪物越界时重置状态、装备奖励图标不可见和战斗进程未回收等回退。

泰拉战斗自然获胜后自动调用尖塔原生 `win` 并等待异步结算完成，避免原版敌人死亡后分裂／补怪导致卡关。寄生后生成四怪、斧头机器人连续接替、偷金怪死后换人三类真实遭遇均已验证可以领奖并返回地图；泰拉 Boss 掉落来源保留。

**实际游戏已走通原生选角→地图→自然战斗胜利→装备奖励→火堆→商店→原生宝箱拾取→事件／远古之民→下一战斗**，并验证顶栏、饰品、药水、清晰血条、药水确认消费和地图暂停。另实际执行两次原生 `win` 命令及战斗中返回主菜单，战斗层消失、输入恢复、对应 worker 退出；最终安装文件与构建产物哈希一致。详见 [本轮恢复与验收](docs/M3-VALIDATION.md)、[旧版内容恢复](docs/CATALOG-RECOVERY.md) 与 [装备来源](docs/EQUIPMENT-VALIDATION.md)。

最新伤害与饰品说明版再次通过实际游戏流程、2452 项模型断言、24 项伤害文字检查及 49 个饰品原生短样本。战斗栏、商店和宝箱的原生悬浮框均已画面复核，战斗提示遮挡已修复；见 [伤害显示验收](docs/DAMAGE-FEEDBACK.md) 与 [48 件饰品说明](docs/ACCESSORY-DESCRIPTIONS.md)。

节点间装备数据写入 Mod 的 state/，包括奖励缓存、材料、词条机会及锻造待选结果。已有远征缺失或不匹配的装备状态会停止接管并提示，不会重新发起装覆盖；19 项隔离恢复检查通过。418 个真实档案、进度和历史文件与测试前哈希一致，旧版源码、安装和快照均保留。

**真实退出游戏后续玩的整套流程仍需验收**。192 把武器检查证明短时攻击与绘制无异常，不代表全部特殊机制与整局平衡通过。四职业三幕通关、完整 Boss 阶段、复杂特效／光照和原生音效转发仍未全面完成。人物已接入原生绘制层，场景仍由自写合成器使用本机纹理绘制，尚未完整接入 Terraria Main.Draw。

源码在本目录开发，没有链接旧版 Mod、TerrariaICEY 的 worker 或其协议。安装仅包含自有宿主 DLL、清单和自有 worker；不打包游戏程序集或提取素材。GitHub 提供源码，本次不创建创意工坊条目或二进制 Release。来源见 [SOURCES](docs/SOURCES.md)，后续路线见 [ROADMAP](docs/ROADMAP.md)。

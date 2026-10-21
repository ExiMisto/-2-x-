# 旧版装备内容恢复记录

2026-10-21。保留 Next 独立战斗接口，以 Classic 目录为内容对照；本次只恢复遗漏装备及职业筛选，不恢复塔防房间。

## 本次变化

新增 4 把酒馆三阶哨兵法杖、8 套酒馆护甲和 22 种饰品。目录从 273 项增至 307 项：192 武器、53 套护甲、48 饰品、14 消耗品。共增加 50 个原生物品符号，目录总计使用 383 个不同符号。

护甲仍以整套装备替换，继承既有奖励、商店和锻造流程。酒馆二阶套装属于第三幕普通入门装备，可通过既有的“提前锻造下一幕入门装备”规则制作；三阶套装属于第三幕稀有装备。没有新增全局伤害成长或强化规则。

三阶哨兵法杖归召唤师第三幕稀有池，不属于可提前锻造的入门装备：

| 武器 | 原生符号 | 物品 ID |
| --- | --- | --- |
| 弩车法杖 | `DD2BallistraTowerT3Popper` | 3826 |
| 爆炸烈焰法杖 | `DD2FlameburstTowerT3Popper` | 3820 |
| 爆炸机关法杖 | `DD2ExplosiveTrapT3Popper` | 3834 |
| 闪电光环法杖 | `DD2LightningAuraT3Popper` | 3831 |

| 职业 | 第三幕普通护甲及原生符号 | 第三幕稀有护甲及原生符号 |
| --- | --- | --- |
| 战士 | 侍卫：`SquireGreatHelm SquirePlating SquireGreaves`（3800–3802） | 英灵殿骑士：`SquireAltHead SquireAltShirt SquireAltPants`（3871–3873） |
| 射手 | 女猎人：`HuntressWig HuntressJerkin HuntressPants`（3803–3805） | 红色骑术：`HuntressAltHead HuntressAltShirt HuntressAltPants`（3877–3879） |
| 法师 | 学徒：`ApprenticeHat ApprenticeRobe ApprenticeTrousers`（3797–3799） | 暗黑艺术家：`ApprenticeAltHead ApprenticeAltShirt ApprenticeAltPants`（3874–3876） |
| 召唤师 | 武僧：`MonkBrows MonkShirt MonkPants`（3806–3808） | 渗透忍者：`MonkAltHead MonkAltShirt MonkAltPants`（3880–3882） |

| 新增饰品原生符号 | 物品 ID | 幕 | 职业范围 |
| --- | --- | --- | --- |
| `WarriorEmblem` | 490 | 2 | 战士 |
| `RangerEmblem` | 491 | 2 | 射手 |
| `SorcererEmblem` | 489 | 2 | 法师 |
| `SummonerEmblem` | 2998 | 2 | 召唤师 |
| `Shackle` | 216 | 1 | 通用 |
| `Aglet` | 285 | 1 | 通用 |
| `AnkletoftheWind` | 212 | 1 | 通用 |
| `SharkToothNecklace` | 3212 | 1 | 通用 |
| `AvengerEmblem` | 935 | 3 | 通用 |
| `ApprenticeScarf` | 3809 | 1 | 召唤师 |
| `SquireShield` | 3810 | 1 | 召唤师 |
| `HuntressBuckler` | 3811 | 1 | 召唤师 |
| `MonkBelt` | 3812 | 1 | 召唤师 |
| `ObsidianShield` | 397 | 1 | 通用 |
| `SpectreBoots` | 405 | 1 | 通用 |
| `FeralClaws` | 211 | 1 | 战士、召唤师 |
| `ManaRegenerationBand` | 982 | 1 | 法师 |
| `MagicCuffs` | 1595 | 1 | 法师 |
| `ManaFlower` | 555 | 1 | 法师 |
| `PygmyNecklace` | 1158 | 1 | 召唤师 |
| `StarVeil` | 862 | 2 | 通用 |
| `MoonCharm` | 485 | 2 | 通用 |

四个酒馆饰品虽然名称对应不同职业，原生效果均为哨兵与召唤伤害，因此放入召唤师池。原有 `TitanGlove` 同时适合战士和鞭子构筑；与 `FeralClaws` 一起使用可选 `CompatibleHeroes` 表达双职业，`Hero` 单职业字段继续兼容，未设置的新字段默认为空数组。

职业筛选统一经 `GearCatalog.IsForHero`，用于目录、奖励接受、锻造、商店、事件及原生卡池。饰品说明记录其原版掉落、售卖或合成来源；本次没有将这些饰品加入错误的 Boss 掉落表，也没有扩大宝箱的既有四种通用来源。

## 本次实际验证

2026-10-21（本机时间）运行：

- `tools/Test-Adventure.ps1`：2448 项断言通过。覆盖旧版 28 个饰品全部迁回、酒馆套装原生部件与幕数、三阶哨兵的职业／阶段／稀有度、四职业筛选、双职业饰品接受／拒绝、保存恢复，以及原有奖励、材料、强化、锻造、三次免费词条、Boss 来源和宝箱防重复规则。
- `tools/Generate-InventoryModels.ps1`：生成 307 个独立的 BaseLib 物品模型，更新 `host/AdventureGearModels.cs`。
- `tools/Test-CatalogNative.ps1`：在本机 Terraria 1.4.5.8 对 383 个符号调用原生 `Item.SetDefaults`；武器、防具部位、饰品分类失败为 0。

证据为 `.local/adventure-test/report.json`、`native-symbols.txt`、`native-categories-report.tsv`。这些检查证明目录、规则和原生物品类型匹配；不代表本次新增装备已经完成整局数值验收。最终宿主构建与实际游戏验证由主流程统一进行。

## 新增装备的原生战斗与渲染

2026-10-21 20:03:25（本机时间），在最终 `DrawNativePlayer` 绘制修复后重新编译并独立运行 `tools/Test-NativeRecoveryContent.ps1`，103.889 秒完成 56 个装备样本，全部通过，失败为 0：

- 4 把三阶哨兵法杖各自实际装备、创建原生哨兵弹幕、推进 37 tick，并渲染至少 20 帧。
- 8 套酒馆护甲各自实际装备，检查三件防御、原生完整套装标记、哨兵上限与召唤伤害，推进 13 tick 并渲染至少 8 帧。
- 22 饰品分别置于原生饰品槽及第八个扩展饰品位置，共 44 样本。每样本实际调用原生玩家更新并绘制，逐项检查伤害、移速、防御、穿透、魔力、免疫、哨兵／召唤上限或对应行为标记。
- 月亮护符在满月夜间激活原生狼人形态且完成绘制；猛爪手套将同一把木剑的实际挥砍时长从 20 tick 降为 17 tick。更新结束后的原生 `meleeSpeed` 是时长乘数 `1/1.12 = 0.892857`。

首次检查定位到 5 套长衣在 `DrawPlayer_16_ArmorLongCoat → Main.LoadArmorLegs` 中读取第二套腿部图集时出错；主流程补齐本机原生腿部纹理预热后，重新编译探针并完整重跑，8 套护甲均通过。探针同时修正了猛爪检查的原生速度字段方向，以及非自动挥舞武器需要先松键再按键的比较基线；这些是测试修正，没有改动生产战斗输入。

最终证据位于 `.local/catalog-recovery-native/verification.json` 及同目录 56 张逐装备截图；侍卫套截图中已目视确认木剑正常显示并跟随挥砍。最终绘制过程只在原生客户端绘制调用期间临时关闭服务端标记，并在 `finally` 恢复，本轮长衣、狼人及饰品绘制无回归。探针执行前后 `NativeRenderer.cs` 与 `NativeArena.cs` 的 SHA256 一致。

诊断阶段报告单独保留为 `verification-before-longcoat-fix.json`、`verification-before-input-baseline-fix.json`；最终绘制修复前的通过报告保留为 `verification-before-item-draw-guard.json`。探针使用独立的 `NativeRecoveryContentProbe.exe`，没有覆盖共享原生探针，没有启动尖塔，也没有加载用户单局或写入用户存档。

本检查验证新增装备能在短原生战斗中产生对应效果并完成绘制，不包含整局平衡、所有 Boss 胜负流程或全部旧装备的渲染验收。

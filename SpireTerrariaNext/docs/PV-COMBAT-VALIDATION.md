# PV 战斗验证记录

本轮为新宣传片补充真实战斗呈现。以下结果来自本机 Terraria 1.4.5.8 的独立内存战斗探针，不会写入玩家存档。

## 已完成

- **月亮领主自然 AI：1800 ticks（30 秒）通过。** 由 MoonLordCore 自然召出头和双手，样本中实际有 4～5 个 NPC、8～13 个投射物；记录含幻影眼、幻影球、幻影死亡射线与幻影矢。原生身体、手臂、头、眼睛使用 `Main.DrawNPCDirect`，死亡射线使用 `Main.DrawProjDirect`。证据：`.local/native-pv-test/moon/report.json` 和连续时刻 PNG。
- **多部位生命周期回归通过。** `moon-phases` 在眼睛开放时调用原生 `StrikeNPC`，4 次命中后确认核心自然开放、进入原生死亡阶段，并在 tick 1320 完成 encounter。该测试有人工伤害，不作为宣传片的实战胜利素材。证据：`.local/native-pv-test/moon-phases/report.json`。
- **代表法师武器 15/16 项通过 90 ticks。** 通过项为 WaterBolt、Vilethorn、CrimsonRod、DiamondStaff、GoldenShower、CrystalStorm、CursedFlames、NimbusRod、MagicMissile、RazorbladeTyphoon、LaserMachinegun、ShadowbeamStaff、InfernoFork、LunarFlareBook、NebulaBlaze。报告按武器记录，失败不会掩盖其他结果。证据：`.local/native-pv-test/mage/report.json`。
- 实际目视截图可见魔刺、黄金雨、利刃台风、星云烈焰，以及月总眼睛、粒子和完整头手。通用投射物恢复原生 `GetAlpha` 颜色；真实 Dust 池按原生更新推进并绘制。

## 保留限制

- **LastPrism 未支持：** 第 49 tick 在原生 `Projectile.AI` 内出现空引用。它不在现有 192 把武器目录中；本次宣传片不用它作可用武器演示。上述 15 项仅代表短样本，不能代替全部法师武器、全部长时战斗回归。
- **数值平衡未在本轮解决。** 第二幕后 Boss 偏强的反馈仍待后续处理。月总使用原版生命；没有为片中战斗降低敌人生命或注入胜利。
- **没有宣称游戏实时音效已实现。** 宣传片的攻击声、冲击声和转场声可以后期设计；它们不构成游戏声音系统已修复的证据。
- **月总是拍摄用可用战斗入口。** 正片使用尖塔原生界面中的内存 fixture 到达此场景，不是完整自然一局通关录像，也没有自动把第三幕一般 Boss 池换成月总。影片以真实交战高潮结束时，不宣称击杀或通关。
- 独立画面仍未完整覆盖 Terraria 全部染色器、粒子着色器和特殊拖尾。Moon Lord 专用帧为 1280×720，普通场景为 640×360。

## 最后修正

原版月总死眼可能保留满 `life`，以 AI phase 表示失活。最新代码按头 phase=-3、手 phase=-2、核心死亡 phase=2 从汇总血量中扣除，并隐藏失活部位的小红条。主代理在统一构建 worker 前复跑此 HUD 回归。

验证工具：`tools/Test-NativePv.ps1`、`tools/NativePvProbe.cs`。`moon-phases` 的人工命中只存在于测试工具，正式拍摄模式不使用它。

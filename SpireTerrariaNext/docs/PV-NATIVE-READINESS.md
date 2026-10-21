# 新纪元 PV：原生战斗拍摄入口

## 月亮领主

配置 `Stage=3, RoomKind="boss", BossSymbol="MoonLordCore"`。保留 Terraria 自身的核心 AI、召唤头部/双手、眼睛开启、幻影球和幻影死亡射线；不是替代伤害或自制 Boss AI。

- 月总房间输出 1280×720 像素原生战斗帧，普通房间保持 640×360；宿主必须使用握手中返回的帧尺寸。
- 核心、头、双手和脱离的真眼使用 `Main.DrawNPCDirect`，加载原生 Npc/Extra/BlackTile 图集依赖。
- 核心尚未开放时，状态的目标坐标选择当前可伤害的头/手；生命显示汇总核心、头和双手。真正的 encounter 胜负仍由核心生命和原生死亡流程决定。
- 场景为夜间观测台：原生方块/墙体/平台、彩窗、柱廊。该场景不是完整 Terraria 世界生成。
- 月总当前是可用的命名战斗入口；并未因此自动替换一般第三幕 Boss 池。

拍摄无需改生命或注入胜利。可以拍摄自然攻击及闪避，并在战斗高潮结束影片；未实拍击杀时，不以字幕或剪辑宣称击杀。

## 弹幕补全

原来的通用弹幕渲染只按 `alpha` 淡出，漏掉原生 `Projectile.GetAlpha` 的特殊自发光颜色。现在使用原生颜色，并推进/绘制真实 Dust 池。Vilethorn、选定光束及月总死亡射线走原生 `DrawProjDirect`。Dust 光照表需 `TorchID.Initialize`。

不代表已完整移植 Main.Draw：所有护甲染色器、全部武器特效、粒子着色器、全部拖尾仍需逐项验证。

## 可重复验证

先编译（无需启动实际尖塔）：

```powershell
pwsh -NoProfile -File tools/Test-NativePv.ps1 -CompileOnly
```

然后在可访问本机图形设备的终端运行探针：

```powershell
.local/native-pv-test/NativePvProbe.exe .. .local/native-pv-test/moon moon
.local/native-pv-test/NativePvProbe.exe .. .local/native-pv-test/mage mage
.local/native-pv-test/NativePvProbe.exe .. .local/native-pv-test/moon-phases moon-phases
```

`moon` 使用原版生命运行 1800 ticks 并保存连续时刻的状态和截图。`mage` 检查代表武器。`moon-phases` **仅为生命周期回归**：在原生部位打开时调用原生 `StrikeNPC`，检查核心解锁和原生死亡。该模式的素材不用于宣传片实战展示。

探针创建独立内存战斗，不读取玩家存档，不注入真实游玩存档，也不启动 Steam 或尖塔。

## 本轮范围

没有调整第二幕之后的 Boss 平衡，也没有宣称实时游戏音效已修复。宣传片可以独立做声音设计，但不得把后期配音当成已实现的游戏声音系统。

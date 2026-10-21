# M1 原生战斗验收

状态：**2026-10-21 本机完整实测通过，60 项检查**。首个 Start 耗时 1776 毫秒；验收程序总时间 7680 毫秒，不含编译。结果见 `.local/battle-test/verification.json`，`passed=true`。这些是本机单次测量，不是实时性能保证。编译成功本身不作为原生战斗通过的依据。

本阶段验证独立核心的本地原生模拟与共享内存预览，不代表完整可玩 Mod，也不代表已经接入泰拉瑞亚完整的原生画面管线。绘图、音效、存档、尖塔交互和正式发布分别验收。

## 实测结果

64 位验收宿主通过认证管道启动并连接 32 位原生工作进程。原生左右移动、跳跃、BlueSlime AI、附魔剑使用与投射物、NPC 受伤/死亡，以及原生水矢武器发射均已观测到。

原生时钟由本地固定 60 Hz 调度维护：每场重置并逐 tick 推进游戏计数、视觉时间、小时循环时间与非暂停随机种子；种子推进调用本机原生 `Utils.RandomNextSeed`。补齐该调度后已重新通过全部 60 项验收。

附魔剑场景的敌人于第 203 tick 死亡，观测到的最大活动投射物数为 1。第二个不攻击的场景通过原生 NPC 接触，将玩家从 300 HP 降至 0，于第 6660 tick 死亡，使用 56 次步进请求；没有通过测试程序手写扣血。引擎在原生死亡完成后冻结，因此最后一批可少于请求的 120 ticks。

三次 Start/End、会话更换、共享映射关闭与回收均通过。重复 Start、无场景 Step、未知装备/敌人、零/超限 ticks、未知输入位、NaN/Infinity 瞄准、截断包及尾随数据均被拒绝，后续合法请求仍可执行。Shutdown 正常退出，验收后没有遗留工作进程；原版程序与现有可玩 Mod 哈希未变。

胜利和死亡后分别额外发送一次 120 ticks 的攻击请求，确认原生 tick、玩家/敌人 HP 保持不变，死亡状态也不会自动复活。连续读取相同帧序号返回 `Unchanged`；Ping 不生成画面也不推进模拟，其后单 tick 的 Step 只推进预期一 tick 并发布新帧。

保存了 640×360 的 `m1-initial.png`、`m1-jump.png`、`m1-projectile.png`、`m1-action.png`、`m1-death.png`。已人工查看初始、跳跃、投射物采样及死亡截图，确认本机素材组成的地面、角色与敌人正常可见；死亡帧中可见原生墓碑投射物。截图证明预览可用，不替代完整原生绘制管线的验收。

验收实际发现并修复了原生死亡路径的依赖：`Netplay.Clients`、`TileObjectData` 和 `Framing` 的初始化。没有通过屏蔽原生死亡或禁掉墓碑来通过检查。

## 运行

```powershell
# 仅编译验收程序，不启动任何工作进程或游戏窗口。
pwsh -File tools/Test-Battle.ps1 -CompileOnly

# 集成后完整验收；若初始化需要图形设备/窗口，按环境的 GUI 审批执行。
pwsh -File tools/Test-Battle.ps1 -TerrariaDir 'D:/steam/steamapps/common/Terraria'
```

使用系统 .NET Framework CSC 绝对路径，C#5 编译 x64 宿主；原生 x86 工作进程由现有 `Build.ps1` 构建。结果、PNG 与临时程序只放在 `.local/battle-test/`。不会写入玩家角色或世界文件，不替换现有可玩 Mod；运行前后核对原版程序与旧版 Mod 文件哈希。

## 验收项目

- 认证管道握手、64 位宿主与 32 位工作进程。
- Start 返回随机帧会话、640×360 BGRA32 预览和初始原生状态；玩家 300 HP、BlueSlime 原始 25 HP。
- 真实左右移动、跳跃、NPC AI 位移。
- 按下/松开攻击，原生附魔剑或魔法武器产生投射物，敌人受伤与死亡。
- 不手写扣血地等待原生敌人接触造成玩家受伤与死亡；最多模拟 24000 ticks，套件预算 60 秒。
- 重复 Start、参数范围、非法输入位、非有限瞄准坐标、截断/尾随数据和无战斗时的 Step 均可恢复。
- 原生状态对应共享内存序号，像素在模拟后发生变化；保存初始、攻击后、死亡时 PNG，供人工检查。
- 同序号重复读取返回 `Unchanged`；Ping 不推进模拟；胜利或死亡后追加输入不会继续推进 tick、改变 HP 或自动复活。
- End 发布关闭，读者释放后回收映射；重开重置状态且使用新会话；最终 Shutdown 无遗留工作进程。

报告保留每项已通过检查和失败时的完整堆栈。只以完整实测报告 `passed=true` 作为 M1 验收通过依据。像素变化只能证明预览在更新，不能单独证明它使用完整的原生游戏渲染；报告始终将 `nativeGameRenderingVerified` 记为 `false`。

## 集成协议

op7 Start：请求 `int seed, string weaponSymbol, string npcSymbol`；响应 `string frameSession, int width, int height, BattleState`。

op8 Step：请求 `int ticks, byte inputFlags, float aimWorldX, float aimWorldY`。ticks 为 1..120，输入位 1 左、2 右、4 跳、8 攻击；响应 `BattleState`。

op9 End：空请求；响应字符串 `ended`。

BattleState 字段依次为 `int tick, int playerLife, int playerMaxLife, float playerX, float playerY, bool playerDead, int npcLife, int npcMaxLife, float npcX, float npcY, bool npcActive, int activeProjectiles, long frameId`。每个响应都验证无尾随数据。

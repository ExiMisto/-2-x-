# 新核心的代码与依赖来源

此记录说明新核心实际使用的代码与依赖。GitHub 提供本目录源码，创意工坊和二进制安装包尚未发布；源码公开不改变第三方组件和游戏素材各自的适用条款。

| 内容 | 来源和用途 | 是否随新核心分发 |
| --- | --- | --- |
| 控制协议、宿主通信、工作进程 | 本项目新编写的 `Protocol.cs`、`BridgeClient.cs`、`Worker.cs` | 将来可分发自有实现，最终许可待确定 |
| 共享画面与验收工具 | 本项目新编写的 `FrameExchange.cs` 与各 Probe / PowerShell 工具 | 同上；合成帧检查单独标识 |
| 原生访问与场地调度 | 本项目新编写的 `TerrariaRuntime.cs`、`NativeArena.cs`；从本机程序集反射确认 API 和必要初始化顺序 | 分发适配代码；不包含游戏程序集或原版方法实现 |
| 场景合成与图标 | 本项目新编写的 `NativeRenderer.cs`；读取本机纹理、字体，图标和四职业选角背景分别经 ItemIcons / SceneArt 接口在内存转交 Godot | 分发适配代码；不包含提取纹理、字体和音频 |
| Terraria 程序与 Content | 玩家本机安装的 Terraria；提供实际角色、武器、物理、AI、伤害和素材 | 不分发；玩家需自行安装，遵守其适用条款 |
| .NET Framework / XNA | Windows 和本机已安装运行库；系统 CSC 与 XNA 图形设备 | 当前不复制、不重新打包运行库 |
| 杀戮尖塔 2 / GodotSharp / Harmony | 原生 Mod 入口、游戏节点、战斗事件、输入接管；引用玩家本机尖塔目录内的组件 | 不复制原版程序集 |
| BaseLib v3.4.7 | 本机 Steam Workshop 安装，提供 `CustomCharacterModel`、`CustomCardModel`、`CustomRelicModel` 与本地化等公开 API；宿主有真实编译引用及清单依赖 | 不打包 BaseLib.dll / .pck；玩家单独安装前置。BaseLib 与旧战斗桥接是不同组件 |
| 尖塔宿主适配与自测 | 本项目新编写的 `host/`、构建和隔离测试脚本；按本机宿主 API 接入 | Mod 安装只包含自有宿主 DLL、清单与自有 worker EXE |
| TerrariaICEY / 旧桥接 | 先前项目研究和行为对照；新宿主与 worker 不引入其战斗桥接代码、DLL、协议类型或工作进程 | 不纳入新核心分发物 |
| SpireTerrariaClassic 源码目录 | 恢复先前开发进度作本地比对；用于核对原生界面、起装及玩法规则，不是新项目运行依赖 | 不复制旧程序到新 Mod 安装目录 |

我们此前读过上游实现，因此不声称经过正式的隔离开发认证。新宿主引用游戏与 BaseLib 的本机 DLL，自有 worker 使用新的启动、通信和场景调度；不链接旧版 Mod、上游 worker 或上游协议。以后实际引入其他代码或资源时，按对应来源记录与处理，不将 BaseLib 前置混同为旧桥接依赖。

验证输出全部位于被 Git 忽略的 `.local/`。其中 PNG 是本机验证证据，不能因为不进入 Git 就视为具有单独的素材再分发授权。

真实饰品与药水为自有 BaseLib / 尖塔模型。图标通过 Godot `TakeOverPath` 注册在当前进程的资源缓存中，供原生界面读取；这些 `res://spire_next/runtime/` 路径及药水模型路径没有对应的打包素材文件，也不向游戏安装目录写出提取图片。

# 来源与外部依赖

本项目的原生桥接依赖 [TerrariaICEY](https://github.com/Hatsunetrump/TerrariaICEY)，作者 Hatsunetrump，协作开发工具 OpenAI Codex；固定提交为 `70bc8822f4bd1fec54e007bc9a156c1c39d71ead`。

构建时从单独的本地上游检出读取 EngineWire、RenderWire、MaintenanceWire、PermanentStats 及原生工作进程源码。`native/` 中是本项目的扩展入口；生成的上游叠加文件、上游源码、编译程序与运行依赖不纳入本仓库。

上游为个人使用与本地修改的自定义许可，限制再分发与商业使用。许可及第三方声明保留在 [notices/TerrariaICEY-LICENSE.md](notices/TerrariaICEY-LICENSE.md) 和 [notices/TerrariaICEY-THIRD-PARTY-NOTICES.md](notices/TerrariaICEY-THIRD-PARTY-NOTICES.md)。本仓库的存在不构成上游作者的额外授权，也不授予原版游戏程序、美术或素材的分发权。

HarmonyX、Mono.Cecil、MonoMod 等遵循各自许可；BaseLib 为独立前置。Terraria 与 Slay the Spire 2 的程序、名称、美术与商标归各自权利人，运行时读取本地安装。

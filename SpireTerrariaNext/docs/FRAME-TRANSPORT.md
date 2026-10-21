# 共享内存画面传输

这是独立撰写的 Windows / .NET Framework 4、C# 5 组件，用于将 x86 工作进程的 BGRA32 像素交给 x64 宿主。实现与测试不使用游戏或上游 Mod 源码。本阶段只验证合成帧传输，**没有验证原生游戏渲染**。

## 接口

生产者创建随机会话名，在控制通道握手完成后把会话名告知宿主。不要复用不同运行的会话名。

```csharp
string session = Guid.NewGuid().ToString("N");
using (FrameExchange output = FrameExchange.CreateWriter(session, 1920 * 1080 * 4)) {
    // bgra 必须由调用方准备，为自上而下、紧密排列的 BGRA32；长度严格等于宽*高*4。
    // 此组件不截取桌面、不读取 GPU 纹理，也不负责游戏渲染。
    long id = output.WriteBgra32(width, height, bgra);
}
```

```csharp
using (FrameExchange input = FrameExchange.OpenReader(session)) {
    long lastId = 0;
    SharedFrame frame;
    FrameReadStatus state = input.ReadLatest(lastId, out frame, 1000);
    if (state == FrameReadStatus.Available) {
        lastId = frame.FrameId;
        // frame.Pixels 是接收进程拥有的副本；用于后续纹理上传。
        // frame.TimestampUtcTicks 使用 UTC DateTime.Ticks，不是音视频时钟。
    }
    // Empty: 尚未发布；Unchanged: 没有更新；Closed: 生产者已经正常关闭。
}
```

`ReadLatest` 只返回最新帧，允许跳帧，不保证逐帧消费。尺寸可在容量内变化；扩大容量需要重新创建会话。每槽容量为 4 字节至 256 MiB，单边上限 16384。尺寸乘法使用 64 位检查，像素数组在验证元数据后才分配。默认互斥等待为 1 秒；显式重载允许设置超时，超时抛出 `TimeoutException`。同一对象的方法与释放过程另有进程内锁，避免本对象被同时释放。

## 传输与生命周期

共享映射名为 `Local\SpireNext.Frames.<session>`，互斥名追加 `.Gate`，只跨同一 Windows 会话中的进程。一个生产者可以对应多个读取者；生产者映射必须用 `CreateNew`，会话冲突不会覆盖已有映射。会话名只允许 ASCII 字母、数字、点、下划线、短横线，长度不超过 160。

固定 128 字节头部存放 magic、版本、格式、每槽容量、映射声明长度、状态、活动槽、64 位帧序号、UTC 时间戳、宽、高、stride、像素长度及生产者 PID。后面是两个等容量槽。32/64 位两侧使用固定偏移和定宽字段，不交换地址或指针。

生产者在命名互斥内写入非活动槽，最后更新元数据和 Ready 状态；读取者在同一互斥内验证并复制完整像素。互斥保护覆盖整个复制过程，双缓冲无需依赖 32 位进程对 64 位值写入的原子性。此版本优先保证正确性，尚未测量 1080p 实时渲染延迟、GC 压力或 GPU 上传成本。

正常释放生产者时先发布 Closed，再释放映射、视图及互斥。读取者仍可观察 Closed；所有持有者退出后由操作系统回收命名对象。释放读取者不会关闭生产者。`Dispose` 可重复调用，释放后调用读写会抛出 `ObjectDisposedException`。若关闭标记在 5 秒内无法取得互斥，则释放自身句柄并显式报超时；上层还应使用工作进程退出通知识别非正常关闭。工作进程被终止而未持有互斥时，也可能留下最后一帧，因此**不能以帧通道单独判断进程存活**。

发现互斥被异常遗弃时，通道标记 Faulted 并拒绝后续画面，避免把可能未提交的像素当作有效帧。读取者需要以读写权限打开映射，才能发布该故障标记。该通道面向同一可信程序的本机会话，不提供防范同用户恶意进程的认证边界。

## 验证

```powershell
pwsh -File tools/Test-Frames.ps1 -CscPath 'C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe'
```

测试脚本用绝对路径的系统 CSC，显式 `/langversion:5`，分别编译 x86 生产者和 x64 读取者。所有生成物位于 `.local/frame-test/`，不接触玩家世界、角色、旧版 Mod 或游戏文件。`verification.json` 明确记录 `syntheticFramesOnly=true`、`nativeGameRenderingVerified=false`。

合成图案把像素位置和帧种子编码进每个字节。检查内容包括跨进程位数、空映射、递增序号、尺寸变化、容量边界、并发写入 2000 帧期间的像素完整性、无更新状态、越界输入、损坏元数据、正常关闭、重复释放及句柄生命周期。还会让独立 x86 进程占用互斥，验证读取超时后可继续工作；随后在持有互斥时终止专用测试进程，验证异常遗弃使通道持续拒绝画面并最终回收映射。它证明的是传输路径，不证明游戏画面或战斗已接入。

2026-10-21 本机验证：**54 项检查通过**，x86 生产者发布 2000 个连续合成帧，x64 读取者在并发期间校验了 883 个完整快照，最终序号为 2003。快照数会随调度变化；跳帧符合最新帧协议。测试结束后没有遗留帧测试进程。

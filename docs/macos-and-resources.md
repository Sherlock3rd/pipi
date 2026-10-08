# macOS 适配与无损资源优化

2026-10-08 用户要求解决 Mac 适配，同时在不降低画质和流畅性的前提下优化包体与运行内存；Apple 芯片与 Intel 均需支持。

## 实现

- Windows 保留 WPF；macOS 新增 Avalonia 原生桌面宿主，链接同一套行为树、照料、动作播放、支撑几何、存档与参数代码。场景绘图抽到 `Scene.Artwork.cs` 共用，未改原动画帧、尺寸、脚底、帧率或流程。
- Mac 提供透明窗口、桌面/悬浮层切换、按物件命中动态点击穿透、不抢键盘焦点、当前主屏全屏避让、菜单栏召回/显隐/退出、猫窝右键设置、参数工作台与原声嘴型绑定。主屏范围沿用 Windows 当前产品边界。
- Mac 存档在 `~/Library/Application Support/Chenpi`，采用原版本与备份格式。只在用户打开设置开关时写入自己的 `~/Library/LaunchAgents/com.chenpi.desktop.plist`，不默认启用自启。
- 行为工作台保留草稿保存、撤销、恢复默认、JSON 导入导出、关闭前保存提示、当前决策与照料倒计时；开关沿用勾选交互。保存/切换参数前直接读取输入框，避免延迟的文本事件漏掉最后一次修改。
- 首轮支持 macOS 15 及以上，分别发布 `osx-arm64` 和 `osx-x64` 自包含 `.app`，不要求用户安装 .NET。系统范围依据 [.NET 10 支持表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)。

## 无损包体

`tools/package_runtime.py` 只收录清单实际引用的帧、五张家具原图和实际绑定的 WAV。每张动画 PNG 转为无损 WebP，保留透明像素 RGB，每张都重新解码并与源 RGBA 完全逐字节比较。源 PNG、原视频、照片与制作资料不改动。原动作清单逐字节复制，重复引用保留，因此不会缩短循环或三连叫。

全部 15,349 个帧路径已完成像素回查；素材总量从 1,508,809,307 字节降至 805,360,830 字节（约减少 46.6%）。这不是删除帧、降低分辨率或有损压缩；发行清单、家具、声音保持原字节。运行时通过 `frames.cpak` 索引访问，开发目录仍支持原 PNG。

## 内存与时序

不再初始化全部动画。帧按文件路径共用 LRU 缓存，解码像素预算 96 MiB，预读后续 12 帧，并用独立播放状态快照预读未来 0.6 秒的动作接缝；缓存 IO 不持锁，旧动作预读队列可被新动作替换。帧边界元数据使用弱引用，避免淘汰后仍通过边界表持有全部图像。Windows 显示宿主关闭/替换时释放缓存。画面仍按原行为时钟取帧，不等待整个片段解码，不改变动作时长。Mac 使用窗口动画帧回调，与屏幕刷新同步。

96 MiB 是缓存像素预算，不是进程总内存承诺；图形驱动、窗口表面、道具、解码临时缓冲与运行时也占内存。最坏磁盘延迟、Retina 与真实 Mac GPU 需要单独测量。

Mac 解码直接扫描 Skia 的原生像素缓冲区，裁切中转使用缓冲池，避免每帧复制完整透明画布到托管大对象堆。保留命中像素的家具仍持有独立数组；图像构造完成后才归还中转缓冲。独立旧算法与新解码器对照各动作首/中/尾，共 420 帧，并在中转缓冲被复用后比较实际 Bitmap 像素和边界。窗口层级、Spaces 和点击穿透只在状态变化时写入系统。

## 构建

下载后使用：

1. 在苹果菜单的“关于本机”查看芯片：Apple 芯片用 `Chenpi-osx-arm64.zip`，Intel 处理器用 `Chenpi-osx-x64.zip`。两包无需同时安装。[Apple 芯片辨别说明](https://support.apple.com/zh-cn/116943)。从 GitHub Actions 下载的产物包含验证资料，先解压该产物，再取 `dist/releases/` 里的对应安装 ZIP。
2. 在 Mac 上解压，把 `Chenpi.app` 拖进“应用程序”，双击启动。需要 macOS 15 或以上；无需安装 .NET、Python 或开发工具。
3. 当前是本地 ad-hoc 签名，尚未 Developer ID 公证。首次被系统拦截时，核对来源后按 [Apple 的打开说明](https://support.apple.com/zh-cn/102445) 在“系统设置 → 隐私与安全性”选择“仍要打开”。不需要关闭系统安全功能。
4. 猫窝右键进入设置与行为工作台；菜单栏小猫可召回、显示/隐藏或退出。自启开关在设置里，默认不启用。

```sh
python -m pip install -r tools/requirements-release.txt
python tools/package_runtime.py --output artifacts/runtime-assets
python tools/build_release.py --rid osx-arm64 --assets artifacts/runtime-assets
python tools/build_release.py --rid osx-x64 --assets artifacts/runtime-assets
```

Windows 将 `--rid` 改为 `win-x64`。输出目录必须为空，脚本拒绝覆盖旧版本。Mac 本机打包会逐个签名原生库与应用，并执行 `codesign --verify --deep --strict`；默认本地 ad-hoc 签名，可通过 `CHENPI_SIGN_IDENTITY` 提供已有签名身份。未配置 Developer ID 公证，不宣称获得 Gatekeeper 公证通过；首次下载可能需在系统隐私与安全性中批准打开。

Mac 程序集采用 .NET single-file 合并（不裁剪代码、不启用启动解压压缩）；原生动态库按对应架构保留，素材及许可放 `Contents/Resources`。首轮原生 CI 发现散放 PE DLL 的旧目录布局导致严格签名失败，已按实际日志调整；编译通过不能代替签名和启动检查。自启仅允许已移入 `/Applications` 或 `~/Applications` 的 `.app`，避免记录下载隔离区临时路径。

## 验证范围

- 当前 761 项共享行为/缓存检查和 13 项 Windows 原生窗口检查通过；两端 Release 编译通过。
- 全量打包后的 Pillow RGBA 像素回查通过；WPF 实际解码/预乘/裁切后 15,349 帧逐帧一致，像素和边界差异均为零。结果见 `docs/qa/macos-resources/windows-pixel-audit.json`。
- WPF 无损发布包的 82 个左右进窝合成样本错误身体遮挡为 0，旧错误负对照仍检出 3140 像素；Avalonia 工作台本地 UI 夹具通过保存回读、无效值拦截、照料重排和默认草稿隔离检查，真实 Mac 同步运行此夹具。
- Windows 单独运行优化版完整 60 秒开场，工作集 310,710,272 字节（约 311 MB），峰值约 350 MB；原版采样约 4.69 GB。P95 帧间隔 17.56 ms，平均 16.72 ms，首个睡眠帧一次 63.20 ms。原版与优化版早期重叠运行的性能采样不用于流畅度结论。不能将 Windows 工作集作为 Mac 内存实测，结果见 `docs/qa/macos-resources/windows-performance.json`。
- Windows 额外连续运行 5 分钟，实际经过 31 种片段，结束工作集约 351 MB、峰值约 389 MB，缓存 100,425,708 字节，未超过 96 MiB；内存采样见 `windows-memory-tour.json`。该轮中途短暂并行了参数面板夹具，只用于内存稳定性，不用于帧率比较。
- GitHub Actions 按 [macOS 官方 runner 架构](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) 分开运行 Apple Silicon 与 Intel。原生测试已通过独立进程普通窗口/全屏窗口、窗口层级往返、点击穿透往返、非激活标记、鼠标坐标和行为工作台四项检查。两端解压安装 ZIP 后严格签名回查、420 帧原生像素/边界与缓冲池哨兵覆盖检查通过。不等于人工验证真实游戏、Spaces、Retina、声音硬件延迟和拖拽手感。

### 最终安装包与完整开场

[构建 37739873431](https://github.com/Sherlock3rd/pipi/actions/runs/37739873431) 双架构全部通过，对应程序提交 `3772e0f9`。以下均为 macOS 15.7.9、各自独立运行 85 秒后的回读，MB 使用十进制；结束工作集包含诊断截图分配，不是缓存预算或跨操作系统的等价内存指标。

| 架构 | 预览 P95 间隔 | 透明桌面 P95 间隔 | 结束工作集（预览 / 桌面） | 安装 ZIP |
| --- | --- | --- | --- | --- |
| Apple 芯片，软件光栅 | 18.74 ms | 18.75 ms | 443 / 443 MB | [838 MB](https://github.com/Sherlock3rd/pipi/actions/runs/37739873431/artifacts/11534085330) |
| Intel，软件光栅 | 36.11 ms | 36.03 ms | 353 / 381 MB | [841 MB](https://github.com/Sherlock3rd/pipi/actions/runs/37739873431/artifacts/11533996624) |

Intel runner 实际显示设备为 Apple Paravirtualized Graphics Device，1920×1080、30 Hz、64 MB 显存；回调频率不能冒称真实 60 Hz Mac 的实测帧率。冷启动最大回调间隔仍记录：Apple 芯片最大约 0.52 秒，Intel 最大约 1.13 秒；不能将 P95 合格表述成所有帧零延迟。两个开场都完成且保留原时长，源码没有人为降低更新频率。

原生验收、工作台回读、安装 ZIP 的 SHA256 及实测明细见 `docs/qa/macos-resources/macos-acceptance.json`。Actions 产物保留 14 天；需要 GitHub 登录下载，安装 ZIP 位于产物内 `dist/releases/`。Mac 包使用 ad-hoc 签名，未进行 Developer ID 公证。Windows 包为 `dist/release-20261008/Chenpi-win-x64.zip`，882,087,411 字节；对应 Windows 源码在后续 Mac 提交中没有变化，SHA256 见 `windows-release.json`。

### 五分钟内存回查

85 秒开场通过后继续采样，发现旧 Metal 路径在 Apple 芯片上的 RSS 从约 704 MB 涨到 1,136 MB；90—150 秒与最后一分钟的中位数相差约 302 MB。帧缓存虽未超过 96 MiB，但进程总内存不合格，不能以 CI 的旧缓存检查通过作为完成依据。

相同已签名程序与全部画面效果切换软件光栅后，Apple 芯片的后续采样稳定在 425—429 MB，P95 回调间隔 18.70 ms，300 秒共 17,902 次回调；两个阶段的 RSS 中位数下降约 1.33 MB。Intel 软件光栅同长度测试稳定在 336—373 MB，P95 34.90 ms，中位数下降约 8.29 MB。Apple 芯片的 OpenGL 请求实际回退到 Software，也稳定在 407—439 MB；不能将其称为 OpenGL 性能证据。

这些是 CI 机器上的五分钟样本，不是所有 Mac 的全天内存保证。软件光栅依赖 CPU，Apple 芯片样本累计 CPU 约 119 秒/300 秒；真实 Retina 屏与功耗仍需实机核对。未使用强制 GC、降低画质、缩小画布、减帧或删除阴影来通过。默认后端改动使用上述相同 Software 配置，随后重新打包运行双架构验收。

手动工作流 `macos-memory.yml` 现在比较两段 RSS 中位数，超过一个 96 MiB 帧缓存预算的增长会失败，并保留原始采样。失败基线与通过对照分别见 `macos-memory-baseline.json`、`macos-memory-accepted.json`。

### Intel 性能回读追加

`e1070d54` 的原生功能、签名和 UI 检查均通过，但 Intel 的 85 秒预览实际只有 829 次回调，P95 322.77 ms，开场未完成，不能视为流畅性通过；前一轮同架构 P95 38.20 ms 也表明存在运行条件差异。已新增性能验收门槛，单看退出码与截图文件存在不再足够。

`ce9a681a` 改为 Metal 优先并添加可见活动后，用户指出的 [37734213003](https://github.com/Sherlock3rd/pipi/actions/runs/37734213003) 仍被性能门槛拦截：Intel P95 90.30 ms。随后 `d24a7a23` 的分阶段计时发现绘图指令 P95 0.91 ms、85 秒内仅 10 次同步解码，却出现多次 Skia 阴影着色器编译超过 300 ms，预览 P95 281.69 ms。同机软件渲染的 20 秒透明桌面对照保留全部效果，P95 35.91 ms；关闭阴影的 GPU 对照也约 36 ms，不能简单归因为“阴影必须删除”。不同场景范围/测试时长的对照仅用于定位，不能当作等价整场性能比较。原始数值见 `docs/qa/macos-resources/macos-renderer-profiles.json`。

`fa4958a3` 起 Intel 选择 Skia 软件光栅路径；随后五分钟采样发现 Apple 芯片 Metal 仍有持续内存增长，`3772e0f9` 起两种架构均默认使用软件光栅。两端都保留高质量采样、38% 阴影、全部素材及原动作时长。软件渲染不降低分辨率或删减效果，代价是依赖 CPU，真实 Retina 机器仍需单独核对。可见动画持有 `NSActivityUserInitiatedAllowingIdleSystemSleep` 活动，隐藏/全屏避让/退出释放，不阻止系统或显示器睡眠，依据 [Apple 活动与 App Nap 说明](https://developer.apple.com/library/archive/documentation/Performance/Conceptual/power_efficiency_guidelines_osx/PrioritizeWorkAtTheAppLevel.html)。没有证据把此前所有卡顿归为 App Nap。

最终验收同时要求预览窗口与真实透明桌面的 85 秒完整开场通过：缓存不超过 96 MiB、开场结束并实际经过 118 收尾片段、至少 1900 次窗口动画回调、P95 回调间隔不超过 45 ms。回调计时是本机呈现节奏指标，不等于摄像测得的屏幕刷新；首帧冷启动最大值单独保留，不隐藏。

平台依据：[Avalonia macOS 打包](https://docs.avaloniaui.net/docs/deployment/macos)、[Apple 点击穿透](https://developer.apple.com/documentation/appkit/nswindow/ignoresmouseevents?language=objc)、[Apple Spaces 行为](https://developer.apple.com/documentation/appkit/nswindow/collectionbehavior-swift.struct/canjoinallspaces)。

# macOS 适配与无损资源优化

2026-10-08 用户要求解决 Mac 适配，同时在不降低画质和流畅性的前提下优化包体与运行内存；Apple 芯片与 Intel 均需支持。

## 实现

- Windows 保留 WPF；macOS 新增 Avalonia 原生桌面宿主，链接同一套行为树、照料、动作播放、支撑几何、存档与参数代码。场景绘图抽到 `Scene.Artwork.cs` 共用，未改原动画帧、尺寸、脚底、帧率或流程。
- Mac 提供透明窗口、桌面/悬浮层切换、按物件命中动态点击穿透、不抢键盘焦点、当前主屏全屏避让、菜单栏召回/显隐/退出、猫窝右键设置、参数工作台与原声嘴型绑定。主屏范围沿用 Windows 当前产品边界。
- Mac 存档在 `~/Library/Application Support/Chenpi`，采用原版本与备份格式。只在用户打开设置开关时写入自己的 `~/Library/LaunchAgents/com.chenpi.desktop.plist`，不默认启用自启。
- 首轮支持 macOS 15 及以上，分别发布 `osx-arm64` 和 `osx-x64` 自包含 `.app`，不要求用户安装 .NET。系统范围依据 [.NET 10 支持表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)。

## 无损包体

`tools/package_runtime.py` 只收录清单实际引用的帧、五张家具原图和实际绑定的 WAV。每张动画 PNG 转为无损 WebP，保留透明像素 RGB，每张都重新解码并与源 RGBA 完全逐字节比较。源 PNG、原视频、照片与制作资料不改动。原动作清单逐字节复制，重复引用保留，因此不会缩短循环或三连叫。

全部 15,349 个帧路径已完成像素回查；素材总量从 1,508,809,307 字节降至 805,360,830 字节（约减少 46.6%）。这不是删除帧、降低分辨率或有损压缩；发行清单、家具、声音保持原字节。运行时通过 `frames.cpak` 索引访问，开发目录仍支持原 PNG。

## 内存与时序

不再初始化全部动画。帧按文件路径共用 LRU 缓存，解码像素预算 96 MiB，预读后续 12 帧，并用独立播放状态快照预读未来 0.6 秒的动作接缝；缓存 IO 不持锁，旧动作预读队列可被新动作替换。帧边界元数据使用弱引用，避免淘汰后仍通过边界表持有全部图像。Windows 显示宿主关闭/替换时释放缓存。画面仍按原行为时钟取帧，不等待整个片段解码，不改变动作时长。Mac 使用窗口动画帧回调，与屏幕刷新同步。

96 MiB 是缓存像素预算，不是进程总内存承诺；图形驱动、窗口表面、道具、解码临时缓冲与运行时也占内存。最坏磁盘延迟、Retina 与真实 Mac GPU 需要单独测量。

## 构建

```sh
python -m pip install -r tools/requirements-release.txt
python tools/package_runtime.py --output artifacts/runtime-assets
python tools/build_release.py --rid osx-arm64 --assets artifacts/runtime-assets
python tools/build_release.py --rid osx-x64 --assets artifacts/runtime-assets
```

Windows 将 `--rid` 改为 `win-x64`。输出目录必须为空，脚本拒绝覆盖旧版本。Mac 本机打包会逐个签名原生库与应用，并执行 `codesign --verify --deep --strict`；默认本地 ad-hoc 签名，可通过 `CHENPI_SIGN_IDENTITY` 提供已有签名身份。未配置 Developer ID 公证，不宣称获得 Gatekeeper 公证通过；首次下载可能需在系统隐私与安全性中批准打开。

## 验证范围

- 当前 761 项共享行为/缓存检查和 13 项 Windows 原生窗口检查通过；两端 Release 编译通过。
- 全量打包后的 Pillow RGBA 像素回查通过；WPF 实际解码/预乘/裁切后 15,349 帧逐帧一致，像素和边界差异均为零。结果见 `docs/qa/macos-resources/windows-pixel-audit.json`。
- Windows 单独运行优化版完整 60 秒开场，工作集 310,710,272 字节（约 311 MB），峰值约 350 MB；原版采样约 4.69 GB。P95 帧间隔 17.56 ms，平均 16.72 ms，首个睡眠帧一次 63.20 ms。原版与优化版早期重叠运行的性能采样不用于流畅度结论。不能将 Windows 工作集作为 Mac 内存实测，结果见 `docs/qa/macos-resources/windows-performance.json`。
- GitHub Actions 按 [macOS 官方 runner 架构](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) 分开运行 Apple Silicon 与 Intel。原生测试检查独立进程普通窗口/全屏窗口、窗口层级往返、点击穿透往返、非激活标记、鼠标坐标及 85 秒开场。CI 结果尚待本轮执行，不等于人工验证真实游戏、Spaces、Retina 和拖拽手感。

平台依据：[Avalonia macOS 打包](https://docs.avaloniaui.net/docs/deployment/macos)、[Apple 点击穿透](https://developer.apple.com/documentation/appkit/nswindow/ignoresmouseevents?language=objc)、[Apple Spaces 行为](https://developer.apple.com/documentation/appkit/nswindow/collectionbehavior-swift.struct/canjoinallspaces)。

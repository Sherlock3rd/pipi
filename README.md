# 陈皮 · 桌面小猫

可体验的 Windows 蓝猫桌宠原型，沿用既有 Rules Bootstrap Spec v1.0。使用 C# / WPF / .NET 10 和代码绘制的占位猫，正式蓝猫序列帧后续替换。

## 启动

仓库：[Sherlock3rd/pipi](https://github.com/Sherlock3rd/pipi)。源码仓库不包含可执行文件，首次使用请按下文构建。构建完成后双击 `dist/Chenpi/Chenpi.exe`。默认在桌面右下角，普通窗口会覆盖它；显示桌面即可看到。右键猫或物品、点击窝旁的 `···`、双击系统托盘猫图标可打开设置。

- 点饭盆添粮、点水盆加水、点猫砂盆清理。
- 随机间隔喝水 10～30 分钟、吃饭 20～60 分钟、如厕 60～120 分钟。
- 空盆／猫砂全满持续 5 分钟后，猫走到屏幕下方中央请求；把鼠标放到猫身上，它会带你去对应物品，回头等你跟上，用动作示意添加／清理。
- 四件家具按住移动即可重新摆放，位置自动保存；拖动不触发补给。
- 点猫互动，长按猫后拖动，放进猫窝睡觉；点睡猫唤醒。
- 原地长按约 0.25 秒，或按住明确移动，即可拎起；鼠标停在醒猫身上 5 秒会蹭鼠标。
- 点窝旁羽毛棒拾取，再点一次归位；小猫进入外圈立即跑向羽毛，贴近内圈才停下抓，羽毛移远就继续追。判定圈不可见。
- 小猫移动后随机停留 2～60 分钟，安静 5 分钟原地睡觉。点击可唤醒或互动。
- 剩余量通过粮粒、水面和猫砂结块表示，无照料数字面板。
- 全程无猫的文本台词；需求和互动通过动作表达。
- 点击／待机表现各预留 12 类扩展槽位，待画风确定后批量填充，见[资源清单与接入](assets/pets/bluecat/README.md)。当前空槽不参与播放。
- 设置支持召回、恢复摆放、显示／隐藏、缩放、静音、悬浮、自启和退出。
- 全屏避让按小猫所在屏幕判断：另一屏全屏不隐藏小猫，普通最大化仍可悬浮；退出全屏后恢复。
- 存档在 `%LOCALAPPDATA%/Chenpi`，带最近一次备份。

程序目录需要整体保留或复制，不能只移动 exe。发行目录包含运行时，无需安装 SDK。

## 从源码构建

在 Windows 上安装与 `global.json` 匹配的 .NET 10 SDK（10.0.401 或同补丁带后续补丁），在仓库根目录运行：

```powershell
dotnet run --project tests/Chenpi.Tests/Chenpi.Tests.csproj -c Release
dotnet run --project tests/Chenpi.Windows.Tests/Chenpi.Windows.Tests.csproj -c Release
dotnet publish src/Chenpi/Chenpi.csproj -c Release -r win-x64 --self-contained true -o dist/Chenpi
```

若工作区已有本地 SDK，也可用 `.tools/dotnet/dotnet.exe` 替代 `dotnet`。Windows 检查会短暂显示不主动激活的测试窗口，覆盖最大化、全屏和恢复，多屏用例按实际连接的显示器运行，不读写个人存档。分发时保留整个 `dist/Chenpi` 目录。SDK、分发包、调试截图和个人存档均不纳入 Git；仓库中的验证文档记录结果，引用的本地调试产物需自行复现。

## 当前边界

同次开机内退出后可补算饥渴需求，睡眠／休眠不计入；跨重启缺失历史尚未补齐。仅实际吃喝扣库存，无未照料惩罚。当前只支持主屏，最终蓝猫素材、节日皮肤和完整全屏／多屏实测待完善。

详见[原型验证](docs/prototype-validation.md)。

## 研发入口

- [AGENTS.md](AGENTS.md)：当前授权与执行入口。
- [基础规则](rules/rules.md)、[总控会话](session/session.md)、[错题](mistakes/README.md)：specrule 流程。
- [需求简述](spec/desktop-cat-brief.md)、[交互规范](spec/desktop-cat-care-and-interaction.md)、[计划](spec/desktop-cat-implementation-plan.md)：需求与阶段目标。
- [GitHub 调研](docs/desktop-pet-research.md)、[skill 来源](docs/skills.md)。

本地 SDK 可放在 `.tools/dotnet`，构建和测试细节见验证文档。`hatch-pet` 已随项目保留并附上游许可证，尚未生成正式素材。Git 提交与交付记录见[总控会话](session/session.md)。

# 桌宠产品与 Skill 调研

调研日期：2026-09-17。查询 GitHub README、仓库 API 和部分依赖文件；星标及更新时间为当时快照。未运行产品，也未完成源码审计。成熟度判断结合产品发布、社区与维护信息，不等同于稳定性保证。

## 当前目标与结论

用户需要桌面养猫：**行为树自主移动、喂饭、喂水、拖回猫窝**。BongoCat 的输入反馈方向不适合作为核心产品参照。

建议先研究 **VPet 的养成框架**与 **SondeR-Cat 的猫和食水碗体验**。前者成熟度证据较强，后者体验更接近但社区很小。当前没有验证一个成熟项目能直接满足全部要求，尤其是明确行为树及拖回猫窝；不把这些能力声称为现成可用。

## 产品候选

| 项目 | 调研快照 | 已知能力及匹配度 | 缺口与判断 |
| --- | --- | --- | --- |
| [VPet](https://github.com/LorisYounger/VPet) | 6,805 stars；2026-09-16 推送；C#/WPF；代码 Apache-2.0 | 有互动投喂、移动动画、物品／食物／饮料、模组与可嵌入 WPF 的核心库，适合养成框架研究 | 现成主角并非本项目的小猫；猫窝、行为树接入需读源码验证；图片动画有独立授权说明 |
| [SondeR-Cat](https://github.com/Verisonder/SondeR-Cat) | 6 stars；2026-07-14 推送；Apache-2.0 | Windows/Linux 像素猫；拖拽、休息、梳毛；可摆放并拖动食碗和水碗，点击补充，耗尽后猫会过来索要 | 体验接近，但尚无成熟社区证据；猫窝与行为树未核实，不能仅据 README 认定可作为稳定底座 |
| [DyberPet](https://github.com/ChaozhongLiu/DyberPet) | 976 stars；2026-08-15 推送；PySide6；GPL-3.0 | 有完整桌宠、互动、养成、物品与模组，AI 不是基础体验前提 | README 标明最新产品 v0.10.3，但源码运行部分仅开源至 v0.6.7；成品与开源能力存在差距，复用前逐项核对；猫窝／行为树未确认 |
| [deskcat](https://github.com/coglabss/deskcat) | 3 stars；2026-06-14 推送；Electron；GitHub 许可证字段 NOASSERTION | 猫会走动、打盹、玩耍、饥饿；支持喂食与拖拽；README 明确提到行为状态机 | 社区小，需自行加美术；喂水、猫窝未确认，状态机不等于行为树；许可还需读取原文件确认 |
| [BongoCat](https://github.com/ayangweb/BongoCat) | 23,274 stars；2026-09-17 推送；Tauri 2/Vue；MIT | 键盘、鼠标、手柄反馈，自定义模型与离线运行；桌面技术可参考 | 与用户自主养猫体验不匹配，不作为首选产品底座 |
| [AIRI](https://github.com/moeru-ai/airi) | 49,212 stars；2026-09-17 推送；MIT；发布 API 返回 v0.12.0-beta.5 | AI 陪伴、实时语音、虚拟角色，仓库较活跃 | 当前不需要对话系统；规模和依赖复杂度超出本轮，不能凭星标称其已稳定 |
| [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber) | 13,793 stars；2026-05-15 推送；发布 API 返回 v1.2.1 | 语音交互、Live2D、跨平台本地运行 | 偏 AI 角色，当前需求匹配低；仓库许可证字段 NOASSERTION，未深入授权审查 |

## Skill 候选

| Skill | 调研结论 | 当前处理 |
| --- | --- | --- |
| [OpenAI hatch-pet](https://github.com/openai/skills/tree/49f948faa9258a0c61caceaf225e179651397431/skills/.curated/hatch-pet) | 官方素材工作流，有图集校验／QA／打包脚本；偏 Codex 宠物格式，不提供桌面养成引擎 | 固定版本安装到项目，未执行；见[来源记录](skills.md) |
| [windows-desktop-pet-builder](https://github.com/Coolkidlab-Yin/windows-desktop-pet-builder) | 2 stars，2026-08-31 推送；WPF 桌宠引导，拖拽、鼠标追踪、物理与验证；偏真实照片素材流程 | 未安装，尚不足以认定成熟通用底座 |
| [desktop-pet-skill](https://github.com/Y1fe1-Yang/desktop-pet-skill) | 0 stars，2026-02-12 推送；图片转动画桌宠，依赖另一个生成器且存在 Claude 路径约定 | 未安装，不能直接视为本机 Codex 可用成熟方案 |
| [native-feel-skill](https://github.com/yetone/native-feel-skill) | 1,908 stars，MIT；关注多平台原生体验及多运行时架构，范围偏重 | 当前不采用 |
| [tauri-skills](https://github.com/full-stack-skills/tauri-skills)／[electron-skills](https://github.com/full-stack-skills/electron-skills) | 分别 13／5 stars；通用桌面框架指导，非桌宠产品能力 | 未安装，框架未定，不能把内容数量等同成熟度 |

## 本机与部署边界

- 已检测 Git 2.51.1、Node.js 22.18.0、npm 10.9.3。
- PATH 未发现 Rust/cargo；`dotnet --list-sdks` 无输出，不能把已有 dotnet host 当 SDK。未检测到可用 MSVC C++ 安装路径。
- `python` 命令指向 WindowsApps 占位，本轮安装 skill 使用 Codex 自带 Python；不将其视为产品发布依赖。
- [Tauri 官方前置条件](https://v2.tauri.app/start/prerequisites/)包括 Windows C++ 构建工具、WebView2 与 Rust，后续选定再准备。
- 本轮“基础环境”按用户纠正特指已有 **specrule**，已部署该规则体系。没有安装上述产品或运行时，也没有决定 Electron／Tauri／WPF。

## 下一步可验证内容

对候选逐项核对：自主行为入口、是否真实行为树、喂饭与水状态、拖拽打断及恢复、猫窝区域与目标点、资源替换成本和许可。源码检查与实机验证通过前，以上都保留为待验证项。

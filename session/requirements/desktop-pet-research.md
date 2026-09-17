# 需求：成熟桌宠产品与 skill 调研

- 日期：2026-09-17。
- 状态：首轮完成。
- 用户需求：先在 GitHub 寻找可用的成熟 skill 或产品。
- 已确认首版方向：轻量 2D 小猫，通过行为树自主移动，可喂饭、喂水、拖回猫窝。

## 结果

产品调研覆盖 BongoCat、VPet、AIRI、Open-LLM-VTuber；用户明确养猫体验后补充 DyberPet、SondeR-Cat、deskcat。技能覆盖 OpenAI hatch-pet、desktop-pet-skill、windows-desktop-pet-builder、native-feel-skill、Tauri／Electron skills。详见[调研报告](../../docs/desktop-pet-research.md)。

通过 GitHub API 查询仓库星标、最近推送、归档状态和许可证字段，并检查 README／skill 原文及部分产品依赖。星标只表示关注度，未运行上述产品，不据此宣称稳定性或性能经过验证。

## 变更与边界

- 使用官方 skill-installer，把 openai/skills 的 hatch-pet 按固定提交安装到项目内，供后续素材工作使用，未运行生成脚本。
- 用户对“基础环境”的纠正优先；Electron 提案未落地，技术栈不作决定。
- 用户进一步明确养猫体验，撤回 BongoCat 作为产品首选的建议：成熟养成重点评估 VPet，猫与食水碗体验看 SondeR-Cat；未验证其现有实现采用行为树，也未确认支持拖回猫窝。

## 后续

按下一步用户指令决定是否深入源码或选型；不要自动克隆并二次开发整套产品，不把功能建议当成已确认设计。

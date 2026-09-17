# 项目 Skill 来源与范围

## 已安装：hatch-pet

- 上游：[openai/skills](https://github.com/openai/skills/tree/49f948faa9258a0c61caceaf225e179651397431/skills/.curated/hatch-pet)。
- 固定提交：`49f948faa9258a0c61caceaf225e179651397431`。
- 安装位置：`.agents/skills/hatch-pet/`。
- 工具：Codex 内置 skill-installer，使用固定 `--ref` 和项目 `--dest`，未改全局技能或配置。
- 许可证：保留上游 `LICENSE.txt`。
- 用途：生成、修复、校验动画精灵图与 Codex pet pack；依赖现有 imagegen 技能进行生成。
- 边界：其图集和动作约定服务于 Codex 宠物，不能直接等同于本独立桌宠的资源协议；只在后续实际需要时采用或适配。
- 状态：已安装，下一轮可发现；未运行生成、安装其 Python 依赖或验证完整素材流水线。

## 其他候选

见[调研报告](desktop-pet-research.md)。没有发现经本次调查足以直接承担“行为树＋喂饭喂水＋猫窝”的成熟通用 skill；产品框架与素材技能应分别评估。

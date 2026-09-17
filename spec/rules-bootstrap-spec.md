# 规则引导规范（Rules Bootstrap Spec）

## 目标
- 在任意新项目中快速建立“规则—会话—错题—复用”四位一体的执行体系
- 仅需与用户确认：用户角色、助手需扮演的角色

## 先决条件
- 已获准在项目仓库内创建文档与目录
- 明确用户角色与助手角色

## 标准交付物
- rules/rules.md：基础规则与对话规范
- session/session.md：总控会话（全局状态与提交总账）
- session/requirements/<doc_slug>.md：需求级会话（单文档状态与修改记录）
- mistakes/：错题记录与归纳
- spec/rules-bootstrap-spec.md：本规范
- docs/beagle_glossary.md：Beagle 专有名词术语表（术语定义、使用场景与关键流程链接）

## 执行步骤
1. 角色确认
   - 与用户确认：用户角色、助手角色与权限边界
   - 将“回复前缀”等对话规范写入 rules.md
2. 建立目录与文档
   - 创建 rules/rules.md、session/session.md、session/requirements/、mistakes/、spec/ 目录与文件
   - 创建 docs/beagle_glossary.md 并初始化术语（地块结构、社交类型、友军判定、事件广播等），为关键流程提供单点引用
3. 固化流程
   - 在 rules.md 中写明：执行原则、错误处理流程、更新流程
   - 在 mistakes/ 中提供记录模板与归纳方式
   - 在 session/session.md 中初始化全局状态与提交总账
   - 为首个需求文档创建对应 session/requirements/<doc_slug>.md 并写入状态、变更记录、继承指引
   - 在文档设计中为术语添加就近链接；新增术语时同步维护 beagle_glossary.md
4. 生效与维护
   - 所有规则自创建起生效
   - 新增规则时同步维护 rules.md，并在 session 中记录变更

## 运行规范
- 回复前缀：所有回复以 “you majesty” 开头
- 不确定事项必须二次确认；未经许可不得擅自改动设计
- 开始新任务前，先检索 mistakes/ 的相关分类与防呆清单

## 通用执行原则（默认收录）
- 明确简洁执行；未经允许严禁修改设计
- 对于不确定的问题必须二次反问确认，禁止主观臆断
- 建立错题文件夹；出现错误时记录，并将同类型错误归纳整合，执行前检索复盘
- Rule 维护：当用户新增或调整规则时，必须同步维护 rules.md
- 变更控制：
  - 设计相关的任何变更须先获得书面确认
  - 对文档/脚手架等不影响设计的事项可直接执行，但需在 session 文档记录

## 集成与维护说明
- 本节为通用规则，使用本 spec 初始化任意项目时，直接写入该项目的 rules.md 中，无需重复确认
- 后续若有基础通用规则的更新，优先维护在本 spec；复用时以本 spec 最新版本为准，并同步到对应项目的 rules.md 与 session

## 版本
- v1.0：初始发布

# 需求：specrule 基础环境部署

- 日期：2026-09-17。
- 状态：已部署并验证。
- 依据：用户说明“基础环境是基于之前的specrule部署”。已定位此前使用的 `D:/gameDesign/DobeDemo/spec/rules-bootstrap-spec.md` v1.0。

## 范围与授权

建立项目规则、总控会话、需求会话、错题记录、规范副本与术语表。用户负责设计决策，助手负责研发协作与体系维护；这些职责来自当前任务范围，不继承其他工作区业务。

## 变更

- 增加 AGENTS.md 读取入口、README、rules/ 通用与桌宠边界。
- 原样保留 spec/rules-bootstrap-spec.md；标准术语路径 docs/beagle_glossary.md 按桌宠领域适配。
- 建立 session/ 与 mistakes/ 全套交付物。
- 将初次误读环境含义归档为 [BOOTSTRAP-001](../../mistakes/bootstrap-001.md)。
- 前期创建的空 src/、scripts/、tests/ 目录暂保留，没有应用文件；已安装的 hatch-pet 作为调研成果保留，固定来源记录。

## 验证与继承

标准文件存在、相对链接可解析、规范副本与上游 SHA256 一致。无应用代码，无需宣称桌宠运行验收。规则后续更新须同步总控／需求会话，不重复初始化，不回写其他项目。

Git 状态：本轮没有初始化仓库、提交或推送；其他项目的推送授权不适用于本项目。

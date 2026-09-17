# B 原画约束眨眼试样 · 第二轮

2026-09-18 用户要求严格按原 B 方案生成序列帧。本目录保留这次实际生成、失败母版和可播放结果。**文件结构通过；严格原画一致性未通过，仍为评审试样。**

## 查看

- 本地服务运行时：[原画／浅色／深色对照播放页](http://127.0.0.1:8767/animation-samples/b-idle-v2/index.html)。可暂停、单步、拖动逐帧、切换速度。
- [GIF](final/idle.gif)、[透明动画 WebP](final/idle.webp)、[PNG 图集](final/spritesheet.png)、[六个 PNG 播放帧](frames/idle/)、[帧时序](final/clip.json)。
- 192 × 208，每轮 2700 ms，六个播放槽／五张独立画面。末帧复用首帧，消除首尾开眼画面的差异；其他帧的局部纹理变化仍存在。
- [原始六张生成大图](decoded/idle.png)保留未重排的全部六张画面。

## 制作与依据

使用内置 image_gen，没有调用 Runway／视频服务／CLI 图片 API。原画板上排中间 B 的端坐主猫为唯一视觉依据；未把旧版失败帧或照片混入本次生成输入。

1. [提取提示](prompts/base.md)生成 [base](decoded/base.png)：歪头与体型保留，但鼻口、嘴垫与毛纹偏离。
2. [修订提示](prompts/base-repair.md)生成 [base-repair](decoded/base-repair.png)：独立检查仍未通过；两张都不作为帧母版。
3. [眨眼提示](prompts/idle.md)直接使用原始 B 图与排版参考，生成六张端坐轻眨眼。
4. `tools/build_idle_sample.py` 调用项目 hatch-pet 脚本切帧、清除透明 RGB 残留、打包，按时序复用首帧到末尾。没有用代码绘制或修补猫的鼻口。

`references/canonical-base.png` 是原画板的原文件复制，不是已批准的单猫提取图。完整任务输入记录在 [imagegen-jobs.json](imagegen-jobs.json)。工具缓存副本的清理被自动审批拒绝，项目内源图已保留，未尝试绕过清理限制。

## 验证与剩余差异

[结构检查](qa/review.json)：六帧尺寸、非空、边界、透明 RGB、键色色边及 GIF 帧数／2700 ms 时长通过；所有脚底包围盒为 y=203。首尾帧文件哈希一致。

[视觉检查](qa/visual-review.md)：歪头、长前腿和厚毛体块比旧版更接近 B；嘴垫仍偏亮且独立、嘴线存在感偏高，毛纹比原画更细碎。深色背景部分轮廓有浅边。不能声称严格复刻原画。

浏览器确认实际尺寸浅底、1.5 倍深底、闭眼帧、末帧单步回首帧、恢复播放。未更换 WPF 桌宠资源，未修改行为树。源图、帧和提示词保留供继续评审。

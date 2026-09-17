# B 风格：坐着呼吸与眨眼小样

用户偏好 B 绘本厚涂，授权先试一段序列帧。此样本沿用照片 02 的端坐、前爪支撑及参考图 B 的轻微歪头；全部 2D。

范围仅一段 idle 试样，不是完整角色动作集，不安装为 Codex 宠物，也未替换陈皮桌宠的运行素材。

当前版本是鼻口修订候选：用户指出首版嘴部不像原画 B，追加定向生成，减弱嘴线与双瓣嘴垫的卡通感。预览页直接并排显示原画；不能把“修过了”理解为用户已批准。生成过程与原图见 `imagegen-jobs.json`，验收结论见 `qa/visual-review.md`。

## 文件

- `index.html`：浅／深背景播放、暂停、逐帧与速度对比，直接打开可本地查看。
- `final/idle.gif`：便于预览的循环 GIF。
- `final/idle.webp`：保留 alpha 的动画 WebP。
- `frames/idle/00.png` 至 `05.png`：六张独立透明帧，各 192×208。
- `final/spritesheet.png`／`.webp`：一行六帧，1152×208。
- `final/clip.json`：帧路径、逐帧时长和循环标记。
- `qa/contact-sheet.png`、`qa/review.json`：逐帧图与结构检查。它们不能单独证明动作流畅，需播放确认。
- `prompts/`、`decoded/`：生成提示与原始产物；保留便于继续调整。

## 节奏和制作

六帧时长依次为 1300、180、100、120、150、850 毫秒，共 2.7 秒：睁眼停留→半闭→闭眼→半开→睁眼→回到初始姿态。视觉重点是轻微眨眼和呼吸，不使用大幅抖动代替动作。此频率仅供快速评审，正式常驻桌宠应把眨眼插入较长、可变化的待机停留。

画面由内置 image_gen 生成一组连续姿态，hatch-pet 脚本负责确定性的分离、对齐、检查和打包。`tools/build_idle_sample.py` 适配为单行试验，没有用代码画猫、造中间帧或宣称生成视频。保留笔触轻微变化的评估，不将 AI 六帧输出当作手工清稿完成。

初版纯洋红底在深色背景播放时出现紫边，视觉 QA 判失败；因此追加透明背景修复，并收紧小样的色边像素检查。最终检查结果单独记录，不能沿用初版结构通过结果冒充修复已通过。

复现（在仓库根目录，需 Python + Pillow）：

```powershell
python tools/build_idle_sample.py art/animation-samples/b-idle-v1 --source art/animation-samples/b-idle-v1/decoded/idle-muzzle-v3.png
python -m http.server 8766 --bind 127.0.0.1 --directory art/animation-samples/b-idle-v1
```

## 连续视频生成路线

可以先用已定稿角色图生成连续视频，再抽帧、处理透明背景和脚底锚点、修首尾循环；交付给现有桌宠仍可使用 PNG 序列帧。

[Runway 官方图生视频说明](https://help.runwayml.com/hc/en-us/articles/48324313115155-Image-to-Video-Prompting-Guide)说明输入图提供首帧的主体、构图与风格；[Adobe Firefly 官方说明](https://www.adobe.com/products/firefly/features/image-to-video.html)提供首帧与可选末帧控制。这些能力支持该路线，但不等于保证透明、无形变或无缝循环。

对于此桌宠，我的制作判断是：呼吸眨眼先验证少量稳定关键帧；长伸懒腰、翻身等复杂动作可以再试连续视频。两条路线都需要检查脸型、尾巴、四肢接触、毛纹闪烁和循环接缝。当前会话没有可调用的视频生成工具，此次未执行外部视频生成，也未购买或安装视频服务。

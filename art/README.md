# 蓝猫 2D 美术探索

本目录保存可复用的美术设计资料。角色身份由用户照片约束，用户偏好 B 绘本厚涂，正在验证单段动画，尚未完成正式定稿。

最新：用户认可第二轮 B，六段基础动作已扩为每段 24 帧并接入，见 [基础动作交付](animation-samples/b-foundation-24/README.md)。其余动作继续迭代；以下未认可／未接入描述保留为历史。

- [第一轮六风格对比图](concepts/style-exploration-v1.png)：A 清线赛璐璐、B 绘本厚涂、C 彩铅速写、D 圆润贴纸、E 复古平涂、F 像素。
- [生成提示词](style-exploration-v1-prompt.md)：使用内置 image_gen；参考图、约束和生成过程可追溯。
- [照片索引](reference/README.md)：8 张原图在 `reference/photos/` 本地保留，未上传公共仓库。
- [身份与动作／行为树规范](../spec/cat-art-and-behavior-identity.md)：照片身份锚点、姿态映射、ArtStation 出处与后续验收。
- [B 风格六帧眨眼试样](animation-samples/b-idle-v1/README.md)：播放／逐帧预览、原画对照、源图和修订记录。用户指出首版嘴部偏离原画，作为明确修订项保留。
- [B 原画约束眨眼第二轮](animation-samples/b-idle-v2/README.md)：重新以原 B 直接生成，六帧／五张独立画面，首尾复用。技术检查通过，鼻口与笔触仍有差异，严格美术一致性未通过。
- [原画偏差与视频路线调研](../docs/video-animation-research.md)：修订版仍未获认可；记录图生视频转帧、直接透明视频的实际项目，后续先确认原画单猫。
- [同源视频／序列帧实验](animation-samples/b-video-vs-frames/README.md)：用户已授权比较，准备页就绪，图生视频服务待连接；尚无新动画效果。

六格统一比较三种姿态：端坐略歪头、蜷团睡眠、扶边伸展。侧躺露肚、抬头观察等完整动作仍在照片规范中，不能因比较图未展示而删除。

这是选型图，不能直接切成游戏序列帧。用户选定方向后，再统一多视图、比例、色板和锚点，制作逐动作资源。本轮不替换现有桌宠运行素材。

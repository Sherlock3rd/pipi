# 空碗与猫粮分层

用户确认饭碗本体不含猫粮，独立猫粮素材在运行时堆叠为五层。内置 image_gen 去掉原饭碗全部粮粒，独立生成同绘本厚涂风格的一粒猫粮；未采用带背景光晕的两版粮堆。

- food-bowl-empty.png：透明空碗，运行资源 assets/props/food-bowl-empty.png。
- kibble.png：独立透明粮粒，运行资源 assets/props/kibble.png。
- 运行时从后向前逐层绘制，使用小幅位移与旋转打散颗粒重复；限制在碗内部，最多五层。
- 每点加粮／加水一次增加一层，每次完整吃喝消耗一层。吃喝分四口结算，每口 5%，被打断后只扣实际吃喝部分。旧存档非整层余量保留，下一次添加补到下一完整视觉层。
- 猫砂每次如厕增加一层脏污，每点击清理一层。

空碗提示词：Remove ALL kibble and crumbs from the illustrated blue cat-ear bowl, repaint only the revealed interior with matching pale blue gouache. Preserve bowl, pedestal, framing and transparent alpha.

粮粒提示词：A single small dry cat kibble pellet, rounded flattened brown nugget, soft storybook gouache thick brushwork, muted brown and beige grain flecks. Slightly elevated view, transparent game sprite, no shadow, halo, glow, bowl or background.

画面检查见 artifacts/smooth-qa/supplies.png，六列依次为空、1、2、3、4、5 层。

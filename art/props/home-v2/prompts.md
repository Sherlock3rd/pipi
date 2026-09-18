# 猫砂盆与参考猫窝

生成方式：内置 image_gen。猫窝遵照用户本轮参考照片，未采用先前棕色布窝候选。

## 猫砂盆

Generate a SQUARE transparent PNG GAME SPRITE, one isolated empty cat litter tray. Match the hand-painted gouache/soft impasto style of the supplied yellow bowl; no yellow bowl in output. Muted sage gray-green shallow rectangular litter tray with rounded corners, thick gently rounded rim, beige fine-grained clean litter inside, no poop or clumps, no scoop. Slightly elevated front view, broad horizontal tray, visible interior and short front wall. Soft painterly material, natural muted colors, no outline stroke. The tray occupies only the CENTRAL 65% of the SQUARE canvas leaving generous padding on ALL sides. The outside background must be actual transparent alpha, absolutely no colored haze, no glow, no cast shadow, no floor, no vignette. Clean anti-aliased sage-colored silhouette without white edge. One sprite only; no text or labels.

## 猫窝

Create a square transparent PNG game sprite of the EXACT cat bed in reference image 1, illustrated in the storybook gouache / soft impasto painterly style of reference image 2. Image1 is strict object identity reference: warm beige/taupe curly sherpa fleece plush mini armchair cat bed, round low seat cushion, tall curved cuddly backrest with two tiny black oval embroidered eyes and a small curved smiling mouth, two thick rounded arms wrapping around the seat, soft circular base with TWO small dark chocolate brown plush foot tabs sticking forward diagonally left and right. Preserve recognizable proportions, material, smile and those dark feet. Empty bed, no cat. Draw slightly elevated FRONTAL three-quarter view, centered symmetric enough for game use, opening facing viewer, all parts visible. Visible dense short curly wool rendered as gentle tactile painted brushwork, not photographic. SQUARE canvas with generous transparent padding all around, whole bed occupying central 75%. REAL transparent alpha background, NO floor, room, wall, external cast shadow, glow, haze, white outline or vignette. No additional text. The lower front seat edge must remain distinct so the runtime can layer it in front of a sleeping cat.

原始生成PNG保存同目录，运行资源 assets/props/nest.png 和 litter-tray.png。运行时统一经过 AlphaMatte 去白色混边、预乘alpha后合成；猫窝前沿由同一纹理局部遮罩覆盖，猫砂脏污由独立五层绘制。参考照片保留在用户原位置，未自动公开上传。

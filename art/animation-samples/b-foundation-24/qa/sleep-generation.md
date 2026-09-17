# Sleep source QA

- Selected: `decoded/sleep.png`; built-in imagegen, 1536 × 1024 RGBA, alpha extrema 0–254, 747026 fully transparent pixels.
- Visually 24 separate complete curled cats; a coarse connected-component check at alpha >32 also finds exactly 24 large components. Six columns and four rows, read by row bands then x.
- Pose: head resting on forepaws, closed eyes, tail wrapped around the body, rounded compact back; private photo 05 used only as pose guidance. No nest, cushion, floor, text, or effects. This bare-cat sleep can be composed on the desktop or in the nest.
- Same muted gray painted fur and compact nose/mouth as accepted v2 reference; breathing is subtle, not a locomotion sequence. Playback still requires parent packaging and temporal review.
- Raw source rows have spacing and baseline drift. Use connected-component extraction with shared alignment rather than fixed 256-pixel crops. Bodies do not touch; tiny low-alpha fur flecks may need normal extraction cleanup.
- A layout-only repair was attempted but did not materially improve spacing and brightened the face; the original generation remains selected. Prompt retained for reproducibility, rejected tool output not used.

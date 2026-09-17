# Targeted alpha repair

Tool: built-in image_gen. Target: `decoded/idle.png` (first generated six-frame magenta strip).

Preserve the same six drawings, frame order, open / half-closed / closed / reopening eye states, body shape, scale, planted paws and B gouache style. Replace only the magenta background with genuine transparent alpha. Remove magenta spill/fringe on the gray fur contour while preserving soft gray fur edges. No solid replacement background, checkerboard pixels, floor shadow, new frames, props, text or pose changes. Keep all six full-body cats separated in one row and restore clean transparent margins.

The first strip passed broad structural checks but failed visual QA on a dark background. This edit repairs the visible contour artifact; it is not locally painted or synthesized animation.

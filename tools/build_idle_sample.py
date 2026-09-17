"""Package one generated idle row using hatch-pet's deterministic pipeline.

No cat pixels or in-between drawings are synthesized here. User-requested scope
is a single six-frame study, so the vendor modules are configured for one row
in memory without modifying their full Codex-atlas defaults on disk.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.agents/skills/hatch-pet/scripts'))
from PIL import Image
import extract_strip_frames as extraction
import inspect_frames as inspection
import compose_atlas as composition
import make_contact_sheet as contact
from render_animation_previews import load_frames, save_preview
from validate_atlas import transparent_rgb_residue_count


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run_dir', type=Path)
    parser.add_argument('--method', default='auto', choices=['auto', 'stable-slots'])
    parser.add_argument('--source', type=Path, help='Generated source; defaults to run_dir/decoded/idle.png')
    args = parser.parse_args()
    run = args.run_dir.resolve()
    request = json.loads((run / 'pet_request.json').read_text(encoding='utf-8'))
    durations = request['durations_ms']
    if len(durations) != 6 or any(not isinstance(d, int) or d <= 0 for d in durations):
        raise ValueError('This study requires six positive integer frame durations')
    frames_root = run / 'frames'
    key = tuple(request['chroma_key']['rgb'])
    source = args.source.resolve() if args.source else run / 'decoded/idle.png'
    selected_source = source.relative_to(run).as_posix()
    with Image.open(source) as generated:
        source_alpha = generated.convert('RGBA').getchannel('A').getextrema()
    row = extraction.extract_state(source, 'idle', frames_root, key, 96, args.method)
    if request.get('reuse_first_frame_at_end'):
        # Timeline assembly: reuse an existing drawing, never synthesize cat pixels.
        shutil.copyfile(row['frames'][0], row['frames'][-1])
        row['last_frame_reuses'] = 0
    # Normalize transparent RGB only; do not paint, interpolate or transform poses.
    for name in row['frames']:
        with Image.open(name) as source:
            clean = composition.clear_transparent_rgb(source)
        clean.save(name)
    row['frames'] = [Path(name).relative_to(run).as_posix() for name in row['frames']]
    write_json(frames_root / 'frames-manifest.json', {'scope': 'idle-only', 'chroma_key': request['chroma_key'], 'rows': [row]})
    thresholds = argparse.Namespace(require_components=True, allow_stable_slots=args.method == 'stable-slots',
        min_used_pixels=400, edge_margin=2, edge_pixel_threshold=24,
        chroma_adjacent_threshold=150, chroma_adjacent_pixel_threshold=24,
        small_outlier_ratio=.35, large_outlier_ratio=2.75)
    report = inspection.inspect_state(frames_root, 'idle', 6, {'idle': row}, key, thresholds)
    frames = load_frames(frames_root, 'idle', 6)
    for info in report['frames']:
        info['file'] = Path(info['file']).relative_to(run).as_posix()
    report['scope'] = 'six-frame sample only; not a complete pet or motion-quality approval'
    report['selected_source'] = selected_source
    report['source_alpha_extrema'] = source_alpha
    report['transparent_rgb_residue'] = sum(transparent_rgb_residue_count(frame) for frame in frames)
    if report['transparent_rgb_residue']:
        report['errors'].append('Transparent RGB residue found')
    if len({frame.tobytes() for frame in frames}) < 4:
        report['errors'].append('Too few distinct drawings for a blink study')
    report['ok'] = not report['errors']
    write_json(run / 'qa/review.json', report)
    composition.COLUMNS = 6
    composition.ROWS = 1
    composition.ATLAS_WIDTH = 1152
    composition.ATLAS_HEIGHT = 208
    composition.ROW_SPECS = [('idle', 0, 6)]
    strip = composition.compose_from_frames(frames_root)
    composition.save_outputs(strip, run / 'final/spritesheet.png', run / 'final/spritesheet.webp')
    save_preview(frames, durations, run / 'final/idle.gif')
    frames[0].save(run / 'final/idle.webp', format='WEBP', save_all=True, append_images=frames[1:],
        duration=durations, loop=0, lossless=True, exact=True, quality=100, method=6)
    write_json(run / 'final/clip.json', {'name': request.get('clip_name', 'B · seated breathing and blink'), 'frameWidth': 192,
        'frameHeight': 208, 'loop': True, 'durationsMs': durations,
        'frames': [f'../frames/idle/{index:02d}.png' for index in range(6)], 'productionReady': False})
    contact.COLUMNS = 6
    contact.ROWS = 1
    contact.ROW_NAMES = ['B painterly idle study']
    contact.USED_COUNTS = [6]
    sys.argv = ['make_contact_sheet.py', str(run / 'final/spritesheet.png'), '--output', str(run / 'qa/contact-sheet.png'), '--scale', '1']
    contact.main()
    with Image.open(run / 'final/idle.gif') as gif:
        gif_durations = []
        for index in range(gif.n_frames):
            gif.seek(index)
            gif_durations.append(gif.info['duration'])
        if gif.n_frames != 6 or gif_durations != durations:
            raise ValueError('GIF frame count or timing differs from source clip')
    print(json.dumps({'ok': report['ok'], 'frames': 6, 'durationMs': sum(durations),
                      'errors': report['errors'], 'warnings': report['warnings']}, ensure_ascii=False))
    if not report['ok']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()

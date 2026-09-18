"""Calibrate shared-pose endpoint height, never normalize each moving frame's bounds.

Writes reversible display metadata; supplied PNGs and videos remain byte-identical.
"""
from pathlib import Path
import json
from PIL import Image
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets/pets/bluecat'


def main():
    path = ASSETS / 'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    actions = []
    for group in ['basic-v2', 'care-v3']:
        actions += json.loads((ROOT / f'art/video-pipeline/{group}/manifest.json').read_text(encoding='utf-8'))['actions']
    canonical = {'F': ('01', 0), 'SR': ('22', 0), 'SL': ('09', 0),
                 'WR': ('right', 0), 'WL': ('14', 0), 'I': ('19', 0),
                 'C': ('20', 0), 'E': ('23', 0), 'Q': ('27', 0),
                 'H': ('33', 0), 'P': ('30', 0), 'R': ('41', 0)}

    def height(clip, frame):
        im = Image.open(ASSETS / manifest['animations']['video-' + clip][frame])
        a = np.asarray(im)[:, :, 3] > 128
        ys = np.where(a)[0]
        return int(ys.max() - ys.min() + 1)

    reference = {pose: height(*sample) for pose, sample in canonical.items()}
    report = []
    for action in actions:
        clip = action['folder'][:2]
        values = [height(clip, 0), height(clip, -1)]
        factors = [reference[pose] / h for pose, h in zip([action['start'], action['end']], values)]
        assert all(.90 < f < 1.10 for f in factors), (clip, factors)
        entry = manifest['clips']['video-' + clip]
        entry['scaleStart'], entry['scaleEnd'] = factors
        report.append({'clip': clip, 'poses': [action['start'], action['end']],
                       'endpointHeights': values, 'scale': factors,
                       'correctedHeights': [h*f for h, f in zip(values, factors)]})
    # The separate right-walk loop uses the same WR reference.
    entry = manifest['clips']['video-right']
    entry['scaleStart'] = 1
    entry['scaleEnd'] = reference['WR'] / height('right', -1)
    manifest['poseScaleRevision'] = 1
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    out = ROOT / 'docs/qa/interaction-alignment'
    out.mkdir(parents=True, exist_ok=True)
    (out/'scale-calibration.json').write_text(json.dumps({'referenceHeights': reference, 'clips': report}, indent=2)+'\n', encoding='utf-8')
    print(f'Calibrated {len(report)+1} clips; correction range '
          f'{min(min(r["scale"]) for r in report):.4f}..{max(max(r["scale"]) for r in report):.4f}')


if __name__ == '__main__':
    main()

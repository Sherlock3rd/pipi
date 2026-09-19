"""Measure visible tongue extension in the supplied, unchanged care frames.

This is a calibration aid, not a substitute for checking the WPF bowl layers.
The mouth ROI and pink threshold were inspected on clips 23 and 25. Retracted
frames are excluded from the extension percentile; the head still moves freely.
"""
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets/pets/bluecat'

def measure(manifest, key):
    clip = manifest['clips'][key]
    frames = manifest['animations'][key]
    samples = []
    for i, relative in enumerate(frames):
        rgba = np.asarray(Image.open(ASSETS / relative)).astype(float)
        r, g, b = [rgba[:, :, c] for c in range(3)]
        mask = (r > g*1.28) & (r > b*1.12) & (r-g > 25) & (rgba[:, :, 3] > 200)
        mask[:420] = False
        mask[:, :390] = False
        mask[:, 445:] = False
        ys, xs = np.where(mask)
        if len(ys) <= 5:
            continue
        tip = int(max(ys))+1
        t = i/(len(frames)-1)
        t = t*t*(3-2*t)
        scale = clip['scaleStart']+(clip['scaleEnd']-clip['scaleStart'])*t
        height = (clip['groundContacts'][i]-tip/rgba.shape[0])*clip['height']*scale
        samples.append(dict(frame=i, sourceTipY=tip, heightAboveGround=height))
    assert samples, f'No visible tongue samples: {key}'
    return dict(clip=key, visibleTongueFrames=len(samples),
                extendedTipHeight=float(np.percentile([s['heightAboveGround'] for s in samples], 20)),
                samples=samples)

if __name__ == '__main__':
    manifest = json.loads((ASSETS/'manifest.json').read_text(encoding='utf-8'))
    report = [measure(manifest, key) for key in ['video-23', 'video-25']]
    out = ROOT/'docs/qa/tongue-contact'
    out.mkdir(parents=True, exist_ok=True)
    (out/'tongue.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    for result in report:
        print(f"{result['clip']}: extended tongue {result['extendedTipHeight']:.3f} above ground ({result['visibleTongueFrames']} visible frames)")

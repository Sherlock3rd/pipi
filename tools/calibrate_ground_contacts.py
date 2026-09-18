"""Register the support line of grounded poses; never resize or rewrite images.

Only grounded foundation/care clips are included. Pickup, airborne play and
other gestures retain their authored vertical motion. Four opaque pixels are
required in a row so isolated matte specks cannot become the contact point.
"""
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets/pets/bluecat'

def contacts_for(files):
    result = []
    for relative in files:
        alpha = np.asarray(Image.open(ASSETS / relative))[:, :, 3]
        rows = np.flatnonzero(np.count_nonzero(alpha >= 200, axis=1) >= 4)
        if not len(rows):
            raise ValueError(f'No opaque support pixels: {relative}')
        result.append((int(rows[-1]) + 1) / alpha.shape[0])
    return result

if __name__ == '__main__':
    path = ASSETS / 'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    keys = ['video-right'] + [f'video-{i:02}' for i in range(1, 32)]
    total = 0
    for key in keys:
        contacts = contacts_for(manifest['animations'][key])
        manifest['clips'][key]['groundContacts'] = contacts
        total += len(contacts)
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{len(keys)} grounded clips / {total} support samples; no image bytes changed')

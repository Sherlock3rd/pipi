"""Register the support line of grounded poses; never resize or rewrite images.

Grounded foundation, care and front-pose interaction clips are included.
Pickup and airborne play retain their authored vertical motion. Four opaque pixels are
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

def sleep_contacts(manifest):
    """Use planted forepaws, not the swinging tail, through the sleep route.

    ROI coordinates refer to the supplied 512px frames inspected in QA. When
    paws tuck under the curled body, support transfers to its resting underside.
    The breathing loop uses one fixed support anchor, not a quantized silhouette.
    """
    def measure(relative, lo, hi):
        rgba = np.asarray(Image.open(ASSETS / relative))
        mask = (rgba[:, :, 3] >= 200) & (rgba[:, :, :3].mean(axis=2) < 190)
        rows = np.flatnonzero(np.count_nonzero(mask[:, int(lo):int(hi)], axis=1) >= 4)
        if not len(rows):
            raise ValueError(f'No paw/support pixels in {lo}:{hi}: {relative}')
        return float(rows[-1] + 1) / rgba.shape[0]
    def ease(t):
        t = np.clip(t, 0, 1)
        return t*t*(3-2*t)
    frames = manifest['animations']
    front = measure(frames['video-01'][0], 225, 310)
    side = measure(frames['video-19'][0], 330, 400)
    curled = float(np.median([measure(f, 230, 320) for f in frames['video-20']]))
    profiles = {'video-01': [front]*len(frames['video-01']),
                'video-20': [curled]*len(frames['video-20'])}
    for key in ['video-17', 'video-18', 'video-19', 'video-21']:
        raw = []
        for i, relative in enumerate(frames[key]):
            t = i/(len(frames[key])-1)
            if key in ['video-17', 'video-18']:
                turn = ease((t if key == 'video-17' else 1-t)/.55)
                raw.append(measure(relative, 225+105*turn, 310+90*turn))
            else:
                curl = ease((t-.3)/.35) if key == 'video-19' else 1-ease((t-.35)/.4)
                paw = measure(relative, 365, 450)
                body = measure(relative, 230, 320)
                raw.append(paw*(1-curl)+body*curl)
        # Remove one-pixel alpha threshold steps without smoothing the animation.
        kernel = np.exp(-np.arange(-4, 5, dtype=float)**2/(2*1.4**2)); kernel /= kernel.sum()
        smooth = np.convolve(np.pad(raw, 4, mode='edge'), kernel, mode='valid')
        start, end = {'video-17': (front, side), 'video-18': (side, front),
                      'video-19': (side, curled), 'video-21': (curled, side)}[key]
        phase = np.linspace(0, 1, len(raw))
        smooth += (start-smooth[0])*(1-ease(phase/.12)) + (end-smooth[-1])*ease((phase-.88)/.12)
        profiles[key] = smooth.tolist()
    for key, values in profiles.items():
        manifest['clips'][key]['groundContacts'] = values
    manifest['supportContactRevision'] = 3
    return front

def interaction_contacts(manifest):
    """Apply the same seated support to every supplied front-pose interaction.

    Seated gestures keep their planted feet; rolling transfers support to the
    underside of the body, with raised paws and the left-hand tail excluded.
    """
    frames = manifest['animations']
    front = manifest['clips']['video-01']['groundContacts'][0]
    def measure(relative, lo, hi):
        rgba = np.asarray(Image.open(ASSETS / relative))
        mask = (rgba[:, :, 3] >= 200) & (rgba[:, :, :3].mean(axis=2) < 190)
        rows = np.flatnonzero(mask[:, int(lo):int(hi)].sum(axis=1) >= 4)
        if not len(rows):
            raise ValueError(f'No interaction support pixels: {relative}')
        return float(rows[-1]+1)/rgba.shape[0]
    def ease(t):
        t = np.clip(t, 0, 1)
        return t*t*(3-2*t)
    rolled = measure(frames['video-41'][0], 250, 450)
    keys = ['video-'+k for k in ['37', '38', '39', '45', '46', '47', '51']]
    for key in keys:
        manifest['clips'][key]['groundContacts'] = [front]*len(frames[key])
    for key in ['video-40', 'video-41', 'video-42']:
        raw = []
        for i, relative in enumerate(frames[key]):
            t = i/(len(frames[key])-1)
            lying = 1 if key == 'video-41' else ease((t if key == 'video-40' else 1-t)/.45)
            raw.append(measure(relative, 225+25*lying, 310+140*lying))
        kernel = np.exp(-np.arange(-4, 5, dtype=float)**2/(2*1.4**2)); kernel /= kernel.sum()
        smooth = np.convolve(np.pad(raw, 4, mode='wrap' if key == 'video-41' else 'edge'), kernel, mode='valid')
        start, end = {'video-40': (front, rolled), 'video-41': (rolled, rolled), 'video-42': (rolled, front)}[key]
        phase = np.linspace(0, 1, len(raw))
        smooth += (start-smooth[0])*(1-ease(phase/.12)) + (end-smooth[-1])*ease((phase-.88)/.12)
        manifest['clips'][key]['groundContacts'] = smooth.tolist()
        keys.append(key)
    manifest['interactionContactRevision'] = 1
    return sum(len(frames[key]) for key in keys)

if __name__ == '__main__':
    path = ASSETS / 'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    keys = ['video-right'] + [f'video-{i:02}' for i in range(1, 32)]
    total = 0
    for key in keys:
        contacts = contacts_for(manifest['animations'][key])
        manifest['clips'][key]['groundContacts'] = contacts
        total += len(contacts)
    # Landing begins in the air: only its seated endpoint joins the ground.
    # Runtime blends this endpoint into the final fifth of the original descent.
    manifest['clips']['video-34']['landingContactY'] = sleep_contacts(manifest)
    total += interaction_contacts(manifest)
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{len(keys)+10} grounded clips / {total} support samples; no image bytes changed')

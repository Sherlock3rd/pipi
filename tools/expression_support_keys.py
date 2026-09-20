"""Reviewed anatomical support lines, in the 640px source canvas.

Eight equally spaced samples per transition, identified on planted paws or the
chest/flank that bears weight. Excludes swinging tail and airborne extremities.
Shared-pose registration still supplies the exact first/last support values.
"""
import numpy as np

SUPPORT_KEYS = {
    '60': [546, 542, 538, 532, 508, 518, 518, 518],
    '61': [518, 518, 528, 530, 533, 542, 546, 546],
    '62': [518, 512, 517, 534, 532, 539, 539, 539],
    '63': [539, 539, 528, 515, 518, 518, 518, 518],
    '64': [518, 518, 517, 512, 524, 566, 572, 572],
    '65': [572, 547, 542, 539, 526, 518, 518, 518],
    '70': [518, 518, 526, 544, 539, 537, 537, 537],
    '74': [539, 539, 534, 533, 537, 537, 537, 537],
    '78': [572, 545, 514, 526, 533, 537, 537, 537],
    '83': [518, 518, 518, 520, 526, 534, 537, 537],
    '85': [537, 537, 537, 537, 530, 520, 518, 518],
}

def reviewed_support(clip, count, first, last):
    keys = np.array(SUPPORT_KEYS[clip], dtype=float) / 640
    keys[0], keys[-1] = first, last
    phase = np.linspace(0, len(keys)-1, count)
    index = np.minimum(phase.astype(int), len(keys)-2)
    t = phase-index
    t = t*t*(3-2*t)
    return keys[index]*(1-t)+keys[index+1]*t

if __name__ == '__main__':
    import json
    from pathlib import Path
    root = Path(__file__).resolve().parents[1]
    path = root/'assets/pets/bluecat/manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    for clip in SUPPORT_KEYS:
        entry = manifest['clips']['video-'+clip]
        old = entry['groundContacts']
        entry['groundContacts'] = reviewed_support(clip,len(old),old[0],old[-1]).tolist()
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    report = root/'docs/qa/expressions-v4/calibration.json'
    data = json.loads(report.read_text(encoding='utf-8'))
    data['reviewedSupportKeys640'] = SUPPORT_KEYS
    for row in data['clips']:
        entry = manifest['clips']['video-'+row['id']]
        row['maxAdjacentContactDelta'] = float(np.abs(np.diff(entry['groundContacts'])).max()*entry['height'])
        if row['id'] in SUPPORT_KEYS: row['supportMethod'] = 'reviewed planted paw/chest/flank keyframes; registered endpoints'
    report.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
    print('Applied reviewed support to', len(SUPPORT_KEYS), 'clips')

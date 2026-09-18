"""Register shared-pose fur features, never normalize a changing pose's outer bounds.

Writes reversible display metadata; supplied PNGs and videos remain byte-identical.
"""
from pathlib import Path
import json
from PIL import Image
import numpy as np
import cv2

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

    detector = cv2.SIFT_create()
    cache = {}
    def features(clip, frame):
        key = (clip, frame)
        if key in cache:
            return cache[key]
        im = Image.open(ASSETS / manifest['animations']['video-' + clip][frame])
        rgba = np.asarray(im)
        mask = (rgba[:, :, 3] > 200).astype('uint8') * 255
        cache[key] = detector.detectAndCompute(cv2.cvtColor(rgba[:, :, :3], cv2.COLOR_RGB2GRAY), mask)
        return cache[key]

    def register(clip, frame, pose, reference=None):
        ref, ref_frame = reference or canonical[pose]
        ka, da = features(clip, frame)
        kb, db = features(ref, ref_frame)
        pairs = cv2.BFMatcher().knnMatch(da, db, k=2)
        matches = [a for a, b in pairs if a.distance < .75*b.distance]
        a = np.float32([ka[p.queryIdx].pt for p in matches])
        b = np.float32([kb[p.trainIdx].pt for p in matches])
        _, keep = cv2.estimateAffinePartial2D(a, b, method=cv2.RANSAC, ransacReprojThreshold=2)
        a, b = a[keep[:, 0] == 1], b[keep[:, 0] == 1]
        assert len(a) >= 18, (clip, frame, pose, len(a))
        # Isotropic scale and translation only: never rotate, stretch or mirror anatomy.
        ac, bc = a-a.mean(0), b-b.mean(0)
        scale = float((ac*bc).sum()/(ac*ac).sum())
        translation = b.mean(0)-scale*a.mean(0)
        residual = float(np.sqrt(((a*scale+translation-b)**2).sum(1)).mean())
        assert .95 < scale < 1.05 and residual < 3, (clip, frame, scale, residual)
        own = manifest['clips']['video-'+clip]
        refdef = manifest['clips']['video-'+ref]
        origin = np.array([own['anchorX'], own['anchorY']])*512
        ref_origin = np.array([refdef['anchorX'], refdef['anchorY']])*512
        offset = (origin*scale+translation-ref_origin)*288/512
        return {'scale': scale, 'offset': offset.tolist(), 'inliers': len(a), 'residualPixels': residual}

    report = []
    for action in actions:
        clip = action['folder'][:2]
        fits = [register(clip, frame, pose) for frame, pose in zip([0, -1], [action['start'], action['end']])]
        entry = manifest['clips']['video-' + clip]
        for suffix, fit in zip(['Start', 'End'], fits):
            entry['scale'+suffix] = fit['scale']
            entry['offset'+suffix+'X'], entry['offset'+suffix+'Y'] = fit['offset']
        report.append({'clip': clip, 'poses': [action['start'], action['end']], 'fits': fits})
    # This route has a real supplied seam. Fit the arriving I pose directly to
    # the preceding I pose, then compose that reference's registration.
    fit = register('18', 0, 'I', ('21', -1))
    prior = manifest['clips']['video-21']
    fit['offset'] = (np.array(fit['offset'])*prior['scaleEnd']+
                     np.array([prior['offsetEndX'], prior['offsetEndY']])).tolist()
    fit['scale'] *= prior['scaleEnd']
    entry = manifest['clips']['video-18']
    entry['scaleStart'] = fit['scale']
    entry['offsetStartX'], entry['offsetStartY'] = fit['offset']
    next(row for row in report if row['clip']=='18')['fits'][0] = fit
    # The separate right-walk loop uses the same WR reference.
    entry = manifest['clips']['video-right']
    for suffix, frame in [('Start', 0), ('End', -1)]:
        fit = register('right', frame, 'WR')
        entry['scale'+suffix] = fit['scale']
        entry['offset'+suffix+'X'], entry['offset'+suffix+'Y'] = fit['offset']
    manifest['poseScaleRevision'] = 2
    # Waking may interrupt any breath phase, not only the loop's first frame.
    manifest['clips']['video-20']['phaseRegistration'] = [
        [fit['scale'], *fit['offset']]
        for i in range(len(manifest['animations']['video-20']))
        for fit in [register('20', i, 'C')]]
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    out = ROOT / 'docs/qa/interaction-alignment'
    out.mkdir(parents=True, exist_ok=True)
    (out/'scale-calibration.json').write_text(json.dumps({'method': 'SIFT RANSAC inliers, isotropic least squares; no silhouette height fit', 'references': canonical, 'clips': report}, indent=2)+'\n', encoding='utf-8')
    print(f'Calibrated {len(report)+1} clips; correction range '
          f'{min(f["scale"] for r in report for f in r["fits"]):.4f}..{max(f["scale"] for r in report for f in r["fits"]):.4f}')


if __name__ == '__main__':
    main()

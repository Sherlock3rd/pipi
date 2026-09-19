"""Undo measured horizontal stretch in head-down/head-up source frames.

Only reversible rendering metadata is written. No vertical scale, contact,
image bytes, frame counts or playback timing are changed.
"""
import json
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT/'assets/pets/bluecat'

def calibrate(manifest):
    detector = cv2.SIFT_create()
    evidence = []
    for key in ['video-22', 'video-24']:
        files = manifest['animations'][key]
        clip = manifest['clips'][key]
        features = []
        for relative in files:
            rgba = np.asarray(Image.open(ASSETS/relative))
            features.append(detector.detectAndCompute(cv2.cvtColor(rgba[:, :, :3], cv2.COLOR_RGB2GRAY), (rgba[:, :, 3] > 200).astype('uint8')*255))
        def fit(index, ref):
            ka, da = features[index];kb, db = features[ref]
            matches = [x for x, y in cv2.BFMatcher().knnMatch(da, db, k=2) if x.distance < .75*y.distance]
            a = np.float32([ka[x.queryIdx].pt for x in matches]);b = np.float32([kb[x.trainIdx].pt for x in matches])
            _, keep = cv2.estimateAffine2D(a, b, method=cv2.RANSAC, ransacReprojThreshold=1.5)
            a, b = a[keep[:, 0] == 1], b[keep[:, 0] == 1]
            x, y = a[:, 0], b[:, 0]
            # The lowering head can shear relative to the stationary torso.
            # Reject those non-rigid points rather than fitting that shear.
            for _ in range(3):
                sx = float(np.dot(x-x.mean(), y-y.mean())/np.dot(x-x.mean(), x-x.mean()))
                tx = float(np.median(y-sx*x))
                error = np.abs(x*sx+tx-y)
                stable = error <= max(.8, float(np.percentile(error, 75)))
                x, y = x[stable], y[stable]
            sx = float(np.dot(x-x.mean(), y-y.mean())/np.dot(x-x.mean(), x-x.mean()))
            tx = float(y.mean()-sx*x.mean())
            residual = float(np.abs(x*sx+tx-y).mean())
            if len(x) < 18 or np.ptp(x) < 140 or residual >= .8:
                return None
            suffix = 'Start' if ref == 0 else 'End'
            ref_scale = clip['scale'+suffix]
            # Map into the established, unchanged endpoint's logical coordinates.
            return sx*ref_scale, (tx+(sx-1)*256)*288/512*ref_scale+clip['offset'+suffix+'X'], len(x), residual
        values = []
        for i in range(len(files)):
            t = i/(len(files)-1);t = t*t*(3-2*t)
            start, end = fit(i, 0), fit(i, len(files)-1)
            assert start is not None or end is not None, (key, i, 'No reliable endpoint registration')
            start, end = start or end, end or start
            desired_scale = start[0]*(1-t)+end[0]*t
            desired_offset = start[1]*(1-t)+end[1]*t
            base_scale = clip['scaleStart']*(1-t)+clip['scaleEnd']*t
            base_offset = clip['offsetStartX']*(1-t)+clip['offsetEndX']*t
            ratio = desired_scale/base_scale
            offset = desired_offset-base_offset*ratio
            assert .95 <= ratio <= 1.05 and abs(offset) < 12
            values.append([ratio, offset])
            evidence.append(dict(clip=key, frame=i, horizontalScale=ratio, offsetX=offset,
                                 inliers=min(start[2], end[2]), residual=max(start[3], end[3])))
        values[0] = values[-1] = [1.0, 0.0]
        clip['horizontalRegistration'] = values
    manifest['careWidthRevision'] = 1
    return evidence

if __name__ == '__main__':
    path = ASSETS/'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    evidence = calibrate(manifest)
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    out = ROOT/'docs/qa/animation-monitor';out.mkdir(parents=True, exist_ok=True)
    (out/'calibration.json').write_text(json.dumps(evidence, indent=2)+'\n', encoding='utf-8')
    print(f'{len(evidence)} horizontal registrations; source images and vertical geometry unchanged')

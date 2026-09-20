"""Scan every WPF-rendered frame, including clip interiors, loops and care seams.

Usage: python tools/check_animation_continuity.py EXPORT_DIR --out REPORT_DIR
Exit 1 means a reliable abrupt body change; outline-only changes need review.
Dependencies: numpy, Pillow, opencv-python-headless. Reports stay offline.
"""
import argparse
import html
import json
from pathlib import Path
import cv2
import numpy as np
cv2.setNumThreads(2)
from PIL import Image, ImageDraw

DETECTOR = cv2.SIFT_create()
CARE_EDGES = [('22', '23'), ('22', '25'), ('23', '24'), ('25', '24')]
COMMON_EDGES = [('01','06'),('06','10'),('10','right'),('right','11'),('11','07'),('07','01'),
                ('01','08'),('08','12'),('12','14'),('14','13'),('13','09'),('09','01'),
                ('11','15'),('15','12'),('13','16'),('16','10'),('16','22'),('11','22'),
                ('24','07'),('01','17'),('17','19'),('19','20'),('20','21'),('21','18'),('18','01'),
                ('26','27'),('27','28'),('29','30'),('30','31'),('32','33'),('33','34'),('34','01'),
                ('01','40'),('40','41'),('41','42'),('42','01')]
COMMON_EDGES += [(a,b) for gesture in ['37','38','39','45','46','47','51'] for a,b in [('01',gesture),(gesture,'01')]]
COMMON_EDGES += [(gesture,'07') for gesture in ['48','49','50']] + [('07','51')]
# The returned expression pack shares declared pose endpoints. Include every
# legal pair, not just a hand-picked happy path through the new sleep graph.
expression_manifest=Path(__file__).resolve().parents[1]/'art/video-pipeline/expressions-v4/manifest.json'
if expression_manifest.exists():
    expressions=json.loads(expression_manifest.read_text(encoding='utf-8'))['clips']
    COMMON_EDGES += [(str(a['id']),str(b['id'])) for a in expressions for b in expressions if a['end']==b['start'] and a['id']!=b['id']]
    COMMON_EDGES += [('01','58'),('59','01'),('24','52'),('11','52'),('13','55'),('54','15'),('57','16'),('70','10'),('74','10'),('78','10'),('70','15'),('74','15'),('78','15')]
    COMMON_EDGES += [('right','86'),('86','right'),('14','87'),('87','14'),('01','88'),('88','01'),('11','93'),('93','10'),('13','94'),('94','12'),('24','15')]

def features(path, frame=None, export=None):
    rgba = np.asarray(Image.open(path).convert('RGBA'))
    alpha = rgba[:, :, 3] > 128
    ys, xs = np.where(alpha)
    if not len(xs):
        raise ValueError(f'Empty frame: {path}')
    feature_mask=alpha.astype('uint8')*255
    if frame and frame['Clip'] in {'video-22','video-23','video-24','video-25'}:
        # These four clips share a planted torso. Exclude the lowering head,
        # lapping tongue and tail tip so a real head movement is not body scale.
        definition=frame['Definition'];ppu=export['PixelsPerUnit']
        x0,x1=[round((export['RootX']+(v/512-definition['AnchorX'])*definition['Width'])*ppu) for v in [110,350]]
        y0,y1=[round((export['RootY']+(v/512-definition['AnchorY'])*definition['Height'])*ppu) for v in [310,425]]
        roi=np.zeros_like(feature_mask);roi[max(0,y0):y1,max(0,x0):x1]=255
        feature_mask &= roi
    # SIFT only needs the occupied region plus a scale-space gutter. Avoid
    # repeatedly building pyramids for the large transparent export canvas.
    my,mx=np.where(feature_mask)
    left=max(0,int(mx.min())-32);right=min(rgba.shape[1],int(mx.max())+33)
    top=max(0,int(my.min())-32);bottom=min(rgba.shape[0],int(my.max())+33)
    keypoints, descriptors = DETECTOR.detectAndCompute(
        cv2.cvtColor(rgba[top:bottom,left:right,:3], cv2.COLOR_RGB2GRAY), feature_mask[top:bottom,left:right])
    for point in keypoints:
        point.pt=(point.pt[0]+left,point.pt[1]+top)
    return keypoints, descriptors, [int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1)]

def compare(a, b, pixels_per_unit):
    ka, da, ba = a
    kb, db, bb = b
    pairs = cv2.BFMatcher().knnMatch(da, db, k=2) if da is not None and db is not None and len(db) > 1 else []
    matches = [x for pair in pairs if len(pair) == 2 for x, y in [pair] if x.distance < .75*y.distance]
    result = dict(outlineWidthDelta=((bb[2]-bb[0])-(ba[2]-ba[0]))/pixels_per_unit,
                  outlineHeightDelta=((bb[3]-bb[1])-(ba[3]-ba[1]))/pixels_per_unit,
                  opaqueBottomDelta=(bb[3]-ba[3])/pixels_per_unit,
                  inliers=0, reliable=False, failures=[], review=[])
    if len(matches) >= 8:
        pa = np.float32([ka[x.queryIdx].pt for x in matches])
        pb = np.float32([kb[x.trainIdx].pt for x in matches])
        matrix, keep = cv2.estimateAffine2D(pa, pb, method=cv2.RANSAC, ransacReprojThreshold=1.5*pixels_per_unit)
        if matrix is not None:
            pa, pb = pa[keep[:, 0] == 1], pb[keep[:, 0] == 1]
            residual = float(np.linalg.norm(pa @ matrix[:, :2].T + matrix[:, 2]-pb, axis=1).mean()/pixels_per_unit)
            width_change = float((np.linalg.norm(matrix[:, 0])-1)*100)
            height_change = float((np.linalg.norm(matrix[:, 1])-1)*100)
            span = float(np.ptp(pa[:, 0])/max(1, ba[2]-ba[0]))
            reliable = len(pa) >= 25 and span >= .45 and residual <= .65
            displacement = (pb-pa).mean(axis=0)/pixels_per_unit
            result.update(inliers=len(pa), reliable=reliable, bodyWidthPercent=width_change,
                          bodyHeightPercent=height_change, bodyDeltaX=float(displacement[0]),
                          bodyDeltaY=float(displacement[1]), residual=residual, horizontalCoverage=span)
            if reliable:
                if abs(width_change) > .8:
                    result['failures'].append('body-width')
                if abs(height_change) > .8:
                    result['failures'].append('body-height')
                if np.linalg.norm(displacement) > 1.5:
                    result['review'].append('body-position')
    if not result['reliable']:
        result['review'].append('insufficient-stable-correspondence')
    if abs(result['outlineWidthDelta']) > 2 or abs(result['outlineHeightDelta']) > 2:
        result['review'].append('outline-change')
    if abs(result['opaqueBottomDelta']) > 1:
        result['review'].append('support-or-tail-change')
    return result

def run(directory, output):
    data = json.loads((directory/'frames.json').read_text(encoding='utf-8-sig'))
    frames = data['Frames']
    groups = {}
    for f in frames:
        groups.setdefault(f['Clip'], []).append(f)
    cache = {}
    def read(f):
        if f['File'] not in cache:
            cache[f['File']] = features(directory/f['File'], f, data)
        return cache[f['File']]
    comparisons = []
    def add(a, b, kind):
        comparisons.append(dict(source=a['File'], target=b['File'], kind=kind,
                                **compare(read(a), read(b), data['PixelsPerUnit'])))
    for clip, sequence in groups.items():
        sequence.sort(key=lambda f: f['Index'])
        for a, b in zip(sequence, sequence[1:]):
            add(a, b, 'within-clip')
        if sequence[0]['Definition']['Loop']:
            add(sequence[-1], sequence[0], 'loop')
        print(f'Scanned {clip}: {len(sequence)} frames', flush=True)
    for a, b in CARE_EDGES+COMMON_EDGES:
        a, b = 'video-'+a, 'video-'+b
        if a in groups and b in groups:
            add(groups[a][-1], groups[b][0], 'care-seam')
    failures = [r for r in comparisons if r['failures']]
    reviews = [r for r in comparisons if r['review']]
    output.mkdir(parents=True, exist_ok=True)
    report = dict(frameCount=len(frames), pairCount=len(comparisons), failureCount=len(failures),
                  reviewCount=len(reviews), comparisons=comparisons,
                  note='Independent width/height affine fit on actual WPF pixels. Outline-only changes and low-confidence poses require review; opaque bottom can be tail, not foot.')
    (output/'report.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    ranked = sorted(comparisons, key=lambda r: (bool(r['failures']), bool(r['review']), abs(r.get('bodyWidthPercent', 0))), reverse=True)
    cards = []
    for i, row in enumerate(ranked[:40]):
        a = Image.open(directory/row['source']).convert('RGBA')
        b = Image.open(directory/row['target']).convert('RGBA')
        canvas = Image.new('RGB', (1152, 354), '#e8e8e0')
        draw = ImageDraw.Draw(canvas)
        for x, im, label in [(0, a, 'Before'), (384, b, 'After'), (768, Image.blend(a, b, .5), 'Overlay')]:
            bg = Image.new('RGBA', im.size, '#e8e8e0');bg.alpha_composite(im)
            canvas.paste(bg.convert('RGB').resize((384, 320)), (x, 34))
            draw.text((x+4, 18), label, fill='black')
        title = f"{row['source']} -> {row['target']} | width {row.get('bodyWidthPercent', 0):+.3f}% height {row.get('bodyHeightPercent', 0):+.3f}%"
        draw.text((4, 2), title, fill='black')
        name = f'pair-{i:03}.png';canvas.save(output/name)
        cards.append(f'<article><h3>{html.escape(title)}</h3><p>{html.escape(str(row["failures"] or row["review"] or "Within thresholds"))}</p><img src="{name}"></article>')
    page = f'''<!doctype html><meta charset="utf-8"><title>动画连续性检查</title>
<style>body{{font:16px system-ui;background:#faf9f6;margin:32px}}img{{max-width:100%}}article{{margin:28px 0;border-top:1px solid #bbb}}strong{{color:#a22}}</style>
<h1>动画连续性检查</h1><p>{len(frames)} 帧 / {len(comparisons)} 对相邻帧；<strong>{len(failures)} 处突变</strong>，{len(reviews)} 处需复核。</p>
<p>宽、高分别测量；轮廓变化可能来自尾巴或姿态，不能当作整体缩放。透明叠图只用于诊断，运行时仍是单帧绘制。</p>
{''.join(cards)}'''
    (output/'index.html').write_text(page, encoding='utf-8')
    print(f'{len(frames)} frames / {len(comparisons)} pairs: {len(failures)} failures, {len(reviews)} review candidates')
    return bool(failures)

def self_test():
    # Independent deterministic texture: verify a 2% width defect is detected
    # while an unchanged frame is accepted, without changing any pet asset.
    rng = np.random.default_rng(7)
    im = (rng.random((240, 360))*255).astype('uint8')
    im = cv2.GaussianBlur(im, (3, 3), .5)
    alpha = np.zeros_like(im);alpha[20:220, 20:340] = 255
    def get(gray, mask):
        k, d = DETECTOR.detectAndCompute(gray, mask)
        return k, d, [20, 20, 340, 220]
    original = get(im, alpha)
    warped = cv2.warpAffine(im, np.float32([[1.02, 0, -3.6], [0, 1, 0]]), (360, 240))
    assert not compare(original, original, 1)['failures']
    assert 'body-width' in compare(original, get(warped, alpha), 1)['failures']
    print('Self-test passed: unchanged accepted; injected 2% horizontal jump detected')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path, nargs='?')
    parser.add_argument('--out', type=Path, default=Path('artifacts/animation-monitor/report'))
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    if args.directory:
        raise SystemExit(run(args.directory, args.out))
    if not args.self_test:
        parser.error('Provide an export directory or --self-test')

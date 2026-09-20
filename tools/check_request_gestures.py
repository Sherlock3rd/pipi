"""Check request/refill routing, shared-pose seams and front-paw alignment.

Run after --audit-animations and --audit-care-request (food and guided water).
This supplements the full adjacent-frame monitor; it never edits source art.
"""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image
from check_animation_continuity import features, compare


def main(root, output):
    rendered = root / 'before'
    def endpoint(clip, last=False):
        files = sorted((rendered / ('video-' + clip)).glob('*.png'))
        return files[-1] if last else files[0]
    def mask(path):
        return np.asarray(Image.open(path).convert('RGBA'))[:, :, 3] > 128
    def overlap(a, b):
        aa, bb = mask(a), mask(b)
        return float((aa & bb).sum() / (aa | bb).sum())
    seams = []
    for a, b in [('48','07'), ('49','07'), ('50','07'), ('07','51'), ('45','01'), ('46','01')]:
        source, target = endpoint(a, True), endpoint(b)
        measured = compare(features(source), features(target), 2)
        # Explicit planted-paw regions: do not measure the tail or raised paw.
        x0, x1 = (478, 529) if b == '07' else (390, 419)
        def support(path):
            ys, _ = np.where(mask(path)[485:530, x0:x1])
            return int(ys.max()) + 485
        foot_delta = (support(target) - support(source)) / 2
        assert measured['reliable'] and not measured['failures'] and not measured['review'], (a,b,measured)
        assert abs(foot_delta) <= 1, (a,b,foot_delta)
        assert overlap(source, target) > .96
        seams.append(dict(source=a, target=b, plantedPawDelta=foot_delta, **measured))
    negative = overlap(rendered/'video-49/0048.png', endpoint('51'))
    assert negative < .8, 'Old direct standing-to-seated jump was not distinguished'
    paw = mask(rendered/'video-45/0048.png')
    ys, xs = np.where(paw[516:526, 360:401])
    assert len(xs) > 20
    paw_x = float(np.mean(xs + 360) / 2 - 192)
    old_error, new_error = abs(paw_x - 94), abs(paw_x)
    assert new_error < 5 and old_error > 90
    traces = []
    for folder, expected in [('food-request-final', ['video-45','video-01']), ('water-guide', ['video-49','video-07','video-51'])]:
        trace = json.loads((root/folder/'trace.json').read_text(encoding='utf-8'))
        assert trace['Transitions'] == expected, trace['Transitions']
        samples = trace['Samples']
        after = [s for s in samples if s['Refilled']]
        assert len({s['X'] for s in samples}) == 1
        stock = 'Food' if trace['Kind'] == 'food' else 'Water'
        assert all(s[stock] == 20 for s in after)
        for i, (a, b) in enumerate(zip(samples, samples[1:])):
            if a['SourceClip'] != b['SourceClip']:
                assert a['SourceFrame'] == len(list((rendered/a['SourceClip']).glob('*.png'))) - 1, (folder,i,a)
        traces.append(dict(kind=trace['Kind'], guided=trace['Guided'], frames=len(samples), transitions=trace['Transitions'], stockPreserved=True, rootStable=True))
    source_report = json.loads((root/'source-monitor-final/report.json').read_text(encoding='utf-8'))
    result = dict(seams=seams, oldInvalidPoseOverlap=negative, requestFoodPawX=paw_x,
                  oldHorizontalError=old_error, newHorizontalError=new_error, traces=traces,
                  sourceFrameCount=source_report['frameCount'], sourcePairCount=source_report['pairCount'],
                  retainedSourceThresholdFlags=source_report['failureCount'], retainedSourceReviewFlags=source_report['reviewCount'],
                  note='Source flags remain for pose changes and source motion. Shared endpoint checks and refill routing passed; this is not blanket art approval.')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'6 seams passed; front-paw horizontal error {old_error:.2f} -> {new_error:.2f}; actual refill traces passed')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    main(args.directory, args.out)

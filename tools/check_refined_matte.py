from pathlib import Path
import sys, json, shutil
import cv2
import numpy as np
from refine_video_matte import refine_matte

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'assets/pets/bluecat'
cv2.setNumThreads(1)

def white_regions(image):
    low=image[:,:,:3].min(2)
    mask=(low>185)&((image[:,:,:3].max(2).astype(float)-low)<35)&(image[:,:,3]>4)
    n,l,s,_=cv2.connectedComponentsWithStats(mask.astype(np.uint8),8)
    seeds=np.bincount(l[low>230],minlength=n)
    selected=(s[:,cv2.CC_STAT_AREA]>=24)&(seeds>=8);selected[0]=False
    return int(s[selected,cv2.CC_STAT_AREA].sum())

def tests():
    im=np.zeros((64,64,4),np.uint8);im[5:59,5:59]=[95,105,115,255]
    im[30:50,26:38]=[245,245,245,255];im[12:14,13:15]=[255,255,255,255]
    im[0,0]=[255,255,255,255]
    im[20,5]=[195,200,205,200]
    im[35,24]=[235,235,235,255]
    fixed=refine_matte(im)
    assert fixed[40,30,3]==0,'enclosed background must become transparent'
    assert np.array_equal(fixed[12:14,13:15],im[12:14,13:15]),'small eye highlights must survive'
    assert np.array_equal(fixed[18:24,18:24],im[18:24,18:24]),'interior texture must not be blurred'
    assert fixed[0,0,3]==0,'detached flash must disappear'
    assert fixed[20,5,:3].max()<170 and fixed[20,5,3]==200,'relative gray-white rim must be corrected without alpha blur'
    assert fixed[35,24,:3].max()<200,'newly exposed hole edge must not retain an interior white pixel'
    assert white_regions(fixed)==0
    print('PASS 7 matte regression checks',flush=True)

def main():
    tests();apply='--apply' in sys.argv;verify='--verify-installed' in sys.argv
    manifest=json.loads((ASSETS/'manifest.json').read_text(encoding='utf-8'))
    report=[];before_total=after_total=changed_count=protected_changes=0
    backup=ROOT/'artifacts/edge-focus/before'
    for clip,paths in manifest['animations'].items():
        if not clip.startswith('video-'):continue
        changed=before=after=0
        for relative in paths:
            path=ASSETS/relative;saved=backup/relative
            old=cv2.imread(str(saved if (apply or verify) and saved.exists() else path),cv2.IMREAD_UNCHANGED)
            new=cv2.imread(str(path),cv2.IMREAD_UNCHANGED) if verify else refine_matte(old)
            pre=white_regions(old);post=white_regions(new)
            before+=pre;after+=post
            inward=cv2.distanceTransform((old[:,:,3]>127).astype(np.uint8),cv2.DIST_L2,5)
            after_inward=cv2.distanceTransform((new[:,:,3]>127).astype(np.uint8),cv2.DIST_L2,5)
            # Restored holes create new silhouette edges. Compare the true solid
            # interior shared by both images; hole boundaries are intentionally repaired.
            protect=(inward>=4)&(after_inward>=4)&(old[:,:,:3].min(2)<=185)
            protected_changes+=int(np.any(old!=new,axis=2)[protect].sum())
            if not np.array_equal(old,new):
                changed+=1
                if apply:
                    saved.parent.mkdir(parents=True,exist_ok=True)
                    if not saved.exists():shutil.copy2(path,saved)
                    assert cv2.imwrite(str(path),new)
            assert post==0,(clip,relative,'white interior leakage',post)
        changed_count+=changed;before_total+=before;after_total+=after
        report.append({'clip':clip,'frames':len(paths),'changedFrames':changed,'whiteLeakPixelsBefore':before,'whiteLeakPixelsAfter':after})
        print(clip,len(paths),'changed',changed,'leak',before,'->',after,flush=True)
    summary={'frames':sum(r['frames'] for r in report),'changedFrames':changed_count,
             'whiteLeakPixelsBefore':before_total,'whiteLeakPixelsAfter':after_total,
             'protectedInteriorChangedPixels':protected_changes,'clips':report}
    assert protected_changes==0,'opaque non-white interior must be byte-identical'
    out=ROOT/'artifacts/edge-focus';out.mkdir(parents=True,exist_ok=True)
    (out/('repair-audit.json' if apply else 'verification.json')).write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps({k:v for k,v in summary.items() if k!='clips'}),flush=True)

if __name__=='__main__':main()

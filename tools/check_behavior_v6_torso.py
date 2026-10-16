"""Secondary stable-torso scan; keeps the full-frame monitor findings intact."""
import json
from pathlib import Path
import cv2
import numpy as np
from PIL import Image
from check_animation_continuity import compare

ROOT=Path(__file__).resolve().parents[1];BASE=ROOT/'artifacts/behavior-v6-integration'
# Source-canvas anatomical windows, excluding the moving head, paw and tail.
# Rolling and airborne bodies do not have a stationary torso window; never
# interpret a low-confidence affine fit there as a verified size change.
ROI={140:(330,500,430,565),144:(345,500,405,565),141:(305,465,445,525),142:(340,465,470,525),145:(305,465,445,525),146:(340,465,470,525),149:(305,465,445,525),150:(340,465,470,525)}

def main():
    data=json.loads((BASE/'frames/frames.json').read_text(encoding='utf-8'));records=[];detector=cv2.SIFT_create()
    for n,roi in ROI.items():
        sequence=[f for f in data['Frames'] if f['Clip']==f'video-{n}'];prior=None
        for f in sequence:
            a=np.asarray(Image.open(BASE/'frames'/f['File']).convert('RGBA'));d=f['Definition']
            x0,x1=[round((data['RootX']+(x/768-d['AnchorX'])*d['Width'])*2) for x in [roi[0],roi[2]]]
            y0,y1=[round((data['RootY']+(y/768-d['AnchorY'])*d['Height'])*2) for y in [roi[1],roi[3]]]
            mask=np.zeros(a.shape[:2],dtype='uint8');mask[y0:y1,x0:x1]=(a[y0:y1,x0:x1,3]>200).astype('uint8')*255
            k,des=detector.detectAndCompute(cv2.cvtColor(a[:,:,:3],cv2.COLOR_RGB2GRAY),mask)
            current=(k,des,[x0,y0,x1,y1])
            if prior is not None:records.append(dict(source=previous,target=f['File'],**compare(prior,current,2)))
            prior=current;previous=f['File']
    result=dict(pairs=len(records),flagged=sum(bool(r['failures']) for r in records),lowConfidence=sum(not r['reliable'] for r in records),excluded=[143,147,148],note='Secondary torso windows only. Original monitor retained; rotating/rolling/airborne anatomy and remaining flags need visual review.',comparisons=records)
    (BASE/'torso-review.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print({k:v for k,v in result.items() if k!='comparisons'})

if __name__=='__main__':main()

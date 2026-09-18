"""Estimate screen-space stance speed; this is a calibration aid, not anatomical QA."""
from pathlib import Path
import cv2,numpy as np,json
root=Path(__file__).resolve().parents[1]
results={}
for clip,sgn in [('video-right',-1),('video-14',1)]:
    fs=sorted((root/'assets/pets/bluecat/video-smooth-v1'/clip).glob('*.png'));values=[]
    for a,b in zip(fs,fs[1:]):
        x=cv2.imread(str(a),-1);y=cv2.imread(str(b),-1)
        g=cv2.cvtColor(x[:,:,:3],cv2.COLOR_BGR2GRAY);h=cv2.cvtColor(y[:,:,:3],cv2.COLOR_BGR2GRAY)
        flow=cv2.calcOpticalFlowFarneback(g,h,None,.5,3,15,3,5,1.2,0)
        mask=x[:,:,3]>240;mask[:220]=False;mask[241:]=False
        mask &= abs(flow[:,:,1])<.5
        v=flow[:,:,0][mask]*sgn;v=v[(v>.12)&(v<3)]
        if len(v)>5:values.append(float(np.median(v)*24*180/256))
    results[clip]={'medianStanceSpeed':float(np.median(values)),'samples':len(values),'quartiles':np.percentile(values,[25,75]).tolist()}
(root/'artifacts/smooth-qa/gait-speed.json').write_text(json.dumps(results,indent=2))
print(json.dumps(results))

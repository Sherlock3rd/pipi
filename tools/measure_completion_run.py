"""Measure planted-foot optical flow in the supplied run cycles at native timing."""
from pathlib import Path
import json,cv2,numpy as np
ROOT=Path(__file__).resolve().parents[1];A=ROOT/'assets/pets/bluecat'
cv2.setNumThreads(1)
m=json.loads((A/'manifest.json').read_text(encoding='utf-8'));results={}
for n,sign in [(109,-1),(112,1)]:
    files=m['animations']['video-'+str(n)];values=[]
    previous=cv2.imread(str(A/files[0]),-1)
    for file in files[1:]:
        nxt=cv2.imread(str(A/file),-1);a=previous;b=nxt
        flow=cv2.calcOpticalFlowFarneback(cv2.cvtColor(a[:,:,:3],cv2.COLOR_BGR2GRAY),cv2.cvtColor(b[:,:,:3],cv2.COLOR_BGR2GRAY),None,.5,3,15,3,5,1.2,0)
        region=np.zeros(a.shape[:2],bool);region[575:625,210:600]=True
        mask=(a[:,:,3]>240)&(np.abs(flow[:,:,1])<.75)&region;v=flow[:,:,0][mask]*sign;v=v[(v>.2)&(v<14)]
        if len(v)>10:values.append(float(np.median(v)*24*.5625))
        previous=nxt
    results[str(n)]={'medianStanceSpeed':float(np.median(values)),'quartiles':np.percentile(values,[25,75]).tolist(),'samples':len(values),'method':'lower-paw ROI, horizontal ground-phase flow; excludes vertical swing >0.75px/frame'}
(ROOT/'docs/qa/completion-v5/run-speed.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
print(json.dumps(results))

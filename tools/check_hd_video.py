"""Audit all native-resolution runtime cutouts for disconnected alpha/white specks."""
from pathlib import Path
import json,cv2,numpy as np
from PIL import Image
cv2.setNumThreads(1)
ROOT=Path(__file__).resolve().parents[1]
base=ROOT/'assets/pets/bluecat'
m=json.loads((base/'manifest.json').read_text(encoding='utf-8'))
assert m['videoMattePrepared'] and m['videoFrameSize']==512
count=0;white=0;max_white=0;crop_bytes=0;source_bytes=0
for clip,paths in m['animations'].items():
    if not clip.startswith('video-'):continue
    for path in paths:
        p=base/path;arr=np.array(Image.open(p));a=arr[:,:,3]
        assert arr.shape==(512,512,4),(clip,p)
        n,labels,stats,_=cv2.connectedComponentsWithStats((a>4).astype(np.uint8),8)
        assert n==2,(clip,p,'disconnected islands',n)
        distance=cv2.distanceTransform((a>127).astype(np.uint8),cv2.DIST_L2,5)
        bright=int(((arr[:,:,:3].min(axis=2)>220)&(a>25)&(distance<3)).sum())
        white+=bright;max_white=max(max_white,bright)
        assert bright==0,(clip,p,'white matte pixels',bright)
        ys,xs=np.where(a>1);crop_bytes+=(xs.max()-xs.min()+1)*(ys.max()-ys.min()+1)*4
        source_bytes+=p.stat().st_size;count+=1
data={'frames':count,'canvas':512,'whiteEdgePixels':white,'maxWhiteEdgePixelsPerFrame':max_white,'detachedAlphaComponents':0,'estimatedCroppedPixelCacheMiB':round(float(crop_bytes)/1024**2,1),'pngMiB':round(source_bytes/1024**2,1)}
out=ROOT/'artifacts/hd-qa';out.mkdir(exist_ok=True,parents=True)
(out/'audit.json').write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
print(json.dumps(data),flush=True)

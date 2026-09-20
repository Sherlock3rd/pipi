"""Remove pale floor-shadow patches at the new clip silhouettes; retain small eye glints."""
from pathlib import Path
import cv2,numpy as np,json,concurrent.futures
from PIL import Image
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'assets/pets/bluecat/video-completion-v5'
def clean(a):
    low=a[:,:,:3].min(2);mask=a[:,:,3]>4
    distance=cv2.distanceTransform(mask.astype('uint8'),cv2.DIST_L2,5)
    pale=(low>185)&(a[:,:,:3].max(2)>210)&mask
    count,labels,stats,_=cv2.connectedComponentsWithStats(pale.astype('uint8'),8)
    near=np.bincount(labels[(distance<5)&pale],minlength=count)
    selected=(stats[:,cv2.CC_STAT_AREA]>=12)&(near>=4);selected[0]=False
    leak=selected[labels]
    # Floor patches are neutral pale regions touching the cutout boundary.
    a[leak]=0
    if leak.any():
        count,labels,stats,_=cv2.connectedComponentsWithStats((a[:,:,3]>4).astype('uint8'),8)
        if count>1:a[labels!=(1+np.argmax(stats[1:,cv2.CC_STAT_AREA]))]=0
    return a,int(leak.sum())
if __name__=='__main__':
    cv2.setNumThreads(1);rows=[]
    def process(path):
        im=Image.open(path);box=im.getchannel('A').getbbox();a=np.array(im);x0,y0,x1,y1=box
        _,count=clean(a[max(0,y0-8):min(a.shape[0],y1+8),max(0,x0-8):min(a.shape[1],x1+8)])
        if count:Image.fromarray(a).save(path);return {'file':str(path.relative_to(OUT)),'removed':count}
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        rows=[r for r in pool.map(process,OUT.glob('video-*/*.png')) if r]
    report=ROOT/'docs/qa/completion-v5/matte.json';report.parent.mkdir(parents=True,exist_ok=True)
    report.write_text(json.dumps({'affectedFrames':len(rows),'details':rows},indent=2)+'\n',encoding='utf-8')
    print('floor patch repair',len(rows),'frames',flush=True)

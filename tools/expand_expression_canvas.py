"""Expand transparent storage, never cat scale; recover clipped pixels from video."""
import json,concurrent.futures
import numpy as np,cv2
from PIL import Image,ImageOps
from rebuild_hd_video import extract,ROOT,ASSETS
RUN=ROOT/'art/video-pipeline/expressions-v4/returned';CACHE=ROOT/'artifacts/expression-video-masks';OUT=ASSETS/'video-expressions-v4'
def expand(meta):
    cv2.setNumThreads(1);n=str(meta['id']);clipped=[]
    for i in range(meta['frames']):
        p=OUT/f'video-{n}/{i:03}.png';im=Image.open(p).convert('RGBA')
        if im.size==(640,640):continue
        a=np.asarray(im)[:,:,3];box=im.getchannel('A').getbbox()
        if min(box[:2])==0 or max(box[2:])==512:clipped.append(i)
        else:ImageOps.expand(im,border=64,fill=0).save(p)
    if clipped:
        extract({'clip':'video-'+n,'sourceFrames':[0,meta['frames']-1],'fps':meta['fps'],'duration':meta['duration'],'canvasOffsetY':-48 if int(n)<=57 else 0,'canvasSize':640,'indices':clipped},False,RUN,CACHE,OUT)
    print(n,'recovered',len(clipped),flush=True)
    return {'id':n,'recoveredFrames':clipped,'canvasSize':640,'physicalPixelScale':288/512}
if __name__=='__main__':
    actions=json.loads((RUN/'sources.json').read_text(encoding='utf-8'))
    with concurrent.futures.ProcessPoolExecutor(max_workers=3) as pool:report=list(pool.map(expand,actions))
    p=ASSETS/'manifest.json';m=json.loads(p.read_text(encoding='utf-8'))
    for a in actions:
        n=a['id'];d=m['clips'][f'video-{n}'];d.update(width=360,height=360,anchorX=.5,anchorY=(472+64+(-48 if n<=57 else 0))/640,frameSize=640,scaleStart=1,scaleEnd=1,offsetStartX=0,offsetEndX=0,offsetStartY=0,offsetEndY=0)
        d.pop('groundContacts',None)
    p.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    out=ROOT/'docs/qa/expressions-v4';out.mkdir(parents=True,exist_ok=True)
    (out/'canvas-recovery.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

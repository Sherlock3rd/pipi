"""Install exactly the supplied 22-51 videos, preserving source bytes and native timing."""
from pathlib import Path
import json,hashlib,shutil,concurrent.futures
import cv2,numpy as np
from PIL import Image
from rebuild_hd_video import extract,largest,ROOT,ASSETS
RUN=ROOT/'art/video-pipeline/care-v3/returned'
CACHE=ROOT/'artifacts/care-video-masks'
OUT=ASSETS/'video-care-v3'

def prepare(action):
    cv2.setNumThreads(1)
    n=action['folder'][:2];sources=list((Path.home()/'Downloads').glob(n+'-*.mp4'))
    assert len(sources)==1,(n,sources)
    source=sources[0];dest=RUN/n;dest.mkdir(parents=True,exist_ok=True)
    digest=hashlib.sha256(source.read_bytes()).hexdigest()
    if not (dest/'source.mp4').exists():shutil.copy2(source,dest/'source.mp4')
    assert hashlib.sha256((dest/'source.mp4').read_bytes()).hexdigest()==digest
    cap=cv2.VideoCapture(str(dest/'source.mp4'));fps=cap.get(cv2.CAP_PROP_FPS)
    count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));width=int(cap.get(3));height=int(cap.get(4))
    maskdir=CACHE/n;maskdir.mkdir(parents=True,exist_ok=True)
    for i in range(count):
        ok,bgr=cap.read();assert ok,(n,i)
        small=cv2.resize(bgr,(640,360),interpolation=cv2.INTER_AREA)
        body=largest(np.min(small,axis=2)<205).astype(np.uint8)
        contours,_=cv2.findContours(body,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        body[:]=0;cv2.drawContours(body,contours,-1,255,cv2.FILLED)
        body=cv2.erode(body,np.ones((3,3),np.uint8))
        Image.fromarray(np.dstack([small[:,:,::-1],body])).save(maskdir/f'{i:03}.png')
        if i in [0,count//2,count-1]:Image.fromarray(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB)).save(dest/f'sample-{i:03}.jpg',quality=92)
    cap.release()
    meta={**action,'originalName':source.name,'sha256':digest,'frames':count,'fps':fps,'width':width,'height':height,'duration':count/fps,'authorization':'用户要求按提供素材直接替换，表现问题后续修订'}
    (dest/'source.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    result=extract({'clip':'video-'+n,'sourceFrames':[0,count-1],'fps':fps,'duration':count/fps,'canvasOffsetY':-48 if n=='33' else 0},False,RUN,CACHE,OUT)
    return meta,result

if __name__=='__main__':
    actions=json.loads((RUN.parent/'manifest.json').read_text(encoding='utf-8'))['actions']
    results=[]
    with concurrent.futures.ProcessPoolExecutor(max_workers=2) as pool:
        for meta,result in pool.map(prepare,actions):results.append((meta,result));print(meta['originalName'],meta['frames'],meta['fps'],flush=True)
    manifest=json.loads((ASSETS/'manifest.json').read_text(encoding='utf-8'))
    for meta,result in results:
        clip=result['clip'];manifest['animations'][clip]=result['files']
        manifest['clips'][clip]={'fps':meta['fps'],'loop':meta['type']=='循环','width':288,'height':288,'anchorX':.5,'anchorY':(472+result.get('canvasOffsetY',0))/512,'mirrorWithFacing':False}
        manifest['reviewClips']=[c for c in manifest['reviewClips'] if c['id']!=clip]
        manifest['reviewClips'].append({'id':clip,'label':meta['originalName'][:-4]+' · 本次原样接入','source':'../../../../art/video-pipeline/care-v3/returned/'+clip[-2:]+'/source.mp4'})
    manifest['careVideoGraph']=True;manifest['runtimeRevision']='hd-v2-with-care-v3-as-provided'
    manifest['videoMatteRevision']=3
    (ASSETS/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (RUN/'sources.json').write_text(json.dumps([m for m,r in results],ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (OUT/'processing.json').write_text(json.dumps([r for m,r in results],indent=2)+'\n',encoding='utf-8')

"""Ingest the 47 supplied v4 videos with native frames and the established matte pipeline."""
from pathlib import Path
import json,concurrent.futures,argparse
import cv2,numpy as np
from PIL import Image
from rebuild_hd_video import extract,largest,ROOT,ASSETS
RUN=ROOT/'art/video-pipeline/expressions-v4/returned'
CACHE=ROOT/'artifacts/expression-video-masks'
OUT=ASSETS/'video-expressions-v4'
def prepare(meta):
    cv2.setNumThreads(1);n=str(meta['id']);maskdir=CACHE/n;maskdir.mkdir(parents=True,exist_ok=True)
    cap=cv2.VideoCapture(str(RUN/n/'source.mp4'))
    for i in range(meta['frames']):
        ok,bgr=cap.read();assert ok,(n,i)
        small=cv2.resize(bgr,(640,360),interpolation=cv2.INTER_AREA)
        body=largest(np.min(small,axis=2)<205).astype(np.uint8)
        contours,_=cv2.findContours(body,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        body[:]=0;cv2.drawContours(body,contours,-1,255,cv2.FILLED)
        body=cv2.erode(body,np.ones((3,3),np.uint8))
        Image.fromarray(np.dstack([small[:,:,::-1],body])).save(maskdir/f'{i:03}.png')
    cap.release()
    result=extract({'clip':'video-'+n,'sourceFrames':[0,meta['frames']-1],'fps':meta['fps'],'duration':meta['duration'],'canvasOffsetY':-48 if int(n)<=57 else 0,'canvasSize':640},False,RUN,CACHE,OUT)
    return meta,result
if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--clips');args=ap.parse_args()
    actions=json.loads((RUN/'sources.json').read_text(encoding='utf-8'));results=[]
    if args.clips:actions=[a for a in actions if str(a['id']) in args.clips.split(',')]
    with concurrent.futures.ProcessPoolExecutor(max_workers=3) as pool:
        for meta,result in pool.map(prepare,actions):results.append((meta,result));print(meta['id'],len(result['files']),flush=True)
    path=ASSETS/'manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
    for meta,result in results:
        clip=result['clip'];manifest['animations'][clip]=result['files']
        manifest['clips'][clip]={'fps':meta['fps'],'loop':meta['type']=='循环','width':360,'height':360,'frameSize':640,'anchorX':.5,'anchorY':(472+64+result.get('canvasOffsetY',0))/640,'mirrorWithFacing':False,'scaleStart':1,'scaleEnd':1,'offsetStartX':0,'offsetStartY':0,'offsetEndX':0,'offsetEndY':0}
        manifest['reviewClips']=[c for c in manifest['reviewClips'] if c['id']!=clip]
        manifest['reviewClips'].append({'id':clip,'label':meta['originalName'][:-4],'source':'../../../../art/video-pipeline/expressions-v4/returned/'+str(meta['id'])+'/source.mp4'})
    manifest['expressionVideoGraph']=True
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    previous=json.loads((OUT/'processing.json').read_text()) if args.clips and (OUT/'processing.json').exists() else []
    previous=[r for r in previous if r['clip'] not in {v['clip'] for _,v in results}]
    (OUT/'processing.json').write_text(json.dumps(previous+[r for m,r in results],indent=2)+'\n',encoding='utf-8')

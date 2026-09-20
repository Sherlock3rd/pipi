"""Archive and ingest the user-returned 099–139 clips at their native timing."""
from pathlib import Path
import json, hashlib, shutil, argparse, concurrent.futures
import cv2, numpy as np
from PIL import Image, ImageDraw
from rebuild_hd_video import extract, largest, ROOT, ASSETS
RUN=ROOT/'art/video-pipeline/completion-v5/returned'
CACHE=ROOT/'artifacts/completion-video-masks'
OUT=ASSETS/'video-completion-v5'
QA=ROOT/'artifacts/completion-v5-integration'

def inventory():
    RUN.mkdir(parents=True,exist_ok=True);QA.mkdir(parents=True,exist_ok=True)
    actions=json.loads((RUN.parent/'manifest.json').read_text(encoding='utf-8'))['clips']; rows=[];tiles=[]
    for a in actions:
        n=f"{a['id']:03}"; sources=list((Path.home()/'Downloads').glob(n+'-*.mp4'))
        assert len(sources)==1,(n,sources)
        src=sources[0]; dest=RUN/n;dest.mkdir(exist_ok=True);target=dest/'source.mp4'
        digest=hashlib.sha256(src.read_bytes()).hexdigest()
        if not target.exists():shutil.copy2(src,target)
        assert hashlib.sha256(target.read_bytes()).hexdigest()==digest
        cap=cv2.VideoCapture(str(target));count=int(cap.get(7));fps=cap.get(5)
        row={**a,'originalName':src.name,'sha256':digest,'frames':count,'fps':fps,'width':int(cap.get(3)),'height':int(cap.get(4)),'duration':count/fps}
        tile=Image.new('RGB',(960,204),'#dddddd');d=ImageDraw.Draw(tile);d.text((5,3),f"{n} {a['start']} -> {a['end']} | {count}f / {fps:g}fps",fill='black')
        for j,i in enumerate([0,count//2,count-1]):
            cap.set(cv2.CAP_PROP_POS_FRAMES,i);ok,bgr=cap.read();assert ok
            im=Image.fromarray(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB));im.thumbnail((320,180));tile.paste(im,(j*320,24))
        cap.release();rows.append(row);tiles.append(tile)
    (RUN/'sources.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for i in range(0,len(tiles),8):
        sheet=Image.new('RGB',(960,204*len(tiles[i:i+8])),'white')
        for j,t in enumerate(tiles[i:i+8]):sheet.paste(t,(0,j*204))
        sheet.save(QA/f'sources-{i//8}.jpg',quality=92)
    print(json.dumps([{'id':r['id'],'frames':r['frames'],'fps':r['fps'],'duration':r['duration']} for r in rows]),flush=True)
    return rows

def prepare(meta):
    cv2.setNumThreads(1);n=f"{meta['id']:03}";maskdir=CACHE/n;maskdir.mkdir(parents=True,exist_ok=True)
    cap=cv2.VideoCapture(str(RUN/n/'source.mp4'))
    for i in range(meta['frames']):
        ok,bgr=cap.read();assert ok,(n,i)
        small=cv2.resize(bgr,(640,360),interpolation=cv2.INTER_AREA)
        body=largest(np.min(small,axis=2)<205).astype(np.uint8)
        contours,_=cv2.findContours(body,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        body[:]=0;cv2.drawContours(body,contours,-1,255,cv2.FILLED);body=cv2.erode(body,np.ones((3,3),np.uint8))
        Image.fromarray(np.dstack([small[:,:,::-1],body])).save(maskdir/f'{i:03}.png')
    cap.release()
    result=extract({'clip':'video-'+str(meta['id']),'sourceNumber':n,'sourceFrames':[0,meta['frames']-1],'fps':meta['fps'],'duration':meta['duration'],'canvasSize':768},False,RUN,CACHE,OUT)
    return meta,result

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--inventory-only',action='store_true');ap.add_argument('--clips');args=ap.parse_args()
    rows=inventory()
    if args.inventory_only:raise SystemExit()
    if args.clips:rows=[r for r in rows if str(r['id']) in args.clips.split(',')]
    results=[]
    with concurrent.futures.ProcessPoolExecutor(max_workers=3) as pool:
        for meta,result in pool.map(prepare,rows):results.append((meta,result));print(meta['id'],len(result['files']),flush=True)
    path=ASSETS/'manifest.json';m=json.loads(path.read_text(encoding='utf-8'))
    for meta,r in results:
        clip=r['clip'];m['animations'][clip]=r['files']
        m['clips'][clip]={'fps':meta['fps'],'loop':meta['type']=='循环','width':432,'height':432,'frameSize':768,'anchorX':.5,'anchorY':600/768,'mirrorWithFacing':False,'scaleStart':1,'scaleEnd':1,'offsetStartX':0,'offsetStartY':0,'offsetEndX':0,'offsetEndY':0}
        m['reviewClips']=[c for c in m['reviewClips'] if c['id']!=clip]
        m['reviewClips'].append({'id':clip,'label':str(meta['id'])+' '+meta['name'],'source':'../../../../art/video-pipeline/completion-v5/returned/'+f"{meta['id']:03}"+'/source.mp4'})
    m['completionVideoGraph']=True
    path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (OUT/'processing.json').write_text(json.dumps([r for _,r in results],indent=2)+'\n',encoding='utf-8')

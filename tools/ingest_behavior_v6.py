"""Archive the explicitly returned v6 clips and extract native-timed RGBA frames."""
from pathlib import Path
import argparse, concurrent.futures, hashlib, json, shutil
import cv2, numpy as np
from PIL import Image, ImageDraw
from rebuild_hd_video import extract, largest, ROOT, ASSETS

RUN=ROOT/'art/video-pipeline/behavior-v6/returned'
CACHE=ROOT/'artifacts/behavior-v6-masks'
OUT=ASSETS/'video-behavior-v6'
QA=ROOT/'artifacts/behavior-v6-integration'

def inventory(ids,replace=False):
    RUN.mkdir(parents=True,exist_ok=True);QA.mkdir(parents=True,exist_ok=True)
    actions=json.loads((RUN.parent/'manifest.json').read_text(encoding='utf-8'))['clips'];rows=[]
    for a in actions:
        n=str(a['id'])
        if a['id'] not in ids:continue
        sources=list((Path.home()/'Downloads').glob(n+'-*.mp4'));assert len(sources)==1,(n,sources)
        src=sources[0];dest=RUN/n;dest.mkdir(exist_ok=True);target=dest/'source.mp4'
        digest=hashlib.sha256(src.read_bytes()).hexdigest()
        if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest()!=digest:
            assert replace, f'{n}: source changed; explicit --replace required'
            old_digest=hashlib.sha256(target.read_bytes()).hexdigest()
            archive=RUN/'superseded'/n/old_digest;archive.mkdir(parents=True,exist_ok=True)
            shutil.copy2(target,archive/'source.mp4')
            if (dest/'source.json').exists():shutil.copy2(dest/'source.json',archive/'source.json')
            assert hashlib.sha256((archive/'source.mp4').read_bytes()).hexdigest()==old_digest
            shutil.copy2(src,target)
        if not target.exists():shutil.copy2(src,target)
        assert hashlib.sha256(target.read_bytes()).hexdigest()==digest
        cap=cv2.VideoCapture(str(target));count=int(cap.get(7));fps=cap.get(5)
        assert count>1 and fps>0,n
        row={**a,'originalName':src.name,'sha256':digest,'frames':count,'fps':fps,'width':int(cap.get(3)),'height':int(cap.get(4)),'duration':count/fps}
        tile=Image.new('RGB',(1280,3*204),'#dddddd');d=ImageDraw.Draw(tile)
        for j,i in enumerate(np.linspace(0,count-1,12).astype(int)):
            cap.set(cv2.CAP_PROP_POS_FRAMES,int(i));ok,bgr=cap.read();assert ok
            im=Image.fromarray(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB));im.thumbnail((320,180))
            x=j%4*320;y=j//4*204;tile.paste(im,(x,y+24));d.text((x+5,y+3),f'{n} / {i} / {count}f @ {fps:g}fps',fill='black')
        cap.release();tile.save(QA/f'source-{n}.jpg',quality=93);rows.append(row)
        (dest/'source.json').write_text(json.dumps(row,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    previous=json.loads((RUN/'sources.json').read_text(encoding='utf-8')) if (RUN/'sources.json').exists() else []
    merged={r['id']:r for r in previous};merged.update({r['id']:r for r in rows})
    (RUN/'sources.json').write_text(json.dumps(sorted(merged.values(),key=lambda r:r['id']),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps([{k:r[k] for k in ['id','frames','fps','width','height','duration']} for r in rows]),flush=True)
    return rows

def prepare(meta):
    cv2.setNumThreads(1);n=str(meta['id']);maskdir=CACHE/n;maskdir.mkdir(parents=True,exist_ok=True)
    cap=cv2.VideoCapture(str(RUN/n/'source.mp4'))
    for i in range(meta['frames']):
        ok,bgr=cap.read();assert ok,(n,i)
        small=cv2.resize(bgr,(640,360),interpolation=cv2.INTER_AREA)
        body=largest(np.min(small,axis=2)<205).astype(np.uint8)
        contours,_=cv2.findContours(body,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        body[:]=0;cv2.drawContours(body,contours,-1,255,cv2.FILLED);body=cv2.erode(body,np.ones((3,3),np.uint8))
        Image.fromarray(np.dstack([small[:,:,::-1],body])).save(maskdir/f'{i:03}.png')
    cap.release()
    result=extract(dict(clip='video-'+n,sourceNumber=n,sourceFrames=[0,meta['frames']-1],fps=meta['fps'],duration=meta['duration'],canvasSize=768),False,RUN,CACHE,OUT)
    return result

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--prepare',action='store_true');ap.add_argument('--replace',action='store_true');ap.add_argument('--ids',default=','.join(map(str,range(140,155))));args=ap.parse_args()
    rows=inventory(set(map(int,args.ids.split(','))),args.replace)
    if args.prepare:
        results=[]
        with concurrent.futures.ProcessPoolExecutor(max_workers=3) as pool:
            for result in pool.map(prepare,rows):results.append(result);print(result['clip'],len(result['files']),flush=True)
        previous=json.loads((OUT/'processing.json').read_text(encoding='utf-8')) if (OUT/'processing.json').exists() else []
        merged={r['clip']:r for r in previous};merged.update({r['clip']:r for r in results})
        (OUT/'processing.json').write_text(json.dumps(list(merged.values()),indent=2)+'\n',encoding='utf-8')

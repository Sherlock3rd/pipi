"""Keep native video frames for QA; never synthesize, reverse or force 30 frames.

All clips use one global crop/scale/root. Output is an isolated review package,
not production acceptance. Source video bytes stay in returned/NN/source.mp4.
"""
from pathlib import Path
import argparse,concurrent.futures,hashlib,json,cv2
import numpy as np
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[1]
RUN=ROOT/'art/video-pipeline/basic-v2/returned'
CACHE=ROOT/'artifacts/basic-video-masks'
OUT=RUN/'preview-assets'

def extract(number):
    cv2.setNumThreads(1)
    d=CACHE/number;d.mkdir(parents=True,exist_ok=True)
    info=json.loads((RUN/number/'source.json').read_text(encoding='utf-8'))
    cap=cv2.VideoCapture(str(RUN/number/'source.mp4')); bounds=[]; diffs=[];old=None
    i=0
    while True:
        ok,frame=cap.read()
        if not ok:break
        bgr=cv2.resize(frame,(640,360),interpolation=cv2.INTER_AREA)
        seed=(np.min(bgr,axis=2)<196).astype(np.uint8)
        n,labels,stats,_=cv2.connectedComponentsWithStats(seed)
        if n<2:raise ValueError((number,i,'empty foreground'))
        body=(labels==1+np.argmax(stats[1:,cv2.CC_STAT_AREA])).astype(np.uint8)
        probable=cv2.dilate(body,np.ones((7,7),np.uint8))
        sure=cv2.erode(body,np.ones((5,5),np.uint8))
        mask=np.full(body.shape,cv2.GC_BGD,np.uint8)
        mask[probable>0]=cv2.GC_PR_FGD;mask[sure>0]=cv2.GC_FGD
        mask[np.min(bgr,axis=2)>215]=cv2.GC_BGD
        cv2.setRNGSeed(0)
        cv2.grabCut(bgr,mask,None,np.zeros((1,65)),np.zeros((1,65)),2,cv2.GC_INIT_WITH_MASK)
        alpha=np.isin(mask,[cv2.GC_FGD,cv2.GC_PR_FGD]).astype(np.uint8)*255
        # Remove floor-shadow remnants that are not connected to the animal.
        n,labels,stats,_=cv2.connectedComponentsWithStats((alpha>0).astype(np.uint8))
        alpha[labels!=1+np.argmax(stats[1:,cv2.CC_STAT_AREA])]=0
        alpha=cv2.erode(alpha,np.ones((3,3),np.uint8))
        ys,xs=np.where(alpha>0);bounds.append([int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)])
        rgba=np.dstack([cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB),alpha]);rgba[alpha==0]=0
        Image.fromarray(rgba).save(d/f'{i:03}.png')
        small=cv2.resize(bgr,(160,90)).astype(float)
        diffs.append(None if old is None else float(np.mean((small-old)**2)));old=small;i+=1
    cap.release();assert i==info['frames'],(number,i,info['frames'])
    stats={'number':number,'count':i,'bounds':bounds,'successiveRgbMse':diffs}
    (d/'stats.json').write_text(json.dumps(stats),encoding='utf-8')
    return stats

def package(stats):
    # Keep the source reference's world root, rather than recentering each cat.
    all_bounds=[b for s in stats for b in s['bounds']]
    root_x=320;ground_y=750/941*360
    left=min(b[0] for b in all_bounds);top=min(b[1] for b in all_bounds)
    right=max(b[2] for b in all_bounds);bottom=max(b[3] for b in all_bounds)
    scale=min(116/max(root_x-left,right-root_x),220/(ground_y-top),.58)
    root_y=236
    assert root_y+(bottom-ground_y)*scale<254,(bottom,scale)
    OUT.mkdir(exist_ok=True)
    manifest={'name':'陈皮 · 21条视频独立预览','frameWidth':256,'frameHeight':256,'fps':24,
              'animations':{},'clips':{},'reviewClips':[], 'artStatus':'review-candidates-not-production'}
    report={'sourceFps':24,'framePolicy':'native frames retained for review; no interpolation or 30-frame compression',
            'globalScale':scale,'sourceRoot':[root_x,ground_y],'outputRoot':[128,root_y],
            'sourceUnion':[left,top,right,bottom],'clips':[]}
    for s in stats:
        number=s['number'];meta=json.loads((RUN/number/'source.json').read_text(encoding='utf-8'))
        clip='video-'+number;dest=OUT/clip;dest.mkdir(exist_ok=True)
        hashes=[];sheet=Image.new('RGB',(256*6,280*4),'#28323d');draw=ImageDraw.Draw(sheet)
        samples=set(np.linspace(0,s['count']-1,24).round().astype(int));sample_slot=0
        for i in range(s['count']):
            with Image.open(CACHE/number/f'{i:03}.png') as im:
                frame=im.transform((256,256),Image.Transform.AFFINE,
                     (1/scale,0,root_x-128/scale,0,1/scale,ground_y-root_y/scale),resample=Image.Resampling.BICUBIC)
            arr=np.array(frame);arr[arr[:,:,3]==0]=0;frame=Image.fromarray(arr)
            box=frame.getbbox();assert box and min(box[:2])>0 and max(box[2:])<256,(number,i,box)
            path=dest/f'{i:03}.png';frame.save(path);hashes.append(hashlib.sha256(path.read_bytes()).hexdigest())
            if i in samples:
                x=(sample_slot%6)*256;y=(sample_slot//6)*280
                sheet.paste(frame,(x,y),frame);draw.text((x+5,y+259),f'{number} / {i:03} / {i/24:.2f}s',fill='white');sample_slot+=1
        sheet.save(RUN/number/'transparent-contact.jpg',quality=93)
        manifest['animations'][clip]=[f'{clip}/{i:03}.png' for i in range(s['count'])]
        manifest['clips'][clip]={'fps':meta['fps'],'loop':meta['type']=='循环','width':180,'height':180,'anchorX':.5,'anchorY':236/256,'mirrorWithFacing':False}
        status='待修订' if number in ['03','05'] else '待动态评审'
        manifest['reviewClips'].append({'id':clip,'label':meta['folder']+' · '+status,'source':'../../'+number+'/source.mp4'})
        report['clips'].append({'id':clip,'count':s['count'],'uniqueFrames':len(set(hashes)),'sha256':hashes,'duration':s['count']/meta['fps'],'status':status})
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (RUN/'extraction.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('PACKAGED',len(stats),'clips',sum(s['count'] for s in stats),'native frames',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--reuse',action='store_true');args=parser.parse_args()
    numbers=[f'{i:02}' for i in range(1,22)]
    if args.reuse:stats=[json.loads((CACHE/n/'stats.json').read_text()) for n in numbers]
    else:
        stats=[]
        with concurrent.futures.ProcessPoolExecutor(max_workers=4) as pool:
            for s in pool.map(extract,numbers):stats.append(s);print('EXTRACTED',s['number'],s['count'],flush=True)
    package(sorted(stats,key=lambda s:s['number']))

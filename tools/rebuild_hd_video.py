"""Re-extract native video color; use old masks only as conservative body guides.

No synthesized artwork, temporal frame interpolation, or upscaling old 256px sprites.
Matte is estimated at native resolution, then premultiplied RGBA is resampled once.
"""
from pathlib import Path
import argparse, json, concurrent.futures
import cv2
import numpy as np
from PIL import Image
from refine_video_matte import refine_matte

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'assets/pets/bluecat'
RUN=ROOT/'art/video-pipeline/basic-v2/returned'
CACHE=ROOT/'artifacts/basic-video-masks'
OUT=ASSETS/'video-hd-v2'

def largest(mask):
    n,labels,stats,_=cv2.connectedComponentsWithStats(mask.astype(np.uint8),8)
    return labels==(1+np.argmax(stats[1:,cv2.CC_STAT_AREA])) if n>1 else mask>0

def final_coverage(result):
    # Quantize before connected-component cleanup: sub-byte alpha bridges must
    # not turn into detached points after PNG encoding.
    keep=largest(result[:,:,3]>4)
    result[~keep]=0
    inward=cv2.distanceTransform((result[:,:,3]>127).astype(np.uint8),cv2.DIST_L2,5)
    bright=(result[:,:,:3].min(axis=2)>215)&(result[:,:,3]>0)&(inward<3)
    if bright.any():
        core=(inward>=3)&(result[:,:,:3].min(axis=2)<200)
        _,labels=cv2.distanceTransformWithLabels((~core).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
        palette=np.zeros((labels.max()+1,3),np.uint8);palette[labels[core]]=result[:,:,:3][core]
        result[:,:,:3][bright]=palette[labels[bright]]
    return refine_matte(result)

def extract(clip,sample=False,run=RUN,cache=CACHE,out=OUT):
    cv2.setNumThreads(1)
    number='00' if clip['clip']=='video-right' else clip['clip'][-2:]
    cap=cv2.VideoCapture(str(run/number/'source.mp4'))
    report=json.loads((RUN/'extraction.json').read_text(encoding='utf-8'))
    start,end=clip['sourceFrames'];offset=62 if number=='00' else 0
    indices=list(range(start+offset,end+offset+1))
    if sample:indices=sorted(set([indices[0],indices[len(indices)//2],indices[-1]]))
    dest=out/clip['clip'];dest.mkdir(parents=True,exist_ok=True)
    files=[];diagnostics=[]
    cap.set(cv2.CAP_PROP_POS_FRAMES,indices[0]);next_index=indices[0]
    for index in indices:
        while next_index<index:cap.grab();next_index+=1
        ok,bgr=cap.read();next_index+=1
        assert ok,(number,index)
        rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
        h,w=rgb.shape[:2]
        old=np.array(Image.open(cache/number/f'{index:03}.png').getchannel('A'))
        core=cv2.resize(old,(w,h),interpolation=cv2.INTER_NEAREST)>127
        # Preserve eye/coat highlights: fill internal guide holes, not texture.
        contours,_=cv2.findContours(core.astype(np.uint8),cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        filled=np.zeros((h,w),np.uint8);cv2.drawContours(filled,contours,-1,1,cv2.FILLED)
        core=largest(filled)
        distance,labels=cv2.distanceTransformWithLabels((~core).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
        colors=np.zeros((labels.max()+1,3),np.float32);colors[labels[core]]=rgb[core]
        foreground=colors[labels]
        # Robust background sample away from the cat, independently per channel.
        bg=np.median(np.concatenate([rgb[:20].reshape(-1,3),rgb[-20:].reshape(-1,3),rgb[:,:20].reshape(-1,3),rgb[:,-20:].reshape(-1,3)]),axis=0)
        delta=bg-foreground
        alpha=np.clip(np.sum((bg-rgb)*delta,axis=2)/np.maximum(np.sum(delta*delta,axis=2),400),0,1)
        # The original guide was eroded at 640px. Recover a narrow native band,
        # but forbid background shadows / floating pixels far from the body.
        alpha*=np.clip((10-distance)/3,0,1)
        alpha[core]=1
        # Gate very low contrast background continuously, not a per-frame binary grabcut.
        outside=~core
        alpha[outside]*=np.clip((np.max(bg-rgb,axis=2)[outside]-3)/12,0,1)
        # Remove detached flecks. No blur or morphological shrinking of the coat.
        alpha[~largest(alpha>.025)]=0
        corrected=rgb.copy()
        edge=outside&(alpha>0)
        recovered=(rgb-bg*(1-alpha[:,:,None]))/np.maximum(alpha[:,:,None],.04)
        corrected[edge]=np.clip(recovered[edge],0,255)
        # Low-coverage edge colors are ill-conditioned: use adjacent actual fur.
        faint=edge&(alpha<.35)
        corrected[faint]=foreground[faint]
        # Native 1280x720 ->512 canvas, matching the old root/physical scale exactly.
        factor=w/640
        scale=report['globalScale']*2/factor
        rx,ry=np.array(report['sourceRoot'])*factor
        ox,oy=np.array(report['outputRoot'])*2
        oy+=clip.get('canvasOffsetY',0)
        matrix=np.array([[scale,0,ox-rx*scale],[0,scale,oy-ry*scale]],np.float32)
        pm=np.dstack([corrected*alpha[:,:,None]/255,alpha])
        scaled=cv2.warpAffine(pm,matrix,(512,512),flags=cv2.INTER_AREA,borderMode=cv2.BORDER_CONSTANT)
        a=np.clip(scaled[:,:,3],0,1)
        color=np.clip(scaled[:,:,:3]/np.maximum(a[:,:,None],1/255)*255,0,255)
        a[~largest(a>.015)]=0
        hard=a>.5
        inward=cv2.distanceTransform(hard.astype(np.uint8),cv2.DIST_L2,5)
        # Remove the few near-white matte sparkles at the silhouette only;
        # eye catchlights and opaque internal fur detail are untouched.
        inner=inward>=3
        _,near=cv2.distanceTransformWithLabels((~inner).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
        palette=np.zeros((near.max()+1,3),np.float32);palette[near[inner]]=color[inner]
        sparkles=(color.min(axis=2)>215)&(a>0)&(inward<3)
        color[sparkles]=palette[near[sparkles]]
        result=np.dstack([color,np.round(a*255)]).astype(np.uint8);result[a==0]=0
        result=final_coverage(result)
        outputIndex=index-offset-start
        path=dest/f'{outputIndex:03}.png';Image.fromarray(result).save(path)
        files.append(path.relative_to(ASSETS).as_posix())
        white=(result[:,:,:3].min(axis=2)>220)&(a>.1)&(inward<3)
        diagnostics.append({'frame':outputIndex,'whiteEdgePixels':int(white.sum()),'opaquePixels':int(hard.sum())})
    cap.release()
    return {**clip,'files':files,'diagnostics':diagnostics}

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--sample',action='store_true');ap.add_argument('--finalize-only',action='store_true');args=ap.parse_args()
    if args.finalize_only:
        count=0
        for path in OUT.glob('video-*/*.png'):
            original=np.array(Image.open(path));fixed=final_coverage(original.copy())
            if not np.array_equal(original,fixed):Image.fromarray(fixed).save(path);count+=1
        print('Finalized alpha quantization in',count,'frames',flush=True);return
    clips=json.loads((ASSETS/'video-smooth-v1/processing.json').read_text())['clips']
    if args.sample: clips=[c for c in clips if c['clip'] in ['video-01','video-14','video-20','video-21','video-right']]
    results=[]
    with concurrent.futures.ProcessPoolExecutor(max_workers=2) as pool:
        jobs=[pool.submit(extract,c,args.sample) for c in clips]
        for job in concurrent.futures.as_completed(jobs):
            r=job.result();results.append(r);print(r['clip'],len(r['files']),'frames',flush=True)
    if not args.sample:
        manifest=json.loads((ASSETS/'manifest.json').read_text(encoding='utf-8'))
        for r in results:manifest['animations'][r['clip']]=r['files']
        manifest['runtimeRevision']='hd-v2-native-matte-once'
        manifest['videoMattePrepared']=True;manifest['videoFrameSize']=512
        manifest['videoMatteRevision']=3
        (ASSETS/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (OUT/('samples.json' if args.sample else 'processing.json')).write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':main()

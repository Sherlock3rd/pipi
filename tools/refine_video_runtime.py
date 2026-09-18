"""Rebuild from preserved candidates: trim holds and decontaminate white matte edges."""
from pathlib import Path
import json,cv2,numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
A=ROOT/'assets/pets/bluecat'
SOURCE=ROOT/'art/video-pipeline/basic-v2/returned/preview-assets'
OUT=A/'video-smooth-v1';OUT.mkdir(exist_ok=True)
# Inclusive native frame ranges. Source video and review assets remain untouched.
cuts={6:(0,90,36),7:(0,103,30),8:(0,96,36),9:(0,103,30),10:(12,96,36),
      11:(8,86,36),12:(12,96,36),13:(8,86,36),14:(0,119,24),
      15:(6,105,36),16:(6,105,36),17:(4,112,30),18:(4,112,30),19:(0,112,30),21:(0,112,30)}
manifest=json.loads((A/'manifest.json').read_text(encoding='utf-8'))
report=[]
for name in ['video-'+f'{n:02}' for n in range(1,22)]+['video-right']:
    n=int(name[-2:]) if name!='video-right' else 0
    start,end,fps=cuts.get(n,(0,38 if n==0 else 120,24))
    source=SOURCE/name if n else A/'video-basic-v2/video-right'
    dest=OUT/name;dest.mkdir(exist_ok=True)
    files=[]
    for out_index,index in enumerate(range(start,end+1)):
        rgba=np.array(Image.open(source/f'{index:03}.png').convert('RGBA'))
        alpha=rgba[:,:,3].astype(np.float32)/255
        hard=(alpha>.5).astype(np.uint8)
        distance=cv2.distanceTransform(hard,cv2.DIST_L2,5)
        core=(distance>=2.2).astype(np.uint8)
        # Nearest interior color replaces contaminated white in the outer 2 pixels.
        _,labels=cv2.distanceTransformWithLabels(1-core,cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
        colors=np.zeros((labels.max()+1,3),np.float32)
        colors[labels[core>0]]=rgba[:,:,:3][core>0]
        interior=colors[labels]
        weight=np.clip((2.5-distance)/2,0,1)[:,:,None]
        rgb=rgba[:,:,:3].astype(np.float32)*(1-weight)+interior*weight
        # Subpixel coverage inward of the old hard cut; never blur white into the fur.
        coverage=cv2.GaussianBlur(hard.astype(np.float32),(3,3),.55)
        newalpha=np.minimum(alpha,coverage)*np.clip(distance,0,1)
        rgba[:,:,:3]=np.clip(rgb,0,255).astype(np.uint8)
        rgba[:,:,3]=np.round(newalpha*255).astype(np.uint8);rgba[rgba[:,:,3]==0]=0
        out=dest/f'{out_index:03}.png';Image.fromarray(rgba).save(out)
        files.append(out.relative_to(A).as_posix())
    manifest['animations'][name]=files;manifest['clips'][name]['fps']=fps
    report.append({'clip':name,'sourceFrames':[start,end],'fps':fps,'duration':len(files)/fps})
manifest['runtimeRevision']='smooth-v1-trimmed-decontaminated'
(A/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(OUT/'processing.json').write_text(json.dumps({'clips':report,'edgeMethod':'nearest-interior RGB decontamination + inward subpixel alpha coverage'},indent=2),encoding='utf-8')
print('Processed',sum(len(manifest['animations'][r['clip']]) for r in report),'frames',flush=True)

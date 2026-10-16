"""Register returned v6 endpoints against approved anatomy, retaining native motion."""
import json, argparse
import cv2
import numpy as np
from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]; A=ROOT/'assets/pets/bluecat'
OUT=ROOT/'artifacts/behavior-v6-integration'; OUT.mkdir(exist_ok=True,parents=True)
CANON={'F':('01',0),'SR':('22',0),'SL':('09',0),'B':('71',0)}

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--ids');args=ap.parse_args()
    cv2.setNumThreads(2)
    path=A/'manifest.json';m=json.loads(path.read_text(encoding='utf-8'))
    rows=json.loads((ROOT/'art/video-pipeline/behavior-v6/returned/sources.json').read_text(encoding='utf-8'))
    all_rows=rows
    if args.ids:rows=[r for r in rows if r['id'] in set(map(int,args.ids.split(',')))]
    detector=cv2.SIFT_create()
    def im(n,i):
        return np.asarray(Image.open(A/m['animations']['video-'+str(n)][i]).convert('RGBA'))
    for r in rows:
        n=r['id'];key=f'video-{n}';files=sorted((A/'video-behavior-v6'/key).glob('*.png'))
        assert len(files)==r['frames'],(n,len(files))
        m['animations'][key]=[p.relative_to(A).as_posix() for p in files]
        m['clips'][key]={'fps':r['fps'],'loop':False,'width':432,'height':432,'anchorX':.5,'anchorY':600/768,'mirrorWithFacing':False}
    def fit(n,i,pose):
        a=im(n,i);ref,ri=CANON[pose];b=im(ref,ri)
        ka,da=detector.detectAndCompute(cv2.cvtColor(a[:,:,:3],cv2.COLOR_RGB2GRAY),(a[:,:,3]>200).astype('uint8')*255)
        kb,db=detector.detectAndCompute(cv2.cvtColor(b[:,:,:3],cv2.COLOR_RGB2GRAY),(b[:,:,3]>200).astype('uint8')*255)
        matches=[x for x,y in cv2.BFMatcher().knnMatch(da,db,k=2) if x.distance<.75*y.distance]
        pa=np.float32([ka[x.queryIdx].pt for x in matches]);pb=np.float32([kb[x.trainIdx].pt for x in matches])
        _,keep=cv2.estimateAffinePartial2D(pa,pb,method=cv2.RANSAC,ransacReprojThreshold=2)
        pa,pb=pa[keep[:,0]==1],pb[keep[:,0]==1];ac,bc=pa-pa.mean(0),pb-pb.mean(0)
        scale=float((ac*bc).sum()/(ac*ac).sum());tx,ty=(pb.mean(0)-scale*pa.mean(0)).tolist()
        residual=float(np.linalg.norm(pa*scale+[tx,ty]-pb,axis=1).mean())
        rd=m['clips']['video-'+ref];rs=rd.get('scaleStart',1);size=b.shape[0]
        offset=((384*scale+tx)-rd['anchorX']*size)*rd['width']/size*rs+rd.get('offsetStartX',0)
        support=(rd.get('groundContacts',[rd['anchorY']])[ri]*size-ty)/scale
        return dict(accepted=len(pa)>=18 and .9<=scale*rs<=1.1 and abs(offset)<20 and residual<3,scale=scale*rs,offsetX=offset,support=support,inliers=len(pa),residual=residual)
    records=[];clipped=[]
    for r in rows:
        n=r['id'];d=m['clips'][f'video-{n}'];count=r['frames'];ends=[fit(n,0,r['start']),fit(n,-1,r['end'])]
        assert all(f['accepted'] for f in ends),(n,ends)
        for suffix,f in zip(['Start','End'],ends):d['scale'+suffix]=f['scale'];d['offset'+suffix+'X']=f['offsetX']
        # Both ends share a physical support plane. A fixed registered root
        # preserves arching, belly rolling and jumps, excluding animated tails.
        phase=np.linspace(0,1,count);ease=phase*phase*(3-2*phase)
        contacts=ends[0]['support']+(ends[1]['support']-ends[0]['support'])*ease
        support_method='registered fixed root; original airborne and body motion preserved'
        if n in (153,154):
            # These are grounded rears, not jumps. Track only the supporting
            # rear paws; the tail and elevated forepaws cannot move the root.
            lo,hi=(280,430) if n==153 else (400,540)
            feet=np.array([np.where(im(n,i)[540:710,lo:hi,3]>180)[0].max()+540 for i in range(count)],dtype=float)
            baseline=feet[0]+(feet[-1]-feet[0])*ease
            contacts+=feet-baseline
            support_method='rear-paw contact ROI; vertical root only, no body scaling or speed changes'
        d['groundContacts']=(contacts/768).tolist()
        sheet=Image.new('RGB',(1280,3*344),'#c5bab0');draw=ImageDraw.Draw(sheet)
        sample=list(np.linspace(0,count-1,12).astype(int))
        for i in range(count):
            a=im(n,i);hard=a[:,:,3]>128
            if hard[0].any() or hard[-1].any() or hard[:,0].any() or hard[:,-1].any():clipped.append([n,i])
            if i in sample:
                j=sample.index(i);x=j%4*320;y=j//4*344
                tile=Image.fromarray(a).resize((320,320));sheet.paste(tile,(x,y+24),tile)
                draw.text((x+4,y+4),f'{n} / {i}',fill='black')
                gy=y+24+contacts[i]*320/768;draw.line((x,gy,x+320,gy),fill='red')
        sheet.save(OUT/f'rgba-{n}.jpg',quality=94)
        records.append(dict(id=n,frames=count,fps=r['fps'],fits=ends,supportMethod=support_method))
    assert not clipped,clipped
    m['behaviorV6Graph']={'clips':[{k:r[k] for k in ['id','start','end']} for r in all_rows],'pending':[n for n in range(140,155) if n not in {r['id'] for r in all_rows}]}
    path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    previous=json.loads((OUT/'calibration.json').read_text(encoding='utf-8')) if (OUT/'calibration.json').exists() else {'clips':[]}
    merged={r['id']:r for r in previous['clips']};merged.update({r['id']:r for r in records})
    (OUT/'calibration.json').write_text(json.dumps(dict(clips=sorted(merged.values(),key=lambda r:r['id']),clippedFrames=clipped),indent=2)+'\n',encoding='utf-8')
    print(json.dumps(records,indent=2))

if __name__=='__main__':main()

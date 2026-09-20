"""Shared-pose registration and semantic ground-contact calibration for v4.

No image normalization: keep native art, fit corresponding fur features at shared
poses, and move only the support anchor using planted feet/body contact regions.
"""
from pathlib import Path
import json
import cv2,numpy as np
cv2.setNumThreads(2)
from PIL import Image
from expression_support_keys import SUPPORT_KEYS, reviewed_support
ROOT=Path(__file__).resolve().parents[1];ASSETS=ROOT/'assets/pets/bluecat'
CANON={'F':('01',0),'SR':('22',0),'SL':('09',0),'WR':('right',0),'WL':('14',0),
       'HR':('53',0),'HL':('56',0),'D':('66',0),'A':('67',0),'B':('71',0),'C':('75',0),'M':('84',0)}
# Reviewed support ROIs in the common 512px canvas: exclude tail and lifted paws.
ROI={'F':(225,310),'SR':(340,445),'SL':(115,205),'WR':(140,420),'WL':(110,365),
     'HR':(220,280),'HL':(225,305),'D':(315,440),'A':(160,290),'B':(170,350),'C':(300,410),'M':(210,350)}
def main():
    path=ASSETS/'manifest.json';m=json.loads(path.read_text(encoding='utf-8'))
    actions=json.loads((ROOT/'art/video-pipeline/expressions-v4/returned/sources.json').read_text(encoding='utf-8'))
    detector=cv2.SIFT_create();cache={}
    def im(n,i):return np.asarray(Image.open(ASSETS/m['animations']['video-'+n][i]).convert('RGBA'))
    def feature(n,i):
        if (n,i) not in cache:
            a=im(n,i);cache[n,i]=detector.detectAndCompute(cv2.cvtColor(a[:,:,:3],cv2.COLOR_RGB2GRAY),(a[:,:,3]>200).astype('uint8')*255)
        return cache[n,i]
    def fit(n,i,pose):
        ref,j=CANON[pose];ka,da=feature(n,i);kb,db=feature(ref,j)
        pairs=cv2.BFMatcher().knnMatch(da,db,k=2) if da is not None and db is not None else []
        matches=[a for pair in pairs if len(pair)==2 for a,b in [pair] if a.distance<.75*b.distance]
        if len(matches)<8:return {'accepted':False,'inliers':len(matches)}
        a=np.float32([ka[p.queryIdx].pt for p in matches]);b=np.float32([kb[p.trainIdx].pt for p in matches])
        matrix,keep=cv2.estimateAffinePartial2D(a,b,method=cv2.RANSAC,ransacReprojThreshold=2)
        if matrix is None:return {'accepted':False,'inliers':0}
        a,b=a[keep[:,0]==1],b[keep[:,0]==1];ac,bc=a-a.mean(0),b-b.mean(0)
        scale=float((ac*bc).sum()/(ac*ac).sum());translation=b.mean(0)-scale*a.mean(0)
        residual=float(np.linalg.norm(a*scale+translation-b,axis=1).mean())
        rd=m['clips']['video-'+ref];rs=rd.get('scaleStart',1)
        ownSize=im(n,i).shape[0];refSize=im(ref,j).shape[0]
        offsetX=float(((ownSize*.5*scale+translation[0])-rd['anchorX']*refSize)*288/512*rs+rd.get('offsetStartX',0))
        scale*=rs
        return {'accepted':len(a)>=18 and .9<=scale<=1.1 and abs(offsetX)<=20 and residual<3,'scale':scale,'offsetX':offsetX,'inliers':len(a),'residual':residual}
    report=[];clipped=[];halfWidth=120
    def contact(a,roi):
        mask=(a[:,:,3]>=200)&(a[:,:,:3].mean(axis=2)<195);pad=(a.shape[0]-512)//2
        rows=np.flatnonzero(mask[:,int(roi[0])+pad:int(roi[1])+pad].sum(axis=1)>=6)
        if not len(rows):return float('nan')
        return (float(rows[-1])+1)/a.shape[0]
    reference_support={}
    for pose,(ref,index) in CANON.items():
        rd=m['clips']['video-'+ref]
        size=im(ref,index).shape[0]
        reference_support[pose]=rd['groundContacts'][index]*size if 'groundContacts' in rd else float(np.median([contact(im(ref,i),ROI[pose]) for i in range(len(m['animations']['video-'+ref]))]))*size
    for action in actions:
        n=str(action['id']);clip='video-'+n;entry=m['clips'][clip];files=m['animations'][clip]
        fits=[fit(n,i,p) for i,p in [(0,action['start']),(-1,action['end'])]]
        for suffix,f in zip(['Start','End'],fits):
            if f['accepted']:entry['scale'+suffix]=f['scale'];entry['offset'+suffix+'X']=f['offsetX']
        raw=[]
        for i in range(len(files)):
            a=im(n,i);hard=a[:,:,3]>128
            if int(n)>=58 and int(n) not in [86,87,88,93,94]:
                _,xs=np.where(hard);s=i/max(1,len(files)-1);s=s*s*(3-2*s)
                scale=entry['scaleStart']+(entry['scaleEnd']-entry['scaleStart'])*s
                offset=entry['offsetStartX']+(entry['offsetEndX']-entry['offsetStartX'])*s
                halfWidth=max(halfWidth,float(np.max(np.abs((xs/a.shape[1]-.5)*entry['width']*scale+offset))))
            if hard[0,:].any() or hard[-1,:].any() or hard[:,0].any() or hard[:,-1].any():clipped.append([n,i])
            t=i/max(1,len(files)-1);t=t*t*(3-2*t)
            if int(n) in [52,53,54]:value=contact(a,(180,280))
            elif int(n) in [55,56,57]:value=contact(a,(225,325))
            elif int(n) in [70,74]:value=contact(a,(150,450))
            elif int(n)==78:value=contact(a,(300,450))
            else:
                # Transfer between two anatomical support regions; moving a
                # narrow ROI across the image can accidentally latch onto a tail.
                startContact=contact(a,ROI[action['start']]);endContact=contact(a,ROI[action['end']])
                if not np.isfinite(startContact):startContact=endContact
                if not np.isfinite(endContact):endContact=startContact
                value=startContact*(1-t)+endContact*t
            if not np.isfinite(value):raise ValueError(f'No supporting region: {n} frame {i}')
            raw.append(value)
        if action['type']=='循环':values=np.full(len(raw),np.median(raw))
        else:
            kernel=np.exp(-np.arange(-3,4)**2/(2*1.1**2));kernel/=kernel.sum()
            values=np.convolve(np.pad(raw,3,mode='edge'),kernel,mode='valid')
            values[0]=raw[0];values[-1]=raw[-1]
        # Shared-pose endpoints must use the same real support line as their
        # canonical reference, transformed into this clip's source coordinates.
        phase=np.linspace(0,1,len(raw));ease=lambda x:(lambda z:z*z*(3-2*z))(np.clip(x,0,1))
        for endpoint,index,p,f in [('Start',0,action['start'],fits[0]),('End',-1,action['end'],fits[1])]:
            if not f['accepted']:continue
            ref,ri=CANON[p];rd=m['clips']['video-'+ref]
            if ref!=n or index!=-1:
                # Refit vertical translation on matching features (not tail bounds).
                ka,da=feature(n,index);kb,db=feature(ref,ri)
                matches=[a for a,b in cv2.BFMatcher().knnMatch(da,db,k=2) if a.distance<.75*b.distance]
                pa=np.float32([ka[a.queryIdx].pt for a in matches]);pb=np.float32([kb[a.trainIdx].pt for a in matches])
                matrix,keep=cv2.estimateAffinePartial2D(pa,pb,method=cv2.RANSAC,ransacReprojThreshold=2)
                if matrix is not None:
                    support=reference_support[p]
                    target=(support-matrix[1,2])/np.linalg.norm(matrix[:,0])/im(n,index).shape[0]
                    weight=1-ease(phase/.2) if index==0 else ease((phase-.8)/.2)
                    values+=(target-values[index])*weight
        if n in SUPPORT_KEYS:
            values=reviewed_support(n,len(values),values[0],values[-1])
        entry['groundContacts']=values.tolist()
        report.append({'id':n,'poses':[action['start'],action['end']],'fits':fits,'supportRange': [min(raw),max(raw)],'maxAdjacentContactDelta':float(np.abs(np.diff(values)).max()*entry['height'])})
    offsets={}
    for direction,n in [('right','53'),('left','56')]:
        a=im(n,0);mask=a[:,:,3]>200;size=a.shape[0];pad=(size-512)//2;y,x=np.where(mask[:380+pad])
        # Front paws are on the outward side of the upper half, not tail/feet.
        px=float(x.max() if direction=='right' else x.min())
        d=m['clips']['video-'+n];offsets[direction]=(px/size-d['anchorX'])*d['width']*d['scaleStart']+d['offsetStartX']
    m['wallContactOffsets']=offsets
    m['relaxedHalfWidth']=float(np.ceil(halfWidth)+2)
    path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    out=ROOT/'docs/qa/expressions-v4';out.mkdir(parents=True,exist_ok=True)
    (out/'calibration.json').write_text(json.dumps({'method':'shared-pose SIFT isotropic fit; planted paw or body ROI; fixed breathing contact','wallContactOffsets':offsets,'clips':report,'clippedFrames':clipped},indent=2)+'\n',encoding='utf-8')
    print('calibrated',len(report),'clips; rejected endpoint fits',sum(not f['accepted'] for r in report for f in r['fits']),'clipped frames',len(clipped),flush=True)
if __name__=='__main__':main()

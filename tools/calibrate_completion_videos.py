"""Register common physical features and support anchors without resizing frame boxes."""
from pathlib import Path
import json,cv2,numpy as np
from PIL import Image
from completion_support_keys import KEYS,support
ROOT=Path(__file__).resolve().parents[1];A=ROOT/'assets/pets/bluecat'
OUT=ROOT/'docs/qa/completion-v5';OUT.mkdir(parents=True,exist_ok=True)
cv2.setNumThreads(2)
CANON={'F':('01',0),'I':('19',0),'C':('20',0),'SR':('22',0),'SL':('09',0),
       'D':('66',0),'A':('67',0),'B':('71',0),'X':('75',0),'M':('84',0),
       'RR':('109',0),'RL':('112',0),'SF':('117',0),
       'H_D':('126',0),'H_A':('129',0),'H_B':('132',0),'H_X':('135',0),'H_M':('138',0)}
ROI={'SF':(240,335),'RR':(130,445),'RL':(100,395),'H_D':(315,440),'H_A':(160,290),'H_B':(170,350),'H_X':(300,410),'H_M':(210,350)}

def main():
    path=A/'manifest.json';m=json.loads(path.read_text(encoding='utf-8'));rows=json.loads((ROOT/'art/video-pipeline/completion-v5/returned/sources.json').read_text(encoding='utf-8'))
    detector=cv2.SIFT_create();cache={};images={}
    def im(n,i):
        key=(str(n),i)
        if key not in images:images[key]=np.asarray(Image.open(A/m['animations']['video-'+str(n)][i]).convert('RGBA'))
        return images[key]
    def feats(n,i):
        key=(str(n),i)
        if key not in cache:
            a=im(n,i);cache[key]=detector.detectAndCompute(cv2.cvtColor(a[:,:,:3],cv2.COLOR_RGB2GRAY),(a[:,:,3]>200).astype('uint8')*255)
        return cache[key]
    def contact(a,roi):
        pad=(a.shape[0]-512)//2; mask=(a[:,:,3]>200)&(a[:,:,:3].mean(axis=2)<195)
        ys=np.flatnonzero(mask[:,roi[0]+pad:roi[1]+pad].sum(axis=1)>=6)
        assert len(ys)>0,roi
        return float(ys[-1]+1)
    # Fixed airborne anchors retain the video's authored bob; never follow a
    # dangling paw or normalize the changing frame silhouette.
    for p in ['RR','RL','SF','H_D','H_A','H_B','H_X','H_M']:
        n,i=CANON[p];d=m['clips']['video-'+n];files=m['animations']['video-'+n]
        value=float(np.median([contact(im(n,k),ROI[p]) for k in range(len(files))]))
        d['groundContacts']=[value/768]*len(files)
    def fit(n,i,p):
        ref,ri=CANON[p];ka,da=feats(n,i);kb,db=feats(ref,ri)
        pairs=cv2.BFMatcher().knnMatch(da,db,k=2) if da is not None and db is not None else []
        matches=[x for pair in pairs if len(pair)==2 for x,y in [pair] if x.distance<.75*y.distance]
        if len(matches)<8:return {'accepted':False,'inliers':len(matches)}
        pa=np.float32([ka[t.queryIdx].pt for t in matches]);pb=np.float32([kb[t.trainIdx].pt for t in matches])
        matrix,keep=cv2.estimateAffinePartial2D(pa,pb,method=cv2.RANSAC,ransacReprojThreshold=2)
        if matrix is None:return {'accepted':False,'inliers':0}
        pa,pb=pa[keep[:,0]==1],pb[keep[:,0]==1];ac,bc=pa-pa.mean(0),pb-pb.mean(0)
        scale=float((ac*bc).sum()/(ac*ac).sum());tx,ty=(pb.mean(0)-scale*pa.mean(0)).tolist()
        residual=float(np.linalg.norm(pa*scale+[tx,ty]-pb,axis=1).mean())
        rd=m['clips']['video-'+ref];rs=rd.get('scaleStart',1);size=im(ref,ri).shape[0]
        offset=((384*scale+tx)-rd['anchorX']*size)*rd['width']/size*rs+rd.get('offsetStartX',0)
        ay=rd['groundContacts'][ri] if 'groundContacts' in rd else rd['anchorY']
        support=(ay*size-ty)/scale
        return dict(accepted=len(pa)>=18 and .9<=scale*rs<=1.1 and abs(offset)<=20 and residual<3,scale=scale*rs,offsetX=offset,support=support,inliers=len(pa),residual=residual)
    records=[];clipped=[]
    # Interior supports use fixed anatomical regions, inspected on 8-frame sheets.
    regions={99:(245,450),100:(145,420),101:(250,445),102:(145,440),103:(250,450),104:(145,440),105:(145,440),106:(150,425),107:(290,440),114:(230,445),115:(145,335),118:(230,320),119:(335,445),120:(115,205),121:(275,435),122:(275,435),123:(160,290),124:(160,290)}
    # Feature endpoint fits are evaluated before metadata writes so references do
    # not drift as different consumers are visited.
    fits={r['id']:[fit(str(r['id']),0,r['start']),fit(str(r['id']),-1,r['end'])] for r in rows}
    for r in rows:
        n=r['id'];s=str(n);d=m['clips']['video-'+s];count=len(m['animations']['video-'+s]);ends=fits[n]
        for suffix,f in zip(['Start','End'],ends):
            if f['accepted']:d['scale'+suffix]=f['scale'];d['offset'+suffix+'X']=f['offsetX']
        start=ends[0].get('support',600);end=ends[1].get('support',600)
        phase=np.linspace(0,1,count);ease=lambda t:(lambda x:x*x*(3-2*x))(np.clip(t,0,1))
        if n>=125 or 108<=n<=113 or n in [116,117]:
            values=start+(end-start)*ease(phase)
            if r['type']=='循环':values[:]=start
            method='fixed registered root; preserve airborne/body motion'
        else:
            roi=regions.get(n,(180,430));raw=np.array([contact(im(s,i),roi) for i in range(count)])
            kernel=np.exp(-np.arange(-3,4)**2/(2*1.1**2));kernel/=kernel.sum()
            values=np.convolve(np.pad(raw,3,mode='edge'),kernel,mode='valid')
            values+=(start-values[0])*(1-ease(phase/.2))+(end-values[-1])*ease((phase-.8)/.2)
            method='planted paw/body region; registered shared endpoints'
        if n in KEYS:values=support(n,count,start,end);method='reviewed anatomical support keys; exclude tail and suspended extremities'
        assert np.all((values/768>=.5)&(values/768<=1)),n
        d['groundContacts']=(values/768).tolist()
        for i in range(count):
            hard=im(s,i)[:,:,3]>128
            if hard[0].any() or hard[-1].any() or hard[:,0].any() or hard[:,-1].any():clipped.append([n,i])
            if i not in [0,count-1]:images.pop((s,i),None)
        records.append(dict(id=n,poses=[r['start'],r['end']],fits=ends,method=method,maxAdjacentSupportDelta=float(np.abs(np.diff(values)).max()*.5625)))
    path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    result=dict(clips=records,clippedFrames=clipped,rejectedFits=sum(not f['accepted'] for r in records for f in r['fits']))
    (OUT/'calibration.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('calibrated',len(rows),'rejected fits',result['rejectedFits'],'clipped',len(clipped),flush=True)
if __name__=='__main__':main()

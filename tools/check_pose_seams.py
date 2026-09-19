"""Independent similarity-fit audit of rendered sleep/care seam correspondences."""
import json
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'assets/pets/bluecat'
manifest=json.loads((ASSETS/'manifest.json').read_text(encoding='utf-8'))
detector=cv2.SIFT_create()

def points(clip,index):
    files=manifest['animations']['video-'+clip]
    index=index if index>=0 else len(files)+index
    rgba=np.asarray(Image.open(ASSETS/files[index]))
    k,d=detector.detectAndCompute(cv2.cvtColor(rgba[:,:,:3],cv2.COLOR_RGB2GRAY),(rgba[:,:,3]>200).astype('uint8')*255)
    return index,k,d

def displayed(clip,index,p):
    c=manifest['clips']['video-'+clip];count=len(manifest['animations']['video-'+clip])
    t=index/(count-1);t=t*t*(3-2*t)
    s=c['scaleStart']+(c['scaleEnd']-c['scaleStart'])*t
    offset=np.array([c['offsetStartX']+(c['offsetEndX']-c['offsetStartX'])*t,c['offsetStartY']+(c['offsetEndY']-c['offsetStartY'])*t])
    result=(p/512-np.array([c['anchorX'],c['anchorY']]))*288*s+offset
    if 'groundContacts' in c:
        result[:,1]=(p[:,1]/512-c['groundContacts'][index])*288*s
    elif 'landingContactY' in c:
        blend=np.clip((index/(count-1)-.8)/.2,0,1);blend=blend*blend*(3-2*blend)
        anchored=(p[:,1]/512-c['landingContactY'])*288*s
        result[:,1]=result[:,1]*(1-blend)+anchored*blend
    return result

def check(a,ia,b,ib,continue_breath=False):
    ia,ka,da=points(a,ia);ib,kb,db=points(b,ib)
    matches=[x for x,y in cv2.BFMatcher().knnMatch(da,db,k=2) if x.distance<.75*y.distance]
    pa=np.float32([ka[x.queryIdx].pt for x in matches]);pb=np.float32([kb[x.trainIdx].pt for x in matches])
    _,keep=cv2.estimateAffinePartial2D(pa,pb,method=cv2.RANSAC,ransacReprojThreshold=2)
    pa,pb=pa[keep[:,0]==1],pb[keep[:,0]==1]
    pa,pb=displayed(a,ia,pa),displayed(b,ib,pb)
    if continue_breath:
        s,x,y=manifest['clips']['video-20']['phaseRegistration'][ia]
        pb=pb/s-np.array([x,0 if 'groundContacts' in manifest['clips']['video-'+b] else y])/s
    matrix,_=cv2.estimateAffinePartial2D(pa,pb,method=cv2.LMEDS)
    ratio=float(np.linalg.norm(matrix[:,0]))
    jump=float(np.linalg.norm(np.mean(pb-pa,axis=0)))
    return dict(source=a,sourceFrame=ia,target=b,targetFrame=ib,breathContinuation=continue_breath,
                bodyScaleRatio=ratio,meanPositionChange=jump,inliers=len(pa))

if __name__=='__main__':
    seams=[check(*pair) for pair in [('17',-1,'19',0),('19',-1,'20',0),('21',-1,'18',0),('18',-1,'01',0),
                                   ('16',-1,'22',0),('11',-1,'22',0),('22',-1,'23',0),('22',-1,'25',0),('25',-1,'24',0),
                                   ('34',-1,'01',0),('01',-1,'17',0),('20',-1,'20',0)]]
    seams += [check('20',i,'21',0,True) for i in range(121)]
    maximum=max(abs(x['bodyScaleRatio']-1) for x in seams)
    assert all(x['inliers']>=50 for x in seams)
    assert maximum<.003, maximum
    assert max(x['meanPositionChange'] for x in seams)<1.5
    out=ROOT/('docs/qa/landing-sleep' if manifest.get('supportContactRevision')==3 else 'docs/qa/support-plane' if 'groundContacts' in manifest['clips']['video-20'] else 'docs/qa/contact-v2');out.mkdir(parents=True,exist_ok=True)
    (out/'seams.json').write_text(json.dumps({'maxScaleDifferencePercent':maximum*100,'seams':seams},indent=2)+'\n',encoding='utf-8')
    print(f'{len(seams)} rendered seam samples passed; maximum matched-body scale difference {maximum*100:.3f}%')

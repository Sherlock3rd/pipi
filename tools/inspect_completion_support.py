from pathlib import Path
import cv2,numpy as np,json
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1];RUN=ROOT/'art/video-pipeline/completion-v5/returned';OUT=ROOT/'artifacts/completion-v5-integration'
for n in [117,119,120]:
    cap=cv2.VideoCapture(str(RUN/f'{n:03}'/'source.mp4'));count=int(cap.get(7));tiles=[]
    for i in range(count):
        ok,bgr=cap.read();assert ok
        h,w=bgr.shape[:2]
        # Front or side face; isolate mouth under the nose, retain eyes as reference.
        box=(.43,.22,.56,.41) if n==117 else ((.60,.25,.81,.46) if n==119 else (.18,.24,.40,.46))
        x0,y0,x1,y1=[int(v*(w if j%2==0 else h)) for j,v in enumerate(box)]
        im=Image.fromarray(cv2.cvtColor(bgr[y0:y1,x0:x1],cv2.COLOR_BGR2RGB));im.thumbnail((120,100))
        tile=Image.new('RGB',(124,124),'white');tile.paste(im,(0,0));ImageDraw.Draw(tile).text((4,106),str(i),fill='black');tiles.append(tile)
    cap.release()
    for start in range(0,count,48):
        part=tiles[start:start+48];sheet=Image.new('RGB',(8*124,((len(part)+7)//8)*124),'#ddd')
        for j,tile in enumerate(part):sheet.paste(tile,((j%8)*124,(j//8)*124))
        sheet.save(OUT/f'mouth-{n}-{start}.jpg',quality=95)

# Eight annotated runtime-canvas samples identify planted paws/body support.
report=json.loads((ROOT/'art/video-pipeline/basic-v2/returned/extraction.json').read_text(encoding='utf-8'))
rows=[]
for n in range(99,140):
    cap=cv2.VideoCapture(str(RUN/f'{n:03}'/'source.mp4'));count=int(cap.get(7));row=Image.new('RGB',(8*220,245),'white')
    for j,i in enumerate(np.linspace(0,count-1,8).round().astype(int)):
        cap.set(cv2.CAP_PROP_POS_FRAMES,int(i));ok,bgr=cap.read();assert ok
        h,w=bgr.shape[:2];factor=w/640;scale=report['globalScale']*2/factor
        rx,ry=np.array(report['sourceRoot'])*factor;ox,oy=np.array(report['outputRoot'])*2+128
        matrix=np.array([[scale,0,ox-rx*scale],[0,scale,oy-ry*scale]],np.float32)
        im=cv2.warpAffine(bgr,matrix,(768,768),borderValue=(255,255,255))
        pil=Image.fromarray(cv2.cvtColor(im,cv2.COLOR_BGR2RGB));d=ImageDraw.Draw(pil)
        for y in range(500,651,10):d.line((120,y,648,y),fill='#e1b9b9');d.text((115,y-10),str(y),fill='red')
        pil=pil.crop((100,240,668,680)).resize((220,171));row.paste(pil,(j*220,40));ImageDraw.Draw(row).text((j*220+5,5),f'{n} f{i}',fill='black')
    cap.release();rows.append(row)
for i in range(0,len(rows),5):
    part=rows[i:i+5];sheet=Image.new('RGB',(1760,245*len(part)),'white')
    for j,row in enumerate(part):sheet.paste(row,(0,j*245))
    sheet.save(OUT/f'support-{i//5}.jpg',quality=95)

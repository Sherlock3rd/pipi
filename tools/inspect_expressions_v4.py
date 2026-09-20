"""Inventory returned clips and produce diagnostic contact sheets; preserve originals."""
from pathlib import Path
import json,hashlib,shutil
import cv2
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/expressions-v4'
RUN=ROOT/'art/video-pipeline/expressions-v4/returned'
def main():
    OUT.mkdir(parents=True,exist_ok=True);RUN.mkdir(parents=True,exist_ok=True)
    actions=json.loads((RUN.parent/'manifest.json').read_text(encoding='utf-8'))['clips']
    rows=[];tiles=[]
    for action in actions:
        n=action['folder'][:2];files=list((Path.home()/'Downloads').glob(n+'-*.mp4'))
        assert len(files)==1,(n,files)
        source=files[0];dest=RUN/n;dest.mkdir(exist_ok=True)
        target=dest/'source.mp4'
        digest=hashlib.sha256(source.read_bytes()).hexdigest()
        if not target.exists():shutil.copy2(source,target)
        assert hashlib.sha256(target.read_bytes()).hexdigest()==digest
        cap=cv2.VideoCapture(str(target));count=int(cap.get(7));fps=cap.get(5)
        row={**action,'originalName':source.name,'sha256':digest,'frames':count,'fps':fps,'width':int(cap.get(3)),'height':int(cap.get(4)),'duration':count/fps}
        tile=Image.new('RGB',(720,160),'#e0e0e0');draw=ImageDraw.Draw(tile)
        draw.text((3,2),f"{n} {action['start']} -> {action['end']} | {count} frames / {fps:g} fps",fill='black')
        for j,i in enumerate([0,count//2,count-1]):
            cap.set(cv2.CAP_PROP_POS_FRAMES,i);ok,bgr=cap.read();assert ok
            im=Image.fromarray(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB));im.save(dest/f'sample-{i:03}.jpg',quality=90)
            im.thumbnail((240,135));tile.paste(im,(j*240,23))
        cap.release();rows.append(row);tiles.append(tile)
    (RUN/'sources.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for i in range(0,len(tiles),12):
        sheet=Image.new('RGB',(720,160*len(tiles[i:i+12])),'white')
        for j,tile in enumerate(tiles[i:i+12]):sheet.paste(tile,(0,j*160))
        sheet.save(OUT/f'sources-{i//12}.jpg',quality=90)
    print(json.dumps([{k:r[k] for k in ['originalName','frames','fps','width','height','duration']} for r in rows],ensure_ascii=False))
if __name__=='__main__':main()

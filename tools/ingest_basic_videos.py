"""Archive user video bytes and create inspection samples, without changing media."""
from pathlib import Path
import cv2,hashlib,json,shutil,subprocess
import numpy as np
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
RUN=ROOT/'art/video-pipeline/basic-v2/returned'
RUN.mkdir(parents=True,exist_ok=True)
actions=json.loads((RUN.parent/'manifest.json').read_text(encoding='utf-8'))['actions']
result=[]
for action in actions:
    number=action['folder'][:2]
    sources=list((Path.home()/'Downloads').glob(number+'-*.mp4'))
    assert len(sources)==1,(number,sources)
    source=sources[0]; d=RUN/number;d.mkdir(exist_ok=True)
    dest=d/'source.mp4'
    digest=hashlib.sha256(source.read_bytes()).hexdigest()
    if not dest.exists():shutil.copy2(source,dest)
    assert hashlib.sha256(dest.read_bytes()).hexdigest()==digest
    probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_format','-show_streams','-of','json',str(dest)],encoding='utf-8'))
    (d/'probe.json').write_text(json.dumps(probe,ensure_ascii=False,indent=2),encoding='utf-8')
    cap=cv2.VideoCapture(str(dest));count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));fps=cap.get(cv2.CAP_PROP_FPS)
    width=int(cap.get(cv2.CAP_PROP_FRAME_WIDTH));height=int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    wanted=list(np.linspace(0,count-1,12).round().astype(int)); samples={}
    for idx in wanted:
        cap.set(cv2.CAP_PROP_POS_FRAMES,int(idx));ok,bgr=cap.read();assert ok,(number,idx)
        samples[int(idx)]=Image.fromarray(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB))
    cap.release()
    sheet=Image.new('RGB',(4*480,3*294),'#ffffff');draw=ImageDraw.Draw(sheet)
    for j,idx in enumerate(wanted):
        tile=samples[int(idx)].resize((480,270),Image.Resampling.LANCZOS)
        x,y=(j%4)*480,(j//4)*294;sheet.paste(tile,(x,y));draw.text((x+8,y+274),f'{number} frame {idx} / {idx/fps:.3f}s',fill='black')
    sheet.save(d/'source-contact.jpg',quality=93)
    samples[0].save(d/'first.png');samples[count-1].save(d/'last.png')
    row={**action,'number':number,'originalPath':str(source),'source':'source.mp4','sha256':digest,'fps':fps,'frames':count,'width':width,'height':height,'duration':float(probe['format']['duration'])}
    (d/'source.json').write_text(json.dumps(row,ensure_ascii=False,indent=2),encoding='utf-8');result.append(row)
    print(number,count,fps,flush=True)
(RUN/'sources.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')

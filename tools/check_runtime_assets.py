from pathlib import Path
from PIL import Image
import json
root=Path(__file__).resolve().parents[1]
base=root/'assets/pets/bluecat';m=json.loads((base/'manifest.json').read_text(encoding='utf-8'))
count=0
for clip,files in m['animations'].items():
    for f in files:
        p=(base/f).resolve();assert p.is_relative_to(base) and p.exists(),p
        if not clip.startswith('video-'):continue
        with Image.open(p) as im:
            assert im.mode=='RGBA' and im.size==(256,256),(clip,p,im.mode,im.size)
            a=im.getchannel('A');box=a.getbbox()
            assert box and min(box[:2])>0 and max(box[2:])<256,(clip,p,box)
            assert a.getextrema()==(0,255),(clip,p,a.getextrema())
        count+=1
for name in ['food-bowl-empty','kibble','water-cup-empty']:
    with Image.open(root/'assets/props'/f'{name}.png') as im:
        assert im.mode=='RGBA' and im.getchannel('A').getextrema()==(0,255),name
print(f'PASS {count} replacement frames and 3 transparent props; all manifest paths resolve')

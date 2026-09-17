"""Deterministically extract generated 6x4 sheets; never draw or interpolate cats."""
import argparse
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / 'art/animation-samples/b-foundation-24'
sys.path.insert(0, str(ROOT / '.agents/skills/hatch-pet/scripts'))
from compose_atlas import clear_transparent_rgb
from render_animation_previews import save_preview
from extract_strip_frames import connected_components, component_group_image

SPECS = {
    'idle': (150, 218, 12, True),
    'walk': (244, 180, 24, True),
    'move-to-sit': (232, 218, 24, False),
    'sit-to-idle': (150, 218, 24, False),
    'sit-to-sleep': (194, 218, 16, False),
    'sleep': (184, 125, 12, True),
}

def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

def guide():
    out = Image.new('RGB', (1536, 1024), '#eeeeee')
    draw = ImageDraw.Draw(out)
    for i in range(24):
        x, y = i%6*256, i//6*256
        draw.rectangle((x+8,y+8,x+248,y+248), outline='#a0a0a0', width=2)
        draw.line((x+20,y+236,x+236,y+236),fill='#86a6a0',width=2)
        draw.text((x+14,y+12),str(i+1),fill='#555555')
    out.save(RUN/'references/layout-24.png')

def pack(name):
    source = RUN/'decoded'/f'{name}.png'
    sheet = Image.open(source).convert('RGBA')
    if sheet.getchannel('A').getextrema()[0] == 255:
        raise ValueError(f'{name}: generated sheet has no transparent background')
    components=connected_components(sheet)
    threshold=max(c['area'] for c in components)*.18
    seeds=[c for c in components if c['area']>=threshold]
    if len(seeds)!=24:raise ValueError(f'{name}: expected 24 full cats, found {len(seeds)} components')
    seeds.sort(key=lambda c:(c['bbox'][1]+c['bbox'][3])/2)
    ordered=[]
    for row in range(4):ordered.extend(sorted(seeds[row*6:row*6+6],key=lambda c:c['center_x']))
    groups=[[seed] for seed in ordered]
    seed_ids={id(c) for c in seeds}
    for part in components:
        if id(part) in seed_ids or part['area']<12:continue
        cx,cy=part['center_x'],(part['bbox'][1]+part['bbox'][3])/2
        nearest=min(range(24),key=lambda i:(ordered[i]['center_x']-cx)**2+((ordered[i]['bbox'][1]+ordered[i]['bbox'][3])/2-cy)**2)
        groups[nearest].append(part)
    sprites=[component_group_image(sheet,group,padding=0) for group in groups]
    maxw,maxh,fps,loop=SPECS[name]
    # One scale for the entire clip retains pose height changes (especially sitting down).
    scale=min(maxw/max(s.width for s in sprites),maxh/max(s.height for s in sprites))
    outdir=ROOT/'assets/pets/bluecat/b-foundation'/name
    outdir.mkdir(parents=True,exist_ok=True)
    previews=RUN/'preview'/name
    previews.mkdir(parents=True,exist_ok=True)
    frames=[]
    for i,sprite in enumerate(sprites):
        # The idle sheet's lower row is drawn smaller. Seat height is invariant;
        # normalize that packing drift, retaining the generated eyelid drawings.
        frame_scale=min(maxh/sprite.height,maxw/sprite.width) if name=='idle' else scale
        sprite=sprite.resize((max(1,round(sprite.width*frame_scale)),max(1,round(sprite.height*frame_scale))),Image.Resampling.LANCZOS)
        frame=Image.new('RGBA',(256,256))
        frame.alpha_composite(sprite,((256-sprite.width)//2,236-sprite.height))
        frame=clear_transparent_rgb(frame)
        frame.save(outdir/f'{i:02}.png')
        frames.append(frame)
    durations=[round(1000/fps)]*24
    # GIF timing is centiseconds; PNG/WebP runtime timing remains the configured fps.
    save_preview(frames,[round(d/10)*10 for d in durations],previews/'animation.gif')
    frames[0].save(previews/'animation.webp',save_all=True,append_images=frames[1:],duration=durations,loop=0,lossless=True,exact=True)
    contact=Image.new('RGB',(1536,1024),'#eee9df')
    for i,frame in enumerate(frames): contact.paste(frame,(i%6*256,i//6*256),frame)
    contact.save(RUN/'qa'/f'{name}-contact.png')
    report={'clip':name,'count':24,'uniqueFrames':len({f.tobytes() for f in frames}),'fps':fps,'loop':loop,
            'source':str(source.relative_to(ROOT)).replace('\\','/'),'sourceSize':sheet.size,'frameSize':[256,256],
            'anchor':[128,236],'scale':scale,'alphaExtrema':sheet.getchannel('A').getextrema(),
            'extraction':'24 connected components, row-major by row center then x',
            'sourceBounds':[s['bbox'] for s in ordered],'bounds':[f.getbbox() for f in frames]}
    if report['uniqueFrames']<20:raise ValueError('Too few distinct generated frames')
    write(RUN/'qa'/f'{name}.json',report)
    print(json.dumps(report,ensure_ascii=False))

def install():
    path=ROOT/'assets/pets/bluecat/manifest.json'
    manifest=json.loads(path.read_text(encoding='utf-8-sig'))
    manifest.update(name='陈皮 · B 绘本厚涂',description='用户认可的 B 第二轮造型；待机、移动、坐下与睡眠基础动画，每段24帧。其他动作暂保留既有表现。',frameWidth=256,frameHeight=256,fps=24)
    manifest['clips']={}
    for name,(_,_,fps,loop) in SPECS.items():
        report=json.loads((RUN/'qa'/f'{name}.json').read_text(encoding='utf-8'))
        if report['count']!=24 or report['uniqueFrames']<20:raise ValueError(f'incomplete clip {name}')
        manifest['animations'][name]=[f'b-foundation/{name}/{i:02}.png' for i in range(24)]
        manifest['clips'][name]={'fps':fps,'loop':loop,'width':180,'height':180,'anchorX':.5,'anchorY':236/256}
    write(path,manifest)
    write(RUN/'clips.json',{'acceptedIdentity':'b-idle-v2, accepted by user 2026-09-18','frameCountPerClip':24,'clips':manifest['clips']})
    print('Installed six 24-frame clips in manifest')

def validate():
    result={}
    for name in SPECS:
        directory=ROOT/'assets/pets/bluecat/b-foundation'/name
        paths=sorted(directory.glob('*.png'))
        if [p.name for p in paths]!=[f'{i:02}.png' for i in range(24)]:raise ValueError(f'{name}: missing or unexpected frames')
        unique=set()
        for path in paths:
            frame=Image.open(path)
            if frame.mode!='RGBA' or frame.size!=(256,256):raise ValueError(f'{path}: format')
            raw=frame.tobytes();unique.add(raw)
            if any(raw[i+3]==0 and (raw[i] or raw[i+1] or raw[i+2]) for i in range(0,len(raw),4)):raise ValueError(f'{path}: hidden RGB')
            box=frame.getbbox()
            if not box or box[0]<3 or box[1]<3 or box[2]>253 or box[3]!=236:raise ValueError(f'{path}: unsafe bounds')
        if len(unique)!=24:raise ValueError(f'{name}: duplicated artwork')
        result[name]={'frames':24,'uniqueFrames':24,'size':[256,256],'bottomAnchor':236,'transparentRgbResidue':0,'edgeTouches':0}
    write(RUN/'qa/validation.json',{'passed':True,'totalFrames':144,'clips':result})
    print('PASS 144 unique transparent frames, six clips, fixed anchors, no edge touch or hidden RGB')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['guide','install','validate',*SPECS]);args=parser.parse_args()
    if args.action=='guide':guide()
    elif args.action=='install':install()
    elif args.action=='validate':validate()
    else:pack(args.action)

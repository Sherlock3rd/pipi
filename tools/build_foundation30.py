"""Package generated animation drawings only; never synthesize cat pixels.

Candidates stay in the run directory. Installing is explicitly gated on separate
structural and visual acceptance records for the hashes being installed.
"""
import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / 'art/animation-samples/b-foundation-30'
sys.path.insert(0, str(ROOT / '.agents/skills/hatch-pet/scripts'))
from extract_strip_frames import connected_components
from compose_atlas import clear_transparent_rgb

NAMES = ['idle', 'walk', 'move-to-sit', 'sit-to-idle', 'sit-to-sleep', 'sleep']
FPS = dict(zip(NAMES, [15, 30, 20, 20, 15, 15]))
LIMITS = {'idle':(154,218),'walk':(240,178),'move-to-sit':(240,218),
          'sit-to-idle':(154,218),'sit-to-sleep':(200,218),'sleep':(186,132)}


def save_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def guide():
    im = Image.new('RGB', (1536, 1024), '#eeeeee')
    dr = ImageDraw.Draw(im)
    for i in range(10):
        x, y = (i % 5) * 307, (i // 5) * 512
        dr.rectangle((x+12, y+16, x+295, y+496), outline='#888888', width=2)
        dr.line((x+20, y+462, x+285, y+462), fill='#999999', width=2)
        dr.text((x+18, y+22), str(i+1), fill='#777777')
    im.save(RUN/'references/layout-10.png')


def extract(source, expected=10, columns=5):
    im = Image.open(source).convert('RGBA')
    if im.getchannel('A').getextrema()[0] != 0:
        raise ValueError(f'{source}: no actual transparent background')
    # Generated cutouts sometimes carry almost invisible red matte specks at
    # alpha 1..8. Apply a documented low-alpha cleanup, never recolor cat fur.
    im.putalpha(im.getchannel('A').point(lambda value:0 if value<=8 else value))
    components = connected_components(im)
    seeds = [c for c in components if c['area'] > max(c['area'] for c in components)*.20]
    if len(seeds) != expected:
        raise ValueError(f'{source}: expected {expected} bodies; found {len(seeds)}')
    seeds.sort(key=lambda c:(c['bbox'][1]+c['bbox'][3])/2)
    ordered = []
    for start in range(0, expected, columns):
        ordered.extend(sorted(seeds[start:start+columns], key=lambda c:c['center_x']))
    groups = [[c] for c in ordered]
    for c in components:
        if any(c is seed for seed in seeds) or c['area'] < 12:
            continue
        cy = (c['bbox'][1]+c['bbox'][3])/2
        index = min(range(expected), key=lambda i:(ordered[i]['center_x']-c['center_x'])**2+
                    ((ordered[i]['bbox'][1]+ordered[i]['bbox'][3])/2-cy)**2)
        groups[index].append(c)
    # Components identify subjects; crop ORIGINAL pixels, including the low-alpha
    # painted fringe. Rebuilding only >16 alpha components would harden B's edges.
    sprites=[]
    for g in groups:
        box=(max(0,min(c['bbox'][0] for c in g)-3), max(0,min(c['bbox'][1] for c in g)-3),
             min(im.width,max(c['bbox'][2] for c in g)+3), min(im.height,max(c['bbox'][3] for c in g)+3))
        # Only the assigned components, with a small fringe allowance; a plain
        # rectangular crop can accidentally include a neighbouring tail tip.
        mask_bytes=bytearray(im.width*im.height)
        for component in g:
            for pixel in component['pixels']: mask_bytes[pixel]=255
        mask=Image.frombytes('L',im.size,bytes(mask_bytes)).crop(box).filter(ImageFilter.MaxFilter(7))
        sprite=im.crop(box)
        sprite.putalpha(ImageChops.multiply(sprite.getchannel('A'),mask))
        sprites.append(sprite.crop(sprite.getbbox()))
    return sprites, [c['bbox'] for c in ordered]


def pack(name):
    config = json.loads((RUN/'pack-config.json').read_text(encoding='utf-8'))[name]
    frames, provenance = [], []
    for part in range(3):
        source = RUN/'decoded'/f'{name}-{part+1}.png'
        sprites, bounds = extract(source)
        # A single documented scale per generated strip, never fit each pose to a box.
        # Cross-strip scales require checking skull size against the canonical sheet.
        scale = config['scales'][part]
        for index, sprite in enumerate(sprites):
            sprite = sprite.resize((round(sprite.width*scale), round(sprite.height*scale)), Image.Resampling.LANCZOS)
            if sprite.width > 244 or sprite.height > 224:
                raise ValueError(f'{name}/{part}/{index}: outside safe bounds')
            frame = Image.new('RGBA', (256, 256))
            frame.alpha_composite(sprite, ((256-sprite.width)//2, 236-sprite.height))
            frames.append(clear_transparent_rgb(frame))
        provenance.append({'source':str(source.relative_to(ROOT)), 'sha256':sha(source), 'scale':scale, 'bounds':bounds})
    out = RUN/'frames'/name
    out.mkdir(parents=True, exist_ok=True)
    for i, frame in enumerate(frames):
        frame.save(out/f'{i:02}.png')
    preview = RUN/'preview'/name
    preview.mkdir(parents=True, exist_ok=True)
    frames[0].save(preview/'animation.webp', save_all=True, append_images=frames[1:],
                   duration=[round(1000/FPS[name])]*30, loop=0, lossless=True, exact=True)
    for dark in (False, True):
        contact = Image.new('RGB', (1280, 1668), '#252c35' if dark else '#eee9df')
        dr = ImageDraw.Draw(contact)
        for i, frame in enumerate(frames):
            x, y = i%5*256, i//5*278
            contact.paste(frame, (x, y), frame)
            dr.text((x+12, y+256), f'{i+1:02}/30', fill='#999999')
        contact.save(RUN/'qa'/f'{name}-contact{"-dark" if dark else ""}.png')
    save_json(RUN/'qa'/f'{name}-source.json', {'provenance':provenance, 'fps':FPS[name], 'frames':30})


def pilot(name):
    """Expose first ten real drawings for review without claiming a full clip."""
    source = RUN/'decoded'/f'{name}-1.png'
    sprites, bounds = extract(source)
    scale = min(240/max(s.width for s in sprites), 214/max(s.height for s in sprites))
    out = RUN/'pilot'/name
    out.mkdir(parents=True, exist_ok=True)
    contact = Image.new('RGB', (1280,556),'#eee9df')
    dr = ImageDraw.Draw(contact)
    files = []
    for i,sprite in enumerate(sprites):
        sprite=sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),Image.Resampling.LANCZOS)
        frame=Image.new('RGBA',(256,256))
        frame.alpha_composite(sprite,((256-sprite.width)//2,236-sprite.height))
        frame=clear_transparent_rgb(frame)
        path=out/f'{i:02}.png';frame.save(path);files.append(sha(path))
        x,y=i%5*256,i//5*278
        contact.paste(frame,(x,y),frame)
        dr.text((x+12,y+256),f'{i+1:02} / 30 (pilot)',fill='#555555')
    contact.save(RUN/'qa'/f'{name}-pilot-contact.png')
    save_json(RUN/'qa'/f'{name}-pilot.json',{'source':str(source),'sha256':sha(source),'scale':scale,
        'frames':10,'bounds':bounds,'hashes':files,'status':'unreviewed; first third only, NOT a full cycle'})
    status=json.loads((RUN/'review-status.json').read_text(encoding='utf-8'))
    status['clips'][name]={'count':10,'fps':FPS[name],'loop':False,'folder':f'pilot/{name}',
        'note':'仅前10张制作候选，尚未通过逐帧验收，不是完整30帧循环。'}
    save_json(RUN/'review-status.json',status)
    print(f'{name}: ten-frame pilot, common scale={scale:.4f}; not installed')


def full(name):
    """Package one actual 30-drawing source as a review candidate, never install."""
    source=RUN/'decoded'/f'{name}-full.png'
    sprites,bounds=extract(source,30)
    maxw,maxh=LIMITS[name]
    scale=min(maxw/max(s.width for s in sprites),maxh/max(s.height for s in sprites))
    out=RUN/'frames'/name;out.mkdir(parents=True,exist_ok=True)
    frames=[]
    for i,sprite in enumerate(sprites):
        sprite=sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),Image.Resampling.LANCZOS)
        frame=Image.new('RGBA',(256,256))
        frame.alpha_composite(sprite,((256-sprite.width)//2,236-sprite.height))
        frame=clear_transparent_rgb(frame);frame.save(out/f'{i:02}.png');frames.append(frame)
    preview=RUN/'preview'/name;preview.mkdir(parents=True,exist_ok=True)
    frames[0].save(preview/'animation.webp',save_all=True,append_images=frames[1:],duration=[round(1000/FPS[name])]*30,loop=0,lossless=True,exact=True)
    for dark in (False,True):
        contact=Image.new('RGB',(1280,1668),'#252c35' if dark else '#eee9df');dr=ImageDraw.Draw(contact)
        for i,frame in enumerate(frames):
            x,y=i%5*256,i//5*278;contact.paste(frame,(x,y),frame);dr.text((x+12,y+256),f'{i+1:02}/30',fill='#999999')
        contact.save(RUN/'qa'/f'{name}-contact{"-dark" if dark else ""}.png')
    save_json(RUN/'qa'/f'{name}-source.json',{'source':str(source),'sourceHash':sha(source),'scale':scale,'frames':30,
        'sourceBounds':bounds,'bounds':[f.getbbox() for f in frames], 'hashes':[sha(out/f'{i:02}.png') for i in range(30)],
        'status':'candidate: anatomy/identity/motion review required'})
    status=json.loads((RUN/'review-status.json').read_text(encoding='utf-8'))
    status['clips'][name]={'count':30,'fps':FPS[name],'loop':name in ('idle','walk','sleep'),
        'folder':f'frames/{name}','note':'30张生成候选：仍需严格身份、解剖、动态检查，尚未接入。'}
    save_json(RUN/'review-status.json',status)
    print(f'{name}: packaged30 actual drawings, common scale={scale:.4f}; NOT installed')


def quarters():
    """Assemble actual 8+8+7+7 generated walk drawings, with no interpolation."""
    pieces = [('ab',8),('bc',8),('cd',7),('da',7)]
    config = json.loads((RUN/'walk-pack-config.json').read_text(encoding='utf-8'))
    frames, sources = [], []
    for label,count in pieces:
        source=RUN/'decoded'/f'walk-{label}.png'
        sprites,bounds=extract(source,count,4)
        scale=config[label]['scale']
        for sprite in sprites:
            sprite=sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),Image.Resampling.LANCZOS)
            if sprite.width>244 or sprite.height>224: raise ValueError(f'{label}: unsafe bounds')
            frame=Image.new('RGBA',(256,256))
            frame.alpha_composite(sprite,((256-sprite.width)//2,236-sprite.height))
            frames.append(clear_transparent_rgb(frame))
        sources.append({'source':str(source.relative_to(ROOT)),'sha256':sha(source),'count':count,'scale':scale,'bounds':bounds})
    out=RUN/'frames/walk';out.mkdir(parents=True,exist_ok=True)
    for i,frame in enumerate(frames): frame.save(out/f'{i:02}.png')
    preview=RUN/'preview/walk';preview.mkdir(parents=True,exist_ok=True)
    frames[0].save(preview/'animation.webp',save_all=True,append_images=frames[1:],duration=[33]*30,loop=0,lossless=True,exact=True)
    for dark in (False,True):
        contact=Image.new('RGB',(1280,1668),'#252c35' if dark else '#eee9df');dr=ImageDraw.Draw(contact)
        for i,frame in enumerate(frames):
            x,y=i%5*256,i//5*278;contact.paste(frame,(x,y),frame);dr.text((x+12,y+256),f'{i+1:02}/30',fill='#999999')
        contact.save(RUN/'qa'/f'walk-contact{"-dark" if dark else ""}.png')
    save_json(RUN/'qa/walk-source.json',{'provenance':sources,'frames':30,'hashes':[sha(out/f'{i:02}.png') for i in range(30)],'status':'candidate: quarter seams require visual review'})
    status=json.loads((RUN/'review-status.json').read_text(encoding='utf-8'))
    status['clips']['walk']={'count':30,'fps':30,'loop':True,'folder':'frames/walk','note':'四段生成的30张走路候选，段落接缝与接地仍待验收。'}
    save_json(RUN/'review-status.json',status)
    print('walk: 30 actual drawings from four generated quarters; NOT installed')


def validate():
    reports = {}
    for name in NAMES:
        paths = sorted((RUN/'frames'/name).glob('*.png'))
        if [p.name for p in paths] != [f'{i:02}.png' for i in range(30)]:
            raise ValueError(f'{name}: expected 30 actual drawings')
        hashes, bounds = [], []
        for path in paths:
            im = Image.open(path)
            if im.mode != 'RGBA' or im.size != (256,256):
                raise ValueError(f'{path}: format')
            raw = im.tobytes()
            if any(raw[i+3] == 0 and any(raw[i:i+3]) for i in range(0,len(raw),4)):
                raise ValueError(f'{path}: hidden RGB')
            box = im.getbbox()
            if not box or box[0]<3 or box[1]<3 or box[2]>253 or box[3]!=236:
                raise ValueError(f'{path}: clipped or ungrounded')
            hashes.append(sha(path)); bounds.append(box)
        if len(set(hashes)) != 30:
            raise ValueError(f'{name}: reused frame files')
        reports[name] = {'hashes':hashes, 'bounds':bounds}
    save_json(RUN/'qa/structure.json', {'passed':True,'totalFrames':180,'clips':reports,
        'limitation':'Geometry only. Does NOT certify anatomy, identity or motion.'})
    print('180 candidate frames structurally valid; independent visual acceptance still required')


def install():
    validate()
    structural = json.loads((RUN/'qa/structure.json').read_text(encoding='utf-8'))
    acceptance = json.loads((RUN/'qa/acceptance.json').read_text(encoding='utf-8'))
    if acceptance.get('passed') is not True or acceptance.get('blockingIssues'):
        raise ValueError('Visual review has not passed')
    for name in NAMES:
        if acceptance['clips'][name]['hashes'] != structural['clips'][name]['hashes']:
            raise ValueError(f'{name}: reviewed and installed drawings differ')
    target = ROOT/'assets/pets/bluecat/b-foundation-30'
    shutil.copytree(RUN/'frames',target,dirs_exist_ok=True)
    path = ROOT/'assets/pets/bluecat/manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8-sig'))
    manifest['description'] = '照片身份与 B 绘本厚涂统一标准；六段基础动作各 30 帧。'
    for name in NAMES:
        manifest['animations'][name] = [f'b-foundation-30/{name}/{i:02}.png' for i in range(30)]
        manifest['clips'][name] = {'fps':FPS[name], 'loop':name in ('idle','walk','sleep'),
            'width':180, 'height':180, 'anchorX':.5, 'anchorY':236/256}
    save_json(path, manifest)
    print('Installed six visually reviewed 30-frame clips')


def install_interim():
    """User 2026-09-18 explicitly requested available clips as an interim build.

    This path does not certify the failed six-clip batch, or relax install().
    Only the two static-reviewed candidates may be selected here.
    """
    selected=('idle','sleep')
    evidence={}
    for name in selected:
        paths=sorted((RUN/'frames'/name).glob('*.png'))
        if [p.name for p in paths]!=[f'{i:02}.png' for i in range(30)]:raise ValueError(f'{name}: incomplete')
        report=json.loads((RUN/'qa'/f'{name}-30-review.json').read_text(encoding='utf-8'))
        if report.get('static_visual_qa')!='pass' or report.get('static_blocking_issues'):
            raise ValueError(f'{name}: static review has not passed')
        hashes=[sha(p) for p in paths]
        if hashes!=[f['sha256'] for f in report['frames']]:raise ValueError(f'{name}: review hash mismatch')
        if len(set(hashes))!=30:raise ValueError(f'{name}: duplicated drawings')
        for p in paths:
            im=Image.open(p);box=im.getbbox()
            if im.mode!='RGBA' or im.size!=(256,256) or not box or min(box[:2])<3 or box[2]>253 or box[3]!=236:
                raise ValueError(f'{p}: invalid canvas/bounds')
            raw=im.tobytes()
            if any(raw[i+3]==0 and any(raw[i:i+3]) for i in range(0,len(raw),4)):raise ValueError(f'{p}: hidden RGB')
        evidence[name]={'hashes':hashes,'count':30,'review':f'qa/{name}-30-review.json'}
    target=ROOT/'assets/pets/bluecat/b-foundation-30'
    for name in selected:shutil.copytree(RUN/'frames'/name,target/name,dirs_exist_ok=True)
    path=ROOT/'assets/pets/bluecat/manifest.json'
    manifest=json.loads(path.read_text(encoding='utf-8-sig'))
    manifest['description']='用户明确要求先替换已有表现的过渡版本：眨眼、蜷睡各30帧；走路和过渡暂保留24帧，整体美术尚未达标，转外部图生视频重制。'
    manifest['artStatus']='interim-user-requested-not-final-art-approval'
    for name in selected:
        manifest['animations'][name]=[f'b-foundation-30/{name}/{i:02}.png' for i in range(30)]
        manifest['clips'][name]={'fps':FPS[name],'loop':True,'width':180,'height':180,'anchorX':.5,'anchorY':236/256}
    save_json(path,manifest)
    save_json(RUN/'qa/interim-install.json',{'status':'user-authorized-interim','fullBatchAccepted':False,
        'authorization':'2026-09-18 用户：先替换已有表现，然后提交；仍然没有达到要求。',
        'installed':evidence,'retained24':['walk','move-to-sit','sit-to-idle','sit-to-sleep'],
        'knownLimitations':['整体美术未达到用户要求','新旧动作混用，端点和风格衔接仍有跳变','外部图生视频返片后再逐段替换']})
    print('Installed interim idle/sleep only; six-clip art acceptance remains false')


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('action', choices=['guide','validate','install','install-interim','walk-quarters',*NAMES,*['pilot-'+n for n in NAMES],*['full-'+n for n in NAMES]])
    args=parser.parse_args()
    if args.action=='guide': guide()
    elif args.action=='validate': validate()
    elif args.action=='install': install()
    elif args.action=='install-interim': install_interim()
    elif args.action=='walk-quarters': quarters()
    elif args.action.startswith('pilot-'): pilot(args.action[6:])
    elif args.action.startswith('full-'): full(args.action[5:])
    else: pack(args.action)

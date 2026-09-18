"""Extract user-supplied video; preserve duration and one common crop/scale."""
from pathlib import Path
import hashlib, json
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / 'art/video-pipeline/walk-v1'
OUT = ROOT / 'assets/pets/bluecat/video-walk-v1'
OUT.mkdir(parents=True, exist_ok=True)
cap = cv2.VideoCapture(str(RUN / 'incoming/right-walk.mp4'))
fps = cap.get(cv2.CAP_PROP_FPS)
frames = []
while True:
    ok, frame = cap.read()
    if not ok: break
    frames.append(frame)
# Source 62 and 101 have matching gait phase; exclude repeated endpoint.
indices = [62 + round(i * 39 / 30) for i in range(30)]
cutouts, bounds = [], []
for idx in indices:
    bgr = cv2.resize(frames[idx], (640, 360), interpolation=cv2.INTER_AREA)
    seed = (np.min(bgr, axis=2) < 205).astype(np.uint8)
    n, labels, stats, _ = cv2.connectedComponentsWithStats(seed)
    body = (labels == 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])).astype(np.uint8)
    probable = cv2.dilate(body, np.ones((9, 9), np.uint8))
    sure = cv2.erode(body, np.ones((5, 5), np.uint8))
    mask = np.full(body.shape, cv2.GC_BGD, np.uint8)
    mask[probable > 0] = cv2.GC_PR_FGD
    mask[sure > 0] = cv2.GC_FGD
    mask[np.min(bgr, axis=2) > 220] = cv2.GC_BGD
    cv2.grabCut(bgr, mask, None, np.zeros((1,65)), np.zeros((1,65)), 2, cv2.GC_INIT_WITH_MASK)
    alpha = np.isin(mask, [cv2.GC_FGD, cv2.GC_PR_FGD]).astype(np.uint8)*255
    alpha = cv2.erode(alpha, np.ones((3,3), np.uint8))
    ys, xs = np.where(alpha > 0)
    bounds.append((int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1)))
    rgba = np.dstack([cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB), alpha])
    rgba[alpha == 0] = 0
    cutouts.append(Image.fromarray(rgba))
box = (min(b[0] for b in bounds)-6, min(b[1] for b in bounds)-6,
       max(b[2] for b in bounds)+6, max(b[3] for b in bounds)+6)
w, h = box[2]-box[0], box[3]-box[1]
scale = min(240/w, 218/h)
size = (round(w*scale), round(h*scale))
sheet = Image.new('RGB', (256*6, 280*5), '#27313c')
report = []
for i, (idx, im) in enumerate(zip(indices, cutouts)):
    tile = im.crop(box).resize(size, Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', (256,256))
    canvas.paste(tile, ((256-size[0])//2,236-size[1]))
    arr = np.array(canvas); arr[arr[:,:,3]==0]=0
    canvas = Image.fromarray(arr)
    path = OUT/f'{i:02}.png'; canvas.save(path)
    sheet.paste(canvas, ((i%6)*256, (i//6)*280), canvas)
    report.append({'output':path.name,'sourceFrame':idx,'seconds':idx/fps,
                   'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
ImageDraw.Draw(sheet).text((5,1380), '30 sampled source frames; same crop / scale / baseline; dark background QA', fill='white')
(RUN/'qa').mkdir(exist_ok=True)
sheet.save(RUN/'qa/video-contact.png')
images = [Image.open(OUT/f'{i:02}.png') for i in range(30)]
images[0].save(RUN/'qa/video-preview.webp', save_all=True, append_images=images[1:], duration=round(1000*39/fps/30), loop=0, lossless=True)
data={'source':'incoming/right-walk.mp4','sourceFps':fps,'sourceFrames':len(frames),
      'startInclusive':62,'endExclusive':101,'duration':39/fps,'outputFps':30*fps/39,
      'commonCrop':box,'scale':scale,'frames':report,'status':'preview-awaiting-user-review'}
(RUN/'qa/video-extraction.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in data.items() if k!='frames'}))

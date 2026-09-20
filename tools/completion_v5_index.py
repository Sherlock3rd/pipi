"""Offline visual index and independent delivery checks for the v5 video inputs."""
import hashlib
import html
import json
from pathlib import Path
from urllib.parse import unquote
from html.parser import HTMLParser
from PIL import Image


def build_index(out, items, anchors):
    cards = []
    for c in items:
        folder = c['folder']
        prompt = (out/c['promptFile']).read_text(encoding='utf-8')
        pics = ''.join(f'<figure><a href="{folder}/{label}.png" target="_blank"><img loading="lazy" src="{folder}/{label}.png" alt="{c[pose]} {label}" width="1672" height="941"></a><figcaption>{label} · {c[pose]}</figcaption></figure>' for label, pose in [('首帧', 'start'), ('尾帧', 'end')])
        review = '<span class="review">新锚点待评审</span>' if c['anchorReviewRequired'] else '<span class="existing">沿用已有锚点</span>'
        cards.append(f'''<article data-group="{c['group']}" data-priority="{c['priority']}" id="clip-{c['id']}">
<div class="cardtitle"><h2>{c['id']} · {c['name']}</h2>{review}</div>
<p class="meta">{c['priority']} · {c['group']} · {c['type']} · {c['suggestedSeconds']} 秒 · {c['start']} → {c['end']}</p>
<div class="pair">{pics}</div><p>{html.escape(c['motion'])}</p>
<div class="actions"><button class="copy" data-target="prompt-{c['id']}">复制完整提示词</button><a href="{folder}/提示词.txt" download>下载提示词</a><a href="{folder}/首帧.png" download>首帧原图</a><a href="{folder}/尾帧.png" download>尾帧原图</a><a href="{folder}/制作说明.md">制作说明</a></div>
<details><summary>查看完整提示词</summary><pre id="prompt-{c['id']}">{html.escape(prompt)}</pre></details></article>''')
    gallery = ''.join(f'<figure><a href="{a["file"]}" target="_blank"><img loading="lazy" src="{a["file"]}" alt="{pose} 候选锚点" width="1672" height="941"></a><figcaption>{pose} · 待用户评审</figcaption></figure>' for pose, a in anchors.items() if a['status'] == 'candidate-awaiting-user-review')
    options = ''.join(f'<option>{g}</option>' for g in dict.fromkeys(c['group'] for c in items))
    page = '''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>陈皮 · 待补动画制作包 v5</title><style>
*{box-sizing:border-box}body{font:16px/1.7 system-ui,"Microsoft YaHei",sans-serif;color:#35463e;background:#f4f1e9;margin:0}main{max-width:1180px;margin:auto;padding:32px 24px}h1{font-size:34px;line-height:1.3;margin:10px 0}h2{font-size:22px;margin:0}.eyebrow,.meta{color:#728176}.stats{display:flex;gap:12px;flex-wrap:wrap}.stats span{background:#e5eadf;border-radius:8px;padding:8px 14px}a{color:#286c58}header{padding:12px 0 22px}article,.intro{background:#fff;border:1px solid #dedfd3;border-radius:15px;padding:22px;margin:24px 0}.cardtitle{display:flex;justify-content:space-between;align-items:center;gap:14px;flex-wrap:wrap}.review,.existing{font-size:12px;border-radius:20px;padding:3px 12px;white-space:nowrap}.review{background:#f7ebd4;color:#896131}.existing{background:#eaf1e5;color:#47744b}.pair,.gallery{display:grid;grid-template-columns:1fr 1fr;gap:14px}figure{margin:0}img{width:100%;height:auto;display:block;border-radius:8px;background:#faf9f4}figcaption{font-size:13px;color:#728176;margin-top:6px}.actions,.filters{display:flex;align-items:center;gap:14px;flex-wrap:wrap}.actions{margin:18px 0}.actions a{font-size:14px}button,select,input{font:inherit;border:1px solid #b6c8b9;border-radius:7px;padding:8px 12px;background:white;color:#35463e}button{background:#286c58;color:white;cursor:pointer}input{min-width:0;flex:1}details{margin-top:16px}summary{cursor:pointer;color:#286c58}pre{font:14px/1.8 system-ui;white-space:pre-wrap;overflow-wrap:anywhere;background:#f7f8f2;padding:18px;border-radius:8px}.filters{padding:18px;background:#e8eadd;border-radius:10px}.filters label{display:flex;align-items:center;gap:8px}#notice{min-height:28px;margin:8px 0;color:#286c58}.gallery{margin-top:16px}#empty{padding:24px;text-align:center}footer{color:#728176;font-size:13px}@media(max-width:650px){main{padding:18px 12px}h1{font-size:27px}.pair,.gallery{grid-template-columns:1fr}article{padding:15px}.filters{align-items:stretch}.filters label{width:100%}.filters select{flex:1}.filters input{width:100%;flex:auto}.actions{gap:10px}.cardtitle h2{font-size:20px}}[hidden]{display:none!important}
</style></head><body><main><header><div class="eyebrow">CHENPI / VIDEO INPUTS / V5</div><h1>陈皮 · 待补动画制作包</h1><p>打开对应动作，上传首尾原图，再复制完整提示词。</p><div class="stats"><span>41 条动作</span><span>82 张首尾图</span><span>41 份提示词</span><span>8 张新候选锚点</span></div></header>
<section class="intro"><strong>与之前相同的交付方式</strong><p>每条目录均包含「首帧.png」「尾帧.png」「提示词.txt」「动作信息.json」「制作说明.md」。循环或单次回原姿态动作的首尾相同是有意设计，中间仍须完成动作。</p><p>新锚点先评审，再制作视频。开场继续等待独立跑步等返片，不启用替代版。相同画布不代表身体尺度或接触高度已通过。</p><p><a href="README.md">总览与开场流程</a> · <a href="制作清单.csv">下载制作清单</a> · <a href="静态审核.md">静态审核与待复核项</a></p><details><summary>查看 8 张新增候选锚点</summary><div class="gallery">GALLERY</div></details></section>
<div class="filters"><label>分组 <select id="group"><option value="">全部</option>OPTIONS</select></label><label>优先级 <select id="priority"><option value="">全部</option><option>P0</option><option>P1</option><option>P2</option><option>P3</option></select></label><input id="search" type="search" aria-label="搜索动作编号或名称" placeholder="搜索编号或动作名"></div><p id="notice" role="status" aria-live="polite">显示 41 / 41 条动作</p><p id="empty" hidden>没有匹配的动作，请清除筛选。</p>CARDS<footer>制作输入包 · 10张原图字节复用 / 8张 image_gen 候选 · 新视频及动态检测待返片</footer></main><script>
const cards=[...document.querySelectorAll('article')],notice=document.getElementById('notice');
function filter(){const group=document.getElementById('group').value,priority=document.getElementById('priority').value,query=document.getElementById('search').value.trim().toLowerCase();let count=0;for(const card of cards){card.hidden=!!((group&&card.dataset.group!==group)||(priority&&card.dataset.priority!==priority)||(query&&!card.querySelector('h2').textContent.toLowerCase().includes(query)));if(!card.hidden)count++;}notice.textContent=`显示 ${count} / 41 条动作`;document.getElementById('empty').hidden=count>0;}
for(const id of ['group','priority','search'])document.getElementById(id).addEventListener('input',filter);
document.querySelectorAll('.copy').forEach(button=>button.addEventListener('click',async()=>{const pre=document.getElementById(button.dataset.target);try{await navigator.clipboard.writeText(pre.textContent);notice.textContent='完整提示词已复制';button.textContent='已复制';setTimeout(()=>button.textContent='复制完整提示词',1800);}catch{pre.closest('details').open=true;const range=document.createRange();range.selectNodeContents(pre);const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);notice.textContent='浏览器未允许自动复制，已选中完整提示词，请按 Ctrl+C。';}}));
</script></body></html>'''.replace('GALLERY', gallery).replace('OPTIONS', options).replace('CARDS', ''.join(cards))
    (out/'index.html').write_text(page, encoding='utf-8', newline='\n')


def verify_pack(out, items, anchors):
    assert len(items) == 41 and len(anchors) == 18
    root = out.parents[2]
    for a in anchors.values():
        assert hashlib.sha256((out/a['file']).read_bytes()).hexdigest() == a['sha256']
        assert hashlib.sha256((root/a['source']).read_bytes()).hexdigest() == a['sha256']
    loops = 0
    for c in items:
        prompt = (out/c['promptFile']).read_text(encoding='utf-8')
        assert c['name'] in prompt and c['motion'] in prompt and '衔接：' in prompt
        for frame, pose in [('startFrame', 'start'), ('endFrame', 'end')]:
            p = out/c[frame]
            assert hashlib.sha256(p.read_bytes()).hexdigest() == anchors[c[pose]]['sha256']
            with Image.open(p) as im:
                assert im.size == (1672, 941)
                im.verify()
        if c['type'] == '循环':
            assert (out/c['startFrame']).read_bytes() == (out/c['endFrame']).read_bytes()
            loops += 1
        assert not c['runtimeEnabled']
    class Links(HTMLParser):
        def handle_starttag(self, tag, attrs):
            for k, v in attrs:
                if k in ('href', 'src') and not v.startswith(('#', 'http')):
                    assert (out/unquote(v)).is_file(), v
    Links().feed((out/'index.html').read_text(encoding='utf-8'))
    return dict(clips=41,anchors=18,newCandidateAnchors=8,endpointCopies=82,fullPrompts=41,
                byteIdenticalSharedEndpoints=True,loopEndpointsIdentical=loops,allImages1672x941=True,
                allLocalHtmlLinksExist=True,newAnchorsUserApproved=False,dynamicQA='pending returned video',runtimeChanged=False)

"""Build the approved v6 video-input pack from unchanged shared pose anchors."""
from pathlib import Path
import base64
import csv
import hashlib
import html
import io
import json
import shutil
import zipfile
import argparse

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "art/video-pipeline/behavior-v6"
ANCHORS = ROOT / "art/video-pipeline/completion-v5/anchors"
ROWS = [
    (140, "摸头眯眼蹭手", "F", "头脸", "3—4", "正面坐姿，感到额头或脸颊被轻触，先自然眯眼，再把额头和脸颊小幅向前侧蹭一次，轻抬下巴，最后恢复原正面坐姿。无叫声、无露齿笑，不站起。", "完成一次反应后回到正面坐姿；现有37/38作为身份与节奏参考，不拼接冒充新片。"),
    (141, "右向摸背拱背伸展", "SR", "背部", "3—4", "朝右四足站立，像背部被温柔抚摸，肩背缓缓拱起，前爪小幅向前伸展，后腿稳定支撑，然后背线放松、四足归位。不是惊吓炸毛或攻击姿态。", "左右单独制作；承重脚不滑动，拱背不得变成长身或缩头。"),
    (142, "左向摸背拱背伸展", "SL", "背部", "3—4", "朝左四足站立，像背部被温柔抚摸，肩背缓缓拱起，前爪小幅向前伸展，后腿稳定支撑，然后背线放松、四足归位。不是惊吓炸毛或攻击姿态。", "使用左向原图，不镜像右向视频。"),
    (143, "摸肚蜷身护肚轻拨", "B", "肚子", "3—4", "从露肚平躺姿态开始，像肚子被轻触，前爪收向胸腹，后腿轻轻屈起，身体小幅蜷缩护住肚子，再用一只前爪朝假想来手轻拨一下，放松并恢复首帧露肚姿态。没有抓伤、咬人、凶狠表情或叫声，不翻出画布。", "保持原露肚姿态方向；肢体始终四条，胸腹支撑连续，尾巴不作为支撑基准。"),
    (144, "碰爪伸爪碰手", "F", "爪子", "2—3", "从正面坐姿开始，目光向前下方的假想手指，抬起一只前爪，轻轻向前伸出碰一下，短暂停留后收回原支撑位置。另一前爪稳定承重，最后恢复原坐姿。只碰一次，不连续挥舞。", "不生成真实手；爪子伸出方向供返片后标注命中与目标接触点。"),
    (145, "右向碰尾回头甩尾", "SR", "尾巴", "2—3", "朝右四足站立，像尾巴被轻触，头和视线转向身后的尾巴位置，耳朵随之轻转，尾巴柔和但明确地甩动一次，随后头与尾恢复首帧姿态。四爪和躯干朝向保持，不转身、不走开、不炸毛。", "回头只是头颈动作，身体仍朝右；只一次尾部反应。"),
    (146, "左向碰尾回头甩尾", "SL", "尾巴", "2—3", "朝左四足站立，像尾巴被轻触，头和视线转向身后的尾巴位置，耳朵随之轻转，尾巴柔和但明确地甩动一次，随后头与尾恢复首帧姿态。四爪和躯干朝向保持，不转身、不走开、不炸毛。", "回头只是头颈动作，身体仍朝左；不镜像。"),
    (147, "朝右轻跳落稳", "SR", "埋砂衔接", "1.5—2.5", "朝右四足站立，先轻屈后腿蓄力，再由后腿推起，前爪向前探出，身体短暂腾空，前爪先接地、后爪随后落稳，最后回到与首帧一致的朝右站姿。表现一次小猫跨出低盆沿的轻跳，不能变成走路、被拎起、垂直漂浮或后退。", "素材采用固定根节点的原地跳跃动作，保留身体腾空曲线；游戏接入时另行安排水平行程与盆面到地面的支撑高度。首尾图相同不代表中间静止。返片需验证蓄力、离地、腾空、前后爪落地四阶段。"),
    (148, "朝左轻跳落稳", "SL", "埋砂衔接", "1.5—2.5", "朝左四足站立，先轻屈后腿蓄力，再由后腿推起，前爪向前探出，身体短暂腾空，前爪先接地、后爪随后落稳，最后回到与首帧一致的朝左站姿。表现一次小猫跨出低盆沿的轻跳，不能变成走路、被拎起、垂直漂浮或后退。", "独立左向动作，不镜像；素材采用固定根节点，世界行程与支撑落差在返片后逐帧校准。"),
    (149, "朝右低头定位嗅闻", "SR", "埋砂衔接", "2—3", "朝右四足站立，头颈向前下方缓缓探出，鼻尖接近前方地面的一个固定目标点，闭嘴轻嗅一至两次，小幅鼻翼和头颈运动，然后抬头恢复原朝右站姿。不要舔舌、咀嚼、刨砂或迈步，不产生任何气味烟雾。", "接入时鼻尖实际对准本次污物坐标，返片测量鼻尖最低接触点；随后再接已有抬爪、刨砂和收爪，不用吃喝低头代替嗅闻。"),
    (150, "朝左低头定位嗅闻", "SL", "埋砂衔接", "2—3", "朝左四足站立，头颈向前下方缓缓探出，鼻尖接近前方地面的一个固定目标点，闭嘴轻嗅一至两次，小幅鼻翼和头颈运动，然后抬头恢复原朝左站姿。不要舔舌、咀嚼、刨砂或迈步，不产生任何气味烟雾。", "独立左向嗅闻；已有刨砂为右向，接入时必须真实转向并重新对准目标，不能镜像或跨姿态硬切。"),
    (151, "朝右对准羽毛伸爪抓取", "SR", "逗猫棒抓取", "2—3", "朝右四足站立，眼睛始终看向右前方、约鼻尖高度的同一个假想羽毛目标。肩部小幅前探，一只前爪迅速而清楚地向该目标伸出，爪掌轻合拢抓取，随即向胸前勾回，最后前爪落回原支撑位置并恢复首帧站姿。只完成一次完整的伸爪、合爪、勾回、落爪；其余三爪稳定承重，身体不跳起、不横移。", "这是朝羽毛目标主动抓取，不是刨砂、碰手、原地招手或四肢乱挥。不要画出羽毛或逗猫棒，运行时另绘；返片需标注爪尖伸出最远点、合爪帧和落爪帧，并据真实触点校准停步距离。左右分别制作，保留既有独立跑步原速。"),
    (152, "朝左对准羽毛伸爪抓取", "SL", "逗猫棒抓取", "2—3", "朝左四足站立，眼睛始终看向左前方、约鼻尖高度的同一个假想羽毛目标。肩部小幅前探，一只前爪迅速而清楚地向该目标伸出，爪掌轻合拢抓取，随即向胸前勾回，最后前爪落回原支撑位置并恢复首帧站姿。只完成一次完整的伸爪、合爪、勾回、落爪；其余三爪稳定承重，身体不跳起、不横移。", "独立制作左向主动抓取，不镜像右向片；不要变成刨砂或点爪碰手。目标不画入视频，游戏接入时以真实爪尖触点对准羽毛，羽毛移远恢复追跑，跨侧须真实转向。"),
    (153, "朝右站起高位快速抓取落地", "SR", "逗猫棒抓取", "1.5—2", "从朝右四足站姿开始，眼睛锁定右前上方、超过四足站姿伸爪高度的同一个假想羽毛目标。后腿短促发力并保持脚掌承重，前身迅速抬起，站到后腿上；两只前爪随前身抬起，朝右前上方目标快速探出、合拢抓取一次，立即屈肘收回，身体自然下落，两只前爪接地缓冲，最后恢复首帧朝右四足站姿。起身、抓取、落地连续利落，无长时间蓄势、直立停顿或慢动作。只抓一次，后脚不离地、不横移，不做全身腾空跳跃。", "这是高位羽毛触发的站起抓取，低位继续用151。必须看清后腿支撑起身、前爪朝上前方主动抓取、前脚落地缓冲三个阶段，不能只抬一只爪点一下。首尾复用SR原图，四足仅在首尾恢复原接触线，中途前身必须升起；不得拉长躯干或缩小角色来容纳动作。不要画出目标或逗猫棒。返片标注最高爪尖、合爪与前脚落地帧，确认全身不裁切；原片应直接表现快速节奏，不通过运行时加速旧片替代。"),
    (154, "朝左站起高位快速抓取落地", "SL", "逗猫棒抓取", "1.5—2", "从首帧朝左四足站姿开始，先看向左前上方的假想羽毛。两只后脚掌牢牢留在首帧地面位置，身体重心留在后脚上方，只适度抬起胸口和前身；不要把身体伸成接近竖直的长条。两只前爪朝左前上方短促伸出，合拢抓取一次，随即收爪、降低前身，两只前爪落回地面缓冲，恢复尾帧四足站姿。起身、抓取、落地是一次连续利落的动作，无直立停顿。高位仅指四足伸爪够不到、适度起身即可触及的高度，不是头顶远处。", "154重制：上一版站起时耳朵和头顶越过画面上沿，而且有向左扑出的趋势，必须消除。整个动作从第一帧到最后一帧，耳尖和头顶距画面上沿始终至少保留画面高度10%的空白，前爪和鼻尖距左边至少保留10%，尾尖不越界。动作幅度以此安全区为上限；接近边界时降低起身角度和抓取目标高度，绝不能通过缩小猫、移动镜头、拉长身体、后脚下移或切掉耳朵来容纳。后脚不离地、不滑动，不全身跳起、不向左扑出、不腾空漂浮。保持首帧头身比例与躯干长度，头和躯干只自然转动。首尾仍用SL同一张原图，不能把中间站起理解为全程四足不动。不要画羽毛或逗猫棒；独立左向制作，不镜像。若平台只能输出4秒，把完整抓取集中在中段约1.5—2秒，余下仅自然首尾稳定，不把抓取拉成慢动作。"),
]
COMMON = """固定镜头、固定光线、浅暖灰背景、2D绘本厚涂。严格保持首尾图陈皮的蓝灰短毛、金色眼睛、短深鼻、自然灰色嘴垫、轻嘴线、粗尾和成年猫头身比例。自身左眼始终略小，正面是画面右眼略小，侧面不强行转正来展示大小眼。保持同组细密短毛笔触与灰阶，不新增条纹。全身、耳尖、尾巴和四肢完整可见。保持输入1672×941比例、主体尺寸与镜头位置，不缩放、不镜像、不推拉、不裁切。无盆、水、食物、猫砂、粪便、逗猫棒、人手、文字、气泡、爱心或其他特效；道具在运行时单独绘制。不得多腿、少腿、肢体穿透或凭空变化。

动作：{motion}

建议时长：{seconds}秒。一次完整动作，首尾精确恢复参考图的姿态、位置、尺寸、脚掌或胸腹接触线。中间自然运动，不在首尾额外长时间冻结；不剪掉起势和收势。若平台只能输出较长时长，保留完整原片，不自行加速或抽帧。

专项要求：{note}
"""

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--zip',action='store_true',help='Explicitly rebuild the input-only ZIP');args=parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    source_path=OUT/'returned/sources.json'
    returned={r['id']:r for r in json.loads(source_path.read_text(encoding='utf-8'))} if source_path.exists() else {}
    clips=[]
    image_data={}
    anchor_hashes={}
    for key in sorted({r[2] for r in ROWS}):
        data=(ANCHORS/f"{key}.png").read_bytes()
        image_data[key]="data:image/png;base64,"+base64.b64encode(data).decode()
        anchor_hashes[key]=hashlib.sha256(data).hexdigest()
    for num,name,pose,group,seconds,motion,note in ROWS:
        folder=OUT/f"{num}-{name}";folder.mkdir(exist_ok=True)
        for endpoint in ["首帧.png","尾帧.png"]:
            shutil.copyfile(ANCHORS/f"{pose}.png",folder/endpoint)
        prompt=COMMON.format(motion=motion,seconds=seconds,note=note)
        (folder/"提示词.txt").write_text(prompt,encoding="utf-8",newline="\n")
        meta=dict(id=num,name=name,group=group,start=pose,end=pose,suggestedSeconds=seconds,
            status="awaiting-video",runtimeEnabled=False,sourceAnchor=f"../completion-v5/anchors/{pose}.png",
            anchorSha256=anchor_hashes[pose],folder=folder.name,startFrame=f"{folder.name}/首帧.png",endFrame=f"{folder.name}/尾帧.png",promptFile=f"{folder.name}/提示词.txt")
        if num in returned:
            meta.update(status='local-runtime-review',runtimeEnabled=True,video=f'returned/{num}/source.mp4?v={returned[num]["sha256"][:12]}',frames=returned[num]['frames'],fps=returned[num]['fps'],duration=returned[num]['duration'],sourceSha256=returned[num]['sha256'])
        (folder/"动作信息.json").write_text(json.dumps(meta,ensure_ascii=False,indent=2)+"\n",encoding="utf-8",newline="\n")
        (folder/"制作说明.md").write_text(f"# {num} {name}\n\n首尾：{pose} → {pose}，建议{seconds}秒，只完成一次。\n\n{motion}\n\n{note}\n\n上传本文件夹首帧.png、尾帧.png，复制提示词.txt全文，返片文件命名为{num}-{name}.mp4。两张输入复用现有共享锚点原字节。当前没有返片，未启用运行时，也未通过动态验收。\n",encoding="utf-8",newline="\n")
        clips.append(meta|dict(prompt=prompt,note=note))
    manifest=dict(version=2,status="local-runtime-review" if returned else "awaiting-video",runtimeChanged=bool(returned),clips=[{k:v for k,v in c.items() if k not in ["prompt","note"]} for c in clips],anchorHashes=anchor_hashes)
    (OUT/"manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8",newline="\n")
    text=io.StringIO();writer=csv.writer(text,lineterminator="\n");writer.writerow(["编号","动作","部位或用途","首尾","建议秒数","状态"])
    for row in ROWS:writer.writerow([row[0],row[1],row[3],row[2]+" → "+row[2],row[4],"已返片／本地接入待审" if row[0] in returned else "待视频返片"])
    (OUT/"制作清单.csv").write_text(text.getvalue(),encoding="utf-8-sig",newline="\n")
    (OUT/"README.md").write_text("""# 陈皮大更新 v6 动画输入包

用户2026-10-15确认五部位互动：头脸眯眼蹭手、背部拱背伸展、肚子蜷身护肚轻拨、爪子伸爪碰手、尾巴回头甩尾。另补齐如厕后跳出猫砂盆、回头走近污物嗅闻的缺失动作。

15条新片140—154：140—146为五部位互动（背/尾含独立左右向），147—150为独立左右轻跳与嗅闻，151/152保留现有左右低位伸爪抓取，153/154为用户追加的左右高位站起快速抓取后落地。首尾复用v5共享锚点F/SR/SL/B原字节，不重画角色、不镜像。每条均附首帧、尾帧、独立完整提示词、动作信息和说明。index.html内嵌原图，可独立离线浏览与下载；本轮只更新本地目录与索引；历史ZIP不含153/154，不重打包。

低位151/152原提示词、首尾图与建议时长保持不变。高位先试153右向，确认后腿撑起前身、朝上前方快速抓取、前爪落地可辨认，再制作154左向；新片建议1.5—2秒，不长时间蓄势或直立停顿。147右轻跳与149右嗅闻核对蓄力/腾空/落地和鼻尖触点，再扩展左向与互动。这里只是投片资料；没有生成新视频，没有动态通过，也未把这些占位接入正式运行包。

已有108—113跑步和88单次叫声继续复用。旧35/36确实是左右抓棒返片，本轮已修复被反复打断的问题；用户明确要求另做专用抓取，故旧动作只作当前基线，不能算新抓取已完成。新流程为独立追跑→停稳并朝向羽毛→按羽毛相对当前支撑面的高度，选择151/152低位伸爪或153/154高位站起快速抓取后落地→视目标距离继续抓取或追跑；返片后以爪尖实际触点校准距离与循环衔接，不把爪尖任意拉伸到鼠标。高低分界按低位返片实际爪尖可及高度校准，不先写死屏幕像素；单次完整抓取内不因高度跨界硬切片，回到四足后再选择下一次动作。四段已返片并接入本地候选；154已替换重返版，旧片归档在returned/superseded。高位仍有原片上沿接触帧待美术复核，未宣称全项通过。

埋砂最终顺序为如厕结束→选有效出口并转向→跳出落稳→真实回头→走向本次污物→嗅闻→对准刨砂站位→29/30/31→离开。返回时按实际接触位置重新进入盆面支撑；不承诺从盆外跨越盆宽凭空刨到污物。清理或搬动必须取消或重新定位当前目标，不重复添加污物。

返片仍沿已确认的外部图生视频流程。保留原视频、原帧率和完整帧数，实际时长另测；随后拆帧、身份/脚底/尺寸/循环/接缝/家具遮挡审计，再接入。睡眠点击先合法醒来再响应部位，长按拖拽保留；部位命中需跟随当帧姿态及可见前景，不能用屏幕固定矩形冒充。
""",encoding="utf-8",newline="\n")
    payload=json.dumps(dict(clips=clips,images=image_data),ensure_ascii=False).replace("</","<\\/")
    page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>陈皮 · 第六批动画制作</title>
<style>*{box-sizing:border-box}body{margin:0;background:#f1eee8;color:#30382e;font:16px/1.7 system-ui,sans-serif}header,main{max-width:1200px;margin:auto;padding:32px}h1{font-size:36px;margin:0}h2{margin:0 0 8px}header p{max-width:900px;color:#5b6256}input,select,button{font:inherit;padding:8px 14px;border:1px solid #a5ad9c;border-radius:8px;background:#fff}nav{display:flex;gap:12px;flex-wrap:wrap}article{background:#fffdf9;border:1px solid #deded1;border-radius:16px;padding:24px;margin-bottom:24px}.meta{color:#626b5b}.images{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:12px 0}img{display:block;width:100%;background:#eee;border-radius:8px}figcaption{display:flex;justify-content:space-between;gap:10px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:15px/1.7 system-ui;background:#f1f2eb;padding:20px;border-radius:10px}a{color:#46633a}.notice{border-left:4px solid #8b9b66;padding:12px 20px;background:#e5e9db}@media(max-width:700px){.images{grid-template-columns:1fr}header,main{padding:20px}h1{font-size:28px}}</style>
<header><div>CHENPI / VIDEO INPUT · V6</div><h1>部位互动、抓棒与埋砂衔接</h1><p>五部位互动：头脸、背部、肚子、爪子、尾巴。低位沿用151／152伸爪抓取；高位新增153／154站起快速抓取后落地。另含跳出猫砂盆与定位嗅闻；左右动作独立制作。</p><p class="notice">15条待返片 · 30张首尾图沿用原共享锚点。当前是制作输入包，没有新视频或动态验收。每条先上传首尾图，再复制完整提示词。</p><nav><input id="q" placeholder="搜索编号或动作" aria-label="搜索编号或动作"><select id="g" aria-label="筛选用途"><option value="">全部用途</option></select><span id="count"></span></nav></header><main id="list"></main>
<script type="application/json" id="data">'''+payload+'''</script><script>
const data=JSON.parse(document.querySelector('#data').textContent),list=document.querySelector('#list'),query=document.querySelector('#q'),group=document.querySelector('#g');
for(const g of new Set(data.clips.map(c=>c.group))){let o=document.createElement('option');o.value=o.textContent=g;group.append(o)}
function download(name,dataUrl){let a=document.createElement('a');a.href=dataUrl;a.download=name;a.click()}
function textDownload(name,text){let u=URL.createObjectURL(new Blob([text],{type:'text/plain;charset=utf-8'}));download(name,u);setTimeout(()=>URL.revokeObjectURL(u),1000)}
function render(){list.replaceChildren();const rows=data.clips.filter(c=>(!group.value||c.group===group.value)&&(`${c.id} ${c.name} ${c.group}`).includes(query.value.trim()));document.querySelector('#count').textContent=`${rows.length} / ${data.clips.length} 条`;for(const c of rows){const a=document.createElement('article');const h=document.createElement('h2');h.textContent=`${c.id} · ${c.name}`;a.append(h);const m=document.createElement('div');m.className='meta';m.textContent=`${c.group} · ${c.start} → ${c.end} · 建议 ${c.suggestedSeconds} 秒 · 待视频返片`;a.append(m);const imgs=document.createElement('div');imgs.className='images';for(const label of ['首帧','尾帧']){let f=document.createElement('figure'),img=document.createElement('img'),cap=document.createElement('figcaption'),b=document.createElement('button');img.src=data.images[c.start];img.alt=`${c.id} ${label} ${c.start}`;b.textContent='下载原图';b.onclick=()=>download(`${c.id}-${label}.png`,img.src);cap.append(label,b);f.append(img,cap);imgs.append(f)}a.append(imgs);let note=document.createElement('p');note.textContent=c.note;a.append(note);let pre=document.createElement('pre');pre.textContent=c.prompt;a.append(pre);let copy=document.createElement('button');copy.textContent='复制完整提示词';copy.onclick=async()=>{try{await navigator.clipboard.writeText(c.prompt);copy.textContent='已复制'}catch{const r=document.createRange();r.selectNodeContents(pre);const s=getSelection();s.removeAllRanges();s.addRange(r);copy.textContent='已选中，请按 Ctrl/Cmd+C'}};let save=document.createElement('button');save.textContent='下载提示词';save.onclick=()=>textDownload(`${c.id}-提示词.txt`,c.prompt);a.append(copy,' ',save);list.append(a)}}
query.oninput=group.onchange=render;render();</script></html>'''
    (OUT/"index.html").write_text(page,encoding="utf-8",newline="\n")
    if returned:
        summary=f"140—154 共 {len(returned)} 段已返片，共 {sum(r['frames'] for r in returned.values()):,} 原帧。151—154 两组抓取已接入本地候选；154已换为10月16日重返版。高位153有5帧、154有3帧触及原片上沿，保留美术待审标记。149 嗅闻高度与145回头方向仍待审。未提交、未发布。"
        page=page.replace('15条待返片 · 30张首尾图沿用原共享锚点。当前是制作输入包，没有新视频或动态验收。每条先上传首尾图，再复制完整提示词。',summary)
        page=page.replace('· 待视频返片`;a.append(m);','· ${c.video?`已返片 ${c.frames}帧 / ${c.fps}fps · ${c.runtimeEnabled?"本地接入待审":c.id===154?"需重制 · 新提示词已更新":"原片待审 · 暂未启用"}`:"待视频返片"}`;a.append(m);if(c.video){let v=document.createElement("video");v.src=c.video;v.controls=true;v.preload="metadata";v.style="display:block;width:100%;max-height:480px;background:#e5e0d8";v.setAttribute("aria-label",`${c.id} 返片预览`);a.append(v)};')
        page=page.replace('<main id="list">','<p style="max-width:1136px;margin:auto"><a href="runtime-review.jpg">查看运行时场景截图</a> · <a href="grab-review.html">151—154抓取验证记录</a> · <a href="review.html">140—150验证记录</a></p><main id="list">')
        (OUT/'index.html').write_text(page,encoding='utf-8',newline='\n')
        readme=OUT/'README.md';readme.write_text('> 当前返片状态：'+summary+'\n\n'+readme.read_text(encoding='utf-8').replace('这里只是投片资料；没有生成新视频，没有动态通过，也未把这些占位接入正式运行包。','以下保留原投片要求；当前本地接入状态见上方，正式包未更新。'),encoding='utf-8')
        for c in clips:
            if c['id'] in returned:
                p=OUT/c['folder']/'制作说明.md';p.write_text(p.read_text(encoding='utf-8').replace('当前没有返片，未启用运行时，也未通过动态验收。',f"已收到 {c['frames']} 帧、{c['fps']:g} fps 返片。"+("已接入本地候选；待用户评审，未发布。" if c["runtimeEnabled"] else "154原片头顶裁切，用户要求重制；本目录提示词已修订，当前视频仍是旧返片。" if c["id"]==154 else "高位原片待审，暂未启用运行分支。")),encoding='utf-8')
    if not args.zip:
        print('Updated local review index; no ZIP or release produced.');return
    zip_path=ROOT/"artifacts/behavior-v6/陈皮-v6-部位互动抓棒与埋砂-首尾帧提示词.zip"
    zip_path.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(zip_path,"w",zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for path in sorted(OUT.rglob("*")):
            if path.is_file() and 'returned' not in path.relative_to(OUT).parts:z.write(path,Path("陈皮-v6")/path.relative_to(OUT))
    with zipfile.ZipFile(zip_path) as z:assert z.testzip() is None
    for c in clips:
        for key in ["startFrame","endFrame"]:assert hashlib.sha256((OUT/c[key]).read_bytes()).hexdigest()==c["anchorSha256"]
    evidence=dict(clips=len(clips),endpointCopies=len(clips)*2,uniqueAnchors=len(image_data),endpointHashesMatch=True,zipCrcPassed=True,zipSha256=hashlib.sha256(zip_path.read_bytes()).hexdigest(),zipBytes=zip_path.stat().st_size,status="input-package-only-no-video")
    (zip_path.parent/"validation.json").write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding="utf-8",newline="\n")
    print(json.dumps(evidence,ensure_ascii=False))

if __name__=="__main__":main()

"""Package existing anchor bytes and approved action descriptions for video handoff."""
from pathlib import Path
import hashlib, json, shutil, zipfile
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'art/video-pipeline/basic-v1'
anchors={'W':'W-右行.png','S':'S-站姿.png','I':'I-坐姿.png','C':'C-蜷睡.png'}
prefix='固定机位、固定构图的一镜到底2D绘本厚涂动画。严格保持首尾图中的同一只成年蓝灰短毛猫、金黄色眼睛、短深色鼻口、自然灰色嘴垫、轻嘴线、厚实尾巴和笔触。保持真实体型与头部大小，不能为填满画面而缩放。全身完整可见，浅暖灰背景、镜头和光线固定；只有这一只猫，没有道具、地面阴影、文字或符号。四肢连接和近远遮挡连续，身体移动仅限姿态变化所需的自然重心调整。'
loop='结束时准确回到尾帧的姿态、大小、位置和动作相位；接缝前后运动连续，不增加首尾定格，不整团缩放，不突然换脸。'
transition='在前约4.7秒内连贯完成这一个动作，最后约0.3秒安定保持尾帧姿态。准确衔接尾图的角度、位置和比例；姿态变化由关节连续运动完成，不溶解变形、不突然切图、不先做相反动作。'
rows=[
 ('01-待机眨眼','I','I','循环','端坐、慢眨眼、轻呼吸','猫安静端坐，前爪和臀部接地点稳定。5秒中缓慢完整闭眼一次，再自然睁开，恢复原视线；胸腹只有极轻微呼吸，头和尾巴基本安定，不抬爪、不走动。'),
 ('02-站立起步','S','W','过渡','重心前移、依次迈步、进入步态','猫始终朝右，从四脚落地的站姿轻微前移重心，后脚开始推进，前脚依次抬起迈出，原地自然走出一两步，进入尾图的步态相位。身体中心不在画面里横向行进，最终以尾图抬爪姿态短暂稳定，供后续行走循环接续。'),
 ('03-行走停步','W','S','过渡','完成短步、四爪落地、平稳站住','从首帧朝右步态接着完成最后一两次短步，逐渐减速，悬空脚依次自然落地承重，四脚收回尾图中性站位。身体不在画面内行进，没有滑步或后退，尾巴自然安定。'),
 ('04-站立坐下','S','I','过渡','前爪支撑、后腿弯曲、臀部坐实','猫以前脚支撑，后腿关节自然弯曲，臀部向后下方缓慢坐实，胸部逐渐直立，粗尾绕到身旁。前爪仅作必要的小步调整；头部在整个动作中逐渐从朝右侧面转到尾图略朝镜头的角度，不能最后一刻突然转头。'),
 ('05-坐稳发呆','I','I','过渡','肩部放松、安定坐姿、平视','猫保持端坐，微微放松肩部并调整胸前重量，头部作一次幅度很小的安定动作，视线回到首尾图的平视位置。前爪、臀部、尾巴接地点保持不变，身体不整体下沉或膨胀，不再次坐下。'),
 ('06-坐姿起身','I','S','过渡','重心向前、后腿伸展、四脚站稳','猫从端坐向前移重心，后脚踏稳，后腿逐渐伸展抬起臀部，胸背回到自然四足站姿；前脚依次小幅调整，头部连续转向右方，最后四只脚按尾图落稳。自然完成起身，不能像倒放坐下那样滑动抬升。'),
 ('07-坐姿入睡','I','C','过渡','俯胸、屈肘、蜷身、枕爪闭眼','猫先俯下胸部、前肘缓慢弯曲，后腿自然收向腹侧，臀部稳定落下，背部弯成放松的C形，头逐渐降到右侧前爪上，粗尾环绕身体前方，再合眼安定。头始终留在身体右侧，臀部在左侧；不得瞬间左右对换或翻滚，不出现猫窝。'),
 ('08-蜷睡呼吸','C','C','循环','蜷睡、浅呼吸、鼻尖枕点固定','猫保持首尾图的自然蜷睡姿态，闭眼，头枕前爪，尾巴环绕。5秒内只有一到两次极轻微、缓慢的胸腹呼吸；鼻尖枕点、前爪、臀部和尾根稳定，背部幅度极小，整只猫的体积与画面占比不变，不抬头、不睁眼。'),
 ('09-睡醒坐起','C','I','过渡','睁眼抬头、前爪撑起、恢复端坐','猫先缓缓睁眼、抬起头，尾巴轻轻松开，前爪伸展支撑胸部，后躯保持承重，再自然恢复尾图端坐姿态与视线。头始终在身体右侧，整个起身过程关节连续，不能把蜷睡整团拉长或把入睡视频倒放。'),
]
negative='多余或缺失四肢、左右腿互换、关节反折、脚掌融化、尾巴变腿、双尾、头身比例变化、白色嘴套、张嘴说话、换脸、幼猫化、3D化、照片化、毛纹闪烁、整团缩放、无支撑悬浮、画面内行进、镜头推拉摇移、背景变化、猫窝、碗、手、字幕、气泡、符号、光圈。'
manifest={'status':'anchors-prepared-for-user-video-generation','anchorApproval':'W approved through user right-walk preview; S/I/C newly generated and not yet video-validated','anchors':{},'actions':[]}
for key,name in anchors.items():
 p=OUT/'anchors'/name
 with Image.open(p) as im:
  assert im.size==(1672,941),(name,im.size)
  size=im.size
 manifest['anchors'][key]={'file':'anchors/'+name,'size':size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
lines=['# 陈皮基础动作视频包','', '右移动视频已获用户认可。这次提供9条基础动作，按编号每个文件夹生成一条视频。', '',
 '## 上传方法','', '每个动作文件夹中：`首帧.png` 上传首帧，`尾帧.png` 上传尾帧，复制 `提示词.txt` 全文。`关键词.txt` 用于快速查看动作，不必再和完整提示词重复叠加。', '',
 '统一先用5秒、原生帧率；沿用成功右移动视频的工具、模型与风格设置。分辨率优先1080p（若该模式支持）；原图1672×941，保持比例，不单独裁切、放大某个端点。关闭运镜、自动配乐和风格重绘。', '',
 '循环首尾相同；过渡共用相同锚点原字节。若平台只有首帧输入，就无法真正锁定尾帧。尾图不是单独重画的新猫。', '',
 '起步尾部的短定格用于端点稳定；返片后由助手剪掉定格并对齐循环相位，不把停住的尾帧接进连续迈步。', '',
 '## 动作清单','', '| 文件夹 | 首帧 → 尾帧 | 类型 | 关键词 |','| --- | --- | --- | --- |']
for folder,a,b,kind,keywords,motion in rows:
 d=OUT/folder;d.mkdir(exist_ok=True)
 shutil.copy2(OUT/'anchors'/anchors[a],d/'首帧.png');shutil.copy2(OUT/'anchors'/anchors[b],d/'尾帧.png')
 prompt=prefix+'\n\n'+motion+'\n\n'+(loop if kind=='循环' else transition)+'\n'
 (d/'提示词.txt').write_text(prompt,encoding='utf-8');(d/'关键词.txt').write_text(keywords+'\n',encoding='utf-8')
 lines.append(f'| [{folder}]({folder}/提示词.txt) | {a} → {b} | {kind} | {keywords} |')
 manifest['actions'].append({'folder':folder,'startAnchor':a,'endAnchor':b,'type':kind,'seconds':5,'keywords':keywords})
lines += ['', 'W=已认可右行相位；S=站姿；I=坐姿；C=蜷睡。右行循环已有，不必重做。', '',
 '建议先返01、04、07、08，检查坐姿和睡姿的身份一致性，再补其余过渡。每条保留原始MP4/MOV，以文件夹编号命名，附工具/模型与生成参数；不要加速、补帧或拼接。', '',
 '## 当前检查与边界','', '三个新锚点由内置 image_gen 分别生成，均已目视检查，采用相同1672×941画布，保留原始输出。站姿四肢可辨、坐姿前爪承重、睡姿头在右侧。姿态变化会改变轮廓和接触部位，不以同画布冒充像素级姿态一致。生成视频后仍要检查角色大小、四肢遮挡、地面接触和循环速度；本包不是九条动画已经验收。', '',
 '关键词排除项见 `排除项.txt`，只在工具支持负向提示栏时使用。']
(OUT/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
(OUT/'排除项.txt').write_text(negative+'\n',encoding='utf-8')
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for action in manifest['actions']:
 for filename,key in [('首帧.png',action['startAnchor']),('尾帧.png',action['endAnchor'])]:
  assert hashlib.sha256((OUT/action['folder']/filename).read_bytes()).hexdigest()==manifest['anchors'][key]['sha256']
archive=OUT.parent/'陈皮-基础动作首尾帧与提示词-v1.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
 for p in OUT.rglob('*'):
  if p.is_file():z.write(p,p.relative_to(OUT))
with zipfile.ZipFile(archive) as z:assert z.testzip() is None
print(f'Validated 4 anchors, 9 actions, 18 endpoint copies; archive {archive}')

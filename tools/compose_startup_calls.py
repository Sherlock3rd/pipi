"""Compose three visible calls from the supplied two-call video, without new pixels."""
from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[1];path=ROOT/'assets/pets/bluecat/manifest.json'
m=json.loads(path.read_text(encoding='utf-8'));key='video-117';d=m['clips'][key]
native=[f'video-completion-v5/video-117/{i:03}.png' for i in range(97)]
indices=list(range(48))+list(range(48))+list(range(48,97))
if len(m['animations'][key])==97:
    d['groundContacts']=[d['groundContacts'][i] for i in indices]
elif len(m['animations'][key])!=145:raise ValueError('Unexpected 117 runtime frame count')
m['animations'][key]=[native[i] for i in indices]
d['sourceFrameIndices']=indices
d['editNote']='Source has two mouth cycles. Repeat the complete first open-close cycle once at a closed-mouth boundary; 24 fps, original source bytes retained.'
path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
cue=ROOT/'art/video-pipeline/completion-v5/returned/voice-cues.json'
cue.write_text(json.dumps({'FrameNumbering':'zero-based runtime index; ClosedFrame exclusive','SourceOpenWindows':[[12,33],[61,81]],'SourceFrames':97,'RuntimeFrames':145,'Composition':'source 0..47 + 0..47 + 48..96; no generated or interpolated art','Bindings':[[117,12,33,['meow1']],[117,60,81,['meow3']],[117,109,129,['meow4']]]},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('117: 97 source frames -> 145 frame references; three complete calls at 12,60,109')

"""Reject a functionally running release that starves authored 24 fps animation."""
import json
import pathlib
import sys

data = json.loads(pathlib.Path(sys.argv[1]).read_text())
assert data['CachedFrameBytes'] <= 96 * 1024 * 1024, data['CachedFrameBytes']
assert not data['StartupActive'], 'Startup did not finish in the 85-second smoke test'
assert data['Frames'] >= 1900, ('Insufficient animation callbacks', data['Frames'])
assert data['P95Milliseconds'] <= 45, ('Animation cadence regression', data['P95Milliseconds'])
assert 'video-118' in {f['Clip'] for f in data['ClipTransitions']}, 'Incomplete greeting'
print('PASS native cadence, complete greeting, bounded cache:',
      {k: data[k] for k in ['WorkingSet', 'Frames', 'P95Milliseconds', 'CpuMilliseconds']})

"""Sample the real self-contained desktop process; never use a personal save."""
import json
import pathlib
import subprocess
import sys
import time

exe = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
child = subprocess.Popen([str(exe), '--data-dir', str(output / 'save'),
                          '--snapshot', str(output / 'scene.png'), '--exit-after', '300'])
started = time.monotonic()
samples = []
try:
    while child.poll() is None:
        elapsed = time.monotonic() - started
        if elapsed > 360:
            raise TimeoutError('Five-minute desktop run did not exit')
        result = subprocess.run(['ps', '-o', 'rss=', '-p', str(child.pid)], capture_output=True, text=True, check=False)
        if result.returncode == 0 and result.stdout.strip():
            sample = dict(seconds=round(elapsed, 2), rssBytes=int(result.stdout.strip()) * 1024)
            samples.append(sample)
            print(json.dumps(sample), flush=True)
        time.sleep(10)
    assert child.returncode == 0, child.returncode
    assert not (output / 'save/error.log').exists()
    metrics = json.loads((output / 'scene.png.json').read_text())
    assert metrics['CachedFrameBytes'] <= 96 * 1024 * 1024
    assert not metrics['StartupActive']
    assert len(samples) >= 29
    report = dict(durationSeconds=300, samples=samples,
                  metrics={k: v for k, v in metrics.items() if k not in ('slowFrames', 'ClipTransitions')},
                  warmMinimum=min(s['rssBytes'] for s in samples if s['seconds'] >= 90),
                  warmMaximum=max(s['rssBytes'] for s in samples if s['seconds'] >= 90))
    (output / 'memory.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report), flush=True)
finally:
    if child.poll() is None:
        child.terminate()
        child.wait(timeout=15)

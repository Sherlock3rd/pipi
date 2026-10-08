"""Capture the real transparent desktop host, separately from preview rendering."""
import pathlib
import subprocess
import sys
import time

exe = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
data = output / 'desktop-save'
child = subprocess.Popen([str(exe), '--native-audit', '--data-dir', str(data),
                          '--snapshot', str(output / 'desktop-scene.png'), '--exit-after', '15'])
try:
    deadline = time.monotonic() + 40
    while not (data / 'native-audit.txt').exists() and time.monotonic() < deadline:
        if child.poll() is not None:
            raise RuntimeError('Desktop host exited before native checks completed')
        time.sleep(.2)
    if not (data / 'native-audit.txt').exists():
        raise TimeoutError('Desktop window failed to open')
    time.sleep(2)
    subprocess.run(['screencapture', '-x', str(output / 'desktop-screen.png')], check=True, timeout=20)
    if child.wait(timeout=40) != 0 or (data / 'error.log').exists():
        raise RuntimeError('Desktop smoke failed; inspect logs')
    assert (output / 'desktop-scene.png.json').exists()
finally:
    if child.poll() is None:
        child.terminate()
        child.wait(timeout=10)

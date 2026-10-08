import pathlib
import subprocess
import sys
import tempfile
import time

exe = pathlib.Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix='chenpi-native-') as folder:
    folder = pathlib.Path(folder)
    fixture = folder / 'window-fixture'
    subprocess.run(['swiftc', 'tools/macos_window_fixture.swift', '-o', str(fixture)], check=True)
    for mode in ['normal', 'fullscreen']:
        ready = folder / (mode + '.ready')
        child = subprocess.Popen([str(fixture), mode, str(ready)])
        try:
            deadline = time.monotonic() + 15
            while not ready.exists() and time.monotonic() < deadline:
                if child.poll() is not None:
                    raise RuntimeError('Native window fixture exited before becoming ready')
                time.sleep(.2)
            if not ready.exists():
                raise TimeoutError('Native window fixture failed to show')
            time.sleep(1)
            actual = subprocess.check_output([str(exe), '--probe-fullscreen'], text=True).strip()
            assert actual == mode, (mode, actual)
            print('PASS separate-process window:', mode)
        finally:
            child.terminate()
            child.wait(timeout=10)

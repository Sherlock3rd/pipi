"""Real window-server mouse input, including the old desktop-layer negative control."""
import json
import pathlib
import subprocess
import sys
import time

exe = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
fixture = output / 'input-fixture'
subprocess.run(['swiftc', 'tools/macos_input_fixture.swift', '-o', str(fixture)], check=True)
receiver = output / 'receiver.json'
helper = subprocess.Popen([str(fixture), str(receiver)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
pet = None
checks = []

def wait_for(read, test, label, seconds=15):
    until = time.monotonic()+seconds
    last = None
    while time.monotonic() < until:
        try:
            last = read()
            if test(last):
                return last
        except (FileNotFoundError, json.JSONDecodeError):
            pass
        if pet is not None and pet.poll() is not None:
            raise RuntimeError('Pet exited during '+label)
        time.sleep(.1)
    raise AssertionError((label, last))

def read_receiver():
    return json.loads(receiver.read_text())

def send(command):
    helper.stdin.write(json.dumps(command)+'\n'); helper.stdin.flush()
    assert helper.stdout.readline().strip() == 'ok'

def event(kind, point):
    send(dict(event=kind, x=float(point['X']), y=float(point['Y'])))

def read():
    return json.loads((audit / 'state.json').read_text())

def command(**kwargs):
    path = audit / 'command.json'
    temp = audit / 'command.tmp'
    temp.write_text(json.dumps(kwargs)); temp.replace(path)
    wait_for(lambda: path.exists(), lambda exists: not exists, 'audit command consumed')

def move(name):
    point = read()['Targets'][name]
    event('move', point)
    wait_for(read, lambda d: abs(d['Native']['Pointer']['X']-point['ClientX']) < 3
        and abs(d['Native']['Pointer']['Y']-point['ClientY']) < 3, 'global/client coordinate agreement')
    time.sleep(.15)
    return point

def click(name, right=False):
    point = move(name)
    event('rightDown' if right else 'down', point); time.sleep(.08)
    event('rightUp' if right else 'up', point)

def record(name):
    checks.append(dict(check=name, scene=read(), receiver=read_receiver()))
    (output/'verification.json').write_text(json.dumps(dict(checks=checks), indent=2)+'\n')
    print('PASS', name, flush=True)

try:
    wait_for(read_receiver, lambda d: True, 'native desktop receiver ready')
    for legacy in (True, False):
        audit = output / ('legacy' if legacy else 'fixed')
        audit.mkdir(exist_ok=True)
        args = [str(exe), '--input-audit', str(audit), '--data-dir', str(audit/'save')]
        if legacy: args += ['--audit-legacy-layer']
        pet = subprocess.Popen(args)
        wait_for(read, lambda d: d['Time'] > 2 and d['IsVisible'], 'pet input audit ready', 45)
        command(food=0)
        before = read_receiver()['downs']
        click('food')
        if legacy:
            wait_for(read_receiver, lambda d: d['downs'] > before, 'old layer sends food click to desktop')
            assert read()['Scene']['Food'] == 0 and read()['Scene']['PointerPresses'] == 0
            record('negative control: old visible pet cannot receive clicks')
            pet.terminate(); pet.wait(timeout=15); pet = None
            continue
        wait_for(read, lambda d: d['Scene']['Food'] > 0, 'food click reaches pet')
        assert read_receiver()['downs'] == before
        assert not read()['Native']['Key']
        record('desktop food click and non-key window')

        for scale in (1.0, 1.4, .7):
            command(scale=scale)
            wait_for(read, lambda d: abs(d['Scene']['Scale']-scale) < .001, 'scale applied')
            before = read_receiver()['downs']
            point = move('nest'); start = read()['Scene']['NestX']
            event('down', point)
            wait_for(read, lambda d: d['Scene']['pressed'] and d['Scene']['pressedObject']=='nest', 'nest press delivered to nest, not overlapping furniture')
            target = dict(X=point['X']-70, Y=point['Y']-80)
            event('drag', target)
            wait_for(read, lambda d: d['Scene']['IsDragging'] and abs(d['Scene']['NestX']-start) > 25, 'nest drag moves object')
            event('up', target)
            wait_for(read, lambda d: not d['Scene']['pressed'] and not d['Scene']['IsDragging'], 'nest release')
            assert read_receiver()['downs'] == before
            record('native nest drag and release at scale '+str(scale))

        command(scale=1.0)
        wait_for(read, lambda d: d['Scene']['NestTargetClear'], 'visible bed target before cat interaction')
        click('nest', right=True)
        wait_for(read, lambda d: d['SettingsOpen'], 'nest right click before cat interaction opens settings')
        record('nest right click settings before cat interaction')
        command(closeSettings=True)
        assert read()['Scene']['Action']=='sleep'
        click('cat')
        wait_for(read, lambda d: d['Scene']['Action']!='sleep', 'cat click wakes sleeping pet')
        assert read()['Scene']['pressedObject']=='cat'
        record('cat click wakes sleeping pet')
        point = move('cat'); before = read_receiver()['downs']
        event('down', point)
        # Holding is the pre-drag pause, intentionally cleared by BeginDrag.
        # The authored drag action and captured scene state are the drag contract.
        wait_for(read, lambda d: d['Scene']['IsDragging'] and d['Scene']['Action']=='drag' and d['Scene']['pressedObject']=='cat', 'cat long press lifts')
        start = read()['Scene']['X']
        target = dict(X=point['X']-120, Y=point['Y']-90)
        event('drag', target)
        wait_for(read, lambda d: abs(d['Scene']['X']-start)>50, 'captured cat follows pointer horizontally')
        event('up', target)
        wait_for(read, lambda d: not d['Scene']['Holding'] and not d['Scene']['IsDragging'], 'cat drop')
        assert read_receiver()['downs'] == before
        record('cat long press, captured drag and drop')

        # The landing animation can cover the bed. Target a currently uncovered
        # bed pixel, exactly as required by the existing foreground hit rule.
        wait_for(read, lambda d: d['Scene']['NestTargetClear'] and d['Scene']['DisplayedClip']=='video-20', 'landing settles and exposes bed target')
        click('nest', right=True)
        wait_for(read, lambda d: d['SettingsOpen'], 'nest right click opens settings')
        record('nest right click settings')
        command(closeSettings=True)
        before = read_receiver()['downs']; click('empty')
        wait_for(read_receiver, lambda d: d['downs'] > before, 'empty desktop remains clickable')
        record('empty area passes through to desktop')

        send(dict(level='normal'))
        before = read_receiver()['downs']; click('food')
        wait_for(read_receiver, lambda d: d['downs'] > before, 'ordinary window remains above desktop pet')
        record('ordinary application remains above desktop mode')
        command(floating=True, food=0)
        before = read_receiver()['downs']; click('food')
        wait_for(read, lambda d: d['Scene']['Food'] > 0, 'floating pet receives click above ordinary window')
        assert read_receiver()['downs'] == before
        record('floating mode receives input')
        command(floating=False, food=0)
        before = read_receiver()['downs']; click('food')
        wait_for(read_receiver, lambda d: d['downs'] > before, 'return to desktop stays below ordinary window')
        assert read()['Scene']['Food']==0
        record('floating to desktop roundtrip preserves routing')
        pet.terminate(); pet.wait(timeout=15); pet = None
    (output/'verification.json').write_text(json.dumps(dict(checks=checks), indent=2)+'\n')
finally:
    if pet is not None and pet.poll() is None:
        pet.terminate(); pet.wait(timeout=15)
    helper.stdin.close()
    helper.wait(timeout=15)

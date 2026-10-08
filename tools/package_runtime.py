"""Lossless release asset pack. Originals and animation metadata are never edited."""
import argparse
import concurrent.futures
import hashlib
import io
import json
import pathlib
import shutil
import zipfile
from PIL import Image


def encode(item):
    root, name = item
    path = root / name
    with Image.open(path) as image:
        rgba = image.convert('RGBA')
        output = io.BytesIO()
        rgba.save(output, format='WEBP', lossless=True, exact=True, method=4)
        data = output.getvalue()
        with Image.open(io.BytesIO(data)) as restored:
            if restored.convert('RGBA').tobytes() != rgba.tobytes():
                raise ValueError(f'Lossless pixel verification failed: {name}')
    return name, data, path.stat().st_size


def package(source, output, workers=4):
    root = source.resolve()
    output = output.resolve()
    if output == root or root in output.parents:
        raise ValueError('Output must be outside source assets')
    output.mkdir(parents=True, exist_ok=True)
    manifest_path = root / 'pets/bluecat/manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    names = sorted({'pets/bluecat/' + name for frames in manifest['animations'].values() for name in frames})
    for name in names:
        if not (root / name).resolve().is_relative_to(root):
            raise ValueError('Unsafe frame path: ' + name)
    index, digests = {}, set()
    original_bytes = packed_bytes = 0
    temporary = output / 'frames.cpak.tmp'
    with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_STORED, allowZip64=True) as archive:
        with concurrent.futures.ProcessPoolExecutor(max_workers=workers) as pool:
            for i, (name, data, size) in enumerate(pool.map(encode, ((root, n) for n in names), chunksize=8)):
                digest = hashlib.sha256(data).hexdigest()
                entry = 'frames/' + digest + '.webp'
                index[name] = entry
                if digest not in digests:
                    archive.writestr(entry, data)
                    digests.add(digest)
                    packed_bytes += len(data)
                original_bytes += size
                if i % 1000 == 0:
                    print(f'{i + 1}/{len(names)} frames, all decoded RGBA bytes verified', flush=True)
        archive.writestr('index.json', json.dumps(index, ensure_ascii=False, separators=(',', ':')).encode())
    temporary.replace(output / 'frames.cpak')
    required = ['pets/bluecat/manifest.json'] + ['props/' + n + '.png' for n in ('food-bowl-empty', 'water-cup-empty', 'kibble', 'nest', 'litter-tray')]
    voice = json.loads((root / 'audio/cat/manifest.json').read_text(encoding='utf-8-sig'))
    required += ['audio/cat/manifest.json'] + sorted({'audio/cat/' + sound['File'] for binding in voice['Bindings'] for sound in binding['Sounds']})
    for name in required:
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / name, target)
    report = dict(framePaths=len(names), uniquePayloads=len(digests), originalFrameBytes=original_bytes,
                  packedFrameBytes=(output / 'frames.cpak').stat().st_size,
                  sourceAssetBytes=sum(p.stat().st_size for p in root.rglob('*') if p.is_file()),
                  outputAssetBytes=sum(p.stat().st_size for p in output.rglob('*') if p.is_file()),
                  verified='Every packed frame decoded to exactly identical RGBA bytes; manifest, props and audio copied byte-for-byte',
                  manifestSha256=hashlib.sha256(manifest_path.read_bytes()).hexdigest())
    (output / 'pack-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=pathlib.Path, default=pathlib.Path('assets'))
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--workers', type=int, default=4)
    args = parser.parse_args()
    package(args.source, args.output, args.workers)

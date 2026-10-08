"""Build a self-contained release. Use separate folders per architecture."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import shutil
import stat
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]


def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--rid', required=True, choices=['osx-arm64', 'osx-x64', 'win-x64'])
    parser.add_argument('--assets', type=pathlib.Path, required=True, help='Verified output of package_runtime.py')
    parser.add_argument('--output', type=pathlib.Path, default=ROOT / 'dist/releases')
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--source', help='Optional local NuGet feed for offline builds')
    args = parser.parse_args()
    assets = args.assets.resolve()
    report = json.loads((assets / 'pack-report.json').read_text(encoding='utf-8'))
    expected = hashlib.sha256((ROOT / 'assets/pets/bluecat/manifest.json').read_bytes()).hexdigest()
    if expected != report['manifestSha256']:
        raise ValueError('Asset pack is stale; rebuild it before publishing')
    release = args.output.resolve() / args.rid
    # Refuse a mixed or stale bundle rather than recursively deleting an old one.
    if release.exists():
        raise FileExistsError(f'Choose an empty output directory: {release}')
    mac = args.rid.startswith('osx-')
    app = release / 'Chenpi.app' if mac else release / 'Chenpi'
    binary = app / 'Contents/MacOS' if mac else app
    project = ROOT / 'src' / ('Chenpi.Mac/Chenpi.Mac.csproj' if mac else 'Chenpi/Chenpi.csproj')
    command = [args.dotnet, 'publish', project, '-c', 'Release', '-r', args.rid, '--self-contained', 'true',
               '-p:SkipAssetCopy=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:NuGetAudit=false', '-o', binary]
    if args.source:
        command += ['--source', pathlib.Path(args.source).resolve()]
    run(*command)
    shutil.copytree(assets, binary / 'assets')
    # Licenses and native third-party notices ship with each architecture.
    notices = binary / 'licenses'
    notices.mkdir()
    for source in (ROOT / 'packaging/licenses').glob('*'):
        if source.is_file():
            shutil.copyfile(source, notices / source.name)
    nuget = pathlib.Path(os.environ.get('NUGET_PACKAGES', str(pathlib.Path.home() / '.nuget/packages')))
    graph = json.loads((project.parent / 'obj/project.assets.json').read_text(encoding='utf-8'))
    packages = []
    for name, info in graph['libraries'].items():
        if info['type'] != 'package':
            continue
        packages.append(name)
        folder = nuget / info['path']
        for source in folder.iterdir():
            if source.is_file() and any(n in source.name.lower() for n in ('license', 'notice')):
                shutil.copyfile(source, notices / (name.replace('/', '-') + '-' + source.name))
    (notices / 'packages.txt').write_text('\n'.join(packages) + '\n', encoding='utf-8')
    if mac:
        resources = app / 'Contents/Resources'
        resources.mkdir()
        with (app / 'Contents/Info.plist').open('wb') as f:
            plistlib.dump(dict(CFBundleName='Chenpi', CFBundleDisplayName='陈皮', CFBundleIdentifier='com.chenpi.desktop',
                              CFBundleExecutable='Chenpi.Mac', CFBundlePackageType='APPL', CFBundleVersion='1.0.0',
                              CFBundleShortVersionString='1.0.0', LSMinimumSystemVersion='15.0',
                              LSUIElement=True, NSHighResolutionCapable=True), f)
        executable = binary / 'Chenpi.Mac'
        executable.chmod(0o755)
        if sys.platform == 'darwin':
            identity = os.environ.get('CHENPI_SIGN_IDENTITY', '-')
            architecture = 'arm64' if args.rid == 'osx-arm64' else 'x86_64'
            for file in binary.rglob('*'):
                if file.suffix == '.dylib' or file == executable:
                    architectures = subprocess.check_output(['lipo', '-archs', str(file)], text=True).split()
                    if architecture not in architectures:
                        raise ValueError(f'Wrong native architecture: {file}: {architectures}')
                    if len(architectures) > 1:
                        thinned = file.with_name(file.name + '.thin')
                        run('lipo', file, '-thin', architecture, '-output', thinned)
                        thinned.replace(file)
                    run('codesign', '--force', '--sign', identity, file)
            run('codesign', '--force', '--sign', identity, '--entitlements', ROOT / 'packaging/macos/entitlements.plist', app)
            run('codesign', '--verify', '--deep', '--strict', app)
    zip_path = args.output.resolve() / ('Chenpi-' + args.rid + '.zip')
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
        for file in sorted(app.rglob('*')):
            if not file.is_file():
                continue
            name = file.relative_to(release).as_posix()
            metadata = zipfile.ZipInfo(name)
            metadata.create_system = 3
            mode = 0o755 if file.name == 'Chenpi.Mac' or file.suffix == '.dylib' else 0o644
            metadata.external_attr = (stat.S_IFREG | mode) << 16
            metadata.compress_type = zipfile.ZIP_STORED if file.suffix == '.cpak' else zipfile.ZIP_DEFLATED
            with file.open('rb') as source, archive.open(metadata, 'w', force_zip64=True) as target:
                shutil.copyfileobj(source, target)
    with zip_path.open('rb') as archive_file:
        digest = hashlib.file_digest(archive_file, 'sha256').hexdigest()
    zip_path.with_suffix('.zip.sha256').write_text(digest + '  ' + zip_path.name + '\n', encoding='ascii')
    print(json.dumps(dict(archive=str(zip_path), bytes=zip_path.stat().st_size, sha256=digest,
                          signedOnMac=mac and sys.platform == 'darwin'), indent=2))


if __name__ == '__main__':
    main()

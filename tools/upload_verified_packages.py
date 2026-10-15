"""Upload unchanged, successful CI packages to an existing draft; never publish."""
import hashlib
import json
import os
import pathlib
import re
import subprocess
import zipfile


def gh(*args):
    return subprocess.check_output(['gh', *args], text=True)


def main():
    repo = os.environ['GH_REPO']
    run_id = os.environ['PACKAGE_RUN']
    commit = os.environ['PACKAGE_COMMIT']
    tag = os.environ['PACKAGE_TAG']
    assert run_id.isdecimal() and re.fullmatch(r'[0-9a-f]{40}', commit)
    assert re.fullmatch(r'v\d+\.\d+\.\d+', tag)
    run = json.loads(gh('api', f'repos/{repo}/actions/runs/{run_id}'))
    assert run['conclusion'] == 'success' and run['head_sha'] == commit
    assert run['path'] == '.github/workflows/desktop-release.yml'
    release = json.loads(gh('release', 'view', tag, '--json', 'isDraft,targetCommitish,assets'))
    assert release['isDraft'] and release['targetCommitish'] == commit
    evidence = dict(sourceCommit=commit, run=f'https://github.com/{repo}/actions/runs/{run_id}', platforms=[])
    for rid in ('osx-arm64', 'osx-x64'):
        folder = pathlib.Path('artifacts/upload-verified') / rid
        gh('run', 'download', run_id, '--name', f'Chenpi-{rid}', '--dir', str(folder))
        archive = folder / 'dist/releases' / f'Chenpi-{rid}.zip'
        checksum = archive.with_suffix('.zip.sha256')
        expected = checksum.read_text().split()[0]
        with archive.open('rb') as stream:
            assert hashlib.file_digest(stream, 'sha256').hexdigest() == expected
        with zipfile.ZipFile(archive) as bundle:
            info = json.loads(bundle.read('Chenpi.app/Contents/Resources/build-info.json'))
            assert info['sourceCommit'] == commit and info['version'] == tag[1:] and info['rid'] == rid
            uncompressed_bytes = sum(entry.file_size for entry in bundle.infolist())
        qa = folder / 'artifacts/native-qa'
        platform = dict(rid=rid, sha256=expected, bytes=archive.stat().st_size,
                        uncompressedBytes=uncompressed_bytes, buildIdentity=info,
                        pixelAudit=json.loads((qa / 'pixel-audit.json').read_text()), inputChecks={}, performance={})
        for stage in ('early-native-qa', 'native-qa'):
            checks = json.loads((folder / 'artifacts' / stage / 'input/verification.json').read_text())['checks']
            assert len(checks) == 13
            platform['inputChecks'][stage] = [check['check'] for check in checks]
        for mode in ('scene', 'desktop-scene'):
            data = json.loads((qa / (mode + '.png.json')).read_text())
            assert data['CachedFrameBytes'] <= 96*1024*1024 and data['Frames'] >= 1900
            assert data['P95Milliseconds'] <= 45 and not data['StartupActive']
            assert 'video-118' in {frame['Clip'] for frame in data['ClipTransitions']}
            platform['performance'][mode] = {key: value for key, value in data.items() if key not in ('slowFrames', 'ClipTransitions')}
        evidence['platforms'].append(platform)
        for path in (archive, checksum):
            # Retry is safe, but never overwrite another asset under the same tag.
            live = json.loads(gh('release', 'view', tag, '--json', 'isDraft,assets'))
            assert live['isDraft']
            existing = next((a for a in live['assets'] if a['name'] == path.name), None)
            if existing:
                with path.open('rb') as stream:
                    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
                api_url = existing['apiUrl']
                assert api_url.startswith(f'https://api.github.com/repos/{repo}/releases/assets/')
                asset = json.loads(gh('api', api_url))
                assert asset['state'] == 'uploaded' and asset['size'] == path.stat().st_size
                assert asset.get('digest') == 'sha256:' + digest
            else:
                gh('release', 'upload', tag, str(path))
        print('Verified and uploaded', rid, expected, flush=True)
    proof = pathlib.Path('artifacts/upload-verified/Chenpi-macos-verification.json')
    proof.write_text(json.dumps(evidence, indent=2) + '\n')
    # Immutable evidence: the CLI refuses to overwrite an existing same-name asset.
    assert json.loads(gh('release', 'view', tag, '--json', 'isDraft'))['isDraft']
    gh('release', 'upload', tag, str(proof))


if __name__ == '__main__':
    main()

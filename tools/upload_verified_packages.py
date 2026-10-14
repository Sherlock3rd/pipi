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
        for path in (archive, checksum):
            # Retry is safe, but never overwrite another asset under the same tag.
            live = json.loads(gh('release', 'view', tag, '--json', 'isDraft,assets'))
            assert live['isDraft']
            existing = next((a for a in live['assets'] if a['name'] == path.name), None)
            if existing:
                with path.open('rb') as stream:
                    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
                asset = json.loads(gh('api', f'repos/{repo}/releases/assets/{existing["id"]}'))
                assert asset['state'] == 'uploaded' and asset['size'] == path.stat().st_size
                assert asset.get('digest') == 'sha256:' + digest
            else:
                gh('release', 'upload', tag, str(path))
        print('Verified and uploaded', rid, expected, flush=True)


if __name__ == '__main__':
    main()

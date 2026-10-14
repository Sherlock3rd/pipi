"""Download every published asset anonymously and hash every byte."""
import concurrent.futures
import hashlib
import json
import os
import pathlib
import re
import urllib.request


def get(url):
    # No GitHub token or Authorization header: test the public distribution path.
    return urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'Chenpi-release-verifier'}), timeout=90)


def main():
    repo = os.environ['PACKAGE_REPO']
    tag = os.environ['PACKAGE_TAG']
    commit = os.environ['PACKAGE_COMMIT']
    expected_manifest = os.environ['PACKAGE_MANIFEST_SHA']
    assert re.fullmatch(r'[\w.-]+/[\w.-]+', repo)
    assert re.fullmatch(r'v\d+\.\d+\.\d+', tag)
    assert re.fullmatch(r'[0-9a-f]{40}', commit) and re.fullmatch(r'[0-9a-f]{64}', expected_manifest)
    with get(f'https://api.github.com/repos/{repo}/releases/tags/{tag}') as response:
        release = json.load(response)
    assert not release['draft'] and not release['prerelease'] and release['target_commitish'] == commit
    with get(f'https://api.github.com/repos/{repo}/commits/{tag}') as response:
        assert json.load(response)['sha'] == commit
    prefix = f'https://github.com/{repo}/releases/download/{tag}/'
    with get(prefix + 'release-manifest.json') as response:
        raw_manifest = response.read()
    assert hashlib.sha256(raw_manifest).hexdigest() == expected_manifest
    manifest = json.loads(raw_manifest)
    assert manifest['sourceCommit'] == commit and manifest['version'] == tag[1:]
    packages = {p['file']: p for p in manifest['packages']}
    assert {p['rid'] for p in packages.values()} == {'win-x64', 'osx-arm64', 'osx-x64'}
    expected_names = set(packages) | {name + '.sha256' for name in packages} | {'release-manifest.json', 'INSTALL.txt', 'Chenpi-macos-verification.json'}
    assets = {a['name']: a for a in release['assets']}
    assert set(assets) == expected_names

    def verify(name):
        asset = assets[name]
        assert asset['state'] == 'uploaded' and asset['browser_download_url'] == prefix + name
        digest = hashlib.sha256()
        total = 0
        small = bytearray()
        with get(asset['browser_download_url']) as response:
            while block := response.read(1024 * 1024):
                total += len(block)
                assert total <= asset['size']
                digest.update(block)
                if name.endswith('.sha256'):
                    small.extend(block)
        actual = digest.hexdigest()
        assert total == asset['size'] and asset['digest'] == 'sha256:' + actual
        if name in packages:
            assert actual == packages[name]['sha256'] and total == packages[name]['compressedBytes']
        if name.endswith('.sha256'):
            assert small.decode('ascii').split()[0] == packages[name.removesuffix('.sha256')]['sha256']
        print('PASS public full download', name, total, actual, flush=True)
        return dict(name=name, assetId=asset['id'], bytes=total, sha256=actual, publicAnonymousDownload=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        checks = list(pool.map(verify, sorted(assets)))
    output = pathlib.Path('artifacts/public-download-verification.json')
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(dict(version=tag[1:], sourceCommit=commit, manifestSha256=expected_manifest,
        url=release['html_url'], verificationRun=f'https://github.com/{repo}/actions/runs/{os.environ["GITHUB_RUN_ID"]}',
        verificationHost='GitHub ubuntu-latest runner', assets=checks), indent=2) + '\n')


if __name__ == '__main__':
    main()

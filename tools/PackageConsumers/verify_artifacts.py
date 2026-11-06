#!/usr/bin/env python3
"""Consume the exact release-directory packages without rebuilding or publishing."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from lockstep import verify_exact_core
from verify import HERE, REPO, PACKAGES, project, run

SYMBOL_PACKAGES = ('SharpPack.Core', 'SharpPack.Streaming')


def normalized_version(value):
    match = re.fullmatch(r'([0-9]+(?:\.[0-9]+){0,3})(?:-([0-9A-Za-z.-]+))?(?:\+([0-9A-Za-z.-]+))?', value)
    if not match:
        raise RuntimeError('Invalid release package version')
    parts = [int(part) for part in match[1].split('.')]
    parts.extend([0] * (3 - len(parts)))
    if len(parts) == 4 and parts[-1] == 0:
        parts.pop()
    return '.'.join(map(str, parts)) + ('-' + match[2].lower() if match[2] else '')


def snapshot_feed(feed):
    if not feed.is_dir():
        raise RuntimeError('Release package directory does not exist')
    snapshot = {}
    for path in feed.iterdir():
        if path.is_symlink() or not path.is_file() or path.suffix not in ('.nupkg', '.snupkg'):
            raise RuntimeError('Release directory contains an unexpected entry')
        snapshot[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    return snapshot


def inspect_feed(feed, version):
    expected_version = normalized_version(version)
    expected = {(name, '.nupkg') for name in PACKAGES} | {(name, '.snupkg') for name in SYMBOL_PACKAGES}
    found = set()
    for path in sorted(feed.iterdir()):
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise RuntimeError('Release package has duplicate ZIP entries')
            nuspecs = [name for name in names if name.endswith('.nuspec')]
            if len(nuspecs) != 1:
                raise RuntimeError('Release package must have one nuspec')
            root = ET.fromstring(archive.read(nuspecs[0]))
            metadata = [e for e in root if e.tag.split('}')[-1] == 'metadata']
            if len(metadata) != 1:
                raise RuntimeError('Release package has invalid metadata')
            fields = {e.tag.split('}')[-1]: e.text for e in metadata[0]}
            if len(fields) != len(metadata[0]):
                raise RuntimeError('Release package has duplicate metadata')
            name = fields.get('id')
            identity = (name, path.suffix)
            if identity not in expected or identity in found:
                raise RuntimeError('Release package inventory mismatch')
            if normalized_version(fields.get('version', '')) != expected_version:
                raise RuntimeError('Release package version does not match requested version')
            found.add(identity)
            payload = {n for n in names if not n.endswith('.nuspec')
                       and n not in ('_rels/.rels', '[Content_Types].xml')
                       and not n.startswith('package/services/metadata/core-properties/')}
            if path.suffix == '.snupkg':
                if payload != {f'lib/net10.0/{name}.pdb'} or not archive.read(next(iter(payload))):
                    raise RuntimeError('Release symbol package has unexpected payload')
            elif name == 'SharpPack.Generator':
                if 'analyzers/dotnet/cs/SharpPack.Generator.dll' not in payload or any(p.startswith('lib/') for p in payload):
                    raise RuntimeError('Packaged generator analyzer placement is invalid')
            elif name in ('SharpPack.Core', 'SharpPack.Streaming'):
                if f'lib/net10.0/{name}.dll' not in payload:
                    raise RuntimeError('Release runtime assembly is missing')
            if name == 'SharpPack.Streaming':
                # Compare against the package's own spelling (NuGet normalizes input versions).
                verify_exact_core(archive, fields['version'])
    if found != expected:
        raise RuntimeError('Release package inventory is incomplete')
    return expected_version


def write_config(path, feed, aot=False):
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='artifact', value=str(feed))
    if aot:
        ET.SubElement(sources, 'add', key='nuget', value='https://api.nuget.org/v3/index.json')
        mapping = ET.SubElement(config, 'packageSourceMapping')
        local = ET.SubElement(mapping, 'packageSource', key='artifact')
        ET.SubElement(local, 'package', pattern='SharpPack*')
        remote = ET.SubElement(mapping, 'packageSource', key='nuget')
        ET.SubElement(remote, 'package', pattern='*')
    ET.ElementTree(config).write(path, encoding='unicode')


def verify_assets(path, packages, version):
    assets = json.loads((path / 'obj/project.assets.json').read_text())
    if not all(item['type'] == 'package' for item in assets['libraries'].values()):
        raise RuntimeError('Release consumer contains a project reference')
    resolved = {key.split('/')[0]: normalized_version(key.split('/')[1])
                for key in assets['libraries'] if key.startswith('SharpPack')}
    if resolved != {name: version for name in packages}:
        raise RuntimeError('Release consumer resolved unexpected SharpPack packages')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--feed', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--architecture', required=True, choices=['X64', 'Arm64'])
    parser.add_argument('--aot', action='store_true')
    args = parser.parse_args()
    feed = args.feed.resolve()
    snapshot = snapshot_feed(feed)
    version = inspect_feed(feed, args.version)
    actual_arch = {'x86_64': 'X64', 'aarch64': 'Arm64', 'arm64': 'Arm64', 'AMD64': 'X64'}.get(platform.machine())
    if actual_arch != args.architecture:
        raise RuntimeError('Unexpected release-validation runner architecture')
    work = Path(tempfile.mkdtemp(prefix='sharppack-release-consumer-', dir=os.environ.get('RUNNER_TEMP')))
    env = dict(os.environ, PACKAGE_WORK=str(work), PACKAGE_EXPECTED_ARCH=args.architecture)
    sdk = run(['dotnet', '--version'], REPO, env).decode().strip()
    if not re.fullmatch(r'10\.0\.\d{3}', sdk):
        raise RuntimeError('Release consumer requires the selected .NET 10 SDK')
    (work / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable', 'allowPrerelease': False}}))
    if run(['dotnet', '--version'], work, env).decode().strip() != sdk:
        raise RuntimeError('Release consumer SDK differs from producer SDK')
    config = work / 'NuGet.Config'
    aot_config = work / 'Aot.NuGet.Config'
    write_config(config, feed)
    write_config(aot_config, feed, aot=True)
    try:
        cases = [('aggregate', ['SharpPack']), ('split', ['SharpPack.Core', 'SharpPack.Generator']),
                 ('streaming', ['SharpPack.Streaming', 'SharpPack.Generator'])]
        if args.aot:
            cases.append(('aot', ['SharpPack']))
        for name, refs in cases:
            path = work / name
            env['NUGET_PACKAGES'] = str(work / (name + '-cache'))
            items = ''.join(f'<PackageReference Include="{item}" Version="[{version}]" />' for item in refs)
            project(path, items, '<DefineConstants>STREAMING</DefineConstants>' if name == 'streaming' else '')
            for source in ['Model.cs', 'Program.cs']:
                shutil.copyfile(HERE / source, path / source)
            expected = set(refs) | {'SharpPack.Core', 'SharpPack.Generator'}
            if name == 'aot':
                run(['dotnet', 'publish', '-c', 'Release', '-p:PublishAot=true', '-p:NuGetAudit=false',
                     '--configfile', str(aot_config), '-o', str(work / 'aot-output')], path, env)
                verify_assets(path, expected, version)
                run([str(work / 'aot-output/Consumer')], path, env)
            else:
                run(['dotnet', 'restore', '--configfile', str(config)], path, env)
                verify_assets(path, expected, version)
                run(['dotnet', 'run', '-c', 'Release', '--no-restore'], path, env)
            print('Exact release package consumer passed: ' + name + '; architecture=' + args.architecture, flush=True)
    finally:
        if snapshot_feed(feed) != snapshot:
            raise RuntimeError('Release package bytes changed during verification')
    print('Release feed verified without repacking; six package hashes unchanged; version=' + version + '; SDK=' + sdk, flush=True)


if __name__ == '__main__':
    try:
        main()
    except RuntimeError as error:
        print('Release package gate failed: ' + str(error), flush=True)
        sys.exit(1)
    except Exception:
        print('Release package gate failed: invalid package or harness input; raw details remain local', flush=True)
        sys.exit(1)

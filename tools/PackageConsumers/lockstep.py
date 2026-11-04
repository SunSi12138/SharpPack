"""Strict package metadata/payload parity and observable NuGet constraint tests."""
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from xml.sax.saxutils import escape


def tag(element):
    return element.tag.split('}')[-1]


def metadata(package):
    root = ET.fromstring(package.read('SharpPack.Streaming.nuspec'))
    return next(e for e in root if tag(e) == 'metadata')


def normalized(element, ignore_dependency_version=False):
    attrs = {k: v for k, v in element.attrib.items() if v}
    if ignore_dependency_version and tag(element) == 'dependency':
        attrs.pop('version', None)
    return (tag(element), tuple(sorted(attrs.items())), (element.text or '').strip(),
            tuple(normalized(c, ignore_dependency_version) for c in element))


def verify_package_parity(original, candidate, version):
    with zipfile.ZipFile(original) as old, zipfile.ZipFile(candidate) as new:
        left = {tag(e): normalized(e, True) for e in metadata(old)}
        right = {tag(e): normalized(e, True) for e in metadata(new)}
        if left != right:
            # Only known XML field names, never arbitrary tool output.
            raise RuntimeError('Streaming package metadata parity failed')
        def payload_names(archive):
            return {n for n in archive.namelist() if not n.endswith('.nuspec')
                    and n not in ('_rels/.rels', '[Content_Types].xml')
                    and not n.startswith('package/services/metadata/core-properties/')}
        old_names, new_names = payload_names(old), payload_names(new)
        if old_names != new_names:
            raise RuntimeError('Streaming package file inventory parity failed')
        if any(old.read(n) != new.read(n) for n in old_names):
            raise RuntimeError('Streaming package payload bytes parity failed')
        expected = {'lib/net10.0/SharpPack.Streaming.dll', 'lib/net10.0/SharpPack.Streaming.xml',
                    'README.md', 'Icon.png', 'NOTICE'}
        if new_names != expected:
            raise RuntimeError('Streaming package payload inventory changed')
        deps = [e for e in metadata(new).iter() if tag(e) == 'dependency']
        if len(deps) != 1 or deps[0].get('id') != 'SharpPack.Core' or deps[0].get('version') != f'[{version}]':
            raise RuntimeError('Streaming exact Core dependency contract failed')
    print('Streaming metadata and payload parity passed; Core dependency is exact', flush=True)


def restore_case(work, name, source, refs, env, project, run, *, error_code=None, core_version=None):
    path = work / name
    project(path, refs, '<WarningsAsErrors>NU1608</WarningsAsErrors>')
    config = path / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="fixture" value="' + escape(str(source)) + '"/></packageSources></configuration>')
    case_env = dict(env, NUGET_PACKAGES=str(path / 'cache'))
    command = ['dotnet', 'restore', '--configfile', str(config)]
    if error_code:
        result = subprocess.run(command, cwd=path, env=case_env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (path / 'restore.log').write_bytes(result.stdout)
        # Require the exact structured NuGet diagnostic, not incidental failure.
        assets_file = path / 'obj/project.assets.json'
        if result.returncode == 0 or not assets_file.is_file():
            raise RuntimeError('Lockstep negative restore did not fail as expected')
        logs = json.loads(assets_file.read_text()).get('logs', [])
        if not any(item.get('code') == error_code and item.get('level') == 'Error' for item in logs):
            raise RuntimeError('Lockstep negative restore lacked expected NuGet error')
    else:
        run(command, path, case_env)
        assets = json.loads((path / 'obj/project.assets.json').read_text())
        actual = {k for k in assets['libraries'] if k.startswith('SharpPack.Core/')}
        if actual != {'SharpPack.Core/' + core_version}:
            raise RuntimeError('Lockstep restore selected unexpected Core')
    print('Lockstep restore case passed: ' + name, flush=True)


def verify_resolution(work, feed, repo, env, project, run, version):
    newer = '1.1.1-issue18-validation'
    run(['dotnet', 'pack', str(repo / 'src/SharpPack.Core/SharpPack.Core.csproj'), '-c', 'Release',
         '-o', str(feed), '-p:PackageVersion=' + newer, '-p:Version=' + newer], repo, env)
    streaming = f'<PackageReference Include="SharpPack.Streaming" Version="[{version}]" />'
    restore_case(work, 'lockstep-same-with-newer-available', feed, streaming, env, project, run, core_version=version)
    newer_only = work / 'newer-only-feed'; newer_only.mkdir()
    for name, selected in [('SharpPack.Core', newer), ('SharpPack.Streaming', version)]:
        shutil.copyfile(feed / f'{name}.{selected}.nupkg', newer_only / f'{name}.{selected}.nupkg')
    restore_case(work, 'lockstep-missing-exact', newer_only, streaming, env, project, run, error_code='NU1102')
    refs = streaming + f'<PackageReference Include="SharpPack.Core" Version="[{newer}]" />'
    restore_case(work, 'lockstep-direct-override-error', feed, refs, env, project, run, error_code='NU1608')


def verify_symbol_parity(original, candidate):
    with zipfile.ZipFile(original) as old, zipfile.ZipFile(candidate) as new:
        if normalized(metadata(old), True) != normalized(metadata(new), True):
            raise RuntimeError('Streaming symbol metadata parity failed')
        def payload(archive):
            return {n: archive.read(n) for n in archive.namelist() if not n.endswith('.nuspec')
                    and n not in ('_rels/.rels', '[Content_Types].xml')
                    and not n.startswith('package/services/metadata/core-properties/')}
        old_files, new_files = payload(old), payload(new)
        if old_files != new_files or set(new_files) != {'lib/net10.0/SharpPack.Streaming.pdb'}:
            raise RuntimeError('Streaming symbol PDB inventory or byte parity failed')
    print('Streaming release-equivalent snupkg metadata and PDB parity passed', flush=True)

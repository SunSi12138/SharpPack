#!/usr/bin/env python3
"""Verify actual packages in isolated consumers; all compiler output stays local."""
import argparse
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
VERSION = '1.1.0-issue18-validation'
PACKAGES = ('SharpPack.Core', 'SharpPack.Generator', 'SharpPack', 'SharpPack.Streaming')

def run(args, cwd, env):
    result = subprocess.run(args, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (Path(env['PACKAGE_WORK']) / 'last-command.log').write_bytes(result.stdout)
    if result.returncode:
        # Intentionally never publish raw output, paths, or arbitrary diagnostics.
        if b'Packaged generator loaded and executed' in result.stdout:
            print('Packaged generator loaded and executed; compilation or emit failed', flush=True)
        codes = sorted(set(re.findall(r'\b(?:CS|NU|MSB|SHARPPACK)\d{3,5}\b', result.stdout.decode(errors='replace'))))
        raise RuntimeError('command failed; diagnostic codes=' + ','.join(codes))
    return result.stdout

def project(path, refs, extra=''):
    path.mkdir(parents=True)
    (path / 'Consumer.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings>
<LangVersion>14.0</LangVersion><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks>{extra}</PropertyGroup>
<ItemGroup>{refs}</ItemGroup></Project>''')

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--aot', action='store_true')
    args = parser.parse_args()
    work = Path(tempfile.mkdtemp(prefix='sharppack-package-', dir=os.environ.get('RUNNER_TEMP')))
    feed = work / 'feed'; feed.mkdir()
    env = dict(os.environ, NUGET_PACKAGES=str(work / 'packages'), PACKAGE_WORK=str(work))
    sdk = json.loads((REPO / 'global.json').read_text())['sdk']['version']
    assert re.fullmatch(r'10\.0\.\d{3}', sdk), 'Expected .NET 10 SDK'
    (work / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable', 'allowPrerelease': False}}))
    for location, label in [(REPO, 'producer'), (work, 'consumer')]:
        actual = run(['dotnet', '--version'], location, env).decode().strip()
        assert actual == sdk, 'Unexpected SDK selection'
        print('Selected ' + label + ' SDK: ' + sdk + '; consumer C#: 14.0', flush=True)
    # Pack only production projects. Restore tooling from normal sources, never consumers.
    for name in PACKAGES:
        run(['dotnet', 'pack', str(REPO / 'src' / name / (name + '.csproj')), '-c', 'Release',
             '-o', str(feed), '-p:PackageVersion=' + VERSION, '-p:Version=' + VERSION], REPO, env)
    for name in PACKAGES:
        file = feed / f'{name}.{VERSION}.nupkg'
        with zipfile.ZipFile(file) as package:
            root = ET.fromstring(package.read(name + '.nuspec'))
            metadata = next(e for e in root if e.tag.split('}')[-1] == 'metadata')
            values = {e.tag.split('}')[-1]: e.text for e in metadata}
            assert values['id'] == name and values['version'] == VERSION
            if name == 'SharpPack.Generator':
                assert 'analyzers/dotnet/cs/SharpPack.Generator.dll' in package.namelist()
                assert not any(p.startswith('lib/') for p in package.namelist())
            if name == 'SharpPack.Streaming':
                deps = [e for e in metadata.iter() if e.tag.split('}')[-1] == 'dependency' and e.get('id') == 'SharpPack.Core']
                assert len(deps) == 1 and VERSION in deps[0].get('version', '')
                # Observe today's lower-bound range. This does NOT establish lockstep policy.
    # Empty package cache plus a cleared, local-only source prevents published-package leakage.
    env['NUGET_PACKAGES'] = str(work / 'consumer-cache')
    config = work / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="artifact" value="' + escape(str(feed)) + '"/></packageSources></configuration>')
    for name, refs in [('aggregate', ['SharpPack']), ('split', ['SharpPack.Core', 'SharpPack.Generator']),
                       ('streaming', ['SharpPack.Streaming', 'SharpPack.Generator'])]:
        path = work / name
        items = ''.join(f'<PackageReference Include="{p}" Version="[{VERSION}]" />' for p in refs)
        extra = '<DefineConstants>STREAMING</DefineConstants>' if name == 'streaming' else ''
        project(path, items, extra)
        for source in ['Model.cs', 'Program.cs']:
            shutil.copyfile(HERE / source, path / source)
        run(['dotnet', 'restore', '--configfile', str(config)], path, env)
        assets = json.loads((path / 'obj/project.assets.json').read_text())
        assert all(v['type'] == 'package' for v in assets['libraries'].values())
        resolved = {k for k in assets['libraries'] if k.startswith('SharpPack')}
        expected = {'SharpPack.Core/' + VERSION, 'SharpPack.Generator/' + VERSION}
        if name == 'aggregate': expected.add('SharpPack/' + VERSION)
        if name == 'streaming': expected.add('SharpPack.Streaming/' + VERSION)
        assert resolved == expected, (name, resolved)
        run(['dotnet', 'run', '-c', 'Release', '--no-restore'], path, env)
        print('Package consumer passed: ' + name)
    # Separate executables, each referencing only one Roslyn host package version.
    cache = Path(env['NUGET_PACKAGES'])
    core = cache / 'sharppack.core' / VERSION / 'lib/net10.0/SharpPack.Core.dll'
    generator = cache / 'sharppack.generator' / VERSION / 'analyzers/dotnet/cs/SharpPack.Generator.dll'
    host_config = work / 'Host.NuGet.Config'
    host_config.write_text('<configuration><packageSources><clear/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    outputs = {}
    for version in ['5.6.0', '4.3.0', '4.4.0']:
        historical = version != '5.6.0'
        path = work / ('host-' + version)
        refs = f'<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="[{version}]" /><Reference Include="SharpPack.Generator"><HintPath>{escape(str(generator))}</HintPath></Reference>'
        project(path, refs)
        shutil.copyfile(HERE / 'RoslynHost.cs', path / 'Program.cs')
        output = work / (version + '-generated.txt')
        try:
            run(['dotnet', 'restore', '--configfile', str(host_config)], path, env)
            run(['dotnet', 'run', '-c', 'Release', '--no-restore', '--', version + '.0', str(core), str(HERE / 'Model.cs'), str(output)], path, env)
            print(('Historical probe compiled: ' if historical else 'Current Roslyn host passed: ') + version, flush=True)
        except RuntimeError as error:
            if not historical:
                raise
            print('Historical probe failed (non-gating): ' + version + '; ' + str(error), flush=True)
        if output.is_file():
            outputs[version] = output.read_text()
        if historical:
            equal = version in outputs and outputs[version] == outputs['5.6.0']
            print('Historical generated-source equality: ' + version + '; ' + ('matched' if equal else 'different or unavailable'), flush=True)
        else:
            assert version in outputs, 'Missing current generator output'
    if args.aot:
        # Runtime/compiler packs require normal package sources; SharpPack remains source-mapped locally.
        aot_config = work / 'Aot.NuGet.Config'
        aot_config.write_text('<configuration><packageSources><clear/><add key="artifact" value="' + escape(str(feed)) + '"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="artifact"><package pattern="SharpPack*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>')
        path = work / 'aggregate'
        run(['dotnet', 'publish', '-c', 'Release', '-p:PublishAot=true', '-p:NuGetAudit=false', '--configfile', str(aot_config), '-o', str(work / 'aot')], path, env)
        run([str(work / 'aot/Consumer'), 'verify'], path, env)
        print('Packaged NativeAOT passed')
    print('Package matrix passed; raw logs retained only on temporary runner')

if __name__ == '__main__':
    try:
        main()
    except RuntimeError as error:
        print('Package gate failed: ' + str(error), flush=True)
        sys.exit(1)
    except Exception:
        print('Package gate failed: harness assertion or input validation; raw details remain local', flush=True)
        sys.exit(1)

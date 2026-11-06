"""Negative package and source-isolation tests, with no .NET/network requirement."""
import json
from pathlib import Path
import tempfile
import unittest
import warnings
import zipfile
import xml.etree.ElementTree as ET
from verify_artifacts import (PACKAGES, SYMBOL_PACKAGES, inspect_feed, normalized_version,
                              snapshot_feed, verify_assets, write_config)

VERSION = '1.1.0-test'


def package(path, name, *, version=VERSION, dependency=None, payload=None):
    if payload is None:
        if path.suffix == '.snupkg':
            payload = {f'lib/net10.0/{name}.pdb': b'pdb'}
        elif name == 'SharpPack.Generator':
            payload = {'analyzers/dotnet/cs/SharpPack.Generator.dll': b'generator'}
        elif name == 'SharpPack':
            payload = {'README.md': b'readme'}
        else:
            payload = {f'lib/net10.0/{name}.dll': b'runtime'}
    deps = ''
    if name == 'SharpPack.Streaming':
        deps = f'<dependencies><group targetFramework="net10.0"><dependency id="SharpPack.Core" version="{dependency or "[" + version + "]"}"/></group></dependencies>'
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr(name + '.nuspec', f'<package><metadata><id>{name}</id><version>{version}</version>{deps}</metadata></package>')
        for filename, data in payload.items():
            archive.writestr(filename, data)


class ArtifactTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.feed = Path(self.tmp.name)
        for name in PACKAGES:
            package(self.path(name), name)
        for name in SYMBOL_PACKAGES:
            package(self.path(name, '.snupkg'), name)

    def path(self, name, suffix='.nupkg'):
        return self.feed / (name + '.' + VERSION + suffix)

    def test_accepts_exact_release_inventory_without_changing_bytes(self):
        before = snapshot_feed(self.feed)
        self.assertEqual(len(before), 6)
        self.assertEqual(inspect_feed(self.feed, VERSION), VERSION)
        self.assertEqual(snapshot_feed(self.feed), before)

    def test_missing_package_fails(self):
        self.path('SharpPack').unlink()
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_missing_symbols_fail(self):
        self.path('SharpPack.Core', '.snupkg').unlink()
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_extra_package_fails(self):
        package(self.path('Unexpected'), 'Unexpected')
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_duplicate_package_identity_fails(self):
        package(self.feed / 'duplicate.nupkg', 'SharpPack.Core')
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_wrong_requested_version_fails(self):
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, '1.1.1')

    def test_one_misaligned_runtime_fails(self):
        package(self.path('SharpPack.Core'), 'SharpPack.Core', version='1.1.1')
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_non_exact_streaming_dependency_fails(self):
        package(self.path('SharpPack.Streaming'), 'SharpPack.Streaming', dependency=VERSION)
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_wrong_streaming_dependency_fails(self):
        package(self.path('SharpPack.Streaming'), 'SharpPack.Streaming', dependency='[1.1.1]')
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_missing_analyzer_fails(self):
        package(self.path('SharpPack.Generator'), 'SharpPack.Generator', payload={'README.md': b'readme'})
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_generator_runtime_asset_fails(self):
        package(self.path('SharpPack.Generator'), 'SharpPack.Generator', payload={
            'analyzers/dotnet/cs/SharpPack.Generator.dll': b'generator', 'lib/net10.0/Unexpected.dll': b'dll'})
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_missing_runtime_dll_fails(self):
        package(self.path('SharpPack.Core'), 'SharpPack.Core', payload={'README.md': b'readme'})
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_empty_symbol_fails(self):
        package(self.path('SharpPack.Core', '.snupkg'), 'SharpPack.Core', payload={'lib/net10.0/SharpPack.Core.pdb': b''})
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_extra_symbol_payload_fails(self):
        package(self.path('SharpPack.Core', '.snupkg'), 'SharpPack.Core', payload={
            'lib/net10.0/SharpPack.Core.pdb': b'pdb', 'lib/net10.0/SharpPack.Core.dll': b'dll'})
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_duplicate_zip_entry_fails(self):
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            with zipfile.ZipFile(self.path('SharpPack'), 'a') as archive:
                archive.writestr('README.md', b'duplicate')
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_multiple_nuspecs_fail(self):
        with zipfile.ZipFile(self.path('SharpPack'), 'a') as archive:
            archive.writestr('Other.nuspec', '<package/>')
        with self.assertRaises(RuntimeError): inspect_feed(self.feed, VERSION)

    def test_extra_release_directory_file_fails(self):
        (self.feed / 'accidental.txt').write_text('not reviewed')
        with self.assertRaises(RuntimeError): snapshot_feed(self.feed)

    def test_symlink_fails(self):
        (self.feed / 'linked.nupkg').symlink_to(self.path('SharpPack'))
        with self.assertRaises(RuntimeError): snapshot_feed(self.feed)

    def test_hashes_detect_payload_change(self):
        before = snapshot_feed(self.feed)
        package(self.path('SharpPack'), 'SharpPack', payload={'README.md': b'changed'})
        self.assertNotEqual(snapshot_feed(self.feed), before)


class ContractTests(unittest.TestCase):
    def test_version_normalization(self):
        for supplied, expected in [('1.1.0', '1.1.0'), ('1.1', '1.1.0'), ('1.1.0.0', '1.1.0'),
                                   ('1.1.0+build.12', '1.1.0'), ('1.1.0-RC.1', '1.1.0-rc.1')]:
            self.assertEqual(normalized_version(supplied), expected)
        for invalid in ['', '../publish', '1.1.0" bad', 'a.b.c']:
            with self.assertRaises(RuntimeError): normalized_version(invalid)

    def test_plain_restore_is_local_only_and_aot_maps_sharppack_locally(self):
        with tempfile.TemporaryDirectory() as tmp:
            config = Path(tmp) / 'NuGet.Config'
            write_config(config, Path(tmp))
            root = ET.parse(config).getroot()
            self.assertIsNotNone(root.find('packageSources/clear'))
            self.assertEqual([s.get('key') for s in root.findall('packageSources/add')], ['artifact'])
            write_config(config, Path(tmp), aot=True)
            root = ET.parse(config).getroot()
            mapping = {s.get('key'): [p.get('pattern') for p in s] for s in root.find('packageSourceMapping')}
            self.assertEqual(mapping, {'artifact': ['SharpPack*'], 'nuget': ['*']})

    def test_assets_reject_project_or_wrong_version(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp); (path / 'obj').mkdir()
            assets = path / 'obj/project.assets.json'
            assets.write_text(json.dumps({'libraries': {'SharpPack.Core/' + VERSION: {'type': 'package'}}}))
            verify_assets(path, ['SharpPack.Core'], VERSION)
            for libraries in [{'SharpPack.Core/' + VERSION: {'type': 'project'}},
                              {'SharpPack.Core/1.1.1': {'type': 'package'}},
                              {'SharpPack.Core/' + VERSION: {'type': 'package'}, 'SharpPack.Unexpected/' + VERSION: {'type': 'package'}}]:
                assets.write_text(json.dumps({'libraries': libraries}))
                with self.assertRaises(RuntimeError): verify_assets(path, ['SharpPack.Core'], VERSION)

    def test_gate_precedes_both_publication_paths(self):
        root = Path(__file__).resolve().parents[2] / '.github/workflows'
        for filename, publish in [('build-release.yaml', '      - name: Create GitHub release'),
                                  ('publish-nuget.yaml', '      - name: NuGet login')]:
            source = (root / filename).read_text()
            gate = '      - name: Verify exact release packages before publication'
            self.assertEqual(source.count(gate), 1)
            self.assertLess(source.index('      - name: Pack'), source.index(gate))
            self.assertLess(source.index(gate), source.index(publish))
            self.assertIn('--feed "$GITHUB_WORKSPACE/publish" --version "$VERSION" --aot --architecture X64', source)
            self.assertNotIn('continue-on-error', source)


if __name__ == '__main__':
    unittest.main()

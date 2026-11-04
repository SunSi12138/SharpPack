import contextlib
import io
from pathlib import Path
import tempfile
import unittest
import zipfile
import xml.etree.ElementTree as ET
from lockstep import verify_package_parity, verify_symbol_parity

VERSION = '1.1.0-test'
FILES = {'lib/net10.0/SharpPack.Streaming.dll': b'dll',
         'lib/net10.0/SharpPack.Streaming.xml': b'xml',
         'README.md': b'readme', 'Icon.png': b'icon', 'NOTICE': b'notice'}

def package(path, constraint, *, description='fixture', files=None, extra_metadata=''):
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr('SharpPack.Streaming.nuspec', f'''<package><metadata>
<id>SharpPack.Streaming</id><version>{VERSION}</version>{extra_metadata}<description>{description}</description>
<dependencies><group targetFramework="net10.0"><dependency id="SharpPack.Core" version="{constraint}" exclude="Build,Analyzers"/></group></dependencies>
</metadata></package>''')
        for name, content in (FILES if files is None else files).items():
            archive.writestr(name, content)

class ParityTests(unittest.TestCase):
    def check(self, *, constraint=None, description='fixture', files=None, fails=False, extra_metadata=''):
        with tempfile.TemporaryDirectory() as tmp:
            old, new = Path(tmp)/'old.nupkg', Path(tmp)/'new.nupkg'
            package(old, VERSION)
            package(new, constraint or f'[{VERSION}]', description=description, files=files, extra_metadata=extra_metadata)
            with contextlib.redirect_stdout(io.StringIO()):
                if fails:
                    with self.assertRaises(RuntimeError): verify_package_parity(old,new,VERSION)
                else: verify_package_parity(old,new,VERSION)
    def test_templates_only_differ_by_pdb(self):
        root = Path(__file__).resolve().parents[2] / 'src/SharpPack.Streaming'
        normal = ET.parse(root / 'SharpPack.Streaming.nuspec').getroot()
        symbols = ET.parse(root / 'SharpPack.Streaming.symbols.nuspec').getroot()
        files = next(e for e in symbols if e.tag.split('}')[-1] == 'files')
        pdb = [e for e in files if e.get('src') == '$output$SharpPack.Streaming.pdb']
        self.assertEqual(len(pdb), 1)
        files.remove(pdb[0])
        from lockstep import normalized
        self.assertEqual(normalized(normal), normalized(symbols))
    def test_root_file_targets_are_explicit(self):
        root = Path(__file__).resolve().parents[2] / 'src/SharpPack.Streaming'
        for filename in ['SharpPack.Streaming.nuspec', 'SharpPack.Streaming.symbols.nuspec']:
            template = ET.parse(root / filename).getroot()
            entries = {e.get('src'): e.get('target') for e in template.iter()
                       if e.tag.split('}')[-1] == 'file'}
            for name in ['README.md', 'Icon.png', 'NOTICE']:
                self.assertEqual(entries['$root$' + name], name)
    def test_omitted_and_false_license_acceptance_are_equivalent(self):
        self.check(extra_metadata='<requireLicenseAcceptance>false</requireLicenseAcceptance>')
    def test_reject_changed_license_acceptance(self):
        self.check(extra_metadata='<requireLicenseAcceptance>true</requireLicenseAcceptance>', fails=True)
    def test_reject_duplicate_metadata(self):
        self.check(extra_metadata='<description>conflicting duplicate</description>', fails=True)
    def test_reject_duplicate_zip_entry(self):
        with tempfile.TemporaryDirectory() as tmp:
            old, new = Path(tmp)/'old.nupkg', Path(tmp)/'new.nupkg'
            package(old, VERSION)
            package(new, f'[{VERSION}]')
            import warnings
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                with zipfile.ZipFile(new, 'a') as archive:
                    archive.writestr('README.md', b'readme')
            with self.assertRaises(RuntimeError): verify_package_parity(old, new, VERSION)
    def test_symbol_package_requires_exact_core(self):
        files = {'lib/net10.0/SharpPack.Streaming.pdb': b'pdb'}
        with tempfile.TemporaryDirectory() as tmp:
            old, new = Path(tmp)/'old.snupkg', Path(tmp)/'new.snupkg'
            package(old, VERSION, files=files)
            for constraint in [VERSION, '[9.0.0]', f'[{VERSION}]']:
                package(new, constraint, files=files)
                with contextlib.redirect_stdout(io.StringIO()):
                    if constraint == f'[{VERSION}]':
                        verify_symbol_parity(old, new, VERSION)
                    else:
                        with self.assertRaises(RuntimeError): verify_symbol_parity(old, new, VERSION)
    def test_exact_preserves_metadata_and_bytes(self): self.check()
    def test_reject_lower_bound(self): self.check(constraint=VERSION,fails=True)
    def test_reject_version_substring(self): self.check(constraint=f'[{VERSION},9.0.0)',fails=True)
    def test_reject_metadata_loss(self): self.check(description='',fails=True)
    def test_reject_missing_notice(self): self.check(files={k:v for k,v in FILES.items() if k!='NOTICE'},fails=True)
    def test_reject_modified_bytes(self): self.check(files={**FILES,'README.md':b'changed'},fails=True)
    def test_reject_embedded_core(self): self.check(files={**FILES,'lib/net10.0/SharpPack.Core.dll':b'core'},fails=True)

if __name__ == '__main__': unittest.main()

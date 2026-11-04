import contextlib
import io
from pathlib import Path
import tempfile
import unittest
import zipfile
from lockstep import verify_package_parity

VERSION = '1.1.0-test'
FILES = {'lib/net10.0/SharpPack.Streaming.dll': b'dll',
         'lib/net10.0/SharpPack.Streaming.xml': b'xml',
         'README.md': b'readme', 'Icon.png': b'icon', 'NOTICE': b'notice'}

def package(path, constraint, *, description='fixture', files=None):
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr('SharpPack.Streaming.nuspec', f'''<package><metadata>
<id>SharpPack.Streaming</id><version>{VERSION}</version><description>{description}</description>
<dependencies><group targetFramework="net10.0"><dependency id="SharpPack.Core" version="{constraint}" exclude="Build,Analyzers"/></group></dependencies>
</metadata></package>''')
        for name, content in (FILES if files is None else files).items():
            archive.writestr(name, content)

class ParityTests(unittest.TestCase):
    def check(self, *, constraint=None, description='fixture', files=None, fails=False):
        with tempfile.TemporaryDirectory() as tmp:
            old, new = Path(tmp)/'old.nupkg', Path(tmp)/'new.nupkg'
            package(old, VERSION)
            package(new, constraint or f'[{VERSION}]', description=description, files=files)
            with contextlib.redirect_stdout(io.StringIO()):
                if fails:
                    with self.assertRaises(RuntimeError): verify_package_parity(old,new,VERSION)
                else: verify_package_parity(old,new,VERSION)
    def test_exact_preserves_metadata_and_bytes(self): self.check()
    def test_reject_lower_bound(self): self.check(constraint=VERSION,fails=True)
    def test_reject_version_substring(self): self.check(constraint=f'[{VERSION},9.0.0)',fails=True)
    def test_reject_metadata_loss(self): self.check(description='',fails=True)
    def test_reject_missing_notice(self): self.check(files={k:v for k,v in FILES.items() if k!='NOTICE'},fails=True)
    def test_reject_modified_bytes(self): self.check(files={**FILES,'README.md':b'changed'},fails=True)
    def test_reject_embedded_core(self): self.check(files={**FILES,'lib/net10.0/SharpPack.Core.dll':b'core'},fails=True)

if __name__ == '__main__': unittest.main()

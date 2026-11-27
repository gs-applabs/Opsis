import importlib.util, pathlib, tempfile, unittest, zipfile
from unittest.mock import patch
p=pathlib.Path(__file__).resolve().parents[2]/'tools/plugin_package_maintenance.py'
spec=importlib.util.spec_from_file_location('maint',p); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root=pathlib.Path(self.temp.name); self.live=self.root/'plugins'/'Example'; self.live.mkdir(parents=True)
        (self.live/'ExamplePlugin.dll').write_bytes(b'old'); (self.live/'asset.txt').write_bytes(b'old asset')
        self.maint=m.Maintenance(self.root); self.archive=self.root/'update.zip'
    def zip(self, extra=(), dll='ExamplePlugin.dll'):
        with zipfile.ZipFile(self.archive,'w') as z:
            z.writestr(dll,b'new'); z.writestr('asset.txt',b'new asset')
            for n,v in extra: z.writestr(n,v)
        return m.sha(self.archive)
    def update(self): return self.maint.transaction('Example',self.archive,self.zip())
    def test_roundtrip(self):
        old=m.inventory(self.live); tid=self.update(); self.assertEqual((self.live/'asset.txt').read_bytes(),b'new asset'); self.assertFalse(self.maint.marker.exists())
        self.maint.restore(tid); self.assertEqual(m.inventory(self.live),old); self.assertTrue((self.maint.root/tid/'displaced').is_dir())
    def test_wrong_hash(self):
        self.zip(); self.assertRaises(ValueError,self.maint.transaction,'Example',self.archive,'0'*64); self.assertEqual((self.live/'ExamplePlugin.dll').read_bytes(),b'old')
    def test_paths(self):
        for name in ['../evil','/evil','C:/evil','CON.txt','x:stream','a/../evil','file.','a//b']:
            with self.subTest(name=name):
                digest=self.zip([(name,b'x')]); self.assertRaises(ValueError,self.maint.transaction,'Example',self.archive,digest)
        self.assertFalse(self.maint.marker.exists())
    def test_raw_zip_names(self):
        # ZipFile.writestr normalizes backslashes on Windows. Inject the raw
        # name into both ZIP headers so the hostile fixture is platform neutral.
        original=m.inventory(self.live)
        for raw in (b'a\\evil', b'a\x00evil'):
            with self.subTest(raw=raw):
                self.zip([('a/evil',b'x')])
                data=self.archive.read_bytes(); self.assertEqual(data.count(b'a/evil'),2)
                self.archive.write_bytes(data.replace(b'a/evil',raw))
                with zipfile.ZipFile(self.archive) as z:
                    self.assertEqual(z.infolist()[-1].orig_filename.encode(),raw)
                self.assertRaises(ValueError,self.maint.transaction,'Example',self.archive,m.sha(self.archive))
                self.assertEqual(m.inventory(self.live),original)
                self.assertFalse(self.maint.marker.exists())
    def test_duplicate_case(self):
        digest=self.zip([('ASSET.TXT',b'x')]); self.assertRaises(ValueError,self.maint.transaction,'Example',self.archive,digest)
    def test_different_entry(self):
        digest=self.zip(dll='OtherPlugin.dll'); self.assertRaises(ValueError,self.maint.transaction,'Example',self.archive,digest)
    def test_link(self):
        (self.live/'link').symlink_to(self.root/'outside'); self.assertRaises(ValueError,self.update)
    def test_interrupted_second_rename(self):
        original=m.os.rename
        def fail(src,dst):
            if pathlib.Path(src).name=='stage': raise OSError('injected interruption')
            return original(src,dst)
        with patch.object(m.os,'rename',side_effect=fail): self.assertRaises(OSError,self.update)
        self.assertTrue(self.maint.marker.exists()); self.assertFalse(self.live.exists()); self.maint.recover(); self.assertEqual((self.live/'ExamplePlugin.dll').read_bytes(),b'old'); self.assertFalse(self.maint.marker.exists())
    def test_interrupted_rollback(self):
        tid=self.update(); original=m.os.rename
        def fail(src,dst):
            if pathlib.Path(src).name=='backup': raise OSError('injected interruption')
            return original(src,dst)
        with patch.object(m.os,'rename',side_effect=fail): self.assertRaises(OSError,self.maint.restore,tid)
        self.maint.recover(); self.assertEqual((self.live/'ExamplePlugin.dll').read_bytes(),b'old')
    def test_modified_live_not_overwritten(self):
        tid=self.update(); (self.live/'asset.txt').write_bytes(b'user change'); self.assertRaises(ValueError,self.maint.restore,tid); self.assertEqual((self.live/'asset.txt').read_bytes(),b'user change')
    def test_bad_backup_not_overwritten(self):
        tid=self.update(); (self.maint.root/tid/'backup'/'asset.txt').write_bytes(b'corrupt'); self.assertRaises(ValueError,self.maint.restore,tid); self.assertEqual((self.live/'asset.txt').read_bytes(),b'new asset')
if __name__=='__main__': unittest.main()

import importlib.util,json,tempfile,unittest
from pathlib import Path
from PIL import Image
here=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("stage",here/"stage-approved-europe.py")
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class ReleaseGate(unittest.TestCase):
    def test_empty_and_partial_full_release(self):
        cat=json.loads((here/"europe-capitals.json").read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as d:
            d=Path(d)
            with self.assertRaises(ValueError):m.stage(cat,{"cities":[]},d,d/"out")
            with self.assertRaises(ValueError):m.stage(cat,{"cities":[{"id":"eu-athens"}]},d,d/"out",True)
            self.assertFalse((d/"out").exists())
    def test_hash_alpha_and_valid_local_fixture(self):
        cat=json.loads((here/"europe-capitals.json").read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as d:
            d=Path(d);src=d/"eu-madrid";src.mkdir()
            size=(1920,1080)
            im=Image.new("RGB",size,(30,40,50));im.save(src/"master.webp",lossless=True)
            mask=Image.new("L",size,0);mask.paste(255,(0,0,1920,360));mask.save(src/"sky-mask.png")
            from PIL import ImageChops
            phase=im.convert("RGBA");phase.putalpha(ImageChops.invert(mask))
            for name in m.PHASES:phase.save(src/(name+".webp"),lossless=True)
            Image.new("RGB",(640,360)).save(src/"poster.jpg")
            (src/"attribution.json").write_text(json.dumps(dict(source_page="fixture",artist="fixture",license="fixture",license_url="fixture",changes="fixture",timezone="Europe/Madrid")))
            r={"id":"eu-madrid","status":"approved","reviewer":"unit-test only","checks":dict.fromkeys(m.GATES,True),"sha256":{n:m.sha(src/n) for n in m.FILES}}
            with self.assertRaises(ValueError):m.stage(cat,{"cities":[r]},d,d/"release")
            m.stage(cat,{"cities":[r]},d,d/"local-test",True)
            self.assertTrue((d/"local-test/widgets/wallpaper/scenes/europe/eu-madrid/release.json").exists())
            Image.new("RGBA",size,(0,0,0,255)).save(src/"night.webp",lossless=True)
            with self.assertRaises(ValueError):m.stage(cat,{"cities":[r]},d,d/"bad-hash",True)
            r["sha256"]["night.webp"]=m.sha(src/"night.webp")
            with self.assertRaises(ValueError):m.stage(cat,{"cities":[r]},d,d/"bad-alpha",True)
if __name__=="__main__":unittest.main()

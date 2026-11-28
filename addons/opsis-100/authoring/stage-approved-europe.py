#!/usr/bin/env python3
"""Stage an installer-owned payload ONLY from explicitly approved, matching assets."""
import argparse, hashlib, json, shutil, tempfile
from pathlib import Path
from PIL import Image, ImageChops
PHASES=("dawn","day","golden","blue","night")
FILES=("master.webp","sky-mask.png","poster.jpg","attribution.json",*(p+".webp" for p in PHASES))
GATES=("geography","viewpoint","license","skyline","lighting","renderer")
def sha(p):
    h=hashlib.sha256()
    with p.open("rb") as f:
        for b in iter(lambda:f.read(1048576),b""): h.update(b)
    return h.hexdigest()
def fail(msg): raise ValueError(msg)
def stage(catalog, approvals, source, dest, partial=False):
    expected={c["id"] for c in catalog["scenes"] if c["id"]!="eu-athens"}
    if len(expected)!=49: fail("expected 49 unique capitals")
    records=approvals["cities"]
    ids=[r["id"] for r in records]
    if len(set(ids))!=len(ids) or not set(ids)<=expected: fail("unexpected/duplicate city")
    if not partial and set(ids)!=expected: fail("full release requires all 49 approved cities")
    if not ids: fail("no approved cities")
    if dest.exists(): fail("destination must not already exist")
    dest.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".europe-stage-",dir=dest.parent) as tmp:
        root=Path(tmp)/"payload"
        for r in records:
            id=r["id"]; src=source/id
            if r.get("status")!="approved" or not all(r.get("checks",{}).get(g) is True for g in GATES) or not r.get("reviewer"):
                fail(id+": missing explicit review")
            hashes=r.get("sha256",{})
            if set(hashes)!=set(FILES): fail(id+": incomplete hashes")
            if src.is_symlink(): fail(id+": symlink")
            for name in FILES:
                p=src/name
                if not p.is_file() or p.is_symlink() or sha(p)!=hashes[name]: fail(id+": missing or changed "+name)
            with Image.open(src/"master.webp") as m:
                w,h=m.size
                if m.format!="WEBP" or m.mode!="RGB" or w*9!=h*16 or not 1920<=w<=3840: fail(id+": invalid runtime master")
                m.load()
            with Image.open(src/"sky-mask.png") as mask:
                if mask.format!="PNG" or mask.mode!="L" or mask.size!=(w,h): fail(id+": native runtime mask mismatch")
                count=sum(i*n for i,n in enumerate(mask.histogram()))/(255*w*h)
                if not .08<=count<=.70: fail(id+": abnormal sky coverage; correct source or mask")
                expected_alpha=ImageChops.invert(mask)
                for phase in PHASES:
                    with Image.open(src/(phase+".webp")) as p:
                        if p.format!="WEBP" or p.size!=(w,h) or p.mode!="RGBA" or ImageChops.difference(p.getchannel("A"),expected_alpha).getbbox():
                            fail(id+": phase size/alpha mismatch "+phase)
            with Image.open(src/"poster.jpg") as im:
                if im.format!="JPEG" or im.width>960 or im.height>540 or im.width*9!=im.height*16: fail(id+": invalid poster")
                im.load()
            meta=json.loads((src/"attribution.json").read_text(encoding="utf-8"))
            if not all(meta.get(k) for k in ("source_page","artist","license","license_url","changes","timezone")): fail(id+": missing attribution")
            target=root/"widgets/wallpaper/scenes/europe"/id
            target.mkdir(parents=True)
            for name in FILES: shutil.copy2(src/name,target/name)
            (target/"release.json").write_text(json.dumps({"schemaVersion":1,"id":id,"status":"approved","masterSha256":hashes["master.webp"],"maskSha256":hashes["sky-mask.png"]},indent=2),encoding="utf-8")
        root.rename(dest)
def main():
    p=argparse.ArgumentParser()
    for name in ("catalog","approvals","source","dest"):p.add_argument("--"+name,type=Path,required=True)
    p.add_argument("--partial-test",action="store_true",help="Local test staging only; never distribute")
    a=p.parse_args()
    stage(json.loads(a.catalog.read_text(encoding="utf-8")),json.loads(a.approvals.read_text(encoding="utf-8")),a.source,a.dest,a.partial_test)
if __name__=="__main__":main()

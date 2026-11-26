#!/usr/bin/env python3
"""Assemble data-only REVIEW PREVIEW installer payload. Never issue release receipts."""
import argparse, hashlib, json, shutil
from pathlib import Path
from PIL import Image, ImageChops
PHASES=("dawn","day","golden","blue","night")
def sha(p):
    h=hashlib.sha256()
    with p.open("rb") as f:
        for b in iter(lambda:f.read(1048576),b""): h.update(b)
    return h.hexdigest()
def run(src,out,catalog):
    allowed={c["id"] for c in json.loads(catalog.read_text(encoding="utf-8"))["scenes"] if c["id"]!="eu-athens"}
    if len(allowed)!=49 or out.exists(): raise ValueError("catalog/output invalid")
    out.mkdir(parents=True); report=[]; seen=set()
    for p in sorted(src.rglob("review.json")):
        id=p.parent.name
        if id not in allowed or id in seen: raise ValueError("unexpected or duplicate "+id)
        seen.add(id); dst=out/id
        try:
            m=json.loads(p.read_text(encoding="utf-8"))
            if m.get("review_status")!="unapproved-draft" or m["city"]["id"]!=id: raise ValueError("review identity mismatch")
            for n,d in m["output_hashes"].items():
                if Path(n).name!=n or sha(p.parent/n)!=d: raise ValueError("source hash mismatch "+n)
            with Image.open(p.parent/"sky-mask-review.png") as mask:
                if mask.mode!="L" or mask.size!=(1920,1080): raise ValueError("mask dimensions")
                fraction=sum(i*n for i,n in enumerate(mask.histogram()))/(255*1920*1080)
                if not .08<=fraction<=.70: raise ValueError("unusable mask")
                alpha=ImageChops.invert(mask)
                dst.mkdir()
                mask.save(dst/"sky-mask.png")
                for phase in PHASES:
                    with Image.open(p.parent/(phase+"-review.webp")) as im:
                        if im.mode!="RGBA" or im.size!=mask.size or ImageChops.difference(im.getchannel("A"),alpha).getbbox(): raise ValueError("invalid phase "+phase)
                    shutil.copy2(p.parent/(phase+"-review.webp"),dst/(phase+".webp"))
            with Image.open(p.parent/"master.webp") as im:
                if im.width<1920 or im.height<1080 or im.width*9!=im.height*16: raise ValueError("invalid master")
                im=im.convert("RGB").resize((1920,1080),Image.Resampling.LANCZOS)
                im.save(dst/"master.webp",lossless=True,method=3)
                im.thumbnail((640,360));im.save(dst/"poster.jpg",quality=88)
            lic=m.get("license","")
            if not (lic.startswith("CC BY") or lic.upper().startswith("CC0") or lic.lower()=="public domain"): raise ValueError("license not suitable for preview")
            if not m.get("source_page") or (lic.startswith("CC BY") and not all(m.get(k) for k in ("artist","license_url"))): raise ValueError("missing provenance")
            # Keep all source attribution including transformation history, without claiming legal review.
            attribution={k:m.get(k) for k in ("source_page","source_title","original_url","artist","license","license_url","credit","changes","restrictions","date","description")}
            attribution["changes"]=str(attribution["changes"])+"; downsampled to 1920x1080, provisional sky mask and five color grades."
            attribution["status"]="review-preview; not approved for production"
            (dst/"attribution.json").write_text(json.dumps(attribution,ensure_ascii=False,indent=2),encoding="utf-8")
            (dst/"preview.json").write_text(json.dumps(dict(schemaVersion=1,id=id,status="review-preview",masterSha256=sha(dst/"master.webp"),maskSha256=sha(dst/"sky-mask.png")),indent=2),encoding="utf-8")
            report.append(dict(id=id,status="preview-installed-payload",sky_fraction=round(fraction,4),sha256={f.name:sha(f) for f in dst.iterdir()}))
        except Exception as e:
            shutil.rmtree(dst,ignore_errors=True)
            report.append(dict(id=id,status="excluded",reason=str(e)))
    for id in sorted(allowed-seen):report.append(dict(id=id,status="missing"))
    (out/"PREVIEW-REPORT.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    count=sum(r["status"]=="preview-installed-payload" for r in report)
    print(json.dumps({"count":count,"report":report},ensure_ascii=False),flush=True)
    if not count: raise ValueError("no usable preview assets")
if __name__=="__main__":
    p=argparse.ArgumentParser()
    for k in ("src","out","catalog"):p.add_argument("--"+k,type=Path,required=True)
    a=p.parse_args();run(a.src,a.out,a.catalog)

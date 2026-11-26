#!/usr/bin/env python3
"""Build review-only weather layers from real photographs. No publishing."""
import argparse, hashlib, json, shutil, traceback
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

def digest(p):
    h=hashlib.sha256()
    with p.open("rb") as f:
        for block in iter(lambda:f.read(1024*1024),b""): h.update(block)
    return h.hexdigest()

def mask_candidate(im):
    # At review resolution: top-connected sky-like color or bright neutral pixels.
    # Intentionally conservative; clouds, buildings and thin skyline details require review.
    arr=np.asarray(im)
    hsv=cv2.cvtColor(arr,cv2.COLOR_RGB2HSV)
    h,s,v=cv2.split(hsv)
    blue=(h>=85)&(h<=125)&(s>=20)&(v>=80)
    cloud=(s<=52)&(v>=165)
    allowed=(blue|cloud).astype(np.uint8)
    allowed[int(arr.shape[0]*.62):]=0
    n,labels,stats,_=cv2.connectedComponentsWithStats(allowed,8)
    ids=np.unique(labels[:max(1,arr.shape[0]//120)])
    ids=ids[ids!=0]
    mask=np.isin(labels,ids).astype(np.uint8)*255
    # No filling of holes or smoothing across buildings: leave conservative mask.
    return Image.fromarray(mask)

def build(city,src,dst):
    meta=json.loads((src/"metadata.json").read_text(encoding="utf-8"))
    if meta["source_page"]!=city["active_source"]:
        raise ValueError("source mismatch vs review manifest")
    for name,expected in meta["sha256"].items():
        if Path(name).name!=name or digest(src/name)!=expected: raise ValueError("input hash mismatch: "+name)
    with Image.open(src/"day.webp") as opened: im=opened.convert("RGB")
    w,h=im.size
    if w*9!=h*16 or w<1920: raise ValueError("invalid native crop")
    dst.mkdir(parents=True,exist_ok=True)
    shutil.copy2(src/"day.webp",dst/"master.webp")
    # Review derivatives only, never upscale. Master remains native and lossless.
    k=min(w//16,h//9,120); rw,rh=16*k,9*k
    review=im.resize((rw,rh),Image.Resampling.LANCZOS) if (rw,rh)!=(w,h) else im.copy()
    mask=mask_candidate(review)
    mask.save(dst/"sky-mask-review.png")
    fraction=float(np.asarray(mask).mean()/255)
    alpha=Image.fromarray(255-np.asarray(mask))
    arr=np.asarray(review,dtype=np.float32)/255
    # Simple sRGB color grades, not photometric simulation. No invented lights or weather.
    phases={"dawn":(.72,(1.10,.94,.98)),"day":(1,(1,1,1)),"golden":(.90,(1.12,.95,.74)),"blue":(.47,(.80,.94,1.20)),"night":(.22,(.78,.91,1.16))}
    for name,(strength,tint) in phases.items():
        rgb=np.clip(arr*strength*np.asarray(tint,dtype=np.float32),0,1)
        image=Image.fromarray(np.rint(rgb*255).astype(np.uint8)).convert("RGBA")
        image.putalpha(alpha)
        p=dst/(name+"-review.webp")
        image.save(p,lossless=True,method=3)
        with Image.open(p) as check:
            if check.size!=(rw,rh) or not np.array_equal(np.asarray(check.convert("RGBA").getchannel("A")),np.asarray(alpha)):
                raise ValueError("phase alpha or dimensions mismatch")
    thumb=review.copy();thumb.thumbnail((640,360));thumb.save(dst/"poster.webp",quality=85)
    overlay=np.asarray(review).copy()
    sky=np.asarray(mask)>0
    overlay[sky]=(overlay[sky].astype(np.float32)*.5+np.array([0,220,220])*.5).astype(np.uint8)
    Image.fromarray(overlay).resize((960,540),Image.Resampling.LANCZOS).save(dst/"mask-overlay-review.jpg",quality=90)
    outmeta={**meta,"review_status":"unapproved-draft","native_master_size":[w,h],"review_layer_size":[rw,rh],"sky_fraction":fraction,"mask_method":"conservative top-connected blue/bright-neutral threshold; not semantic segmentation","review_required":["source location and viewpoint","license and freedom of panorama","skyline manual correction at native resolution","phase lighting and renderer verification"],"output_hashes":{p.name:digest(p) for p in dst.iterdir() if p.is_file()}}
    (dst/"review.json").write_text(json.dumps(outmeta,ensure_ascii=False,indent=2),encoding="utf-8")
    return {"id":city["id"],"status":"draft-only","native_size":[w,h],"review_size":[rw,rh],"sky_fraction":round(fraction,4)}

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--manifest",type=Path,required=True)
    ap.add_argument("--input",type=Path,required=True)
    ap.add_argument("--out",type=Path,required=True)
    ap.add_argument("--batch",type=int,required=True)
    a=ap.parse_args()
    cities=[c for c in json.loads(a.manifest.read_text(encoding="utf-8"))["cities"] if c["state"]=="downloaded-review-required"]
    a.out.mkdir(parents=True,exist_ok=True)
    report=[]
    for c in cities[a.batch*8:(a.batch+1)*8]:
        try:
            options=[]
            for p in a.input.rglob("metadata.json"):
                if p.parent.name==c["id"]:
                    m=json.loads(p.read_text(encoding="utf-8"))
                    if m.get("source_page")==c["active_source"]: options.append(p.parent)
            if not options: raise ValueError("approved-for-review source not present")
            row=build(c,options[0],a.out/c["id"])
        except Exception as e:
            shutil.rmtree(a.out/c["id"],ignore_errors=True)
            row={"id":c["id"],"status":"not-built","error":str(e)}
        report.append(row)
        print(json.dumps(row,ensure_ascii=False),flush=True)
        (a.out/"report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
if __name__=="__main__":main()

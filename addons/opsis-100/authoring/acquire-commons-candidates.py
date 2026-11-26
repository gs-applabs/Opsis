#!/usr/bin/env python3
"""Acquire real Commons photographs as REVIEW CANDIDATES. Never publish automatically."""
import argparse, hashlib, html, io, json, re, time, urllib.parse, urllib.request
from pathlib import Path
from PIL import Image, ImageOps
API="https://commons.wikimedia.org/w/api.php"
UA="OpsisWallpaperReview/1.0 (https://github.com/gs-applabs/Opsis)"
EXACT={
"dublin":"Samuel_Beckett_Bridge_-_aerial_-_2025-07-07.jpg",
"paris":"Panoramic_from_the_Tour_Montparnasse.jpg",
"rome":"Panoramic_view_of_Rome_from_Gianicolo_Hill_01.jpg",
"andorra-la-vella":"Andorra_la_Vella_view2.jpg",
"ankara":"Views_from_the_Ankara_Castle_(5).jpg",
}
VIEWS={
"madrid":"Royal Palace Almudena panorama","copenhagen":"Christiansborg tower panorama",
"stockholm":"Monteliusvagen panorama","oslo":"Ekeberg panorama","helsinki":"South Harbour panorama",
"belgrade":"Kalemegdan panorama","bern":"Rosengarten panorama","bratislava":"castle panorama",
"bucharest":"city panorama","chisinau":"city panorama","lisbon":"Senhora do Monte panorama",
"prague":"Letna panorama","london":"Greenwich Park panorama","budapest":"Gellert Hill panorama",
"vienna":"city panorama","amsterdam":"city panorama","berlin":"city panorama","brussels":"city panorama"
}
def get(url, limit=90*1024*1024):
    if urllib.parse.urlparse(url).hostname not in {"commons.wikimedia.org","upload.wikimedia.org"}:
        raise ValueError("Unexpected source host")
    for attempt in range(3):
        try:
            with urllib.request.urlopen(urllib.request.Request(url,headers={"User-Agent":UA}),timeout=35) as f:
                if urllib.parse.urlparse(f.geturl()).hostname not in {"commons.wikimedia.org","upload.wikimedia.org"}:
                    raise ValueError("Unexpected redirect")
                data=f.read(limit+1)
                if len(data)>limit: raise ValueError("Size limit")
                return data
        except Exception:
            if attempt==2: raise
            time.sleep(2**attempt)
def query(**kw):
    time.sleep(.25)
    return json.loads(get(API+"?"+urllib.parse.urlencode(dict(action="query",format="json",formatversion=2,**kw)), 12*1024*1024))
def plain(s):
    return html.unescape(re.sub(r"<[^>]*>","",s or "")).strip()
def candidates(city):
    slug=city["id"].removeprefix("eu-")
    props=dict(prop="imageinfo",iiprop="url|size|mime|sha1|extmetadata")
    batches=[]
    if slug in EXACT:
        batches.append(dict(titles="File:"+EXACT[slug]))
    batches += [dict(generator="search",gsrnamespace=6,gsrlimit=18,gsrsearch=q)
                for q in [city["nameEn"]+" "+VIEWS.get(slug,city.get("landmark","")+" panorama"),
                          city["nameEn"]+" skyline panorama",
                          city["nameEn"]+" city view aerial"]]
    seen=set()
    for args in batches:
        pages=query(**props,**args).get("query",{}).get("pages",[])
        ranked=[]
        for p in pages:
            title=p.get("title","")
            if title in seen or not p.get("imageinfo"): continue
            seen.add(title)
            i=p["imageinfo"][0]; m=i.get("extmetadata",{})
            v=lambda k:plain(m.get(k,{}).get("value",""))
            lic=v("LicenseShortName"); artist=v("Artist"); lu=v("LicenseUrl")
            if not re.match(r"^(CC BY(-SA)? [234]\.[05]|CC0|Public domain)$",lic,re.I): continue
            if not artist and lic.lower() not in {"public domain","cc0"}: continue
            if lic.upper().startswith("CC BY") and not lu: continue
            if i.get("mime") not in {"image/jpeg","image/png"}: continue
            w,h=i.get("width",0),i.get("height",0); k=min(w//16,h//9)
            if k<120 or w*h>100_000_000 or i.get("size",0)>90*1024*1024: continue
            if re.search(r"\b(map|flag|logo|collage|montage|painting|drawing|night|sunset|dusk|poster|diagram|screenshot|illustration)\b",title,re.I): continue
            score=100-abs(w/h-16/9)*30+min(k,240)/12
            if re.search(r"panoram|aerial|skyline|view",title,re.I): score+=10
            ranked.append((score,p,i,dict(license=lic,license_url=lu,artist=artist,credit=v("Credit"),description=v("ImageDescription"),date=v("DateTimeOriginal"),restrictions=v("Restrictions"))))
        for _,p,i,meta in sorted(ranked,key=lambda x:-x[0]):
            yield p,i,meta
def sha(data): return hashlib.sha256(data).hexdigest()
def base36(n):
    s=""
    while n: n,r=divmod(n,36); s="0123456789abcdefghijklmnopqrstuvwxyz"[r]+s
    return s or "0"
def acquire(city,out):
    errors=[]
    for p,i,meta in candidates(city):
        try:
            data=get(i["url"]); actual=hashlib.sha1(data).hexdigest()
            if i.get("sha1") and i["sha1"].lstrip("0").lower() not in {actual.lstrip("0"),base36(int(actual,16))}:
                raise ValueError("Commons SHA1 mismatch")
            with Image.open(io.BytesIO(data)) as raw:
                if raw.format not in {"JPEG","PNG"} or raw.size!=(i["width"],i["height"]): raise ValueError("Image type or size mismatch")
                im=ImageOps.exif_transpose(raw).convert("RGB")
            w,h=im.size;k=min(w//16,h//9);cw,ch=16*k,9*k
            if k<120: raise ValueError("Crop below minimum")
            x=(w-cw)//2;y=(h-ch)//2
            crop=im.crop((x,y,x+cw,y+ch))
            target=out/city["id"];target.mkdir(exist_ok=True,parents=True)
            original=target/("source.jpg" if i["mime"]=="image/jpeg" else "source.png")
            original.write_bytes(data)
            crop.save(target/"day.webp",lossless=True,method=4)
            with Image.open(target/"day.webp") as check:
                if check.size!=crop.size or check.convert("RGB").tobytes()!=crop.tobytes(): raise ValueError("Lossless mismatch")
            crop.thumbnail((640,360));crop.save(target/"poster.webp",quality=85)
            details=dict(city=city,status="candidate-only",source_title=p["title"],source_page=i["descriptionurl"],original_url=i["url"],source_size=[w,h],crop_box=[x,y,x+cw,y+ch],native_crop_size=[cw,ch],**meta,changes="EXIF orientation, centered 16:9 crop, WebP conversion; no generated content",review_required=["verify city and viewpoint against map/photo","verify daytime and sky fraction","verify license and freedom of panorama","manual skyline mask and phase grading","renderer tests"],sha256={f.name:sha(f.read_bytes()) for f in target.iterdir() if f.is_file()})
            (target/"metadata.json").write_text(json.dumps(details,ensure_ascii=False,indent=2),encoding="utf-8")
            return dict(id=city["id"],status="candidate-only",source=p["title"],source_page=i["descriptionurl"])
        except Exception as e:
            errors.append(str(e))
            import shutil
            shutil.rmtree(out/city["id"],ignore_errors=True)
    return dict(id=city["id"],status="not-acquired",errors=errors[-4:])
def main():
    ap=argparse.ArgumentParser();ap.add_argument("--catalog",type=Path,required=True);ap.add_argument("--out",type=Path,required=True);ap.add_argument("--limit",type=int,default=49);a=ap.parse_args()
    a.out.mkdir(exist_ok=True,parents=True)
    cities=[c for c in json.loads(a.catalog.read_text(encoding="utf-8"))["scenes"] if c["id"]!="eu-athens"]
    first=["madrid","copenhagen","stockholm","oslo","helsinki","dublin","paris","rome","andorra-la-vella","ankara"]
    order={f"eu-{v}":i for i,v in enumerate(first)}
    cities.sort(key=lambda c:order.get(c["id"],100))
    report=[]
    for c in cities[:a.limit]:
        try: row=acquire(c,a.out)
        except Exception as e: row=dict(id=c["id"],status="not-acquired",errors=[str(e)])
        report.append(row);print(json.dumps(row,ensure_ascii=False),flush=True)
        (a.out/"report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print("Review candidates:",sum(r["status"]=="candidate-only" for r in report),"/",len(report),flush=True)
if __name__=="__main__":main()

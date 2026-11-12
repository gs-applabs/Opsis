"""
Builds the photographic landmark sprites of the weather wallpaper (src/Widgets/wallpaper/landmarks/*.png + ATTRIBUTION.md).

Sources: Wikimedia Commons photos whose licence allows reuse without share-alike (public domain, CC0, CC BY). The licence, author and page of every photo are read from the
Commons API at build time and written to ATTRIBUTION.md. Each photo is a clear-sky shot: the sky is keyed out (relative blueness), the monument is cropped and scaled to 700 px.
Run:  python -I build_sprites.py <output folder>      (needs numpy + Pillow + network; the downloaded files are only decoded as images, never executed)
"""
import sys, os, io, json, re, time, datetime, urllib.request, urllib.parse
import numpy as np
from PIL import Image, ImageFilter

UA = {'User-Agent': 'OpsisLandmarkSprites/1.0 (build script)'}
# id, Commons file title, crop (left, top, right, bottom as fractions), key thresholds, bottom fade (fraction of the height faded out at the foot, for photos cut above the ground)
SPRITES = [
    ('parthenon', 'File:Parthenon east side Acropolis Athens, Greece.jpg', (0.0, 0.0, 1.0, 1.0), (0.12, 0.38), 0.0),
]
if len(sys.argv) > 2 and os.path.exists(sys.argv[2]):
    SPRITES = [tuple(x) if not isinstance(x, tuple) else x for x in json.load(open(sys.argv[2], encoding='utf-8-sig'))]

def api(params):
    with urllib.request.urlopen(urllib.request.Request('https://commons.wikimedia.org/w/api.php?' + urllib.parse.urlencode(params), headers=UA), timeout=60) as r:
        return json.load(r)

def fetch(url):
    for attempt in range(4):                      # Commons answers 429 when asked too fast: wait and try again
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 3: raise
            time.sleep(8 * (attempt + 1))

def cutout(im, crop, thr, fade, width=700):
    w, h = im.size
    im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h))).convert('RGB')
    a = np.asarray(im).astype(np.float32); r, g, b = a[..., 0], a[..., 1], a[..., 2]
    blueness = (b - np.maximum(r, g * 0.8)) / (b + 8.0)
    alpha = 1.0 - np.clip((blueness - thr[0]) / (thr[1] - thr[0]), 0.0, 1.0)
    fringe = (alpha > 0.02) & (alpha < 0.98)
    a[..., 2] = np.where(fringe, np.minimum(b, np.maximum(r, g) + 4), b)
    mask = Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
    out = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).convert('RGBA'); out.putalpha(mask)
    out = out.crop(mask.point(lambda v: 255 if v > 20 else 0).getbbox())
    out = out.resize((width, max(1, int(out.height * width / out.width))), Image.LANCZOS)
    if fade > 0:                                     # photo cut above the ground: let the foot melt into the haze instead of ending on a hard line
        al = np.asarray(out.getchannel('A')).astype(np.float32); n = int(out.height * fade)
        al[-n:] *= np.linspace(1, 0, n)[:, None]; out.putalpha(Image.fromarray(al.astype(np.uint8)))
    return out

outdir = sys.argv[1]; os.makedirs(outdir, exist_ok=True)
credits = []
for sid, title, crop, thr, fade, *rest in SPRITES:
    d = api({'action': 'query', 'format': 'json', 'titles': title, 'prop': 'imageinfo', 'iiprop': 'url|extmetadata', 'iiurlwidth': 2000, 'iiextmetadatafilter': 'LicenseShortName|Artist|LicenseUrl'})
    ii = list(d['query']['pages'].values())[0]['imageinfo'][0]
    lic = ii['extmetadata']['LicenseShortName']['value']; artist = re.sub('<[^>]+>', '', ii['extmetadata'].get('Artist', {}).get('value', 'unknown')).strip()
    if not re.match(r'(?i)(public domain|cc0|cc by [0-9.]+$|pd)', lic): raise SystemExit('licence not allowed for ' + sid + ': ' + lic)
    img = Image.open(io.BytesIO(fetch(ii['thumburl'])))
    width = rest[0] if rest else 700
    cutout(img, crop, thr, fade, width).save(os.path.join(outdir, sid + '.webp'), 'WEBP', quality=82, alpha_quality=92, method=6)
    credits.append({'file': sid + '.webp', 'commonsTitle': title[5:], 'commonsUrl': ii['descriptionurl'], 'author': artist, 'license': lic,
                    'licenseUrl': ii['extmetadata'].get('LicenseUrl', {}).get('value', ''), 'attributionRequired': lic.upper().startswith('CC BY'),
                    'changes': 'Sky removed (colour keying), cropped, scaled to %d px wide, converted to WebP.' % width,
                    'checkedOn': datetime.date.today().isoformat()})
    print(sid, lic); time.sleep(4)
with open(os.path.join(outdir, 'ATTRIBUTION.md'), 'w', encoding='utf-8') as f:
    f.write('# Landmark photos\n\nThe landmark sprites of the weather wallpaper are cut-outs of photographs from Wikimedia Commons. Only public domain, CC0 and CC BY material is used (no share-alike, no non-commercial, no unknown licences). Every photo was checked individually against the Commons licence data on the date below. Changes: the sky was removed, the picture was cropped and scaled, and converted to WebP.\n\n| Sprite | Photo | Author | Licence | Source | Checked |\n|---|---|---|---|---|---|\n')
    for c in credits: f.write(f"| {c['file']} | {c['commonsTitle']} | {c['author']} | {c['license']} | {c['commonsUrl']} | {c['checkedOn']} |\n")
json.dump(credits, open(os.path.join(outdir, 'manifest.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)



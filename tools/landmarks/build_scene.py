"""
Builds a PHOTOGRAPHIC scene of a landmark for the weather wallpaper (src/Widgets/wallpaper/scenes/<id>.webp + <id>-lights.webp + manifest.json).

The scene is a real aerial photograph from Wikimedia Commons (public domain / CC0 / CC BY only, checked here through the Commons API): it is cropped to the part that matters, widened at both sides
with mirrored, blurred copies of its own edges so it fills a 16:9 / 21:9 screen, and its top fades into transparency so that the animated sky of the wallpaper (clouds, sun, moon, stars) shows
through the haze. A second image holds warm window lights placed on the bright buildings, used at night.
Run:  python -I build_scene.py <output folder>        (needs numpy + Pillow + network; downloaded files are only decoded as images)
"""
import sys, os, io, json, re, time, datetime, urllib.request, urllib.parse
import numpy as np
from PIL import Image, ImageFilter

UA = {'User-Agent': 'OpsisLandmarkSprites/1.0 (build script)'}
SCENES = [
    # id, Commons title, crop (l, t, r, b as fractions of the photo), target aspect (w/h) of the final panorama
    ('acropolis', 'File:The Acropolis of Athens from Mount Lycabettus on March 14, 2020.jpg', (0.0, 0.03, 1.0, 0.74), 2.7),
]

def api(params):
    with urllib.request.urlopen(urllib.request.Request('https://commons.wikimedia.org/w/api.php?' + urllib.parse.urlencode(params), headers=UA), timeout=60) as r:
        return json.load(r)

def fetch(url):
    for attempt in range(4):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 3: raise
            time.sleep(8 * (attempt + 1))

def widen(im, aspect):
    w, h = im.size; target = int(h * aspect)
    if target <= w: return im
    side = (target - w) // 2; out = Image.new('RGB', (target, h))
    # each side is the outer 14 % of the photo stretched sideways (the city and the sea simply continue), slightly softened; a long cross-fade hides the seam
    band = int(w * 0.14); ramp = min(band, 160)
    left = im.crop((0, 0, band, h)).resize((side + ramp, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.2))
    right = im.crop((w - band, 0, w, h)).resize((side + ramp, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.2))
    out.paste(left, (0, 0)); out.paste(right, (side + w - ramp, 0)); out.paste(im, (side, 0))
    a = np.asarray(out).astype(np.float32)
    lo = np.asarray(left).astype(np.float32); ro = np.asarray(right).astype(np.float32); io_ = np.asarray(im).astype(np.float32)
    for i in range(ramp):
        t = (i + 0.5) / ramp
        a[:, side + i] = lo[:, side + i] * (1 - t) + io_[:, i] * t                              # left seam: stretched band -> photo
        a[:, side + w - ramp + i] = io_[:, w - ramp + i] * (1 - t) + ro[:, i] * t               # right seam: photo -> stretched band
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))

outdir = sys.argv[1]; os.makedirs(outdir, exist_ok=True); credits = []
for sid, title, crop, aspect in SCENES:
    d = api({'action': 'query', 'format': 'json', 'titles': title, 'prop': 'imageinfo', 'iiprop': 'url|extmetadata', 'iiurlwidth': 2400, 'iiextmetadatafilter': 'LicenseShortName|Artist|LicenseUrl'})
    ii = list(d['query']['pages'].values())[0]['imageinfo'][0]
    lic = ii['extmetadata']['LicenseShortName']['value']; artist = re.sub('<[^>]+>', '', ii['extmetadata'].get('Artist', {}).get('value', 'unknown')).strip()
    if not re.match(r'(?i)(public domain|cc0|cc by [0-9.]+$|pd)', lic): raise SystemExit('licence not allowed for ' + sid + ': ' + lic)
    im = Image.open(io.BytesIO(fetch(ii['thumburl']))).convert('RGB'); w, h = im.size
    im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))
    pano = widen(im, aspect)
    pw, ph = pano.size; W = 2400; pano = pano.resize((W, int(ph * W / pw)), Image.LANCZOS)
    a = np.asarray(pano).astype(np.float32); H = a.shape[0]
    # alpha: the top 30 % fades to transparent (haze of the gulf over the animated sky), the lower part is opaque
    alpha = np.clip((np.arange(H) / (H * 0.30)), 0, 1) ** 1.3
    img = Image.fromarray(a.astype(np.uint8)).convert('RGBA'); img.putalpha(Image.fromarray((np.repeat(alpha[:, None], W, axis=1) * 255).astype(np.uint8)))
    img.save(os.path.join(outdir, sid + '.webp'), 'WEBP', quality=80, alpha_quality=90, method=6)
    # night lights: warm dots on the bright (white / cream) buildings of the lower 60 %, never on the dark pine hill or on the sea / sky
    lum = (0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]); sat = a.max(axis=2) - a.min(axis=2)
    rnd = np.random.RandomState(11); mask = (lum > 150) & (sat < 70) & (np.arange(H)[:, None] > H * 0.42) & (rnd.rand(H, W) < 0.035)
    lights = np.zeros((H, W, 4), np.uint8); ys, xs = np.nonzero(mask)
    for y, x in zip(ys, xs):
        c = (255, int(190 + rnd.rand() * 50), int(100 + rnd.rand() * 60), int(150 + rnd.rand() * 105)); lights[y:y + 2, x:x + 2] = c
    Image.fromarray(lights, 'RGBA').filter(ImageFilter.GaussianBlur(0.7)).save(os.path.join(outdir, sid + '-lights.webp'), 'WEBP', quality=70, alpha_quality=80, method=6)
    credits.append({'file': sid + '.webp', 'lights': sid + '-lights.webp', 'commonsTitle': title[5:], 'commonsUrl': ii['descriptionurl'], 'author': artist, 'license': lic,
                    'licenseUrl': ii['extmetadata'].get('LicenseUrl', {}).get('value', ''), 'attributionRequired': lic.upper().startswith('CC BY'),
                    'changes': 'Cropped, widened with blurred mirrored edges, top faded to transparency, scaled to 2400 px wide, converted to WebP; the lights image is generated from the photo.',
                    'checkedOn': datetime.date.today().isoformat()})
    print(sid, lic, pano.size)
json.dump(credits, open(os.path.join(outdir, 'manifest.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

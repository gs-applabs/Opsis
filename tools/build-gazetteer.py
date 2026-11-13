#!/usr/bin/env python3
"""Builds src/Widgets/Data/gazetteer-gr.tsv.gz from the GeoNames dump of Greece (GR.zip, admin1CodesASCII.txt, admin2Codes.txt).
GeoNames data: https://www.geonames.org (CC BY 4.0). Usage: python tools/build-gazetteer.py <folder with GR.txt, admin1.txt, admin2.txt>"""
import gzip, re, sys, os
src = sys.argv[1]
GREEK = re.compile(r"[Ͱ-Ͽἀ-῿]")
KEEP = {"PPL", "PPLA", "PPLA2", "PPLA3", "PPLA4", "PPLC", "PPLX", "PPLL", "PPLS", "PPLF", "PPLR"}

def greek(alts, fallback):
    for a in alts.split(","):
        if GREEK.search(a):
            return a.strip()
    return fallback

rows = [l.rstrip("\n").split("\t") for l in open(os.path.join(src, "GR.txt"), encoding="utf-8")]
a1, a2 = {}, {}
for r in rows:
    if r[7] == "ADM1": a1[r[10]] = (greek(r[3], r[1]), r[2])
    if r[7] == "ADM2": a2[(r[10], r[11])] = (greek(r[3], r[1]), r[2])
out = []
for code, (el, en) in sorted(a1.items()): out.append(f"A1\t{code}\t{el}\t{en}")
for (c1, c2), (el, en) in sorted(a2.items()): out.append(f"A2\t{c1}\t{c2}\t{el}\t{en}")
n = 0
for r in rows:
    if r[6] != "P" or r[7] not in KEEP: continue
    el = greek(r[3], r[1])
    out.append("P\t%s\t%s\t%.4f\t%.4f\t%s\t%s\t%s" % (el, r[2], float(r[4]), float(r[5]), r[10], r[11], r[7]))
    n += 1
dst = os.path.join("src", "Widgets", "Data", "gazetteer-gr.tsv.gz")
with gzip.GzipFile(dst, "wb", mtime=0) as f: f.write(("\n".join(out) + "\n").encode("utf-8"))
print(n, "places,", len(a1), "regions,", len(a2), "units ->", os.path.getsize(dst), "bytes")

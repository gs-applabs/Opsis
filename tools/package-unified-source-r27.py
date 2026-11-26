#!/usr/bin/env python3
"""Pack one source+asset snapshot; retain newest review set per city, not old R17–R21 duplicates."""
import json, pathlib, shutil, subprocess, zipfile, hashlib
root=pathlib.Path.cwd()
stage=root/"consolidated-package";stage.mkdir()
source=stage/"source";source.mkdir()
with subprocess.Popen(["git","archive","--format=tar","HEAD"],stdout=subprocess.PIPE) as proc:
    subprocess.run(["tar","-xf","-","-C",str(source)],stdin=proc.stdout,check=True)
    proc.stdout.close()
    if proc.wait():raise RuntimeError("git archive failed")
review=stage/"wallpapers"/"photo-review"
review.mkdir(parents=True)
records={}
for rev in ("r22","r23","r25"):
    for p in sorted((root/"incoming"/rev).rglob("review.json")):
        id=p.parent.name
        if not id.startswith("eu-") or id=="eu-athens" or not id.replace("-","").isalnum():raise ValueError(id)
        m=json.loads(p.read_text(encoding="utf-8"))
        if m.get("review_status")!="unapproved-draft":raise ValueError(id+" invalid review status")
        for n,h in m["output_hashes"].items():
            if pathlib.Path(n).name!=n or hashlib.sha256((p.parent/n).read_bytes()).hexdigest()!=h:raise ValueError(id+" hash mismatch")
        dest=review/id
        if dest.exists():shutil.rmtree(dest)
        shutil.copytree(p.parent,dest)
        records[id]=rev
for src,dst in [("installer","installers/R26-preview"),("host","installers/R26-compatible-host")]:
    d=stage/dst;d.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(root/"incoming"/src,d)
meta={"status":"review-only; not final release","source_commit":subprocess.check_output(["git","rev-parse","HEAD"],text=True).strip(),"photo_review_cities":records,"missing":["R16 Dublin original local authoring archive","London replacement","Podgorica replacement","original 110-wallpaper add-on source and assets","local R20 integrated source and offline patches"],"installer":"previous tested R26 preview installer and matching host; not a build of R27 source"}
(stage/"PACKAGE-STATUS.json").write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding="utf-8")
(stage/"START-HERE.txt").write_text("OPSIS R27 consolidated development package. Read source/docs/SOURCE-CONSOLIDATION.md and PACKAGE-STATUS.json. The R26 installer is a preview and its compatible host is included; R27 source is not the R26 executable. Do not publish as final. No rejected synthetic R7/R8/R10 or old acquisition archives. No previous archive copies of the source.\n",encoding="utf-8")
out=root/"dist";out.mkdir()
archive=out/"OPSIS-3.0.0.3200-Unified-R27.zip"
with zipfile.ZipFile(archive,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=3,allowZip64=True) as z:
    for p in sorted(stage.rglob("*")):
        if p.is_file():z.write(p,p.relative_to(stage))
with zipfile.ZipFile(archive) as z:
    bad=z.testzip()
    if bad:raise RuntimeError("ZIP integrity failed: "+bad)
    print(json.dumps({"files":len(z.namelist()),"review_cities":len(records),"zip_bytes":archive.stat().st_size}))
h=hashlib.sha256()
with archive.open("rb") as f:
    for chunk in iter(lambda:f.read(1024*1024),b""):h.update(chunk)
(out/"SHA256.txt").write_text(h.hexdigest()+"  "+archive.name+"\n",encoding="utf-8")

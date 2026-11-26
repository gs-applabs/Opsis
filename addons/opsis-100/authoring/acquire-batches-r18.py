#!/usr/bin/env python3
"""Isolate city failures and preserve each completed batch for review."""
import argparse, importlib.util, json, shutil, subprocess, sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
def load():
    spec=importlib.util.spec_from_file_location("acquire",HERE/"acquire-commons-candidates.py")
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
def main():
    p=argparse.ArgumentParser()
    p.add_argument("--catalog",type=Path,required=True)
    p.add_argument("--out",type=Path,required=True)
    p.add_argument("--batch",type=int,default=0)
    p.add_argument("--batch-size",type=int,default=7)
    p.add_argument("--city")
    a=p.parse_args()
    cities=[c for c in json.loads(a.catalog.read_text(encoding="utf-8"))["scenes"] if c["id"] not in ("eu-athens","eu-dublin")]
    priority=["madrid","copenhagen","stockholm","oslo","helsinki","paris","rome","andorra-la-vella","ankara"]
    rank={f"eu-{s}":i for i,s in enumerate(priority)}
    cities.sort(key=lambda c:(rank.get(c["id"],100),c["id"]))
    a.out.mkdir(parents=True,exist_ok=True)
    if a.city:
        city=next(c for c in cities if c["id"]==a.city)
        row=load().acquire(city,a.out)
        print(json.dumps(row,ensure_ascii=False),flush=True)
        (a.out/(a.city+".result.json")).write_text(json.dumps(row,ensure_ascii=False),encoding="utf-8")
        return
    report=[]
    for c in cities[a.batch*a.batch_size:(a.batch+1)*a.batch_size]:
        slug=c["id"]; result=a.out/(slug+".result.json")
        result.unlink(missing_ok=True)
        try:
            subprocess.run([sys.executable,str(Path(__file__).resolve()),"--catalog",str(a.catalog),"--out",str(a.out),"--city",slug],check=True,timeout=160)
            row=json.loads(result.read_text(encoding="utf-8"))
        except Exception as e:
            shutil.rmtree(a.out/slug,ignore_errors=True)
            row={"id":slug,"status":"not-acquired","error":str(e)}
        if row.get("status")!="candidate-only":
            shutil.rmtree(a.out/slug,ignore_errors=True)
        result.unlink(missing_ok=True)
        report.append(row)
        (a.out/"report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
        print(json.dumps(row,ensure_ascii=False),flush=True)
if __name__=="__main__": main()

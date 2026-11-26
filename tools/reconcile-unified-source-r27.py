#!/usr/bin/env python3
"""Three-way reconcile only text conflicts that Git can merge without markers.
Keep unresolved conflicts as exact Git references, never introduce conflict markers
or overwrite an unresolved file. Do not downgrade the RC version from 2.8 main."""
import json, pathlib, subprocess, tempfile
ROOT=pathlib.Path.cwd()
report_path=ROOT/"docs/SOURCE-CONSOLIDATION.json"
report=json.loads(report_path.read_text(encoding="utf-8"))
def blob(sha):
    return subprocess.check_output(["git","cat-file","blob",sha])
def textlike(b):
    if b"\0" in b or len(b)>3*1024*1024:return False
    try:b.decode("utf-8");return True
    except UnicodeDecodeError:return False
resolved=[];pending=[]
for row in report["conflicts"]:
    path=row["path"];p=ROOT/path
    if row["branch"]=="main-2.8-followup" and path in ("Version.props","Opsis.csproj","global.json"):
        pending.append({**row,"reason":"do not downgrade RC; manually port relevant changes"});continue
    try:
        if not p.is_file() or not row["base_blob"] or not row["incoming_blob"]:
            raise ValueError("add/add or missing common base")
        a,b,c=p.read_bytes(),blob(row["base_blob"]),blob(row["incoming_blob"])
        if a==c or b==c:
            resolved.append({**row,"method":"identical/no incoming delta"});continue
        if a==b:
            p.write_bytes(c);resolved.append({**row,"method":"current equals base"});continue
        if not all(textlike(x) for x in (a,b,c)):raise ValueError("not safe UTF-8 text")
        with tempfile.TemporaryDirectory() as d:
            files=[pathlib.Path(d)/n for n in ("current","ancestor","incoming")]
            for f,data in zip(files,(a,b,c)):f.write_bytes(data)
            merged=subprocess.run(["git","merge-file","-p",*map(str,files)],capture_output=True)
            if merged.returncode:raise ValueError("Git three-way conflict")
            if any(x in merged.stdout for x in (b"<<<<<<< ",b">>>>>>> ")):raise ValueError("conflict marker")
            p.write_bytes(merged.stdout)
        resolved.append({**row,"method":"clean Git three-way merge"})
    except Exception as e:pending.append({**row,"reason":str(e)})
report["three_way_resolved"]=resolved
report["unresolved_after_three_way"]=pending
report["status"]="PARTIAL_CONSOLIDATION_SOURCE_REVIEW_REQUIRED" if pending else "SOURCE_RECONCILED_TESTS_PENDING"
report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
md=ROOT/"docs/SOURCE-CONSOLIDATION.md"
md.write_text(md.read_text(encoding="utf-8")+"\n## Automated three-way result\n\nCleanly reconciled: "+str(len(resolved))+". Unresolved: "+str(len(pending))+".\n\n"+"\n".join("- "+r["branch"]+": "+r["path"]+" — "+r["reason"] for r in pending)+"\n",encoding="utf-8")
print(json.dumps({"resolved":len(resolved),"unresolved":len(pending),"paths":[r["path"] for r in pending]},ensure_ascii=False))

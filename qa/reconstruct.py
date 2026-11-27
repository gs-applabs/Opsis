#!/usr/bin/env python3
"""Reconstruct the exact R13 compile-only snapshot. No release or installer."""
import argparse, hashlib, io, json, pathlib, re, subprocess, zipfile
OMIT = frozenset([
 'src/Widgets/wallpaper/scenes/europe/amsterdam.webp',
 'src/Widgets/wallpaper/scenes/europe/ancient-rome-320.webp',
 'src/Widgets/wallpaper/scenes/europe/berlin.webp',
 'src/Widgets/wallpaper/scenes/europe/brussels.webp',
 'src/Widgets/wallpaper/scenes/europe/budapest.webp',
 'src/Widgets/wallpaper/scenes/europe/lisbon.webp',
 'src/Widgets/wallpaper/scenes/europe/london.webp',
 'src/Widgets/wallpaper/scenes/europe/prague.webp',
 'src/Widgets/wallpaper/scenes/europe/vienna.webp',
 'wallpapers/samples/opsis-glass-signature/signature-preview-60s.mp4',
 *['wallpapers/samples/opsis-glass-signature/static/OPSIS-Spectrum-%02d-%s-1920x1080.png'%(i+1,c) for i,c in enumerate(('Blue','Violet','Magenta','Amber','Lime','Emerald'))],
])
OVERLAY_SHA256='8a79978c6d8f0904c1edd6fac16679527d97295cff9567aeafafdd99c3dd03b8'

def git(repo,*args):
 return subprocess.check_output(['git','-C',str(repo),*args])

def blob_id(data):return hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()

def expected_manifest(data):
 result={}
 for line in data.decode('utf-8').splitlines():
  h,n,m,p=line.split('\t'); pp=pathlib.PurePosixPath(p)
  if p in result or not re.fullmatch('[a-f0-9]{40}',h) or not n.isdigit() or m not in ('100644','100755') or pp.is_absolute() or '..' in pp.parts or '\\' in p or ':' in p or any(x in ('.git','.','') for x in pp.parts): raise ValueError('invalid manifest row: '+p)
  result[p]=(h,int(n),m)
 if len(result)!=820 or not OMIT<=set(result):raise ValueError('manifest count or omissions changed')
 return result

def verify(data,record,p):
 if len(data)!=record[1] or blob_id(data)!=record[0]:raise ValueError('content mismatch: '+p)

def reconstruct(repo,dest,evidence):
 if dest.exists():raise ValueError('destination already exists')
 manifest=expected_manifest(git(repo,'show','HEAD:qa/manifest.tsv'))
 overlay=git(repo,'show','HEAD:qa/compile-overlay.zip')
 if hashlib.sha256(overlay).hexdigest()!=OVERLAY_SHA256:raise ValueError('overlay digest mismatch')
 with zipfile.ZipFile(io.BytesIO(overlay)) as z:
  names=z.namelist()
  if len(names)!=len(set(names)) or set(names)&OMIT or not set(names)<=set(manifest) or len(names)!=174:raise ValueError('overlay coverage mismatch')
  existing={p:record[0] for p,record in manifest.items() if p not in OMIT and p not in names}
  if len(existing)!=630 or set(names)|set(existing)|OMIT!=set(manifest):raise ValueError('snapshot and overlay coverage mismatch')
  dest.mkdir(parents=True)
  with subprocess.Popen(['git','-C',str(repo),'cat-file','--batch'],stdin=subprocess.PIPE,stdout=subprocess.PIPE) as proc:
   for p,h in existing.items():
    proc.stdin.write((h+'\n').encode());proc.stdin.flush()
    header=proc.stdout.readline().decode().strip().split()
    if header!=[h,'blob',str(manifest[p][1])]:raise ValueError('unexpected blob header: '+p)
    data=proc.stdout.read(manifest[p][1]);
    if proc.stdout.read(1)!=b'\n':raise ValueError('invalid blob framing: '+p)
    verify(data,manifest[p],p);out=dest/p;out.parent.mkdir(parents=True,exist_ok=True);out.write_bytes(data)
   proc.stdin.close()
   if proc.wait()!=0:raise ValueError('git cat-file failed')
  for p in names:
   data=z.read(p);verify(data,manifest[p],p);out=dest/p;out.parent.mkdir(parents=True,exist_ok=True);out.write_bytes(data)
 evidence.mkdir(parents=True,exist_ok=True)
 report={'stagingCommit':git(repo,'rev-parse','HEAD').decode().strip(),'expectedFiles':820,'verifiedFiles':804,'fromGitBlobs':630,'fromOverlay':174,'omittedMedia':sorted(OMIT),'overlaySha256':OVERLAY_SHA256,'productionApproved':False}
 (evidence/'source-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps(report,indent=2))

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('repo',type=pathlib.Path);p.add_argument('dest',type=pathlib.Path);p.add_argument('evidence',type=pathlib.Path);a=p.parse_args();reconstruct(a.repo.resolve(),a.dest.resolve(),a.evidence.resolve())

#!/usr/bin/env python3
"""Explicit, offline maintenance of an existing per-user Opsis plugin package.
No DLL execution, trust auto-approval, network access or deletion of backups.
Run with Opsis and all external plugin installers closed. Production CLI takes
its normal Windows single-instance mutex; testable operations accept a temp root.
"""
import argparse, contextlib, ctypes, hashlib, json, os, re, stat, sys, uuid, zipfile
from pathlib import Path

MAX_FILES = 4096
MAX_BYTES = 512 * 1024 * 1024
NAME = re.compile(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}\Z')
HEX = re.compile(r'[0-9a-fA-F]{64}\Z')
RESERVED = re.compile(r'(?i)^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)')

def component(s):
    if not s or s in ('.', '..') or s.endswith((' ', '.')) or RESERVED.match(s) or any(ord(c)<32 or c in '<>:"/\\|?*' for c in s):
        raise ValueError('Unsafe Windows path component: ' + repr(s))
    return s

def package_name(s):
    if not NAME.fullmatch(s): raise ValueError('Invalid package folder name')
    return component(s)

def safe_path(p):
    p = Path(p).absolute()
    for q in (*reversed(p.parents), p):
        if q.is_symlink() or (q.exists() and getattr(q.lstat(), 'st_file_attributes', 0) & 1024):
            raise ValueError('Linked/reparse paths are not allowed: ' + str(q))
    return p

def sha(p):
    with open(p, 'rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()

def inventory(folder):
    safe_path(folder)
    rows = {}; total = 0
    for base, dirs, files in os.walk(folder, followlinks=False):
        for name in dirs + files: component(name); safe_path(Path(base)/name)
        for name in files:
            p = Path(base)/name
            if not p.is_file() or not stat.S_ISREG(p.stat().st_mode): raise ValueError('Nonregular file')
            rel = p.relative_to(folder).as_posix(); key=rel.casefold()
            if key in rows: raise ValueError('Case collision')
            total += p.stat().st_size
            if len(rows)>=MAX_FILES or total>MAX_BYTES: raise ValueError('Package limits exceeded')
            rows[key] = [rel, sha(p)]
    return rows

def atomic_json(path, data):
    tmp = path.with_name(path.name + '.tmp-' + uuid.uuid4().hex)
    with tmp.open('x', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, sort_keys=True); f.flush(); os.fsync(f.fileno())
    os.replace(tmp, path)

def extract_verified(archive, expected, stage):
    if not HEX.fullmatch(expected): raise ValueError('Expected SHA256 required')
    # Copy and verify the very bytes which are extracted, not a second open of the external ZIP.
    local = stage.parent/'package.zip'
    if Path(archive).stat().st_size > MAX_BYTES: raise ValueError('Archive too large')
    with open(archive,'rb') as f, local.open('xb') as out:
        copied=0
        while chunk:=f.read(1024*1024):
            copied+=len(chunk)
            if copied>MAX_BYTES: raise ValueError('Archive too large')
            out.write(chunk)
    if sha(local) != expected.lower(): raise ValueError('ZIP SHA256 mismatch')
    with zipfile.ZipFile(local) as z:
        entries=z.infolist(); total=0; seen=set(); paths={}
        if len(entries)>MAX_FILES: raise ValueError('Too many entries')
        for e in entries:
            # ZipInfo may normalize separators on Windows or truncate a NUL.
            # Validate the original central-directory name before using filename.
            raw=e.orig_filename
            if raw!=e.filename or '\\' in raw or '\x00' in raw: raise ValueError('Unsafe ZIP raw path')
            n=e.filename.rstrip('/')
            parts=n.split('/')
            if not n or '\\' in n or any(component(x)!=x for x in parts): raise ValueError('Unsafe ZIP path')
            key=n.casefold()
            if key in seen: raise ValueError('Duplicate ZIP path')
            seen.add(key)
            kind=(e.external_attr>>16)&0o170000
            if kind not in (0,stat.S_IFREG,stat.S_IFDIR) or e.external_attr & 1024: raise ValueError('Linked/special ZIP entry')
            total+=e.file_size
            if total>MAX_BYTES: raise ValueError('Uncompressed size limit')
            for i in range(1,len(parts)+1):
                sub='/'.join(parts[:i]); k=sub.casefold()
                if k in paths and paths[k]!=sub: raise ValueError('Case collision')
                paths[k]=sub
            target=stage.joinpath(*parts)
            if e.is_dir(): target.mkdir(parents=True,exist_ok=True); continue
            target.parent.mkdir(parents=True,exist_ok=True)
            with z.open(e) as src, target.open('xb') as out:
                copied=0
                while chunk:=src.read(1024*1024):
                    copied+=len(chunk)
                    if copied>e.file_size: raise ValueError('Unexpected expanded size')
                    out.write(chunk)
                if copied!=e.file_size: raise ValueError('Unexpected expanded size')
    local.unlink()
    if not any(p.is_file() and p.name.lower().endswith('plugin.dll') for p in stage.iterdir()): raise ValueError('ZIP must contain an entry *Plugin.dll at its root')
    return inventory(stage)

class Maintenance:
    def __init__(self, data):
        self.data=safe_path(data); self.plugins=safe_path(self.data/'plugins'); self.root=safe_path(self.data/'plugin-maintenance'); self.marker=self.root/'pending.json'
    def transaction(self, name, archive, expected):
        name=package_name(name); live=safe_path(self.plugins/name)
        safe_path(self.marker)
        if self.marker.exists(): raise ValueError('Incomplete transaction: recover first')
        if not live.is_dir(): raise ValueError('Only an existing per-user package can be replaced')
        self.root.mkdir(parents=True,exist_ok=True)
        tid=uuid.uuid4().hex; txn=self.root/tid; txn.mkdir(); stage=txn/'stage'; stage.mkdir()
        old=inventory(live); new=extract_verified(archive,expected,stage)
        entry = lambda rows: {k for k in rows if '/' not in k and k.endswith('plugin.dll')}
        if not entry(old) or entry(old)!=entry(new): raise ValueError('Entry DLL names must match the installed package')
        if old==new: raise ValueError('Package unchanged')
        record={'id':tid,'name':name,'old':old,'new':new,'zip_sha256':expected.lower()}
        atomic_json(txn/'receipt.json',record)
        # Do not expose backups/staging to plugin discovery. Persist intent before either rename.
        atomic_json(self.marker,{'id':tid,'name':name})
        if inventory(live)!=old or inventory(stage)!=new: raise ValueError('Package changed during maintenance; recover first')
        os.rename(live,txn/'backup')
        os.rename(stage,live)
        if inventory(live)!=new: raise ValueError('Installed files failed verification; recover first')
        self.marker.unlink()
        return tid
    def record(self, tid):
        if not re.fullmatch('[0-9a-f]{32}',tid): raise ValueError('Invalid transaction ID')
        txn=safe_path(self.root/tid); r=json.loads(safe_path(txn/'receipt.json').read_text(encoding='utf-8'))
        if r['id']!=tid: raise ValueError('Receipt mismatch')
        return txn,r,safe_path(self.plugins/package_name(r['name']))
    def restore(self, tid, recovering=False):
        safe_path(self.marker)
        txn,r,live=self.record(tid); backup=safe_path(txn/'backup'); displaced=safe_path(txn/'displaced')
        if self.marker.exists():
            pending=json.loads(self.marker.read_text(encoding='utf-8'))
            if pending!={'id':tid,'name':r['name']}: raise ValueError('Another transaction is incomplete')
        elif recovering: raise ValueError('No pending transaction')
        if live.is_dir() and inventory(live)==r['old']:
            if self.marker.exists(): self.marker.unlink()
            return
        if not backup.is_dir() or inventory(backup)!=r['old']: raise ValueError('Verified original backup unavailable')
        if live.exists() and (not live.is_dir() or inventory(live)!=r['new'] or displaced.exists()): raise ValueError('Live package changed; manual recovery required (no files overwritten)')
        atomic_json(self.marker,{'id':tid,'name':r['name']})
        if live.exists(): os.rename(live,displaced)
        os.rename(backup,live)
        if inventory(live)!=r['old']: raise ValueError('Restored package failed verification')
        self.marker.unlink()
    def recover(self):
        pending=json.loads(safe_path(self.marker).read_text(encoding='utf-8')); self.restore(pending['id'],recovering=True)

@contextlib.contextmanager
def opsis_mutex():
    if os.name!='nt': raise RuntimeError('Production maintenance requires Windows')
    if os.environ.get('OPSIS_APPDATA'): raise RuntimeError('Isolated test profiles not supported by CLI')
    k=ctypes.WinDLL('kernel32',use_last_error=True)
    k.CreateMutexW.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_wchar_p]; k.CreateMutexW.restype=ctypes.c_void_p
    k.CloseHandle.argtypes=[ctypes.c_void_p]; k.ReleaseMutex.argtypes=[ctypes.c_void_p]
    h=k.CreateMutexW(None,1,r'Local\Opsis.SingleInstance'); err=ctypes.get_last_error()
    if not h: raise ctypes.WinError(err)
    try:
        if err==183: raise RuntimeError('Close Opsis before maintenance')
        try: yield
        finally: k.ReleaseMutex(h)
    finally: k.CloseHandle(h)

def main():
    p=argparse.ArgumentParser(description=__doc__); sub=p.add_subparsers(dest='cmd',required=True)
    a=sub.add_parser('update'); a.add_argument('name'); a.add_argument('archive'); a.add_argument('--sha256',required=True)
    b=sub.add_parser('rollback'); b.add_argument('transaction'); sub.add_parser('recover')
    p.add_argument('--confirm',action='store_true',required=True,help='Explicitly authorize disk changes, after closing external plugin installers')
    args=p.parse_args()
    with opsis_mutex():
        m=Maintenance(Path(os.environ['APPDATA'])/'Opsis')
        if args.cmd=='update': print('Transaction:',m.transaction(args.name,args.archive,args.sha256))
        elif args.cmd=='rollback': m.restore(args.transaction)
        else: m.recover()
    print('Done. Restart Opsis; normal plugin approval still applies.')

if __name__=='__main__':
    try: main()
    except Exception as e: print('Maintenance stopped:',e,file=sys.stderr); sys.exit(1)

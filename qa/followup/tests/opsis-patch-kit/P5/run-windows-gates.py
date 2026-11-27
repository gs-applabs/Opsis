#!/usr/bin/env python3
"""Development gate only; intentionally never signs, installs or publishes a release."""
import argparse, hashlib, json, os, pathlib, re, shutil, subprocess, sys, tempfile, time, xml.etree.ElementTree as ET
KIT=pathlib.Path(__file__).resolve().parents[1]
TESTS=[('cache','P0.2-P0.3-Hotfix/tests/CacheTests/CacheTests.csproj'),('updater','P0.4-P0.5/tests/UpdaterTests/UpdaterTests.csproj'),('metadata','P1.2/tests/MetadataTests/MetadataTests.csproj'),('viewport','P1.4/ViewportTests/ViewportTests.csproj'),('recovery','P0.8/RecoveryTests/RecoveryTests.csproj'),('packages','P1.5/PackageTests/PackageTests.csproj'),('monitor-scenes','P1.6/MonitorTests/MonitorTests.csproj'),('package-lineage','P1.7/LineageTests/LineageTests.csproj'),('manifest','P1.11/ManifestTests/ManifestTests.csproj')]
def git_text(repo,*args):
    return subprocess.check_output(['git','-C',str(repo),*args],text=True).strip()

def source_provenance(repo):
    head=git_text(repo,'rev-parse','--verify','HEAD^{commit}')
    if git_text(repo,'status','--porcelain','--untracked-files=all'):
        raise ValueError('development gate requires a clean committed source checkout')
    return head

def verify_tracked_source(repo,head):
    if git_text(repo,'rev-parse','--verify','HEAD^{commit}')!=head or git_text(repo,'status','--porcelain','--untracked-files=no'):
        raise RuntimeError('source commit or tracked files changed during validation')

def file_record(path):
    h=hashlib.sha256();n=0
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b);n+=len(b)
    return {'path':path.name,'bytes':n,'sha256':h.hexdigest()}

def history_publish(repo,publish):
    """Every generated runtime asset must reach publish byte-for-byte; no approval inferred."""
    rel=pathlib.Path('scenes/living-history');source=repo/'src/Widgets/wallpaper'/rel;dest=publish/'widgets/wallpaper'/rel
    raw=(source/'catalog.json').read_bytes()
    if len(raw)>256*1024:raise RuntimeError('oversize history catalog')
    try:entries=json.loads(raw)
    except (ValueError,TypeError) as e:raise RuntimeError('invalid history catalog') from e
    if not isinstance(entries,list) or len(entries)>32:raise RuntimeError('history catalog must be an array of at most 32 entries')
    names=[pathlib.Path('catalog.json')];seen=set()
    for entry in entries:
        id=entry.get('id') if isinstance(entry,dict) else None
        if not isinstance(id,str) or not re.fullmatch(r'history-[a-z0-9-]{1,60}',id) or id in seen or entry.get('historical') is not True or entry.get('manualOnly') is not True or not entry.get('sceneTimeZone') or entry.get('file')!=f'scenes/living-history/{id}/day.webp':raise RuntimeError('invalid or duplicate history entry')
        seen.add(id);names.extend(pathlib.Path(id)/(role+'.webp') for role in ('day','golden','blue','night','poster'))
    for name in names:
        a,b=source/name,dest/name
        if not a.is_file() or not b.is_file() or a.is_symlink() or b.is_symlink() or file_record(a)!=file_record(b):raise RuntimeError('history asset missing or altered: '+str(name))
    return {'scenes':len(seen),'filesVerified':len(names)}

SELFTEST_NAMES = frozenset(("HardwareProfile.DetectAndOverrideRoundTrip", "AtomicFile.CorruptedFileRecovery", "SafeMode.CounterRoundTrip", "WallpaperPackages.PreflightAndTransactionalImport"))

def validate_selftest(path):
    if not path.is_file() or path.stat().st_size>1024*1024:raise RuntimeError('missing or oversized self-test report')
    try:doc=json.loads(path.read_text(encoding='utf-8-sig'))
    except (ValueError,OSError) as e:raise RuntimeError('invalid self-test report') from e
    if not isinstance(doc,dict) or doc.get('Passed') is not True or not isinstance(doc.get('Checks'),list):raise RuntimeError('self-test did not pass')
    names=set()
    for check in doc['Checks']:
        if not isinstance(check,dict) or not isinstance(check.get('Name'),str) or not check['Name'] or check['Name'] in names or check.get('Passed') is not True:raise RuntimeError('invalid, duplicate or failing self-test check')
        names.add(check['Name'])
    if not SELFTEST_NAMES.issubset(names):raise RuntimeError('required self-test checks missing')
    return sorted(names)

# Only these explicitly unavailable media may be omitted from this compile-only
# snapshot. No code, DLL, configuration, poster or approval record may be skipped.
COMPILE_ONLY_MEDIA = frozenset([
    *('src/Widgets/wallpaper/scenes/europe/'+n+'.webp' for n in ('amsterdam','ancient-rome-320','berlin','brussels','budapest','lisbon','london','prague','vienna')),
    'wallpapers/samples/opsis-glass-signature/signature-preview-60s.mp4',
    *('wallpapers/samples/opsis-glass-signature/static/OPSIS-Spectrum-%02d-%s-1920x1080.png'%(i+1,c) for i,c in enumerate(('Blue','Violet','Magenta','Amber','Lime','Emerald'))),
])

def compile_only_coverage(repo, verification):
    if verification is None:return [], None
    if not verification.is_file() or verification.stat().st_size>64*1024:raise ValueError('invalid compile-only verification report')
    doc=json.loads(verification.read_text(encoding='utf-8-sig'))
    paths=doc.get('omittedMedia') if isinstance(doc,dict) else None
    if not isinstance(paths,list) or any(not isinstance(p,str) for p in paths) or len(paths)!=len(set(paths)) or set(paths)!=COMPILE_ONLY_MEDIA:raise ValueError('unexpected compile-only media list')
    if doc.get('productionApproved') is not False or doc.get('expectedFiles')!=820 or doc.get('verifiedFiles')!=804 or doc.get('fromGitBlobs')!=630 or doc.get('fromOverlay')!=174:raise ValueError('invalid compile-only verification counts')
    if any((repo/p).exists() for p in paths):raise ValueError('declared omitted media are present: use normal gate')
    prefix='src/Widgets/wallpaper/'
    return sorted(p.removeprefix(prefix) for p in paths if p.startswith(prefix)), doc

def run_gate(repo,report,iscc=None,verification=None):
    report=report.resolve(); repo=repo.resolve()
    if report.is_relative_to(repo) or report.exists():raise ValueError('report directory must be new and outside source repo')
    if os.name!='nt':raise ValueError('Windows required; Linux is not evidence of Windows build/installer compatibility')
    if not (repo/'Opsis.csproj').is_file():raise ValueError('not an Opsis checkout')
    if not (repo/'src/UI/GalleryViewport.cs').is_file():raise ValueError('P1.4 patch missing')
    if not (repo/'src/Widgets/RecoveryBudget.cs').is_file():raise ValueError('P0.8 patch missing')
    for required in ('src/Widgets/MonitorScenePolicy.cs','src/Services/PackageLineage.cs'):
        if not (repo/required).is_file():raise ValueError('missing final patch: '+required)
    for relative,marker in (('src/Widgets/wallpaper/weather-procedural.js','waterDetail'),('src/Services/WallpaperPackages.cs','CheckAncestors(dir)'),('src/UI/MainWindow.WallpaperGallery.cs','Identical package already installed.')):
        if marker not in (repo/relative).read_text(encoding='utf-8-sig'):raise ValueError('missing P2.2/P1.9 patch: '+relative)
    for exe in ('dotnet','git','node'):
        if shutil.which(exe) is None:raise ValueError(exe+' missing')
    if iscc and not iscc.is_file():raise ValueError('Inno Setup compiler missing')
    commit=source_provenance(repo)
    omitted, coverage=compile_only_coverage(repo, verification)
    if coverage is not None and iscc is not None:raise ValueError('compile-only source cannot compile an installer')
    for marker in ('const videoAudioGains = new WeakMap();','function syncVideoRotation()'):
        if marker not in (repo/'src/Widgets/wallpaper/index.html').read_text(encoding='utf-8-sig'):raise ValueError('missing P0.11: '+marker)
    if not (repo/'src/Widgets/wallpaper/scenes/living-history/catalog.json').is_file():raise ValueError('missing P3.2 history catalog')
    if 'OpsisLivingHistory.draw' not in (repo/'src/Widgets/wallpaper/weather.js').read_text(encoding='utf-8-sig'):raise ValueError('missing P3.2 renderer integration')
    for relative,marker in (('src/Widgets/wallpaper/weather-procedural.js','mountainBuilds'),('src/Services/WallpaperPackages.cs','ValidatePackageHierarchy')):
        if marker not in (repo/relative).read_text(encoding='utf-8-sig'):raise ValueError('missing P2.4/P1.10 patch: '+relative)
    if "private static bool ValidSlider(" not in (repo/"src/Widgets/WallpaperManifest.cs").read_text(encoding="utf-8-sig"):raise ValueError("missing P1.11 manifest patch")
    if 'class OpsisFramePacer' not in (repo/'src/Widgets/wallpaper/index.html').read_text(encoding='utf-8-sig'):raise ValueError('missing P0.12 frame pacer')
    if 'OPSIS P2.5:' not in (repo/'src/Widgets/wallpaper/weather-procedural.js').read_text(encoding='utf-8-sig'):raise ValueError('missing P2.5 renderer')
    report.mkdir(parents=True)
    results=[]; result={'productionApproved':False,'runner':os.environ.get('RUNNER_OS','Windows'), 'steps':results,'unverified':['real GPU and multi-monitor stress','interactive UI','plugin ABI external apps','upgrade/reinstall/uninstall','add-on payload approval','historical assets']}
    if coverage is not None:result['compileOnlySourceCoverage']=coverage
    def step(name,args,timeout=600,env=None,cwd=None,required=True):
        start=time.monotonic(); path=report/(name+'.log')
        with path.open('w',encoding='utf-8') as f:
            try:
                p=subprocess.run([str(x) for x in args],cwd=cwd or repo,env=env,stdout=f,stderr=subprocess.STDOUT,timeout=timeout,check=False);code=p.returncode
            except subprocess.TimeoutExpired:code=124
            except OSError as e:f.write(str(e));code=127
        results.append({'name':name,'exitCode':code,'seconds':round(time.monotonic()-start,2),'log':path.name});print(name,code,flush=True)
        if code and required:raise RuntimeError(name+' failed')
        return code==0
    try:
        result['commit']=commit
        result['sourceVersion']=ET.parse(repo/'Version.props').findtext('.//OpsisAppVersion')
        if result['sourceVersion']=='3.0.0.3200':raise RuntimeError('Production version already set; this kit is development-only')
        step('wallpaper-js',[sys.executable,KIT/'P5/check-wallpaper-js.py',repo],required=False)
        pacer_env=dict(os.environ,OPSIS_PACER_HTML=str(repo/'src/Widgets/wallpaper/index.html'))
        step('frame-pacer',['node','--test',KIT/'P0.12/pacer.test.js'],env=pacer_env,required=False)
        js_tests=sorted((repo/'tests').glob('*.test.cjs'))+sorted((repo/'tests/weather-spatial').glob('*.test.cjs'))
        js_env=dict(os.environ);js_env.pop('OPSIS_QA_OMITTED_WEATHER_MEDIA',None)
        if coverage is not None:js_env['OPSIS_QA_OMITTED_WEATHER_MEDIA']=json.dumps(omitted)
        if js_tests:step('repo-js-tests',['node','--test',*js_tests],env=js_env,required=False)
        step('baseline-release',[sys.executable,repo/'tests/integration/check_baseline_and_release.py'],required=False)
        step('maintenance',[sys.executable,'-m','unittest','discover','-s','tests/integration','-p','test_*.py'],required=False)
        step('sdk',['dotnet','--info'])
        step('build',['dotnet','build','Opsis.csproj','-c','Release','-r','win-x64','--nologo'])
        for name,rel in TESTS:
            cmd=['dotnet','run','--project',KIT/rel,'-c','Release','-p:OpsisRepo='+str(repo)]
            step('test-'+name,cmd,required=False)
        with tempfile.TemporaryDirectory(prefix='opsis-gate-publish-') as td:
            publish=pathlib.Path(td)/'publish'
            step('publish',['dotnet','publish','Opsis.csproj','-c','Release','-r','win-x64','--self-contained','false','-o',publish])
            required=['Opsis.exe','Opsis.dll','widgets/wallpaper/index.html','widgets/wallpaper/weather-procedural.js','widgets/wallpaper/history-renderer.js','widgets/wallpaper/scenes/living-history/catalog.json','assets/seasonal-brand/christmas.png','widgets/wallpaper/gallery-weather-preview.html','widgets/wallpaper/gallery-weather-preview.js']
            missing=[p for p in required if not (publish/p).is_file()]
            if missing:raise RuntimeError('publish missing '+', '.join(missing))
            result['historyPublish']=history_publish(repo,publish)
            # Execute the existing published app self-test, not a second implementation.
            # Never touch the real user settings. Each test has a fresh isolated directory.
            isolated=pathlib.Path(td)/'isolated-selftest';isolated.mkdir()
            env={k:v for k,v in os.environ.items() if not k.upper().startswith('OPSIS_')};env.update(OPSIS_APPDATA=str(isolated),OPSIS_E2E='1')
            step('published-selftest',[publish/'Opsis.exe','--run-selftest'],timeout=120,env=env,cwd=publish)
            selftest=isolated/'selftest-report.json'
            result['publishedSelfTestChecks']=validate_selftest(selftest)
            shutil.copyfile(selftest,report/'published-selftest.json')
            result['nativeExplorerPresent']=(publish/'OpsisExplorer.dll').is_file()
            result['publishedBinaries']=[file_record(publish/name) for name in ('Opsis.exe','Opsis.dll')]
            if result['nativeExplorerPresent']:result['publishedBinaries'].append(file_record(publish/'OpsisExplorer.dll'))
            if iscc:
                out=report/'development-installer';out.mkdir();step('inno',[iscc,'/DPublishDir='+str(publish),'/O'+str(out),str(repo/'installer.iss')])
                if not list(out.glob('OpsisSetup-*.exe')):raise RuntimeError('installer output absent')
                result['installerCompiled']=True
                result['installerArtifacts']=[file_record(f) for f in sorted(out.glob('OpsisSetup-*.exe'))]
            else:result['installerCompiled']=False
        verify_tracked_source(repo,commit)
        result['developmentPassed']=all(s['exitCode']==0 for s in results)
        if not result['developmentPassed']:result['error']='one or more independent QA steps failed'
    except (RuntimeError, OSError, subprocess.SubprocessError) as e:result['developmentPassed']=False;result['error']=str(e)
    finally:(report/'gate-report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return 0 if result.get('developmentPassed') else 1

def main():
    p=argparse.ArgumentParser();p.add_argument('repo',type=pathlib.Path);p.add_argument('report',type=pathlib.Path);p.add_argument('--iscc',type=pathlib.Path);p.add_argument('--compile-only-report',type=pathlib.Path,help='verified explicit media omission report; not a full release gate');a=p.parse_args()
    try:return run_gate(a.repo,a.report,a.iscc,a.compile_only_report)
    except (ValueError,OSError) as e:print(e,file=sys.stderr);return 1
if __name__=='__main__':sys.exit(main())

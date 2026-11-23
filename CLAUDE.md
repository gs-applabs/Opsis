# CLAUDE.md — OPSIS Desktop Customization 2.8.0.2500 / R10

> This repository is a **QA handoff reconstructed from user-provided source ZIP snapshots**, NOT the official GitHub commit history. All commits tagged `[RECONSTRUCTED source snapshot]` are review artifacts only. The last product code checkpoint is **R10**; the last handoff commit changes only documentation.
>
> Do not report a release, Windows installer, verified plugin sandbox or passing .NET build until the corresponding real Windows checks complete. This is **preview source**.

## Primary task for Claude Code
Audit, repair, finish and verify the 2.8.0.2500 source without removing features or accidentally breaking other OPSIS apps. Start with `CLAUDE_CODE_HANDOFF/START-HERE-EL.md`, then `TASKS-AND-ACCEPTANCE-EL.md`, `QA-WINDOWS-RUNBOOK-EL.md` and `HISTORY/RECONSTRUCTED-HISTORY-EL.md`. Consult `IMPLEMENTATION-100-STATUS-2.8.0.2500.json` for all 100 tasks; written status is an **assertion, not verification**.

## Code and version contracts
- Main project: `Opsis.Studio.csproj`, `net11.0-windows10.0.19041.0`, WinForms, x64; `global.json` requests SDK 11.0.100.
- App release number: **2.8.0.2500** (`Version.props`); user requires consistent UI, About, metadata and installer number.
- Plugin DLL **AssemblyVersion AND FileVersion 1.0.0.0**: only `OpsisNewsCrawler.OpsisPlugin.dll` and `Chronikologio.OpsisPlugin.dll` (source in companion repos). Do **NOT** arbitrarily set shared `Opsis.UI.dll` or `OpsisNewsCrawler.Plugin.Abstractions.dll` to 1.0.0.0. Verify actual assembly identities on Windows before integration.
- Shared ABI currently expects `OpsisNewsCrawler.Plugin.Abstractions` 0.8.0.0; `Opsis.UI` has its own stable assembly identity and independent package version. Review assembly-load contexts, singleton shared contract types, copies in plugin folders and deployment manifests.
- The News Crawler and Chronikologio are **in-process .NET plugins**; Media Suite is a **standalone Electron app** that integrates via launch/detection, NOT a `IOpsisNewsCrawlerPlugin` assembly.
- Preserve existing WinForms UI, Weather Wallpaper, DeskZones, DeskStrip, DeskContainers, translations, assets, low-idle/resource protections, SSD write reduction, plugin trust and recovery.

## Non-negotiable safety and accuracy
- Do not weaken security to get green tests, skip UI tests, disable signature checking or mutate end-user registry/system settings on a non-disposable PC. Test shell integration, auto-start, install/uninstall, crashes, driver/monitor behavior only on clean Windows VMs.
- Status of known unresolved items: **#12, #17, #20, #24, #32, #40, #97** (partial); **#92, #93, #99** (Windows release gates pending). See the handoff checklist and product source report.
- Running JavaScript tests is not equivalent to validating WinForms, native DLLs, Explorer integration, monitor topology, WASAPI, WebView2 or Inno Setup.
- Never assert full plugin permission enforcement for an in-process plugin solely because a manifest or consent prompt exists. It needs OS-enforced isolation/out-of-process broker or a safe alternative explicitly documented.
- Never force-merge this **reconstructed history** into the existing `gs-applabs/Opsis` main branch. Establish the desired repository base, use a **new review branch**, preserve real upstream commits; reconcile changes before submitting a PR.
- No product binary / installer is supplied here as R10 release output. Generate and verify one only after successful Windows build and release gates.

## Starting commands
```bash
git status --short
git log --oneline -12
git diff 'HEAD~1' HEAD      # last commit is documentation-only
node --test               # tests/*.test.cjs, requires Node.js 22
```

Windows PowerShell (from the repo root on a disposable Windows runner):
```powershell
dotnet --info
dotnet restore .\Opsis.Studio.csproj
dotnet build .\Opsis.Studio.csproj -c Release -r win-x64 --no-restore
dotnet publish .\Opsis.Studio.csproj -c Release -r win-x64 --self-contained false -o .\publish
# After clean build, locate Inno Setup 7 and follow CLAUDE_CODE_HANDOFF/QA-WINDOWS-RUNBOOK-EL.md
```

## Required final report
Return a table for all 100 points: implemented / partial / fails build / unverified, files changed, precise tests and results. Distinguish tests performed from planned checks; include Windows OS/SDK/installer versions, code commit hash, artifact hashes, failed cases and release readiness. Do not claim a final 2.8.0.2500 installer if any required release gate remains red/unrun.

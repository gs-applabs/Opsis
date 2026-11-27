# OPSIS — consolidated source working tree (R27)

Base: latest unified capability/DeskFlip PR #16, 96bd979a03b736c8d879add9e8af888295196e2f.

Selected non-overlapping changes, with all incoming commit references preserved in SOURCE-CONSOLIDATION.json. Conflicting files are not overwritten. This is a review branch, not a final release. User-side R20 source and original 110-wallpaper installer were not recoverable from Library bytes; preserve them. Existing 100/110 pack and Ancient Athens/Greek scenes must not be overwritten. Ancient Rome final art remains unverified. Keep only one released source ZIP after reconciliation, not earlier generated archives.

## Consolidation summary

| branch | copied | already same | overlaps | proposed deletions retained |
|---|---:|---:|---:|---:|
| appearance | 78 | 0 | 0 | 0 |
| desktop-materials | 5 | 0 | 3 | 0 |
| europe-R26 | 41 | 14 | 17 | 0 |
| europe-R25 | 1 | 0 | 0 | 0 |
| crawler-bridge | 1 | 0 | 4 | 0 |
| main-2.8-followup | 3 | 10 | 24 | 0 |

Legacy add-on handoff: 3 reference files. Its legacy code is documentation only; the active installer source is R26 and the real original add-on installer is missing.

## Available build artifacts (not a release)

- R26 installer: https://github.com/gs-applabs/Opsis/actions/runs/45744541834/artifacts/15082443211
- R26 compatible host: https://github.com/gs-applabs/Opsis/actions/runs/45744541834/artifacts/15082223673
- Photo masters/review layers: R22 15024050313, R23 15023840456, R25 15022663892. Keep newest revision per ID. Do not include rejected synthetic R7/R8/R10 or duplicate raw download batches in new release.

## Outstanding gates

Reconcile every overlapping code file and unrelated-history feature, compare against latest local integrated R20, verify original 110-wallpaper package, complete real-photo mask/geography/rights and remaining London/Podgorica, include Dublin R16 and confirm Ancient Rome. Re-run full Windows build, installer, UI, WebView2, plugin compatibility, security and performance. No final installer until these gates are met. Main is untouched.

## Automated three-way result

Cleanly reconciled: 14. Unresolved: 34.

- desktop-materials: .github/workflows/windows-audit.yml — workflow edits require connector review and workflows permission
- europe-R26: Opsis.csproj — Git three-way conflict
- europe-R26: src/Services/OpsisTaskbarDesigner.cs — Git three-way conflict
- europe-R26: src/UI/MainWindow.WallpaperGallery.cs — Git three-way conflict
- europe-R26: src/UI/MainWindow.cs — Git three-way conflict
- europe-R26: src/UI/ThemeManager.cs — Git three-way conflict
- europe-R26: src/Widgets/DeskFlipEngine.cs — Git three-way conflict
- europe-R26: src/Widgets/DesktopLauncher.cs — Git three-way conflict
- europe-R26: src/Widgets/ReplacementTaskbar.cs — Git three-way conflict
- europe-R26: src/Widgets/wallpaper/index.html — Git three-way conflict
- europe-R26: src/Widgets/wallpaper/weather.js — Git three-way conflict
- europe-R26: tests/Opsis.ExperienceSmoke/Program.cs — Git three-way conflict
- europe-R26: src/Services/WallpaperGalleryCatalog.cs — add/add or missing common base
- crawler-bridge: .github/workflows/plugin-compatibility.yml — workflow edits require connector review and workflows permission
- crawler-bridge: Opsis.csproj — Git three-way conflict
- main-2.8-followup: .github/workflows/windows-audit.yml — workflow edits require connector review and workflows permission
- main-2.8-followup: Opsis.csproj — do not downgrade RC; manually port relevant changes
- main-2.8-followup: Version.props — do not downgrade RC; manually port relevant changes
- main-2.8-followup: locales/ar-SA.json — Git three-way conflict
- main-2.8-followup: locales/de-DE.json — Git three-way conflict
- main-2.8-followup: locales/el-GR.json — Git three-way conflict
- main-2.8-followup: locales/en-US.json — Git three-way conflict
- main-2.8-followup: locales/es-ES.json — Git three-way conflict
- main-2.8-followup: locales/fr-FR.json — Git three-way conflict
- main-2.8-followup: locales/hi-IN.json — Git three-way conflict
- main-2.8-followup: locales/it-IT.json — Git three-way conflict
- main-2.8-followup: locales/ja-JP.json — Git three-way conflict
- main-2.8-followup: locales/ko-KR.json — Git three-way conflict
- main-2.8-followup: locales/pt-PT.json — Git three-way conflict
- main-2.8-followup: locales/ru-RU.json — Git three-way conflict
- main-2.8-followup: locales/tr-TR.json — Git three-way conflict
- main-2.8-followup: locales/zh-CN.json — Git three-way conflict
- main-2.8-followup: src/Widgets/wallpaper/index.html — Git three-way conflict
- main-2.8-followup: tests/WeatherInfoTone.test.cjs — add/add or missing common base

## R28 targeted reconciliation

Excluded documentation C# examples from application compilation (duplicate Gallery type). Ported R26 European photo discovery into current paginated Gallery, explicit preview labels and selection state. Ported mask handling into current native renderer, preserving existing photograph-ready logic. Retained current Glass Signature deduplication in WallpaperGalleryCatalog. Remaining conflict records: 31. Windows tests pending.

## R29 reconciliation

All 14 locale files retain their existing values and now include the missing localized VersionNotes.2.8.0.2511 entry. Version.props declares 3.0.0.3200 for the integrated RC, not a production release. The JSON ledger records these 15 resolutions; 16 conflict records remain. Windows build and installer verification are pending.

## R30 reconciliation

Ported opt-in Signature Composition project reference, native DLL staging and prerequisite checks while preserving default bundled Opsis.UI, SkiaSharp dependencies, .NET 11 and existing host exclusion globs. Three project-file overlap records resolved; 13 remain. Windows compile and optional Signature build are not yet verified. WeatherInfoTone overlap deliberately remains open: incoming automatic dark-text behavior conflicts with the user's fixed-white desktop text requirement; reconcile HTML and regression tests together.

## R31 reconciliation

Retained fixed-white weather text in plain and Signature card layouts; legacy adaptive black text is intentionally superseded. R26 additions already present except the obsolete contrast sampling guard. Added regression assertions for both layouts. Existing StartStudioPreferences.StartButtonStyle and OpsisStartButtonArt supersede the proposed duplicate StartGlassIcon boolean. Four records resolved by policy/source comparison; nine remain. Runtime tests pending. ReplacementTaskbar styling and animation overlap remains open.

## R32 reconciliation

Ported SignatureGlass named palette and dark-intensity guard; retained accent settings and traced repaint. Integrated Launcher all-term search, high-contrast field colors, Signature shortcut styling and branded Start header while preserving power actions, Quick Look and shortcut diagnostics. Two conflict records resolved; seven remain. Windows compile and UI tests pending.

## R33–R34 reliability and navigation

R33 adds guarded UI-thread shutdown, per-stage exception isolation and diagnostics, stops only started plugins, and routes version recovery through the tray exit path. A hung synchronous stop remains a release blocker. R34 aligns classic navigation ordering and Studio groups (Appearance, Desktop, Media, System, Add-ons); preserves shortcuts and About last. About separates host, plugins, family, dependencies and consolidated guidance. Pending: interactive Windows control checks, hung-stop reproduction and bounded recovery design, per-plugin cards, intra-page duplicate audit, seven unresolved source overlaps and Windows build. No release certification.

## R35–R36 authoritative roadmap amendment (release 3.0.0.3200)

R35 implemented individual About plugin cards and opt-in shutdown HangTrace. Seven source overlaps remain; no Windows certification yet.

### MP2A–MP2E: native DeskContainers migration and legacy retirement (P0)
- MP2A inventory legacy WPF bridge, native host, settings, installer assets, dependencies, commands and migration paths. Preserve existing user data and recovery backups.
- MP2B fix native inconsistencies: icon ownership and positions, drag/drop, resize, snapping, z-order, Explorer restarts, DPI 100–200%, monitor disconnect/reconnect, folder portals, notes, hidden icons restoration, keyboard accessibility, theme consistency, idle CPU/GPU/RAM, disk writes and shutdown.
- MP2C migrate legacy configuration with versioned, idempotent conversion, dry run, atomic backup, validation and rollback. Never run both container engines simultaneously; native owns desktop icon management after successful migration.
- MP2D release gate: Windows 10/11 supported builds, clean install and legacy upgrade, 100 repeated create/move/resize/delete cycles using disposable fixtures, 20 start/exit cycles, Explorer restart, sleep/resume, monitor/DPI change, failure injection, 24-hour soak with no unexplained resource growth, no lost files or icons, no unbounded exit. Benchmark against a licensed Stardock Fences installation if available; do not claim parity without evidence.
- MP2E remove legacy implementation, obsolete bridge/dependencies, old UI toggles and installer payload only after MP2D passes and migration/rollback verified. Keep legacy data importer and backups, not a second live engine. Retire before production 3.0.0.3200; no arbitrary calendar deadline. If gate fails, defer removal and block final release.

### MP4/MP6/MP7: unified widgets and provider routing (P1; compatibility gate P0)
- Calendar: prefer Chronologio compatible enabled provider for events, reminders and calendar data; preserve built-in offline calendar fallback. News: prefer compatible enabled Crawler provider, cached/offline fallback to existing Opsis source, no duplicate feed polling or notifications. Existing PluginHost.FindWidgetReplacing selects first started provider; replace ambiguous ordering with deterministic per-capability selection, explicit preference, capability/ABI check, health and permissions. Do not equate app installation with provider readiness.
- Common OpsisUI widget shell: typography, spacing, Glass/Light/Dark/high contrast, DPI, sizing, settings, loading/error/empty states, accessibility and navigation. Provider supplies typed data and actions where supported; legacy widget adapter only during transition. Keep user widget placement and settings on provider switch. Single scheduler/cache per feed, bounded refresh and cancellation.
- Media Suite: audit actual capabilities before reuse (decode/playback, audio spectrum, equalizer, visualization, GPU acceleration, metadata, codecs). Share a compatible engine/library or versioned IPC provider only where useful; wallpaper rendering stays Opsis-owned, Media Suite owns its player UI. Standalone Opsis must retain a functional local fallback for supported wallpaper/media features. No requirement to install Media Suite for basic wallpapers.
- Capability authority: one owner per exclusive resource (system-wide audio effects, shared capture stream, wallpaper/desktop host, desktop icon management, hotkeys where exclusive). Prefer user-selected healthy compatible provider; Auto defaults to Media Suite for its supported advanced media/audio services, Opsis native for desktop/wallpaper/container orchestration, Chronologio calendar, Crawler news. Independent playback sessions may coexist only when they do not contend for exclusive resources. Never disable a separate application's ordinary independent playback.
- Use explicit capabilities, version/ABI handshake, permission checks, session-scoped leases/mutex or authenticated local IPC with heartbeat and release; acquire before activation, stop old owner and confirm release before starting new owner. No simultaneous fallback while ownership uncertain. Crash/disconnect recovery must validate lease and restore safe Windows state, with bounded retries and visible status. Avoid raw DLL injection, duplicate global hooks, duplicate audio effects and competing settings writes. Avoid loading incompatible third-party DLLs directly.
- UI shows active owner, installed/compatible/healthy status, fallback reason and user preference (Auto/Opsis/provider). Plugin install/uninstall/disable/restart must be safe and reversible. Test host alone, each provider alone, both installed, versions mismatch, crash/hang, upgrade/uninstall, sleep/resume, concurrent launch, and Windows recovery; capture CPU/GPU/RAM/disk metrics and verify single ownership.
- MP3 functional button audit and MP9 final certification must include these scenarios. No production release until P0 gates pass.

### Revised pass order
MP1 finish source reconciliation; MP2 stability/exit and native Containers retirement gates; MP3 functional control audit; MP4 navigation and duplicate settings; MP5 Windows customization; MP6 visual consistency/About/widgets; MP7 capability broker and cross-app provider integrations (design starts during MP2); MP8 wallpaper/media integration and assets; MP9 Windows build, installer, regression and compatibility certification. Existing work may overlap, but release gates are mandatory. Keep one cumulative source and this history; do not delete recovery sources prematurely.

## R37 — coordinated Media Suite 3.0 architecture decision

Source reviewed: gs-applabs/OpsisMediaSuite active branch feat/photo-tools-fxsound-coexistence, docs/P0-PASS-1.md at a4bd3691ec151c783b30133ff72d8453a54b0cc4 and updated 8b11d8f55a82d776335c94a5117fe2c1820a1ed6 (MP17D.1). Also reviewed master WAVEFRAME_ROADMAP.md, docs/MEDIA-SUITE.md, MPP-MIGRATION-LEDGER.md, QA checklist and plugin README. The older master roadmap is not authoritative for 3.0. The released bridge is launcher-only, not an engine API.

Decisions superseding R36 where necessary:
- Adopt the Media Suite native C++ DSP as the canonical shared source, not a second independently developed Opsis DSP. Build a versioned redistributable component only after ABI, licensing, integrity, parity and performance verification. Standalone Opsis may bundle the same approved component; installation of Media Suite is not required. No runtime loading of arbitrary DLLs.
- MP17D.1 currently provides experimental developer-only offline PCM conversion (30 s mono/stereo, 44.1–96 kHz, bounded process/IPC, exactly one effect route, no automatic fallback on failure). This is NOT a packaged or live DSP provider. MP17C.3 multichannel library support does not imply multichannel converter support. No production activation until Media Suite MP17D/E and MP19 gates relevant to that capability pass.
- Keep existing Opsis desktop and wallpaper host/rendering. Evaluate reusable playback session abstractions and separately packaged native LibVLC, DirectShow/LAV and Media Foundation components only if they improve actual wallpaper reliability or measured CPU/GPU cost. Chromium/WebView2 remains local fallback; never launch the full Electron application for routine wallpaper playback. DirectShow filter identity/consent, native child lifecycle, hardware decode, cross-process presentation, license and real Windows display gates must pass. No automatic global codec registration.
- Reuse shared visualizer algorithms/presets via compatible lightweight modules, one shared spectrum source when feasible, no duplicate capture for same purpose; keep independent ordinary playback sessions. Reuse photo processing for optional wallpaper preparation and FFmpeg conversion via bounded jobs, not as permanent background services. No import of unrelated subtitle/media-library features into Opsis core.
- System-wide audio: do not confuse Web Audio per-stream DSP, WASAPI loopback capture, Equalizer APO export, native PCM library and a certified system APO. Current APO deployment blocked; WASAPI hardware render test skipped on CI without default device. Do not change device routing, install unsigned drivers, disable Windows security, or emulate transparent processing by same-endpoint capture/replay. Existing working Opsis audio behavior remains until validated migration; expose unavailable advanced modes honestly.
- Capability Broker must arbitrate exclusive resources, not impose one global playback process. Prefer certified Media Suite provider for its own media processing and supported advanced capabilities only; local fallback remains available if no compatible provider and ownership is released. Explicit one-route DSP per signal, external FxSound/APO coexistence with bypass policy, user consent, no accidental double processing. Device/provider readiness must be proven, not inferred from installed executable. Fail closed on ambiguous ownership.
- Cross-project contract: define capability identifiers, version ranges, provider health, scoped commands, session lifetimes, deterministic selection, cancellation, bounded timeout, leases and recovery. No second preset store; one canonical schema with migrations. Common OpsisUI6 design tokens, separate host-specific controls.
- Align Opsis MP7/MP8 with Media Suite MP17D/E (native integration), MP18 (UI/library), MP19 (certification), MP20 (release). Opsis MP2/MP3/MP4/MP5/MP6 and legacy Containers retirement proceed independently; optional media capabilities cannot block basic standalone wallpaper playback. Do not mark the shared engine implemented merely because the roadmap is updated.
- Integration tests: both apps alone and together, same and different ABI, disabled/uninstalled provider, concurrent launch/exit, provider crash/hang, repeated playback and conversion, resource release, device changes, sleep/resume, CPU/GPU/RAM/IO measurements, audio bypass and no doubled effects, real Windows hardware evidence. No final release certification in R37.

Keep existing R36 native Containers and Chronologio/Crawler widget requirements unchanged. One source history, no duplicate roadmap archive.

## R38 — MP2A/B native Containers inventory and recovery fixes

AppSettings identifies native ContainerHost as default since 2.6.0; comments in ContainerHost claiming legacy WPF is still live were obsolete. NativeMigration reads old Profiles without modifying them. Do not remove live native pipe or importer merely because they reference legacy concepts. Remaining legacy installer/dependency inventory required. Fixed DesktopIconVisibilityEngine.HideOriginals to commit recovery coordinates before moving each icon and count only successful moves (failed movement removes its recovery entry). Added duplicate active ContainerHost guard and repeated Dispose guard. These are source fixes, not Windows-tested guarantees. Per-icon durable saves may increase writes on large explicit drop batches; optimize with a single durable batch journal before mutation after correctness tests. NativeMigration currently saves live layout before snapshots, so failure can leave partial migration that is skipped next start; staged/verified migration and interruption tests remain P0. ContainerHost disposal can still fail or block in an intermediate service, and ItemRemoved must check remaining references before restoring shared originals. Seven integration overlaps remain. No release certification.

## R39–R40 — native Containers migration, references and persistence

R39 commits live migration layout after profile snapshots and blocks host startup on migration exception. Partial snapshots may still exist after failure; collision policy, retry idempotence and fault injection remain open. R40 checks stored, pending and live container/tab references before individual original restoration, retains dirty entries until service update returns successfully, and batches desktop icon recovery journal before Explorer mutations. Rechecks item names before moves and retains recovery entries on uncertain native failure. This reduces explicit drop-batch journal writes to one initial write plus at most one cleanup write; no physical SSD measurement. Save retry policy, native identity collisions, startup recovery, shutdown exceptions, legacy inventory and Windows regression tests remain open. No Windows build or release certification.

## R41 — bounded native container save retries and staged disposal

Timer-driven Flush failures are caught and retried at 2/4/8 seconds, then pending edits remain in memory until a subsequent user edit or exit. Direct Flush still propagates exceptions. Disposed guard blocks new HostApi calls and queued display/sync callbacks. Shutdown stops timers, unsubscribes events, cancels organization, restores icons, attempts pending layout/history save, closes/disposes windows and dispatcher before pipe disposal. Each stage logs exceptions and optional HangTrace markers. This isolates exceptions, not indefinitely blocked synchronous calls; in-flight IPC and actual PipeServer disposal still need runtime validation. Persistent disk failures at process exit may still lose unsaved changes; explicit user-visible recovery and tests remain required. No legacy deletion, Windows compile or release certification.

## R42 — DeskFlip overlap resolution

Retained existing asynchronous window snapshot capture gate, persisted Flip 3D / Thumbnail grid / Compact list choices and mouse/keyboard input. Ported high contrast list mode/system colors, accessibility name, reduced motion using OpsisMotion.Enabled, valid HWND check before activation, and desktop capture restricted to Flip 3D. One source overlap resolved in the JSON ledger; six remain (42/48 accounted for). Windows compile, high contrast and actual UI rendering are not verified. IPC implementation discovery via code search did not expose PipeServer source; no claim that the tray exit deadlock is resolved. Container persistence recovery remains open.

## R43 — independent source audit added to every Mega Pass

User requires proactive discovery beyond known conversation backlog. Audit each subsystem for correctness, resource ownership/lifetime, synchronization, UI blocking, native handles, idle polling, CPU/GPU/RAM/disk, exception/recovery, security, accessibility, compatibility and functional controls. Each finding must have source location, priority, change or reason deferred, and evidence; static inspection is not runtime certification. Prioritize P0 data loss, hangs, crashes and unsafe native changes; P1 measurable resource waste and functional inconsistencies. Continue existing MP1–MP9 gates without counting newly discovered items as completed features.

R43: ContainerHost retains dirty entries when windows close due to temporary hiding (actual deletion still removes entries), isolates rule/z-order/history timer exceptions, guards queued look updates during disposal, and disposes all process wrappers even if enumeration/name lookup fails. Hidden-window model reconciliation and save-vs-visibility ordering need tests; persistent timer error logging needs throttling review. MediaSuiteDetector now disposes the parent registry key as well as subkey. Existing R31 weather regression test anchors checked against HTML source (not executed). R28 workflow push path filter only triggers on changes to itself, so ordinary code commits do not automatically run this workflow; workflow permissions/manual dispatch or PR CI must be arranged. No Windows build verified. Six historical overlaps remain. Continue source audit of startup, IPC, widgets, WebView2, shell, plugins, installers, resource ownership and real Windows regression checks.

## R44 — ReplacementTaskbar reconciliation and automatic Windows build gate

Ported R26 Signature/high contrast palette and pause-aware pulse without adding a duplicate Start icon setting. Retained per-monitor profile, segmented layout, preview and existing Start art. Added OpsisMotion guard, stable clock update, DPI-aware cache key and bounded/cycle-safe Z-order traversal (1024 handles). Source overlap ledger now 43/48, five remaining. Removed self-only path filter from unified-r28-checks.yml so subsequent pushes to the integration branch trigger Windows build and wallpaper Node tests. Actual run results not yet verified; no claim of successful Windows compilation. Existing full functional, hardware and installer gates remain required.

## R45 — source audit, experience smoke reconciliation and CI expansion

Reopen temporarily hidden containers using their pending model rather than persisted stale state; guard restyle timer and mutation callback during shutdown. Existing composite Rainmeter importer supersedes R26 single-meter rejection test: retained positive multi-row/persistence tests and negative executable-measure tests. Ledger 44/48; four overlaps remain. Unified CI now includes existing ExperienceSmoke project (actual Windows UI handle smoke and 5000-read settings regression), cancels superseded runs on the same ref and also triggers on PRs targeting main. No successful build result yet: commit-status query empty and workflow-run connector is PR-only. Full interactive, performance and installer certification still pending.

## R46 — modular runtime / optional standalone Containers host

User wants to close Opsis while keeping selected modules, particularly Containers. Current ContainerHost is in-process; PerformExit is a full cleanup followed by Application.Exit/ExitThread. Closing only MainWindow is not suspension, and skipping ContainerHost.Dispose before Application.Exit cannot preserve WinForms windows. Do not overload RequestExit (installer/updater/recovery/uninstall must always request full exit).

- MP2F (P1, after MP2B/C stability, before MP9 certification): implement explicit runtime modes Full, Containers-only, and later Custom. Model module dependencies and state (Stopped/Starting/Running/Stopping/Faulted), separate reversible Stop from final Dispose; no creation of a service merely to stop it. Shared tray initially, optional secondary Containers tray in standalone mode only, no duplicate tray by default. Keep active runtime/profile preference separate from saved enabled/startup choices, so a temporary session reduction never silently disables configured modules permanently. No autostart change without consent.
- First inventory all global event subscriptions, windows, timers, background tasks, WebView2 processes, hotkeys, audio and plugin sessions. A Containers-only mode must retain only required UI loop, ContainerHost/store, minimal theme/localization, necessary Explorer/icon recovery and container-specific hotkeys/IPC. Suspend wallpaper/other widgets/DeskStrip/taskbar/audio/automation/plugins and their dependency timers with verified stop/restart semantics. Restore shell state for stopped modules without restoring icons still owned by Containers. Preserve user data; do not claim process exited when the lightweight host is still running.
- Tray commands: Full exit; Keep only Containers; Keep selected functions (only when each lifecycle contract is certified); Resume full Opsis. Show actual active state, resources and failure reason. If transition fails, remain in safe known state and do not claim success. Full exit available at all times. Do not enable a partial shutdown UI until actual lifecycle tests pass.
- Optional MP7/MP8 follow-up: same executable with dedicated minimal --containers-only host mode or thin separately packaged host sharing the identical Native ContainerHost/core, never old WPF or second implementation. Prefer one process initially; isolated process option only after measured benefit. Explicit authenticated per-user/session ownership lease for desktop icon management, hotkeys, IPC and data store. Handoff flush/backup, old owner quiesce/restore and confirm release, new owner acquires and starts; abort safely on failure. No concurrent host or shared-file writes, no arbitrary command execution, no elevated daemon. Preserve single-instance rules and route existing-instance requests appropriately; full updater/uninstaller must detect and stop all hosts. Standalone installer optional later, no additional app required for Containers-only mode.
- Tests: full -> Containers-only -> full repeatedly; settings and positions intact; Quick Hide, drag-drop, notes, portals and snapshots; no lingering wallpaper/audio/plugin processes/timers/hotkeys; Explorer restart, crash, concurrent launch, upgrade/uninstall, failed stop/start, state restoration, no second host, measured CPU/RAM/GPU/I/O. Windows CI plus interactive desktop; no invented performance targets or claims of Stardock parity.
- R46 implemented foundation fixes only: Program isolated appdata single-instance mutex now uses SHA256 of normalized path instead of process-randomized GetHashCode; default mutex unchanged. Tray Containers settings now correctly opens DeskContainers instead of DeskZones. No modular transition or standalone host implemented yet. PR #18 draft remains unmerged; PR-triggered workflow lookup returned no runs at inspection.

## R47 — modular lifecycle groundwork and hotkey audit

Split seven native container hotkeys into their own lazily created HotkeyManager, independent of general DeskFlip/launcher shortcuts. Track startup-health and plugin approval one-shot timers, guard callbacks after exit begins, prevent duplicate approval timer, unsubscribe plugin navigation, explicitly dispose dock restorer and cancel pending timers during full shutdown. HotkeyManager now guards repeat disposal and registration after disposal, clears callbacks and destroys its native message window in finally, and isolates exceptions from shortcut handlers. This is groundwork, not a Containers-only runtime or independently certified restart. Actual minimal-mode inventory still needs MainWindow session restoration, AppActivity, wallpaper/WebView2, backdrop contrast, Discord presence, performance governor, appearance, widget lifetimes, shared icon ownership and other background processes. Current full-exit wallpaper and other service cleanup must be verified before selective transition. PR #18 remains draft; workflow lookup for R46 returned no runs, no successful Windows build verified. Historical ledger remains 44/48.

## R48 — discoverability, onboarding and documentation acceptance gate (all GS App Labs applications)

User requires every capability to be understandable and accessible in the UI, not merely present in source. Adopt as common OpsisUI6 contract across Opsis Desktop, Media Suite, Crawler, GearWin, Chronologio, ENFIA Pro and other maintained applications. This entry is a cross-product specification, not proof that other repositories were modified.

- Maintain a canonical feature catalog: stable feature ID, category/page/control/deep-link, localized title, plain-language purpose, search synonyms (Greek/Greeklish/English), availability and prerequisite reason, risk/permission, default, apply/reset semantics, provider/dependency, help topic, introduced/changed version, and test evidence. Reuse it for settings search, contextual help, history and welcome tour; avoid separate contradictory lists. Every exposed user capability has an accessible route; internal-only mechanisms are classified rather than exposed as unnecessary toggles.
- General Settings is a directory to category settings, not a second store of duplicate switches. Category pages group Basic/Advanced, descriptive headings, short actionable descriptions, explicit dependencies and disabled reasons; show preview/reset where appropriate. Search must include never-visited pages and plugin contributions without eagerly constructing their UI or starting services. Search results navigate to and highlight the exact control when feasible. Explicitly label unavailable, experimental, or planned features and avoid misleading activation.
- Help: offline searchable and localized, F1 contextual at page/control level, numbered practical steps, expected result, requirements, undo/recovery, troubleshooting and keyboard accessibility. Link directly to relevant screen where possible. Help opens independently and does not change settings merely by reading it. User-selectable onboarding, replay welcome tour; never force a long tutorial.
- History: single release manifest tied to compiled version and actual release date, new/changed/fixed/security/compatibility sections, clear user benefit and location of feature, links to help/settings; distinguish RC/experimental from verified release; don't invent published date. CI validates current version notes, translation coverage, feature IDs and history-to-help links. Keep application and plugin versions separate.
- Splash/startup: retain approved Spectrum identity and intro media; instrument real startup phases (settings/recovery, compatible services, optional plugins, workspace readiness) with honest status, no simulated percentages, no fixed waits, no blocking UI merely to display animation; skip optional intro and support reduced motion, high contrast, no-audio preference. Splash concise, detailed steps in welcome/Help, no screen presented for silent background mode. Installer progress must reflect actual operations, not fake stages. Common reusable visual tokens and lifecycle hooks, not duplicate per-app engines.
- MP3/MP4/MP6/MP7 and MP9 gate: test novice discoverability, keyboard-only navigation, F1, search from cold launch, exact link target, disabled prerequisite explanation, localized strings, DPI and accessibility, actual action and reset, upgrade history/first-run/splash, plugins present/absent. No feature marked release-complete without both functionality and discoverability tests. Track coverage numerator/denominator from audited feature catalog only, not a guessed percentage.
- R48 implemented Opsis-specific first steps in Help (Containers, DeskFlip, Ctrl+K, F1 and History), added direct feature search/help/history actions in Settings and About, and clarified the existing late startup splash as workspace opening with useful shortcuts. Actual early startup stage instrumentation and comprehensive catalog, localized history notes, shared OpsisUI6 propagation and other app repository changes remain open. No Windows UI verification yet.

## R49 — Spectrum Glass Now Playing and shared media metadata

Implemented extra WinRT session metadata fields (separate album/subtitle, album artist, track number/count, genres), payload including source, track identity, reduced motion and timeline privacy. Existing title/artist, cover and timeline retained; hide metadata also hides progress and identifying fields. Canvas renderer now has a Spectrum-inspired glass orb/note with intersecting orbit highlights, translucent glass metadata panel, persistent note, title slide/fade on track change with reduced-motion fallback, and extra metadata line. Compact display may truncate; full details view is still required. No fabricated missing values. Rendering and C# build not verified.

MP7/MP8 follow-up: single Now Playing broker shared by widget and wallpaper overlay, event-driven Windows GSMTC fallback with bounded reads, optional versioned/authenticated Media Suite provider for extended file/stream metadata and transport capabilities. Publish immutable snapshot with provider/session/track ID, revision, timestamps, state, elapsed/duration, title/artist/album/artwork, album artist/genre/track/disc/year/composer, codec/bitrate/sample rate/bit depth/channels only when actually reported; explicit missing fields, distinguish live/unknown duration. Prefer explicit user-selected available session, otherwise active playing session; Media Suite enhanced data only for its own matching session, never replace another app's metadata. Do not mix artwork/timeline across sessions. AUMID display-name resolution, manual source picker, pause/seek/next only if source supports; no simulated controls or forced playback, no external process injection. Test Media Suite alone/both, Spotify/browser/VLC/other GSMTC providers (only if actual session exposed), local files, streams, unsupported sources, simultaneous playback, fast changes, pause, lock/presentation and disconnect. Metadata-free fallback for unsupported players, no audio fingerprinting or extra network lookup by default. Share existing approved audio capture where appropriate; no second DSP route or global audio effect. Cache bounded artwork by identity/revision, decode safely, no repeated disk writes, no second renderer just for metadata. Add compact/expanded/ambient presets, optional artwork, detail visibility, contrast and per-monitor privacy, help and settings discovery; verify actual source compatibility before advertising. Other app repositories not changed in R49.

## R50 — weather-priority music overlay and exact Spectrum asset requirement

Music canvas now places itself outside the actual transformed #wxInfo bounding rectangle (clock/date/temperature/wind/city/source, normal and Signature). Preferred normalized position remains unchanged; nearest safe top/bottom/left/right position with 12px minimum clearance; hide the overlay if no safe rectangle fits. Added four geometry regression cases to existing WeatherInfoTone Node suite; CI not verified. Current implementation reads geometry before canvas changes per active frame; profile actual layout overhead and consider invalidation-based geometry caching if measurable. Animation must remain inside that same safe rectangle and never move the weather overlay.

Weather location audit: manual city match, opt-in Windows device coordinates, offline naming / opt-in reverse lookup and manual coordinates converge at SetWeatherLocation, which saves city and coordinates, notifies widget, refreshes wallpaper configuration. Existing weather.js resets prior observation when coordinates or historical Athens mode changes; #wxCity uses configured name (Ancient Athens explicit label) and fixed white text. R50 adds request generation guards so earlier geocoding/device lookup cannot overwrite a later choice or reset; WeatherService now caches up to eight coordinate keys and deduplicates background refresh by key (max four concurrent), rather than one shared cache/refresh flag which could make Ancient Athens and current city contend. Actual Windows location permission, providers, offline/denied fallback, time zone and rendering tests remain required.

User requires the ORIGINAL approved Spectrum sphere rolling in first, then revealing album art/title/artist/metadata, not a redesign or alternative orb. Previous conversation identifies OPSIS-3.0-Official-Logo.png/.ico, OPSIS Intro Version A V1 Spectrum Universe, OP SIS Intro V2 Blue FHD WebM (exact filename OPSIS-Intro-A-Spectrum-Universe-V2-Blue-FHD.webm), and OPSIS-3.0-Intro-Installer-Source-Patch.zip. None of those exact assets was accessible in active filesystem/Library or checked integration branch (assets/intro not found); prior names alone do not verify pixels. Do not treat R49 procedural orb as approved replacement or generate another design. Recover original bytes and generation source from approved patch/video assets, verify visually, then implement rolling reveal inside reserved safe rectangle using original sphere texture/geometry. No invented asset or claim of implemented rolling animation. Keep reduced-motion direct reveal, avoid idle animation or duplicate capture, hide sensitive metadata on presentation monitors. All five R50 commits limited to source/tests/roadmap; no Windows build or actual geolocation certification.

## R51 — music placement and shared settings controls

New installations use X=.5/Y=.88 (lower center); explicit reset restores this default, existing saved positions and earlier migration remain unchanged. R50 collision avoidance does not persist relocation. Music settings adds a 3x3 keyboard-accessible radio anchor grid (0/50/100%), accurate numeric position/size controls and default reset. Periodic refresh now 5s instead of 500ms, skips focused edits, bounds audio device enumeration, avoids rebuilding unchanged list and skips unchanged position writes. Shared host StyleStudioInputs styles button-appearance radio selectors, assigns missing native button accessible names, uses system rendering for previously standard buttons in High Contrast. Original approved rolling sphere still requires asset recovery.

Adopt Microsoft Windows app settings/keyboard accessibility guidance (https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings and https://learn.microsoft.com/windows/apps/design/accessibility/keyboard-accessibility): one canonical setting, grouped SettingsCards, toggles for binary state, radio/segmented choices for small sets, combos for longer lists, keyboard-visible focus, labels/descriptions, DPI and High Contrast, no hover-only critical help. Preserve semantic behavior and compatibility; do not globally replace existing controls without regression tests. Use existing Opsis.UI SettingsRow/Card/AdvancedSection and SettingsEditSession where appropriate; no parallel library. Opsis.UI branch feat/signature-glass-ui6 received additive accessibility label/description inheritance in SettingsLayout.cs (c157a4c42e47e307776821a73b274479bdc4d9e5), preserving explicitly provided consumer labels. Not yet built/deployed to host or other apps. Preserve shared DLL AssemblyVersion and update consumers together after tests. Further control audit includes wheel/keyboard commit, focus/high contrast, touch targets, responsive layout, disabled reasons, search and help. Existing R50 no-overlap tests and all Windows builds still unverified.

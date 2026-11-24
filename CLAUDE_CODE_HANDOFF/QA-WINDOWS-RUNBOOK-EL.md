# Windows 11 / GitHub Actions release runbook — OPSIS 2.8.0.2500

**Εκτέλεση μόνο σε απομονωμένο Windows runner ή δοκιμαστικό VM** όταν τα scripts αλλάζουν registry, shell menu, taskbar, installed applications, auto-start ή προφίλ. Το διαθέσιμο πακέτο είναι source-only. Αυτή η παράδοση **δεν εκτελεί** Windows tests ούτε ισχυρίζεται πως παρήγαγε `OpsisSetup.exe`.

## 0. Ταυτότητα και περιβάλλον
```powershell
Get-ComputerInfo | Select-Object WindowsProductName,OsVersion,OsBuildNumber
Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber
dotnet --info
node --version
Test-Path "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
Get-Content .\Version.props
Get-Content .\global.json
```
Απαιτούνται SDK 11.0.100 ή συμβατό feature roll-forward, Node 22, Inno Setup 7, Windows x64, Desktop Runtime 11 για framework-dependent build, και WebView2 Runtime όπου χρησιμοποιείται. Δεν αλλάζουμε αυθαίρετα το TFM σε .NET 10 για να περάσει το build.

## 1. Στατικός έλεγχος και JavaScript
```powershell
node --test
# Εμφάνισε και αποθήκευσε το πραγματικό αποτέλεσμα: δεν αρκεί το ιστορικό QA/node-test-r10.txt
```
Επιθεώρησε επίσης τις third-party DLLs στο `lib/` (δεν καλύπτονται από `dotnet list package --vulnerable` αν έχουν προστεθεί ως τοπικές References), τις συντηρημένες εκδόσεις, licence attribution και τη διατήρηση ιδιωτικών δεδομένων.

## 2. .NET 11 build + publish (μην συνεχίσεις αν σπάσει)
```powershell
dotnet restore .\Opsis.csproj --nologo
dotnet build .\Opsis.csproj -c Release -r win-x64 --no-restore --nologo
dotnet publish .\Opsis.csproj -c Release -r win-x64 --self-contained false -o .\publish --nologo
Test-Path .\publish\Opsis.exe
& .\tools\emit-release-identity.ps1 -PublishDir .\publish -OutFile .\Output\release-identity.json
```
Καταχώρισε compiler errors **ολόκληρα**, όχι μόνο exit codes. Έλεγξε ότι οι εκδόσεις 2.8.0.2500 συμφωνούν σε About, `Version.props`, Assembly/FileVersion, Inno Setup, app family catalogue.

## 3. Κοινές βιβλιοθήκες και plugins
```powershell
# Αφού κλωνοποιήσεις τις συμβατές branches των repos, κάνε build τις DLL.
# Οι ακριβείς refs βρίσκονται στα .github/workflows/plugin-compatibility.yml.
& .\tools\check-plugin-dll-versions.ps1 -CrawlerDir .\plugin-crawler -ChronikologioDir .\plugin-chronikologio -HostDir .\publish
```
Οι **plugin implementations** Crawler και Χρονικολόγιο πρέπει να είναι AssemblyVersion+FileVersion 1.0.0.0. Το **shared contract** `OpsisNewsCrawler.Plugin.Abstractions` πρέπει να παραμένει συμβατό (στην παρούσα βάση 0.8.0.0). Το Opsis.UI ακολουθεί δικό του stable version contract. Επιθεώρησε `lib/*.dll`, τον loader και `deps.json` για binding conflicts. Εκτέλεσε host με απομονωμένο `OPSIS_APPDATA`, έλεγξε `plugin-widget.txt`, `plugins.log`, calendar replacement και feed `san-simera`. Για το Media Suite, τρέξε `npm ci`, `npm test`, `npm run selftest` στο δικό του checkout Windows και δοκίμασε πραγματική εκκίνηση από το OPSIS.

## 4. Γραφικό περιβάλλον, διαρροές, ανθεκτικότητα
```powershell
# Ακολούθησε τη ροή του υπάρχοντος, προς έλεγχο script:
Get-Content .\tools\qa-r9-windows.ps1
# Προσοχή: το script κάνει παρεμβάσεις, τρέξ' το μόνο σε disposable runner.
```
Μεγαλύτερη έμφαση σε DeskStrip/DeskContainers/DeskZones, WeatherWallpaper WebView2, εκκίνηση/τερματισμό, αποκλίσεις DPI 100/150/200%, GL recovery, αλλαγή ή αποσύνδεση μόνιτορ, NVIDIA/AMD/Intel όπου υπάρχουν, low/high tier, WASAPI device hotplug, πολυγλωσσικά layouts και UI Automation. Για #20 εκτέλεσε `tools/verify-atomic-powerloss.ps1` μόνο απομονωμένα.

## 5. Inno Setup 7 installer
```powershell
$compiler = "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
if (!(Test-Path $compiler)) { throw 'Install Inno Setup 7 before building' }
& .\installer\build.ps1 -Compiler $compiler
# Εναλλακτικά ακολούθησε την παραγωγή publish και installer.iss όπως ορίζει ο source.
Get-ChildItem .\Output -File
Get-ChildItem .\Output -File | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name }
```
Η `installer/build.ps1` κάνει και κλασικό Explorer integration build. **Μη παρακάμψεις** σφάλμα εκεί μόνο και μόνο για να βγει installer: αν η optional λειτουργία χρειάζεται πραγματικά native tools, είτε πρόσθεσε πλήρη portable fallback είτε τεκμηρίωσε υποβάθμιση. Δεν προσαρτούμε κατ' όνομα παλιό OpsisSetup.exe ως «νέο» installer.

## 6. Μηχανισμοί και ροή CI
- Υπάρχουν `.github/workflows/r9-release-qa.yml`, `plugin-compatibility.yml` και `windows-audit.yml`. Το R9 workflow στοχεύει προς το παρόν branch `release/opsis-2.8.0.2500-r9` — πριν χρησιμοποιηθεί για **R10**, πρέπει να ενημερωθούν branch/path/runners/labels και να εκτελεστεί στο σωστό checked-out source.
- Εξέτασε και διόρθωσε κενά: required checks/artefacts, SHA-256 και version metadata, Windows E2E, μηχανισμός ασφαλούς rollback, neutral appdata.
- Δοκίμασε clean install, upgrade από παλαιότερο, coexistence με 2.4.6.1, plugins preserved, uninstall cleanup και ακύρωση/διακοπή εγκατάστασης.
- Πέρασε Windows Defender, vulnerabilities scan, secrets scan, native DLL security, υπογραφή αρχείων όπου διαθέσιμη. Η έλλειψη code-sign certificate δεν πρέπει να μεταμφιέζεται σε επιτυχία SmartScreen.

## 7. Release decision
Μόνο με **όλα τα mandatory gates επιτυχημένα**, με screenshots/logs και SHA-256 του πραγματικού `.exe`, χαρακτηρίζεται `release ready`. Διαφορετικά: `NOT RELEASE READY`, με συγκεκριμένες αιτίες. Η προηγούμενη εγγραφή «39/39 Node» δεν αποδεικνύει τίποτα για installer/WinForms.

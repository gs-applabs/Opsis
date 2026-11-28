#if Ver < EncodeVer(7,0,0)
  #error This installer requires Inno Setup 7 or newer.
#endif
#define MyAppName "Opsis - Desktop Customization"
#define MyAppExeName "Opsis.exe"
#define MyAppPublisher "gs-applabs"
#define MyAppURL "https://github.com/gs-applabs/Opsis"
#define OpsisAppId "A9E8F765-1234-4567-89AB-CDEF01234567"
#ifndef PublishDir
  #define PublishDir "publish"
#endif
; Release version is derived from the built EXE, not entered a second time.
; A source-only Inno compilation without `dotnet publish` must fail explicitly.
#define MyAppVersion GetFileVersion(PublishDir + "\" + MyAppExeName)
#include "installer\dependencies.iss"

[Setup]
AppId={{{#OpsisAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoCompany={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=Opsis - Desktop Customization Setup
VersionInfoProductName={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=OpsisSetup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
AppMutex=Opsis.SingleInstance
CloseApplications=yes
CloseApplicationsFilterExcludes=OpsisExplorer.dll
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SetupIconFile=assets\Opsis.ico
WizardImageFile=installer\wizard\large-100.bmp,installer\wizard\large-125.bmp,installer\wizard\large-150.bmp,installer\wizard\large-200.bmp
WizardSmallImageFile=installer\wizard\small-100.bmp,installer\wizard\small-125.bmp,installer\wizard\small-150.bmp,installer\wizard\small-200.bmp
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=LICENSE
DisableWelcomePage=no

[Languages]
Name: "greek"; MessagesFile: "installer\Greek.isl"; InfoBeforeFile: "installer\Info-el.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "chinese"; MessagesFile: "installer\Chinese.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"; InfoBeforeFile: "installer\Info-en.txt"
Name: "hindi"; MessagesFile: "installer\Hindi.isl"; InfoBeforeFile: "installer\Info-en.txt"

[CustomMessages]
greek.UpdateDetected=Εντοπίστηκε ήδη εγκατεστημένη έκδοση %1 του {#MyAppName}.%n%nΘα ενημερωθεί στην έκδοση {#MyAppVersion}.
english.UpdateDetected=An existing installation of {#MyAppName} version %1 was detected.%n%nIt will be updated to version {#MyAppVersion}.
german.UpdateDetected=Eine vorhandene Installation von {#MyAppName} in Version %1 wurde erkannt.%n%nSie wird auf Version {#MyAppVersion} aktualisiert.
french.UpdateDetected=Une installation existante de {#MyAppName} version %1 a été détectée.%n%nElle sera mise à jour vers la version {#MyAppVersion}.
spanish.UpdateDetected=Se detectó una instalación existente de {#MyAppName} versión %1.%n%nSe actualizará a la versión {#MyAppVersion}.
korean.UpdateDetected={#MyAppName} 버전 %1이(가) 이미 설치되어 있습니다.%n%n버전 {#MyAppVersion}(으)로 업데이트됩니다.
chinese.UpdateDetected=检测到已安装 {#MyAppName} 版本 %1。%n%n将更新到版本 {#MyAppVersion}。
italian.UpdateDetected=È stata rilevata un'installazione esistente di {#MyAppName} versione %1.%n%nVerrà aggiornata alla versione {#MyAppVersion}.
russian.UpdateDetected=Обнаружена существующая установка {#MyAppName} версии %1.%n%nОна будет обновлена до версии {#MyAppVersion}.
japanese.UpdateDetected={#MyAppName} バージョン %1 の既存のインストールが検出されました。%n%nバージョン {#MyAppVersion} に更新されます。
portuguese.UpdateDetected=Foi detetada uma instalação existente do {#MyAppName} versão %1.%n%nSerá atualizada para a versão {#MyAppVersion}.
turkish.UpdateDetected={#MyAppName} uygulamasının %1 sürümü zaten yüklü.%n%nSürüm {#MyAppVersion} olarak güncellenecek.
arabic.UpdateDetected=تم اكتشاف تثبيت موجود من {#MyAppName} الإصدار %1.%n%nسيتم تحديثه إلى الإصدار {#MyAppVersion}.
hindi.UpdateDetected={#MyAppName} संस्करण %1 का मौजूदा इंस्टॉलेशन मिला।%n%nइसे संस्करण {#MyAppVersion} में अपडेट किया जाएगा।
greek.AutostartTask=Αυτόματη εκκίνηση με την είσοδο στα Windows (System Tray)
english.AutostartTask=Start automatically when signing in to Windows (System Tray)
german.AutostartTask=Automatisch beim Anmelden bei Windows starten (Infobereich)
french.AutostartTask=Démarrer automatiquement à l'ouverture de session Windows (zone de notification)
spanish.AutostartTask=Iniciar automáticamente al iniciar sesión en Windows (bandeja del sistema)
korean.AutostartTask=Windows 로그인 시 자동으로 시작(시스템 트레이)
chinese.AutostartTask=登录 Windows 时自动启动（系统托盘）
italian.AutostartTask=Avvia automaticamente all'accesso a Windows (area di notifica)
russian.AutostartTask=Запускать автоматически при входе в Windows (область уведомлений)
japanese.AutostartTask=Windows へのサインイン時に自動的に起動する (システム トレイ)
portuguese.AutostartTask=Iniciar automaticamente ao iniciar sessão no Windows (área de notificação)
turkish.AutostartTask=Windows'ta oturum açılırken otomatik başlat (Sistem Tepsisi)
arabic.AutostartTask=البدء تلقائيًا عند تسجيل الدخول إلى Windows (علبة النظام)
hindi.AutostartTask=Windows में साइन इन करते समय अपने आप शुरू करें (सिस्टम ट्रे)
greek.StartupGroup=Ρυθμίσεις Εκκίνησης:
english.StartupGroup=Startup settings:
german.StartupGroup=Starteinstellungen:
french.StartupGroup=Paramètres de démarrage :
spanish.StartupGroup=Configuración de inicio:
korean.StartupGroup=시작 설정:
chinese.StartupGroup=启动设置：
italian.StartupGroup=Impostazioni di avvio:
russian.StartupGroup=Параметры запуска:
japanese.StartupGroup=起動設定:
portuguese.StartupGroup=Definições de arranque:
turkish.StartupGroup=Başlangıç ayarları:
arabic.StartupGroup=إعدادات بدء التشغيل:
hindi.StartupGroup=स्टार्टअप सेटिंग्स:

greek.DesktopMenuTask=Εμφάνιση του Opsis στο δεξί κλικ της επιφάνειας εργασίας
english.DesktopMenuTask=Show Opsis in the desktop right-click menu
english.LegacyFolder=This folder contains the earlier Opsis branch (Opsis.exe). Select a separate folder for this installation.
greek.LegacyFolder=Αυτός ο φάκελος περιέχει το παλιό branch του Opsis (Opsis.exe). Επιλέξτε ξεχωριστό φάκελο για αυτή την εγκατάσταση.
greek.CrawlerTask=Εγκατάσταση του News Crawler ως πρόσθετου του Opsis
english.CrawlerTask=Install the News Crawler as an Opsis plugin

[Tasks]
Name: "desktopmenu"; Description: "{cm:DesktopMenuTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
#ifdef CrawlerSetup
Name: "installcrawler"; Description: "{cm:CrawlerTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
#endif
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Opsis.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#PublishDir}\Opsis.exe"; DestDir: "{app}"; DestName: "Opsis.scr"; Flags: ignoreversion restartreplace
Source: "{#PublishDir}\Opsis.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#PublishDir}\OpsisExplorer.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace skipifsourcedoesntexist
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "\Opsis.exe,\Opsis.dll,\OpsisExplorer.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef CrawlerSetup
Source: "{#CrawlerSetup}"; DestDir: "{tmp}"; DestName: "OpsisNewsCrawler-Setup.exe"; Tasks: installcrawler; Flags: deleteafterinstall
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\shellex\ContextMenuHandlers\Opsis"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{7F45B246-0BA9-4C86-AB71-09F969610F62}"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\Shell\Opsis"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Opsis"; ValueData: """{app}\{#MyAppExeName}"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
#ifdef CrawlerSetup
Filename: "{tmp}\OpsisNewsCrawler-Setup.exe"; Parameters: "/MODE=integrated /PLUGINROOT=""{app}\plugins"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DEPS=none"; Tasks: installcrawler; Flags: waituntilterminated
#endif
Filename: "{app}\{#MyAppExeName}"; Parameters: "--setup-desktop-menu on {language}"; Tasks: desktopmenu; Flags: runhidden waituntilterminated runasoriginaluser; Check: DotNetDesktop11Present
Filename: "{app}\{#MyAppExeName}"; Parameters: "--setup-desktop-menu off {language}"; Tasks: not desktopmenu; Flags: runhidden waituntilterminated runasoriginaluser; Check: DotNetDesktop11Present
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: DotNetDesktop11Present

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Current, Marker, Original, OwnSaver: String;
  Present: Cardinal;
  Restored: Boolean;
begin
  if CurUninstallStep <> usUninstall then exit;
  OwnSaver := ExpandConstant('{app}\Opsis.scr');
  if not RegQueryStringValue(HKCU, 'Control Panel\Desktop', 'SCRNSAVE.EXE', Current) then exit;
  if CompareText(Current, OwnSaver) <> 0 then exit;
  Restored := False;
  if RegQueryStringValue(HKCU, 'Software\Opsis\ScreensaverBackup', 'SelectedPath', Marker) and
     (CompareText(Marker, OwnSaver) = 0) and
     RegQueryDWordValue(HKCU, 'Software\Opsis\ScreensaverBackup', 'OriginalPresent', Present) then
  begin
    if Present = 0 then
      Restored := RegDeleteValue(HKCU, 'Control Panel\Desktop', 'SCRNSAVE.EXE')
    else if (Present = 1) and RegQueryStringValue(HKCU, 'Software\Opsis\ScreensaverBackup', 'OriginalValue', Original) then
      Restored := RegWriteStringValue(HKCU, 'Control Panel\Desktop', 'SCRNSAVE.EXE', Original);
  end
  else
    Restored := RegDeleteValue(HKCU, 'Control Panel\Desktop', 'SCRNSAVE.EXE');
  if Restored then
  begin
    RegDeleteValue(HKCU, 'Software\Opsis\ScreensaverBackup', 'SelectedPath');
    RegDeleteValue(HKCU, 'Software\Opsis\ScreensaverBackup', 'OriginalPresent');
    RegDeleteValue(HKCU, 'Software\Opsis\ScreensaverBackup', 'OriginalValue');
  end
  else Log('Opsis screensaver selection could not be restored for the current uninstall account.');
end;

procedure InitializeWizard();
var
  Previous: String;
begin
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#OpsisAppId}' + '}_is1', 'DisplayVersion', Previous) then
    WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('UpdateDetected'), [Previous]);
  DepsInit(wpSelectTasks);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not WizardSilent then
    Result := DepsDownloadSelected();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousVersion: String;
begin
  Result := '';
  { Protect a folder containing an unregistered Opsis executable, but allow
    recognized in-place upgrades/reinstalls with this exact product AppId. }
  if FileExists(ExpandConstant('{app}\Opsis.exe')) and
     not RegQueryStringValue(HKLM64,
       'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#OpsisAppId}' + '}_is1',
       'DisplayVersion', PreviousVersion) then
  begin
    Result := CustomMessage('LegacyFolder');
    Exit;
  end;
  if WizardSilent then
    if not DepsDownloadSelected() then
    begin
      Result := FmtMessage(CustomMessage('DepsFailed'), ['.NET / WebView2', 'download']);
      Exit;
    end;
  DepsInstallSelected();
end;

#if Ver < EncodeVer(7,0,0)
  #error This installer requires Inno Setup 7 or newer.
#endif
#define MyAppName "Opsis Studio"
#define MyAppVersion "2.6.0-studio.22"
#define MyAppExeName "Opsis.Studio.exe"
#define MyAppPublisher "gs-applabs"
#define MyAppURL "https://github.com/gs-applabs/Opsis"
#define StudioAppId "0F9363FA-64CB-45CF-8D70-E748B9C5BDA8"
#ifndef PublishDir
  #define PublishDir "publish"
#endif
#include "installer\dependencies.iss"

[Setup]
AppId={{{#StudioAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
VersionInfoCompany={#MyAppPublisher}
VersionInfoVersion=2.6.0.2202
VersionInfoProductVersion=2.6.0.2202
VersionInfoDescription=Opsis Studio Setup
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=Opsis-Studio-{#MyAppVersion}-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
AppMutex=Opsis.SingleInstance
CloseApplications=yes
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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked

[Files]
#ifdef ExternalPayload
Source: "{src}\payload\Opsis.Studio.exe"; DestDir: "{app}"; Flags: external ignoreversion restartreplace
Source: "{src}\payload\Opsis.Studio.dll"; DestDir: "{app}"; Flags: external ignoreversion restartreplace
Source: "{src}\payload\*"; DestDir: "{app}"; Excludes: "\Opsis.Studio.exe,\Opsis.Studio.dll"; Flags: external ignoreversion recursesubdirs createallsubdirs
#else
Source: "{#PublishDir}\Opsis.Studio.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#PublishDir}\Opsis.Studio.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "\Opsis.Studio.exe,\Opsis.Studio.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OpsisStudio"; ValueData: """{app}\{#MyAppExeName}"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: DotNetDesktop11Present

[Code]
#ifdef ExternalPayload
function InitializeSetup(): Boolean;
begin
  Result := FileExists(ExpandConstant('{src}\payload\{#MyAppExeName}')) and
    FileExists(ExpandConstant('{src}\payload\Opsis.Studio.dll')) and
    FileExists(ExpandConstant('{src}\payload\Opsis.Studio.runtimeconfig.json'));
  if not Result then
    SuppressibleMsgBox('Extract the full setup ZIP and run Setup from the extracted folder.' + #13#10 +
      'Αποσυμπίεσε όλο το ZIP και άνοιξε τον οδηγό εγκατάστασης από τον φάκελο.', mbError, MB_OK, IDOK);
end;

function CheckPayloadFile(const Name: String): String;
var
  Source: TFileStream;
begin
  Result := '';
  try
    Source := TFileStream.Create(ExpandConstant('{src}\payload\') + Name, fmOpenRead or fmShareDenyNone);
    try
      if Source.Size = 0 then RaiseException('Empty file');
    finally
      Source.Free;
    end;
  except
    Log('Cannot read payload file ' + Name + ': ' + GetExceptionMessage);
    if ActiveLanguage = 'greek' then
      Result := 'Δεν είναι δυνατή η ανάγνωση του αρχείου ' + Name + '. Αποσυμπίεσε ξανά όλο το ZIP σε έναν τοπικό φάκελο, όπως οι Λήψεις, και εκτέλεσε τον οδηγό από εκεί.'
    else
      Result := 'Setup cannot read ' + Name + '. Extract the entire ZIP again into a local folder such as Downloads and run Setup from there.';
  end;
end;
#endif

procedure InitializeWizard();
var
  Previous: String;
begin
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#StudioAppId}' + '}_is1', 'DisplayVersion', Previous) then
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
begin
  Result := '';
#ifdef ExternalPayload
  if CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')), ExpandConstant('{src}\payload')) = 0 then
  begin
    if ActiveLanguage = 'greek' then
      Result := 'Ο φάκελος εγκατάστασης πρέπει να είναι διαφορετικός από τον φάκελο payload.'
    else
      Result := 'The installation folder must be different from the payload folder.';
    Exit;
  end;
  Result := CheckPayloadFile('{#MyAppExeName}');
  if Result = '' then Result := CheckPayloadFile('Opsis.Studio.dll');
  if Result = '' then Result := CheckPayloadFile('Opsis.Studio.deps.json');
  if Result = '' then Result := CheckPayloadFile('Opsis.Studio.runtimeconfig.json');
  if Result <> '' then Exit;
#endif
  if WizardSilent then
    DepsDownloadSelected();
  DepsInstallSelected();
end;

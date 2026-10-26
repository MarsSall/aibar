; Inno Setup 6. Inputs are supplied by scripts/Build-Installer.ps1.
#ifndef AppVersion
  #error AppVersion must be supplied with /DAppVersion
#endif
#ifndef PublishDirectory
  #error PublishDirectory must be supplied with /DPublishDirectory
#endif
#ifndef RepositoryRoot
  #error RepositoryRoot must be supplied with /DRepositoryRoot
#endif

[Setup]
; Keep this identity stable across releases so upgrades reuse the installation.
AppId={{B1A16C52-01DA-4857-BBD8-328670BBF0C2}
AppName=AIBar
AppVersion={#AppVersion}
AppPublisher=MarsSall
AppPublisherURL=https://github.com/MarsSall/aibar
AppSupportURL=https://github.com/MarsSall/aibar/issues
AppUpdatesURL=https://github.com/MarsSall/aibar/releases
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\AIBar
DefaultGroupName=AIBar
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#RepositoryRoot}\src\AIBar.Desktop\Assets\AIBar.ico
UninstallDisplayIcon={app}\AIBar.Desktop.exe
LicenseFile={#RepositoryRoot}\LICENSE
AppMutex=AIBar.mutex
CloseApplications=yes
RestartApplications=no
OutputBaseFilename=AIBar-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.DeleteData=Delete my AIBar data too
spanish.DeleteData=Borrar también mis datos
english.DataPrompt=AIBar keeps your data unless you select the checkbox below.
spanish.DataPrompt=AIBar conserva tus datos salvo que marques la casilla siguiente.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AIBar"; Filename: "{app}\AIBar.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\AIBar"; Filename: "{app}\AIBar.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AIBar.Desktop.exe"; Description: "{cm:LaunchProgram,AIBar}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
var
  DeleteDataRequested: Boolean;

function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  Explanation: TNewStaticText;
  DeleteData: TNewCheckBox;
  ContinueButton, CancelButton: TNewButton;
begin
  DeleteDataRequested := False;
  Result := True;
  { Silent uninstall must never opt into deleting user data. }
  if UninstallSilent then
    Exit;

  Form := CreateCustomForm(ScaleX(440), ScaleY(170), False, False);
  try
    Form.Caption := 'AIBar';
    Explanation := TNewStaticText.Create(Form);
    Explanation.Parent := Form;
    Explanation.SetBounds(ScaleX(16), ScaleY(16), ScaleX(408), ScaleY(48));
    Explanation.AutoSize := False;
    Explanation.WordWrap := True;
    Explanation.Caption := CustomMessage('DataPrompt');
    DeleteData := TNewCheckBox.Create(Form);
    DeleteData.Parent := Form;
    DeleteData.SetBounds(ScaleX(16), ScaleY(72), ScaleX(408), ScaleY(24));
    DeleteData.Caption := CustomMessage('DeleteData');
    DeleteData.Checked := False;
    ContinueButton := TNewButton.Create(Form);
    ContinueButton.Parent := Form;
    ContinueButton.SetBounds(ScaleX(238), ScaleY(126), ScaleX(90), ScaleY(26));
    ContinueButton.Caption := SetupMessage(msgButtonOK);
    ContinueButton.Default := True;
    ContinueButton.ModalResult := mrOK;
    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.SetBounds(ScaleX(334), ScaleY(126), ScaleX(90), ScaleY(26));
    CancelButton.Caption := SetupMessage(msgButtonCancel);
    CancelButton.Cancel := True;
    CancelButton.ModalResult := mrCancel;
    Result := Form.ShowModal() = mrOK;
    if Result then
      DeleteDataRequested := DeleteData.Checked;
  finally
    Form.Free();
  end;
end;

procedure RemoveOwnedStartupValue();
var
  Command, ExpectedCommand: String;
begin
  { Match exactly the command written by PerUserStartupRegistration. A portable
    copy or another installation's startup entry must be left alone. }
  ExpectedCommand := '"' + ExpandConstant('{app}\AIBar.Desktop.exe') + '" --startup';
  if RegQueryStringValue(HKCU, RunKey, 'AIBar', Command) then
    if CompareText(Command, ExpectedCommand) = 0 then
      RegDeleteValue(HKCU, RunKey, 'AIBar');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveOwnedStartupValue();
  if CurUninstallStep = usPostUninstall then
    if DeleteDataRequested then
      if not DelTree(ExpandConstant('{localappdata}\AIBar'), True, True, True) then
        Log('AIBar data could not be completely removed.');
end;

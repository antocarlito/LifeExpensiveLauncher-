[Setup]
AppName=LifeExpensive RP Launcher
AppVersion=1.0.0
AppPublisher=LifeExpensive RP
AppPublisherURL=https://lifeexpensive.com
DefaultDirName={autopf}\LifeExpensive Launcher
DefaultGroupName=LifeExpensive RP
OutputDir=installer
OutputBaseFilename=LifeExpensive_Launcher_Setup
SetupIconFile=LifeExpensiveLauncher\Resources\icon.ico
UninstallDisplayIcon={app}\LifeExpensiveLauncher.exe
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
WizardSizePercent=100,120
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
WizardImageFile=installer_assets\wizard_image.bmp
WizardSmallImageFile=installer_assets\wizard_small.bmp

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Creer un raccourci sur le Bureau"; GroupDescription: "Raccourcis:"
Name: "startmenuicon"; Description: "Creer un raccourci dans le menu Demarrer"; GroupDescription: "Raccourcis:"

[Files]
Source: "publish\LifeExpensiveLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\media\*"; DestDir: "{app}\media"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "music\*"

[Icons]
Name: "{autodesktop}\LifeExpensive RP"; Filename: "{app}\LifeExpensiveLauncher.exe"; Tasks: desktopicon
Name: "{group}\LifeExpensive RP"; Filename: "{app}\LifeExpensiveLauncher.exe"; Tasks: startmenuicon
Name: "{group}\Desinstaller LifeExpensive RP"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\LifeExpensiveLauncher.exe"; Description: "Lancer LifeExpensive RP Launcher"; Flags: nowait postinstall shellexec

[Code]
procedure InitializeWizard();
var
  i: Integer;
begin
  // === FOND SOMBRE PARTOUT ===
  WizardForm.Color := $1A0D0D;
  WizardForm.InnerPage.Color := $1A0D0D;
  WizardForm.MainPanel.Color := $280D0D;

  // === TITRES (Cyan/Or) ===
  WizardForm.PageNameLabel.Font.Color := $DDBB00;
  WizardForm.PageNameLabel.Font.Size := 11;
  WizardForm.PageNameLabel.Font.Style := [fsBold];
  WizardForm.PageDescriptionLabel.Font.Color := $AAAAAA;
  WizardForm.PageDescriptionLabel.Font.Size := 9;

  // === BIENVENUE ===
  WizardForm.WelcomeLabel1.Font.Color := $DDBB00;
  WizardForm.WelcomeLabel1.Font.Size := 18;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.WelcomeLabel2.Font.Color := $E0E0E0;
  WizardForm.WelcomeLabel2.Font.Size := 10;

  // === FIN ===
  WizardForm.FinishedHeadingLabel.Font.Color := $DDBB00;
  WizardForm.FinishedHeadingLabel.Font.Size := 18;
  WizardForm.FinishedHeadingLabel.Font.Style := [fsBold];
  WizardForm.FinishedLabel.Font.Color := $E0E0E0;
  WizardForm.FinishedLabel.Font.Size := 10;

  // === DOSSIER DESTINATION ===
  WizardForm.DirEdit.Color := $2A1A1A;
  WizardForm.DirEdit.Font.Color := $E0E0E0;
  WizardForm.DirEdit.Font.Size := 10;
  WizardForm.SelectDirLabel.Font.Color := $E0E0E0;
  WizardForm.SelectDirBrowseLabel.Font.Color := $AAAAAA;

  // === LISTE DES TACHES (checkboxes) ===
  WizardForm.TasksList.Color := $1A0D0D;
  WizardForm.TasksList.Font.Color := $E0E0E0;
  WizardForm.TasksList.Font.Size := 10;
  WizardForm.SelectTasksLabel.Font.Color := $E0E0E0;

  // === COMPOSANTS ===
  WizardForm.ComponentsList.Color := $1A0D0D;
  WizardForm.ComponentsList.Font.Color := $E0E0E0;
  WizardForm.SelectComponentsLabel.Font.Color := $E0E0E0;

  // === PROGRESSION ===
  WizardForm.StatusLabel.Font.Color := $E0E0E0;
  WizardForm.FilenameLabel.Font.Color := $AAAAAA;

  // === CHECKBOX LANCER APRES INSTALL ===
  for i := 0 to WizardForm.RunList.Items.Count - 1 do
  begin
    WizardForm.RunList.Font.Color := $E0E0E0;
  end;
  WizardForm.RunList.Color := $1A0D0D;
  WizardForm.RunList.Font.Color := $E0E0E0;
end;

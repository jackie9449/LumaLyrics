#ifndef AppVersion
  #error AppVersion must be supplied by publish-installer.ps1
#endif
#ifndef PayloadDirectory
  #error PayloadDirectory must be supplied by publish-installer.ps1
#endif
#ifndef ArtifactDirectory
  #error ArtifactDirectory must be supplied by publish-installer.ps1
#endif

[Setup]
AppId={{B2DF22B2-8DE1-4C4B-91B6-DA46A7E9B109}
AppName=浮光歌词
AppVersion={#AppVersion}
AppPublisher=jackie9449
AppPublisherURL=https://github.com/jackie9449/LumaLyrics
AppSupportURL=https://github.com/jackie9449/LumaLyrics/issues
AppUpdatesURL=https://github.com/jackie9449/LumaLyrics/releases
DefaultDirName={localappdata}\Programs\LumaLyrics
DefaultGroupName=浮光歌词
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#ArtifactDirectory}
OutputBaseFilename=LumaLyrics-{#AppVersion}-setup-win-x64
SetupIconFile=..\src\LumaLyrics.App\Assets\luma.ico
UninstallDisplayIcon={app}\luma.ico
UninstallDisplayName=浮光歌词 {#AppVersion}
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
CloseApplications=yes
CloseApplicationsFilter=LumaLyrics.dll,LumaLyrics.exe
RestartApplications=no
UsePreviousTasks=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=浮光歌词安装程序

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userdesktop}\浮光歌词"; Filename: "{app}\浮光歌词.exe"; WorkingDir: "{app}"; IconFilename: "{app}\luma.ico"; Comment: "浮光歌词 LumaLyrics"; AppUserModelID: "LumaLyrics.Desktop"
Name: "{userprograms}\浮光歌词\浮光歌词"; Filename: "{app}\浮光歌词.exe"; WorkingDir: "{app}"; IconFilename: "{app}\luma.ico"; Comment: "浮光歌词 LumaLyrics"; AppUserModelID: "LumaLyrics.Desktop"
Name: "{userprograms}\浮光歌词\卸载浮光歌词"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\浮光歌词.exe"; Description: "启动浮光歌词"; Flags: postinstall nowait skipifsilent

[Messages]
chinesesimplified.WelcomeLabel2=此向导将在你的电脑上安装浮光歌词。%n%n程序会创建名为“浮光歌词”的桌面快捷方式，并保留现有个人歌词、译文、封面和设置。%n%n升级前请从旧版托盘菜单选择“退出”。
chinesesimplified.FinishedLabel=浮光歌词已安装。你可以通过桌面上的“浮光歌词”快捷方式启动。%n%n卸载只移除程序，个人资料保留在原位置。

; Deliberately no UninstallDelete entries: personal data is outside {app} and is never removed.

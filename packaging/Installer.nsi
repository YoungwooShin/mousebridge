Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!ifndef PAYLOAD
!error "Pass /DPAYLOAD=path-to-self-contained-publish"
!endif
!ifndef OUTPUT
!define OUTPUT "MouseBridge-Setup-win-x64.exe"
!endif
Name "MouseBridge PoC"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\MouseBridge"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "0.3.0.0"
VIAddVersionKey "ProductName" "MouseBridge PoC"
VIAddVersionKey "FileDescription" "MouseBridge per-user installer"
VIAddVersionKey "FileVersion" "0.3.0.0"
VIAddVersionKey "LegalCopyright" "MouseBridge contributors"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Korean"
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "MouseBridge requires 64-bit Windows."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "MouseBridge requires Windows 11."
    Abort
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 22000
    MessageBox MB_ICONSTOP "MouseBridge requires Windows 11 (build 22000 or later)."
    Abort
  ${EndIf}
  SetShellVarContext current
FunctionEnd
Section "MouseBridge"
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}/*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\MouseBridge"
  CreateShortcut "$SMPROGRAMS\MouseBridge\MouseBridge.lnk" "$INSTDIR\MouseBridge.exe"
  CreateShortcut "$SMPROGRAMS\MouseBridge\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "DisplayName" "MouseBridge PoC"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "DisplayVersion" "0.3.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "Publisher" "MouseBridge"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "DisplayIcon" "$INSTDIR\MouseBridge.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC" "NoRepair" 1
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  !include "UninstallFiles.nsh"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\MouseBridge\MouseBridge.lnk"
  Delete "$SMPROGRAMS\MouseBridge\Uninstall.lnk"
  RMDir "$SMPROGRAMS\MouseBridge"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBridgePoC"
SectionEnd

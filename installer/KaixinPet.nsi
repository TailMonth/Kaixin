Unicode true
!include "MUI2.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "0.7.0"
!endif
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR is required"
!endif
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "."
!endif
!ifndef APP_ICON
  !define APP_ICON "..\assets\ui\app.ico"
!endif

!define APP_NAME "开心桌宠"
!define APP_EXE "KaixinPet.exe"
!define APP_PUBLISHER "SOPII"
!define APP_URL "https://github.com/TailMonth/Kaixin"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KaixinPet"

Name "${APP_NAME}"
OutFile "${OUTPUT_DIR}\KaixinPet-${APP_VERSION}-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\KaixinPet"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "© ${APP_PUBLISHER}"

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "启动开心桌宠"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Section "开心桌宠" SecMain
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "${APP_URL}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "KaixinPet"

  RMDir /r "$INSTDIR"

  IfSilent no_data_prompt
  MessageBox MB_YESNO|MB_ICONQUESTION "是否同时删除设置、日志和自定义音效？选择“否”将保留它们。" IDNO no_data_prompt
  RMDir /r "$LOCALAPPDATA\KaixinPet"
  no_data_prompt:
SectionEnd

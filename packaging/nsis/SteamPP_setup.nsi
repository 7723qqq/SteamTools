; Watt Toolkit 安装包脚本（自包含，不依赖外部 NSIS-Build 项目）
; 构建示例：
;   makensis /DPUBLISH_DIR="...\Publish\[Release] Steam++_v3.3.0_win_x64_xxx" ^
;            /DOUTFILE="...\Steam++_v3.3.0_win_x64_setup.exe" packaging/nsis/SteamPP_setup.nsi

Unicode true
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define APP_NAME "Watt Toolkit"
!define APP_EXE "Steam++.exe"
!define APP_PUBLISHER "BeyondDimension"
!define APP_VERSION "3.3.0"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\WattToolkit"
!define INSTALL_DIR "$PROGRAMFILES64\Watt Toolkit"
Var InstallDirValid

!ifndef PUBLISH_DIR
  !error "需要定义 PUBLISH_DIR（发布目录）"
!endif
!ifndef OUTFILE
  !define OUTFILE "Steam++_setup.exe"
!endif

Name "${APP_NAME}"
OutFile "${OUTFILE}"
InstallDir "${INSTALL_DIR}"
InstallDirRegKey HKLM "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel admin

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!macro CheckInstallDir PREFIX
Function ${PREFIX}CheckInstallDir
  StrCpy $InstallDirValid "1"
  StrCpy $R0 "$INSTDIR"
  StrCpy $R1 "$R0" "" -1
  ${If} $R1 == "\"
    StrCpy $R0 "$R0" -1
  ${EndIf}

  ${GetRoot} "$INSTDIR" $R1
  StrCpy $R2 "$R1" "" -1
  ${If} $R2 == "\"
    StrCpy $R1 "$R1" -1
  ${EndIf}
  ${If} $R0 == $R1
    StrCpy $InstallDirValid "0"
  ${EndIf}
  ${If} $R0 == "$WINDIR"
  ${OrIf} $R0 == "$SYSDIR"
  ${OrIf} $R0 == "$PROGRAMFILES"
  ${OrIf} $R0 == "$PROGRAMFILES64"
  ${OrIf} $R0 == "$COMMONFILES"
  ${OrIf} $R0 == "$COMMONFILES64"
  ${OrIf} $R0 == "$APPDATA"
  ${OrIf} $R0 == "$LOCALAPPDATA"
  ${OrIf} $R0 == "$DESKTOP"
  ${OrIf} $R0 == "$SMPROGRAMS"
  ${OrIf} $R0 == "$STARTMENU"
  ${OrIf} $R0 == "$DOCUMENTS"
    StrCpy $InstallDirValid "0"
  ${EndIf}

  ${If} $InstallDirValid == "1"
    SetRegView 64
    ReadRegStr $R3 HKLM "${UNINST_KEY}" "InstallLocation"
    ${If} $R3 == "$INSTDIR"
      IfFileExists "$INSTDIR\uninst.exe" check_install_dir_done
    ${EndIf}
    FindFirst $R0 $R1 "$INSTDIR\*.*"
    StrCmp $R0 "" check_install_dir_done
    StrCpy $InstallDirValid "0"
    FindClose $R0
    check_install_dir_done:
  ${EndIf}

  ${If} $InstallDirValid != "1"
    MessageBox MB_OK|MB_ICONSTOP "请选择盘符下的独立子目录作为安装目录，不能使用根目录、系统目录、桌面、开始菜单、文档或包含其他文件的目录。"
  ${EndIf}
FunctionEnd
!macroend

!insertmacro CheckInstallDir ""

Function .onVerifyInstDir
  Call CheckInstallDir
  ${If} $InstallDirValid != "1"
    Abort
  ${EndIf}
FunctionEnd

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetRegView 64
  SectionIn RO

  Call CheckInstallDir
  ${If} $InstallDirValid != "1"
    Abort
  ${EndIf}

  SetOutPath "$INSTDIR"
  ; AppData 与 Cache 是运行时数据，不随安装包分发
  File /r /x AppData /x Cache "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\uninst.exe"

  CreateDirectory "$SMPROGRAMS"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  ; 清掉早期版本写入 32 位视图的卸载项，避免出现两条
  SetRegView 32
  DeleteRegKey HKLM "${UNINST_KEY}"
  SetRegView 64

  WriteRegStr HKLM "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
SectionEnd

!insertmacro CheckInstallDir "un."

Section "Uninstall"
  SetRegView 64
  Call un.CheckInstallDir
  ${If} $InstallDirValid != "1"
    Quit
  ${EndIf}

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINST_KEY}"
SectionEnd

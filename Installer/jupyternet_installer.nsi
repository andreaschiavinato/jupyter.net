Name "jupyter.net installer"

OutFile "jupyternet_setup.exe"

InstallDir $PROGRAMFILES\jupyternet

;--------------------------------

Page components
Page directory
Page instfiles

UninstPage uninstConfirm
UninstPage instfiles

;--------------------------------

Section "jupyter.net"
  SectionIn RO  
  SetOutPath $INSTDIR
  
  File "JupiterNet.exe"
  File "JupiterNetClient.dll"
  File "Newtonsoft.Json.dll"
  File "ZeroMQ.dll"  
  CreateDirectory $INSTDIR\i386
  CreateDirectory $INSTDIR\amd64
  File /oname=$INSTDIR\i386\libzmq.dll "i386\libzmq.dll" 
  File /oname=$INSTDIR\amd64\libzmq.dll "amd64\libzmq.dll" 
   
  WriteRegStr HKLM SOFTWARE\jupyternet "Install_Dir" "$INSTDIR"
    
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\jupyternet" "DisplayName" "jupyter.net"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\jupyternet" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\jupyternet" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\jupyternet" "NoRepair" 1
  WriteUninstaller "$INSTDIR\uninstall.exe"
  
SectionEnd

Section "Start Menu Shortcuts"
  CreateDirectory "$SMPROGRAMS\jupyter.net"
  CreateShortcut "$SMPROGRAMS\jupyter.net\Uninstall.lnk" "$INSTDIR\uninstall.exe" "" "$INSTDIR\uninstall.exe" 0
  CreateShortcut "$SMPROGRAMS\jupyter.net\jupyter.net.lnk" "$INSTDIR\JupiterNet.exe" "" "$INSTDIR\JupiterNet.exe" 0
SectionEnd

;--------------------------------

; Uninstaller

Section "Uninstall"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\jupyternet"
  DeleteRegKey HKLM SOFTWARE\jupyternet
  
  Delete "$INSTDIR\JupiterNet.exe"
  Delete "$INSTDIR\JupiterNetClient.dll"
  Delete "$INSTDIR\Newtonsoft.Json.dll"
  Delete "$INSTDIR\ZeroMQ.dll"
  Delete "$INSTDIR\i386\*"
  Delete "$INSTDIR\amd64\*" 
  Delete "$INSTDIR\uninstall.exe"

  Delete "$SMPROGRAMS\jupyter.net\*.*"

  RMDir "$SMPROGRAMS\jupyter.net"
  RMDir /r "$INSTDIR"
SectionEnd

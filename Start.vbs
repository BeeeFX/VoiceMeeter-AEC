' GPL-3.0-only. Launch the GUI without a console window.
Option Explicit
Dim shell, files, folder, command, app
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
folder = files.GetParentFolderName(WScript.ScriptFullName)
app = folder & "\VoiceMeeter AEC.exe"
If Not files.FileExists(app) Then app = folder & "\target\release\app\VoiceMeeter AEC.exe"
If files.FileExists(app) Then
  command = """" & app & """"
  shell.Run command, 0, False
Else
  MsgBox "VoiceMeeter AEC has not been built yet. Run Build.cmd first.", 48, "VoiceMeeter AEC"
End If

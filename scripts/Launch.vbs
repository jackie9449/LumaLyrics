Option Explicit
Dim fs, root, host, app, shell
Set fs = CreateObject("Scripting.FileSystemObject")
root = fs.GetParentFolderName(WScript.ScriptFullName)
host = fs.BuildPath(root, "runtime\dotnet.exe")
app = fs.BuildPath(root, "LumaLyrics.dll")
Set shell = CreateObject("WScript.Shell")
shell.Run Chr(34) & host & Chr(34) & " exec " & Chr(34) & app & Chr(34), 0, False

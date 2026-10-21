Option Explicit
Dim fs, root, host, app, shell, command, argument
Set fs = CreateObject("Scripting.FileSystemObject")
root = fs.GetParentFolderName(WScript.ScriptFullName)
host = fs.BuildPath(root, "runtime\dotnet.exe")
app = fs.BuildPath(root, "LumaLyrics.dll")
Set shell = CreateObject("WScript.Shell")
command = QuoteArgument(host) & " exec " & QuoteArgument(app)
For Each argument In WScript.Arguments
    command = command & " " & QuoteArgument(argument)
Next
shell.Run command, 0, False

Function QuoteArgument(value)
    Dim result, slashes, index, character
    result = Chr(34)
    slashes = 0
    For index = 1 To Len(value)
        character = Mid(value, index, 1)
        If character = "\" Then
            slashes = slashes + 1
        ElseIf character = Chr(34) Then
            result = result & String(slashes * 2 + 1, "\") & Chr(34)
            slashes = 0
        Else
            result = result & String(slashes, "\") & character
            slashes = 0
        End If
    Next
    QuoteArgument = result & String(slashes * 2, "\") & Chr(34)
End Function

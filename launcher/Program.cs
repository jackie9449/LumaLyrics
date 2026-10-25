using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

// Windows 10's built-in .NET Framework launches the bundled Microsoft host without
// a console or VBScript. The actual WPF application uses the packaged .NET 10 runtime.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string host=Path.Combine(root,"runtime","dotnet.exe");
            string app=Path.Combine(root,"LumaLyrics.dll");
            if(!File.Exists(host) || !File.Exists(app))
                throw new FileNotFoundException("程序文件不完整，请完整解压便携包，或重新运行安装程序。");
            var arguments=new StringBuilder("exec ").Append(Quote(app));
            foreach(string argument in args)arguments.Append(' ').Append(Quote(argument));
            var start=new ProcessStartInfo(host,arguments.ToString())
            {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true};
            using(var child=Process.Start(start))
            {if(child==null)throw new InvalidOperationException("无法启动程序，请重新运行安装程序。");}
            return 0;
        }
        catch(Exception error)
        {MessageBox.Show(error.Message,"浮光歌词 · 启动失败",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    private static string Quote(string value)
    {
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char character in value)
        {
            if(character=='\\'){slashes++;continue;}
            if(character=='"'){result.Append('\\',slashes*2+1).Append('"');slashes=0;continue;}
            result.Append('\\',slashes).Append(character);slashes=0;
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
}

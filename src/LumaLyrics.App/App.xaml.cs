using System.IO;
using System.Windows;
using System.Threading;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;
public partial class App : System.Windows.Application
{
    Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int dataArg=Array.IndexOf(e.Args,"--data-dir");
        if(dataArg>=0 && (dataArg+1>=e.Args.Length||e.Args[dataArg+1].StartsWith("--"))){MessageBox.Show("请为 --data-dir 指定独立的个人数据目录。");Shutdown();return;}
        string dataDirectory;
        try{dataDirectory=DataDirectoryConfiguration.Resolve(dataArg>=0?e.Args[dataArg+1]:null);PersonalLibraryArchive.EnsureOutsideSoftware(dataDirectory);}
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"浮光歌词 · 个人资料目录不可用");
            if(dataArg>=0){Shutdown();return;}
            var choose=new OpenFolderDialog{Title="重新选择已有的个人数据目录",FolderName=DataDirectoryConfiguration.DefaultRoot};
            if(choose.ShowDialog()!=true){Shutdown();return;}
            try{DataDirectoryConfiguration.SetDefault(choose.FolderName);dataDirectory=DataDirectoryConfiguration.Resolve();}
            catch(Exception error){MessageBox.Show(error.Message,"浮光歌词 · 目录未切换");Shutdown();return;}
        }
        var mutexSuffix=dataDirectory.Equals(Path.GetFullPath(DataDirectoryConfiguration.DefaultRoot),StringComparison.OrdinalIgnoreCase)?"Desktop":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToLowerInvariant())))[..16];
        mutex = new Mutex(true, "Local\\LumaLyrics."+mutexSuffix, out bool created);
        if (!created) { MessageBox.Show("浮光歌词已在运行，请从系统托盘恢复窗口。", "浮光歌词"); Shutdown(); return; }
        DispatcherUnhandledException += (_, a) => { try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "LumaLyrics-crash.log"), a.Exception + Environment.NewLine); } catch { } MessageBox.Show(a.Exception.Message, "浮光歌词 · 发生错误"); a.Handled = true; };
        if((e.Args.Contains("--verify-import")||e.Args.Contains("--capture-dir")||e.Args.Contains("--verify-ui")||e.Args.Contains("--verify-native-library-playback")) && dataArg<0){MessageBox.Show("验证模式必须通过 --data-dir 使用独立数据目录。");Shutdown();return;}
        var window = new MainWindow(dataDirectory); MainWindow = window; window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}

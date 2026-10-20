using System.IO;
using System.Windows;
using System.Threading;
namespace LumaLyrics.App;
public partial class App : System.Windows.Application
{
    Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int dataArg=Array.IndexOf(e.Args,"--data-dir");
        var dataDirectory=dataArg>=0 && dataArg+1<e.Args.Length?e.Args[dataArg+1]:null;
        var mutexSuffix=dataDirectory is null?"Desktop":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToLowerInvariant())))[..16];
        mutex = new Mutex(true, "Local\\LumaLyrics."+mutexSuffix, out bool created);
        if (!created) { MessageBox.Show("浮光歌词已在运行，请从系统托盘恢复窗口。", "浮光歌词"); Shutdown(); return; }
        DispatcherUnhandledException += (_, a) => { try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "LumaLyrics-crash.log"), a.Exception + Environment.NewLine); } catch { } MessageBox.Show(a.Exception.Message, "浮光歌词 · 发生错误"); a.Handled = true; };
        if((e.Args.Contains("--verify-import")||e.Args.Contains("--capture-dir")||e.Args.Contains("--verify-ui")) && dataArg<0){MessageBox.Show("验证模式必须通过 --data-dir 使用独立数据目录。");Shutdown();return;}
        var window = new MainWindow(dataDirectory); MainWindow = window; window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}

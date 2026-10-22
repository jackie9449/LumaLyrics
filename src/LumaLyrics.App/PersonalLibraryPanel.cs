using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;

internal sealed class PersonalLibraryPanel : StackPanel
{
    readonly string dataRoot;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,14,0,0)};
    readonly ProgressBar progress=new(){Height=3,IsIndeterminate=true,Visibility=Visibility.Collapsed,Margin=new(0,14,0,0)};
    readonly List<Button> actions=[];
    readonly Button cancel=new(){Content="取消当前操作",Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,10,0,0)};
    CancellationTokenSource? cancellation;
    internal bool IsBusy=>cancellation is not null;
    internal string DataRoot=>dataRoot;
    static string Documents=>Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    static string Timestamp=>DateTime.Now.ToString("yyyyMMdd-HHmmss");
    static TextBlock Copy(string text,double size=12)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,FontSize=size,Margin=new(0,0,0,8)};
    public PersonalLibraryPanel(string dataRoot)
    {
        this.dataRoot=dataRoot;
        Children.Add(new TextBlock{Text="个人资料",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
        var hint=Copy("歌词、中文译文、封面与校准记录独立保存。更新软件会继续使用这些资料；分发时只分享软件安装包。");ThemeManager.Bind(hint,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(hint);
        var location=new StackPanel();location.Children.Add(Copy("当前个人数据目录",11));
        var path=new TextBox{Text=dataRoot,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new(0),Background=System.Windows.Media.Brushes.Transparent,Padding=new(0),FontSize=12};
        AutomationProperties.SetName(path,"当前个人数据目录");location.Children.Add(path);
        var card=new Border{Child=location,Padding=new(16),CornerRadius=new(16),Margin=new(0,5,0,4)};ThemeManager.Bind(card,Border.BackgroundProperty,"RaisedBrush");Children.Add(card);
        ActionButton("打开个人数据目录",()=>{var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.ArgumentList.Add(dataRoot);Process.Start(start);});
        Children.Add(new TextBlock{Text="单独备份，安心更新。",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,18,0,8)});
        var detail=Copy("文件夹中可直接读取歌词与原始封面。个人备份 ZIP 还保留手动绑定、偏移和导入队列，支持离线恢复。");ThemeManager.Bind(detail,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(detail);
        var exportRow=Row();
        ActionButton("导出个人文件夹…",async()=>{
            var choose=new OpenFolderDialog{Title="选择个人文件夹的保存位置",FolderName=Documents};
            if(choose.ShowDialog(Window.GetWindow(this))==true)await ExportToAsync(Path.Combine(choose.FolderName,"浮光歌词个人资料-"+Timestamp));
        },exportRow);
        ActionButton("导出个人备份 ZIP…",async()=>{
            var choose=new SaveFileDialog{Title="单独保存个人备份",Filter="浮光歌词个人备份|*.lumalibrary.zip",FileName="浮光歌词个人资料-"+Timestamp+".lumalibrary.zip",InitialDirectory=Documents,OverwritePrompt=false};
            if(choose.ShowDialog(Window.GetWindow(this))==true)await ExportToAsync(choose.FileName,true);
        },exportRow);
        var restoreRow=Row();
        ActionButton("从文件夹恢复…",async()=>{
            var choose=new OpenFolderDialog{Title="选择个人备份文件夹",FolderName=Documents};if(choose.ShowDialog(Window.GetWindow(this))==true)await ChooseRestoreDestination(choose.FolderName);
        },restoreRow);
        ActionButton("从 ZIP 恢复…",async()=>{
            var choose=new OpenFileDialog{Title="选择个人备份 ZIP",Filter="浮光歌词个人备份|*.lumalibrary.zip;*.zip",InitialDirectory=Documents};if(choose.ShowDialog(Window.GetWindow(this))==true)await ChooseRestoreDestination(choose.FileName);
        },restoreRow);
        ActionButton("下次启动使用其他资料目录…",()=>{
            var choose=new OpenFolderDialog{Title="选择已恢复的个人数据目录",FolderName=Documents};if(choose.ShowDialog(Window.GetWindow(this))!=true)return;
            try{DataDirectoryConfiguration.SetDefault(choose.FolderName);status.Text="已记住新目录。请退出当前浮光歌词后重新打开；当前资料和任务保持不变。";}
            catch(Exception ex){status.Text="目录未切换："+ex.Message;}
        });
        var safety=Copy("恢复会创建一个新目录，保留原有资料。新增下载可随时再次导出。",11);ThemeManager.Bind(safety,TextBlock.ForegroundProperty,"MutedBrush");safety.Margin=new(0,12,0,0);Children.Add(safety);
        ThemeManager.Bind(progress,ProgressBar.ForegroundProperty,"AccentBrush");Children.Add(progress);Children.Add(status);Children.Add(cancel);cancel.Click+=(_,_)=>Cancel();
    }
    WrapPanel Row(){var row=new WrapPanel{Margin=new(0,2,0,0)};Children.Add(row);return row;}
    void ActionButton(string text,Action action,Panel? panel=null)
    {
        var button=new Button{Content=text,FontSize=12,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,10,0)};AutomationProperties.SetName(button,text);
        button.Click+=(_,_)=>{try{action();}catch(Exception ex){status.Text="操作未完成："+ex.Message;}};(panel??this).Children.Add(button);actions.Add(button);
    }
    async Task ChooseRestoreDestination(string source)
    {
        var choose=new OpenFolderDialog{Title="选择恢复资料的保存位置（会新建子目录）",FolderName=Documents};
        if(choose.ShowDialog(Window.GetWindow(this))==true)await RestoreFromAsync(source,Path.Combine(choose.FolderName,"浮光歌词数据-"+Timestamp));
    }
    internal Task<PersonalLibraryManifest?> ExportToAsync(string destination,bool zip=false)=>RunAsync((token,report)=>zip?PersonalLibraryArchive.ExportZip(dataRoot,destination,token,report):PersonalLibraryArchive.Export(dataRoot,destination,token,report),destination,"已导出个人资料");
    internal Task<PersonalLibraryManifest?> RestoreFromAsync(string source,string destination)=>RunAsync((token,report)=>PersonalLibraryArchive.Restore(source,destination,token,report),destination,"已恢复到新目录。可选择下次启动使用该目录");
    async Task<PersonalLibraryManifest?> RunAsync(Func<CancellationToken,IProgress<string>,PersonalLibraryManifest> operation,string path,string complete)
    {
        if(IsBusy)return null;
        cancellation=new();var token=cancellation.Token;foreach(var action in actions)action.IsEnabled=false;progress.Visibility=cancel.Visibility=Visibility.Visible;
        var report=new Progress<string>(message=>status.Text=message);
        try
        {
            var result=await Task.Run(()=>operation(token,report),token);var count=result.Summary;
            status.Text=$"{complete}。\n{path}\n{count.Tracks} 首曲目 · {count.LyricFiles} 个歌词文件 · {result.Files.Count(f=>f.Kind=="translation")} 个中文译文 · {count.CoverTracks} 首有封面 · {count.HighResolutionCoverTracks} 首高清";
            if(result.Warnings.Count>0)status.Text+=$"\n有 {result.Warnings.Count} 项待补齐，详见个人资料清单。";
            return result;
        }
        catch(OperationCanceledException){status.Text="已取消。原有资料未更改。";return null;}
        catch(Exception ex){status.Text="操作未完成："+ex.Message;return null;}
        finally{cancellation.Dispose();cancellation=null;foreach(var action in actions)action.IsEnabled=true;progress.Visibility=cancel.Visibility=Visibility.Collapsed;}
    }
    internal void Cancel()=>cancellation?.Cancel();
}

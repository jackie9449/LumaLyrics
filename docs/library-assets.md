# 资料库歌词与封面

## 需求与约束

读取用户本机 Apple Music 完整资料库，为每首歌预缓存可验证的动态歌词与高清封面。自动歌词必须同时满足歌名、歌手、录音版本、时长和内容完整性检查；缺失或存疑的结果留在待补齐队列，不生成时间戳，不把静态全文算作动态歌词。手动锁定的内容保留。

纯音乐只有在匹配的来源明确标记且没有相矛盾的歌词时才自动确认。播放纯音乐时，主窗口歌词区和悬浮歌词文本留空，切回有歌词的歌曲恢复正常。

封面优先使用 Apple Music `artwork.sqlite` 按曲目持久 ID 对应的地址与本地图片；同一专辑共享缓存。下载目标为 1200 像素，按实际解码头尺寸统计高清，低清回退不能算作高清。仅允许 Apple 图片域名，不访问账户凭据。Apple Music 的数据库和图片只读，浮光缓存放在用户数据目录。

应用主题仍只在设置中切换，悬浮歌词外观仍独立。个人资料库、歌词正文、封面和缓存报告不得提交 GitHub。

## 来源与可验证范围

- [Apple MusicKit Song](https://developer.apple.com/documentation/musickit/song)：公开属性有 `hasLyrics`，没有公开的歌词正文读取属性。不能据此声称第三方歌词已获 Apple 逐首核对。
- [LRCLIB](https://lrclib.net/docs)：使用服务返回的时间戳和纯音乐标记，保留来源和筛查结果。自动检查降低错误风险，不能证明每个字或每个时间点准确。
- [Apple Artwork](https://developer.apple.com/documentation/applemusicapi/artwork)：使用 Apple Music 本地索引中的图片地址及尺寸模板；不按歌名盲配封面。

未取得可靠结果时保留明确的缺失、待确认或网络错误状态。没有歌词不能推断为纯音乐。

## 导入与恢复

主程序启动后并行启动歌词与封面导入，二者各自持有独占锁，不会因另一项网络失败而停掉。播放查词优先，后台使用相同的歌词请求节流器。跨进程暂停也会取消正在等待的请求或退避，进度不丢失。

封面索引使用只读 SQLite 连接，支持 Windows 曲目记录的专辑关联，并要求被关联的封面来源确认为专辑类型。缓存仅写入浮光数据目录，文件原子替换，记录 SHA256 与实际尺寸；损坏的本地图片重新排队。高清统计要求宽高均达到 1000 像素，不通过放大低清源凑数。

开发者可以用随包 host 执行 Probe；以下命令只在本机保存结果：

```powershell
./runtime/dotnet.exe exec ./LumaLyrics.Probe.dll --asset-status "$env:LOCALAPPDATA/LumaLyrics"
./runtime/dotnet.exe exec ./LumaLyrics.Probe.dll --artwork "$env:LOCALAPPDATA/LumaLyrics" --retry
./runtime/dotnet.exe exec ./LumaLyrics.Probe.dll --prefetch "$env:LOCALAPPDATA/LumaLyrics" --drain --batch
```

`--drain` 处理剩余队列，遇到连续服务错误会退避后继续。缺失、待确认和网络错误依然分别保留，之后可在主程序继续或重试。请勿将进度文件、缓存数据库、下载的歌词正文或封面放进源代码提交。

## 本版验证

84 项单元测试与 94 项隔离 WPF 检查通过；覆盖真实 PNG 缓存解码、手动绑定保护、迁移备份与原始全文复查、冲突的纯音乐标记、专辑 ID 回退、损坏图片和网络等待期间的跨进程暂停。真实本机资料库的封面已完成全量导入，歌词批量处理仍会受来源覆盖和服务繁忙限制。界面截图使用原创文字和几何封面，不能用来宣称已经逐首验证 Apple Music 原生时间轴。

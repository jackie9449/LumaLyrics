namespace LumaLyrics.Infrastructure;

public static class LocalAgentTranslationPrompt
{
    public static string Create(string root,string taskId,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var task=LibraryTaskCatalog.Read(root,token).SingleOrDefault(t=>t.Id==taskId)??throw new InvalidDataException("所选任务已不存在，请刷新任务列表。");
        if(task.EntryPath is not { } entry||task.Jobs.Count==0)throw new InvalidDataException("任务说明或成员清单不可用，请重新生成任务后再复制。");
        var paused=task.Jobs.Where(j=>j.Receipt.State is TranslationJobState.Cancelled or TranslationJobState.Rejected or TranslationJobState.Conflict or TranslationJobState.Stale or TranslationJobState.PartialReady).ToArray();
        string restrictions=paused.Length==0?"本任务没有额外的暂停项。": "以下登记任务当前不应重新提交："+string.Join("；",paused.Select(j=>j.Ticket.Request.JobId+" ("+j.Receipt.State+")"))+"。这些项目需要用户先在应用中重新检查或确认，不能靠修改旧结果重新接收。";
        return $$"""
请使用本机文件读写能力，完成下面这份 LumaLyrics 中文歌词翻译任务。请实际保存结果文件，按说明连续处理，不要只在聊天中返回译文。

任务标识：{{task.Id}}
任务说明的绝对路径：
```text
{{entry}}
```

{{restrictions}}

执行要求：
1. 先完整读取上述 Markdown，再读取它指向的清单、分块和每首 request.json；以这些文件中的要求、路径和不可变原词为准。批量任务应自动继续到后续分块，不要求用户逐首确认。
2. 开始或续做前核对已有结果、应用回执与 agent-progress.json。只制作 AwaitingResult 项，已接收或上述暂停项不要重复翻译或覆盖；已最终提交但暂无回执的项目记为“已提交，待接收”，不要冒称应用已经采用。
3. 以专辑为理解与风格单位。先阅读每首说明中的 AlbumContext，了解同专辑的可用原词和已有译文，建立人物/称谓/指代、术语、反复意象与语体约定；同专辑跨分块沿用并复核。只选择一首时也必须参考同专辑，但只提交已登记任务，不翻写或覆盖未选歌曲。曲序、剧情、人物性别不能靠猜测；不要把不同歌曲的第一人称强行视为同一人。
4. 按 task.md 的“信、达、雅”流程初译和复核：忠实保留含义与歧义，使用自然中文，在不增删原意的前提下保留意象、节奏和张力。已有译文供一致性参考，不照抄错误。严格保留 JobId、请求哈希、SourceHash、GroupIndex 和 Original；不改动原词、时间戳或行组，不借用其他录音的时间轴。
5. 严格按每首 task.md 指定的 JSON 结构和绝对结果路径写文件。解析并复制 Result 骨架，只填写允许字段，保存临时文件并自检，再最终化为 translation-result.json；不要修改数据库、软件文件、登记清单或应用回执，也不要将结果写到另一个任务目录。
6. 无法可靠翻译的组按协议留空并填写具体 UntranslatedReason；不要编造内容或时间戳。DraftEngine 记录实际使用的模型，无法获知时如实说明；保留 AI 来源，不声称经过人工语义审核。
7. 每首完成后保存进度及按 AlbumKey 区分的专辑约定，单首失败应记录原因并继续其余项目；中断后复核专辑约定再续做。遇到原词/哈希/路径不一致时停止该首并说明，不擅自修补任务身份。AlbumContext 缺失的旧任务不扫描数据库或猜路径，说明限制，建议在应用重新生成任务。
8. 完成后报告完整提交、部分提交、跳过和失败的数量及结果位置。若不能访问本机文件，请明确说明能力限制，不要声称已落盘或已导入。

现在请完整阅读指定任务说明并开始执行。
""";
    }
}

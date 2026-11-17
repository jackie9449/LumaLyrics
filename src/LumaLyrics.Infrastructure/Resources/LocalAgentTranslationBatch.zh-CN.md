# 连续处理这批中文歌词

你需要本机文件读写能力。用户只交给你此入口，按下面绝对路径读取清单和逐首任务，不需要用户逐首发送文件。

1. 读取 batch.json 和每块清单，先按各首 request.json 中 AlbumContext.AlbumKey 汇总登记任务。以专辑为翻译工作单元：读一次该专辑参考、建立约定，再逐首制作本专辑已选歌曲，完成后进入下一专辑。专辑可以跨多个分块，默认每块 5 首只是文件分页，不是独立翻译语境。只处理登记的 JobId 和路径，不搜索其它目录，不改清单、request、任务 MD 或数据库；不要一次把整库歌词塞进上下文。没有专辑语境的旧任务单独标明限制。
2. 每首先查看最终结果和回执。回执已接收则跳过；已有最终文件但没有回执，记“已提交，待接收”并继续；拒绝、冲突、过期、需采用部分稿的记录原因并继续。禁止覆盖已有最终结果。
3. 没有最终结果时，完整读取该首 task.md，按“信达雅”流程翻译、自检、写临时结果再无覆盖原子改名。共享专辑人物/称谓、术语、反复意象与语体约定，参考其他歌曲的原词和已有译文；不借其他歌曲的时间轴，不挪用其他歌曲的句意，不代做未选歌曲。单首结果仍逐首提交，不等待全专辑结束才保存。
4. 每完成一首，原子更新 agent-progress.json：包含 BatchId、AlbumDecisions 和 Items。AlbumDecisions 以 AlbumKey 区分，记录 Terms、Characters、Images、Register、Uncertainties 及证据歌曲/组；Items 每项记录 JobId、State（Submitted/Applied/NeedsAttention/Failed）、UpdatedUtc、Message，部分提交需注明已译/必要组数。先写同目录临时文件再替换自己的进度文件；不写应用 receipt.json。进度只作提示，以实际最终文件和应用回执复核。
5. 自动继续下一首、下一块，不要求用户每块重复发指令。单首失败记原因后继续后面的歌曲。应用关闭也可继续制作，不等每首入库；应用下次启动统一接收。
6. 接近上下文或工具限额时保存进度及专辑约定再结束。用户再次提供本入口说“继续”时，读回进度、专辑约定、最终文件和回执，跳过已提交/已完成项，先完成当前专辑剩余歌曲。遗留临时文件不等于完成，须重新核对该首任务再提交。不能为统一风格覆盖已提交文件；发现前后冲突记录为需处理，交用户在应用重新生成。
7. 最后报告实际已提交、已接收、需处理、失败和剩余数量。未读取回执不能宣称已接收；不承诺语义已经人工校对。目录不可用或已迁移则停止说明，不猜新位置、不创建个人库。

进度结构示例（用实际 BatchId、AlbumKey、JobId 与时间替换示例值；不要把示例写入 translation-result.json）：

```json
{
  "BatchId": "实际批次标识",
  "AlbumDecisions": [{
    "AlbumKey": "复制当前 AlbumContext.AlbumKey",
    "Terms": [{"Original": "lantern", "Chinese": "灯火", "Evidence": "实际歌曲/GroupIndex"}],
    "Characters": [],
    "Images": [],
    "Register": "凝练的现代抒情中文，保留原有口语力度",
    "Uncertainties": ["角色性别无证据，保持中性；不认定不同歌曲的 I 为同一人"]
  }],
  "Items": [{"JobId": "实际任务标识", "State": "Submitted", "UpdatedUtc": "实际 UTC ISO 8601 时间", "Message": "已完整提交；尚未读到应用回执"}]
}
```

receipt.json 的 State：Applied=已完整接收；AppliedPartial=已接收部分稿，需注明缺行；PartialReady=待用户采用，不再提交；Rejected/Conflict/Stale/Cancelled=停止该项并记录需处理。已有最终文件无回执=已提交待接收。没有最终文件且没有终态回执才可开始；仍应遵守复制入口提供的暂停项。磁盘回执可能滞后，进度文件不能授权覆盖；最终由应用检查当前状态。

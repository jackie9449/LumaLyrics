# Argos 离线基础机翻

为没有 agent 的用户提供英、法、德、西班牙、日、韩语原词的基础中文译文。已有来源、agent 或手工译文优先。英语直接译中，其他五语按原来的歌词组先译成英语，再译成中文；中转路径保存在来源记录中。这是基础机翻，尤其不代替概念专辑的人物、代词、叙事与文学表达校对。

## 使用

1. 打开「设置 → 资料库与歌词 → 总览」，选择「原词语言」。单一语种可直接指定，歌曲之间语种不同的批量任务可用「自动识别」。选择即时保存，独立于软件界面语言和歌词显示字体。
2. 点击「准备离线组件」，联网下载所选语言需要的固定组件。自动识别选择会准备全部六条路径；指定一种语言只准备该路径。已安装英文基础环境和校验通过的下载会复用，无需安装系统 Python、agent、API Key、系统运行库或付费服务。Windows x64、CPU 推理。下载可暂停并重新准备；完整组件还需要解压空间。
3. 当前歌曲有可靠动态原词时，点击「翻译当前歌曲」。也可在「歌曲」页多选，通过「更多操作… → 生成基础中文机翻」批量处理。可先用缺中文筛选、查找现有来源译文；机翻按钮本身不联网查词。
4. 总览显示完成、跳过、失败和待处理数量，支持暂停与继续。退出会取消当前工作，已完成结果保留；启动不擅自续做。「继续」沿用各歌曲入队时的语言，改变上方选项只影响新选择的任务。识别不准/失败时，指定正确语言并重新选择对应歌曲生成；明确重新选择会更新该首待办的语言参数。
5. 开启共用「中文译文」开关查看结果；主窗口、悬浮工具栏和设置同步。制作机翻不会擅自打开显示开关。

| 原词选择 | 实际路径 | 完整组件约 MB | 英文基础已备好时新增模型约 MB |
|---|---|---:|---:|
| 英文 | en→zh | 118.2 | 0 |
| 法文 | fr→en→zh | 184.8 | 66.6 |
| 德文 | de→en→zh | 268.7 | 150.5 |
| 西班牙文 | es→en→zh | 403.4 | 285.2 |
| 日文 | ja→en→zh | 235.3 | 117.2 |
| 韩文 | ko→en→zh | 237.0 | 118.9 |
| 自动识别 | 上述六条 | 856.5 | 738.3 |

自动识别结合本机 Windows ELS 与日文假名/韩文字形信号。短句、同形词、纯汉字日文和单首混合多种语言仍有歧义；无法判断或检测到未支持的语言时明确失败，不偷偷当英文翻译。手动指定可以纠正短句，不能绕过明显不兼容的文字脚本。单首以一种原词语言处理；“混合语种批量”指批次内不同歌曲可有不同语言，不承诺一首歌逐句自动切换六种语言。

机翻只填补完全缺少译文的歌曲，已有部分译文同样保留。原词、时间戳、空行、偏移、原词 SHA-256 不变，按原来组编号保存；日文含假名上下文中的汉字行仍参与翻译。原词变化后需针对新原词重新制作，不借用其他录音的时间轴。没有可靠动态原词的选中歌曲跳过。

之后可查找来源译文或生成本地 agent 任务。非手工的六种机翻路径均可升级为来源/agent 译文，旧版保留历史；不自动覆盖手工译文。在机翻之前导出的 agent 任务也能升级后来生成的机翻，部分 agent 稿需明确采用后才替换完整机翻。手工导入、来源/AI 更新或原词/资料库来源变化发生在机翻执行期间时，迟到结果不会覆盖它们。

## 存储、离线与迁移

- 工具目录：`%LOCALAPPDATA%\LumaLyricsTools\argos`。保留原英文基础环境路径，额外模型按各自 SHA-256 存在 `source-models` 中，互不复制整套 Python/英文模型。组件下载档案在 `downloads`，中断/重试复用完整且散列一致的文件。模型损坏时，停止任务后点击准备会重新校验下载并重建所选路径。
- 准备是明确联网操作，独立于「联网查词」。完成后推理不下载、不调用云端接口，不上传歌词。worker 在导入模型前禁用 socket 连接、DNS 和发送入口；中间英语仅在子进程内存中使用，不另存歌词/日志。单个 worker 最多持有英→中与当前原词→英两套模型，换语言释放旧源模型；完成/取消释放进程。CPU int8、两个计算线程、低于正常优先级。
- 当前个人目录的 `translations.db`、`offline-translation-queue.json` 保存译文、来源和任务。每首队列存入队语言（旧队列缺少字段按自动识别读取）；保留 Draft/Review 真实属性，隐藏校对标志不等于人工已审核。五个中转方向的 SourceUrl 为 `local:argos/{fr,de,es,ja,ko}-en-zh`，原英文 `local:argos/en-zh` 继续识别。
- 个人备份包含译文、历史、语言设置和可读队列，模型/运行环境不进入个人备份。另一台电脑恢复已存机翻可直接显示，制作新机翻需在那台机器准备所需组件。数据库快照与原子队列若完成时刻不同，续做会核对现有译文并安全跳过。
- 软件 ZIP 和 Git 不包含 Python 下载包、模型、私人歌词、译文、任务或数据库。清理工具时完全退出程序后删除上述 `LumaLyricsTools\argos` 目录即可；个人译文仍保留，再生成需重新准备。英文老用户可选择英文继续复用原环境，无需为了已存译文下载其他五个模型。

单首超时两分钟，当前首保持待处理。任一步输入超过 150 个模型 token 不截断、不写部分机翻；异常输出/组数、无中文、模型未准备同样拒绝写入。基础模型对专名、短句和叙述可能生硬，英语中转会带来额外歧义，可换用 agent 或手动导入。

## 固定组件、维护与许可

`src/LumaLyrics.Infrastructure/Resources/argos-engine-lock.json` 继续固定原英文环境，`argos-model-lock.json` 固定五个新增官方 HTTPS 地址、字节数和 SHA-256。下载先校验再安全解压，分别在临时目录完成后切换；失败不会让半个模型变为可用，已备好的其他方向保留。准备最后使用固定原创句子检验所请求的模型，不使用个人歌词。

使用 [Argos Translate 1.11.0](https://github.com/argosopentech/argos-translate) 的 Package/tokenizer（MIT / CC0，采用 MIT）、[CTranslate2 4.6.0](https://github.com/OpenNMT/CTranslate2)（MIT）、[SentencePiece](https://github.com/google/sentencepiece)（Apache-2.0）、[Python 3.12.10 embedded](https://www.python.org/downloads/release/python-31210/)（PSF）和固定辅助 wheels。已有确切分组，因此不运行高层自动分句的 Stanza/Torch 链，不从网络补资源。保留原包许可/credits；使用 NumPy 校验 wheel 中微软签名、未修改的 MSVCP140 本地副本及 embedded Python 的 VCRUNTIME，不安装系统运行库。

模型来自 [Argos 官方索引](https://github.com/argosopentech/argospm-index)。法→英 1.9、德→英 1.3、西→英 1.9、日→英 1.1、韩→英 1.1；准确字节数/散列见模型锁文件。英→中、法→英、西→英包的 README 标注原始 OPUS 模型 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)，署名 Jörg Tiedemann、Santhosh Thottingal，*OPUS-MT — Building open translation services for the World*，EAMT 2020，Lisbon。德文包仅有版本说明，不替它推断许可；日/韩包保留其 OPUS、Wiktionary/Wiktextract、Stanza、CCAligned、WikiMatrix、OpenSubtitles 等原始数据/模型 credits 与链接，不将所有不同来源概括成同一种许可。模型由用户从上游主动下载，软件不携带权重，推理未重新训练；输出是机器生成。模型说明不授予歌词本身的版权。

维护复现：Windows CPython 3.12 使用 `pip download --only-binary=:all: --no-deps -r scripts/argos/requirements-minimal.txt -d .tools/argos-wheels`；将官方 embedded ZIP 与六个模型档案放到 `.tools/argos-downloads`，运行 `python scripts/argos/refresh-lock.py`。脚本向 PyPI 校验 wheel 散列，并核对本地模型方向、计算固定散列，保持基础锁与源模型锁分离。修改版本后需再次验证接口/许可、真实离线原创夹具和 UI；不要自动安装上游完整高层依赖。所有下载和测试个人目录保持在软件/Git 之外。

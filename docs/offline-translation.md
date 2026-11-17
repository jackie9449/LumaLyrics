# Argos 离线基础机翻

此功能为没有 agent 的用户提供基础英文→中文译文。已有来源、agent 或手工译文优先；机翻不承担概念专辑的人物、叙事和文学表达校对。

## 使用

1. 打开「设置 → 资料库与歌词 → 总览」，点击「准备离线组件」。第一次需要联网，固定组件合计约 118 MB；可暂停，重试会复用校验通过的下载。无需安装 Python、配置 agent、API Key 或付费服务。Windows x64，CPU 推理。
2. 当前歌曲已有可靠动态原词时，点击「翻译当前歌曲」。未播放也可在「歌曲」页多选歌曲，通过「更多操作… → 生成英译中基础机翻」批量处理。可先用「缺中文译文」筛选，并先查找现有来源译文；生成机翻的按钮本身不联网查词。
3. 总览显示完成、跳过、失败和待处理数量；暂停后点击「继续离线翻译」。退出程序会取消当前工作，已完成结果保留；再次启动不会擅自继续。失败歌曲需重新选择生成。没有可靠动态原词的选中歌曲会跳过。当前版本仅处理英文，检测出的其他语言或模型过长分组会失败；短句语言识别仍可能有歧义。
4. 开启共用「中文译文」开关查看结果。主窗口、悬浮工具栏与设置同步；程序不会为了生成机翻擅自打开开关。

机翻只填补完全缺少译文的歌曲，已有部分译文同样保留。原词、时间戳、空行、偏移与原词 SHA-256 不变，译文按原来的组编号保存。英文和原有中文混合时，中文行不重复生成。改变原词后，需要针对新原词重新制作译文，不借用其他录音的时间轴。

之后可照常查找来源译文或生成本地 agent 任务。非手工机翻可以升级为来源/agent 译文，旧版保留在历史中；不自动覆盖手工译文。在机翻生成前就已导出的 agent 任务也能升级机翻。部分 agent 译稿需明确采用后才替换完整机翻。升级、手工导入和原词修改发生在机翻执行期间时，迟到的机翻不会覆盖它们。

## 存储、联网与迁移

- 运行环境和模型：`%LOCALAPPDATA%\LumaLyricsTools\argos`，按固定清单版本和散列分目录存放。此处不存歌词译文。准备组件是明确的联网操作，独立于「联网查词」；准备完毕后，翻译过程不需要联网。Python worker 在导入模型之前禁用 socket 连接、DNS 和发送入口；不依赖云端接口，也不把歌词写入工具日志。
- 个人资料：当前个人目录的 `translations.db` 和可读 `offline-translation-queue.json`。结果保留 Argos 来源、基础机翻和未人工校对属性；隐藏待校对标志不会把它变成人工已审核结果。
- 个人备份包含译文、历史及队列，采用数据库快照与原子队列文件；恢复到新资料目录后，如队列与数据库的完成时刻有差异，继续时会核对现有译文并安全跳过。恢复需要支持此功能的新版本程序。
- 软件 ZIP 和 Git 不包含模型、Python 下载包、个人原词、译文、任务或数据库。另一台电脑恢复个人备份后，已保存机翻可直接显示；要制作新机翻需在那台机器重新「准备离线组件」。
- 如组件损坏，停止任务后再次准备会重新校验下载并重建环境。要清理占用空间，完全退出程序后删除上述 `LumaLyricsTools\argos` 目录；这不会删除个人译文。重新生成时需要再次下载。

推理子进程按需启动，CPU int8、两个计算线程，低于正常优先级；结束/取消释放模型与进程。单首超时两分钟，当前首保持待处理。长歌词组超过 150 个模型输入 token 时不截断、不写部分机翻，以免丢失内容。输出无中文或组数异常也拒绝写入。效果属于基础机翻，专名和短句可能失败，用户可改用 agent 或手动导入。

## 开发与固定组件

程序内嵌公开的 `src/LumaLyrics.Infrastructure/Resources/argos-engine-lock.json`：每个官方 HTTPS 地址、字节数和 SHA-256 均固定。下载后先校验，再安全解压到临时目录，全部完成后切换为可用环境。为 CTranslate2/SentencePiece 提供 NumPy 已校验 wheel 内微软签名的 MSVCP140 DLL 的未修改本地副本；VCRUNTIME 使用 Python embedded 随附副本，保留原包及许可，不安装系统运行库。准备完成后还使用固定原创句子检验模型启动；不运行 pip，不修改系统 Python。

采用 Argos Translate 1.11.0 的 `Package`/tokenizer 和它使用的 CTranslate2 4.6.0 CPU 后端。已有动态歌词提供确切分组，因此不加载上游高层 `argostranslate.translate` 的自动分句链，也不下载 Stanza/Torch。此适配不是完整通用 Argos Python 环境，不能直接用 pip 自动补齐上游所有依赖；更改 Argos 版本应重新验证包接口、模型、Unicode、逐组映射和离线行为。

开发者复现下载清单时，使用 Windows CPython 3.12 的 `pip download --only-binary=:all: --no-deps -r scripts/argos/requirements-minimal.txt -d .tools/argos-wheels`；将 Python embedded ZIP 和官方模型放到 `.tools/argos-downloads`，再执行 `python scripts/argos/refresh-lock.py`。该脚本向官方 PyPI 核对 wheel 哈希，Python/model 则从已人工核对来源的本地档案计算固定散列。仅更新锁文件不等于已经完成接口/许可验收，必须再跑真实离线原创夹具和 UI 回归。依赖缓存不提交。

## 来源与许可

- [Argos Translate](https://github.com/argosopentech/argos-translate)：MIT / CC0 双许可。本适配使用 MIT；下载 wheel 中保留原许可。
- [CTranslate2](https://github.com/OpenNMT/CTranslate2)：MIT；[SentencePiece](https://github.com/google/sentencepiece)：Apache-2.0；[CPython embedded](https://www.python.org/downloads/release/python-31210/)：PSF 许可，原许可随下载保留。
- [Argos 官方模型索引](https://github.com/argosopentech/argospm-index)：直接英→中 `translate-en_zh-1_9.argosmodel`，70,743,021 字节，SHA-256 `433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab`。
- 模型包 README 标明其原始 OPUS 模型为 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)。署名：Jörg Tiedemann、Santhosh Thottingal，*OPUS-MT — Building open translation services for the World*，EAMT 2020，Lisbon。程序使用原始模型包进行 CPU int8 推理，未重新训练或分发修改后的权重；翻译为机器生成。许可/署名不授予歌词本身的版权。
- setuptools、packaging、NumPy、PyYAML、sacremoses、regex、click、joblib、tqdm、cloudpickle、colorama 的具体版本见最小 requirements 与锁文件；完整 wheel 的 `.dist-info`、许可证及 notices 安装时保留。模型和运行环境为用户主动从上游下载，不随软件 ZIP 携带。

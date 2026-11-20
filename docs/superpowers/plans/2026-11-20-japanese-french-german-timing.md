# 日语、法语和德语离线打轴

**目标** 在「制作动态歌词」支持日、法、德语原词，使用实际歌声校验覆盖率、句首、句尾和间奏。

**实现** 法语、德语复用 W2V-BERT 歌声模型，按词表处理变音字母、连字符与省音撇号。真实唱段显示日语需要独立的歌声模型，采用 NextFire 的日语 MMS 与 Sudachi 读音。复用现有采样、逐行预检和草稿编辑，原词和音频始终留在本机。

**依据** [离线打轴规格](../specs/2026-11-19-offline-lyric-timing.md) [W2V-BERT 歌声模型](https://huggingface.co/mrfakename/w2v-bert-2.0-music-lyrics-ctc) [日语歌声模型](https://huggingface.co/NextFire/mms-300m-ForcedAligner-karaoke-ja-Latn) [GTSinger 带标注歌声](https://huggingface.co/datasets/AaronZ345/GTSinger)

## 词表与真实人声基线

- [x] 用已安装的 `lyric-timing-worker.py` 编码三种语言的原词。对三语标注唱段和一首日语完整混音走正式链路。重复句及间隔留在原顺序里，错误日语原词对照未产生时间建议。
- [x] 在 `src/LumaLyrics.Infrastructure/Resources/lyric-timing-worker.py` 让预检和推理共享编码函数。法、德语保留声学词表能识别的重音、连字符分词和字母。日语读音用于声学比对，显示和保存保留原来的文字。

## 接入与验证

- [x] 在 `OfflineLyricTiming.cs` 统一向覆盖检查和推理传入语言，保留已安装的 W2V-BERT 权重。日语模型与词典固定官方下载地址、大小和 SHA-256，使用独立的续传目录。
- [x] 在 `LyricTimingWindow` 接入模型选择及四语文案，覆盖不足继续在读入音频前提示。草稿保存时核验原绑定，并记录实际使用的模型与语种。
- [x] 完成三语真音频、旧模型兼容、取消、逐行校对、打包版 UI 和隐私验证。量化结果见[本地开发记录](../../development-log.md)。

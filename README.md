# Pinyin Gloss

微软拼音旁的多语言翻译外挂。微软拼音继续负责拼音、中文候选、排序、用户记忆和上屏；Pinyin Gloss 只观察可见候选，在独立窗口中并排显示释义。

**当前为 0.1 实验开发版。** 已实现可构建工程、核心测试与模拟闭环；微软拼音候选的 UI Automation 暴露情况、Windows 焦点/DPI 和 LMT Q8_0 质量/性能仍待现场验证。演示成功不代表真实候选读取成功。不会安装或注册新输入法。

| 能力 | 首版状态 |
| --- | --- |
| UIA 独立读取进程、只读 WinEvent 通知、进程看门狗 | 已实现，Windows 兼容性待测 |
| 中文设置、系统托盘、关闭到托盘、登录启动 | 已实现，交互待 Windows 验收 |
| 非激活、点击穿透翻译窗、候选中文/编号对应 | 已实现，焦点/多屏 DPI 待测 |
| 多来源、多语言、各列颜色、列排序 | 已实现 |
| SQLite 词典、UTF-8 TSV 导入、同语言兜底 | 已实现；随包仅 15 条原创中英示例 |
| LMT-60-1.7B Q8_0 / llama.cpp 托管 | 接口已实现，需自行准备权重，尚未实测推理 |
| 其他本机模型、HTTPS 在线兼容模型 | 内置适配器；高级 JSON 配置，在线默认关闭 |
| 每来源 / 每列提示词、样例测试、完整模板 token 计数 | 已实现；token 计数适用于 llama.cpp |
| OCR、外部插件进程、磁盘缓存、应用启停名单 | 后续扩展，尚未实现 |

## 快速开始

环境：Windows x64，普通桌面会话。开发需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。自包含发布包不需要预装 .NET。当前不承诺安全桌面、管理员应用或任何尚未验证的 Windows build。

```powershell
git clone https://github.com/Ancho5515/pinyin-gloss.git
cd pinyin-gloss
./scripts/Publish.ps1
./artifacts/pinyin-gloss-win-x64/PinyinGloss.Desktop.exe
```

若 PowerShell 策略阻止脚本，可使用你通常的本地脚本执行方式；无需更改全机策略。`Publish.ps1` 先运行测试，再分别发布主程序与读取器。CI 同样生成 `pinyin-gloss-win-x64.zip` 工件。

1. 打开“概览”，运行真实候选诊断；切换微软拼音，在记事本键入拼音并保持候选窗可见。
2. 若诊断没有识别候选，查看 [Windows 验收清单](docs/windows-validation.md)。读取器仅接受受保护的微软输入宿主和明确标识的候选容器；不会扩展到应用正文。
3. 选择“模拟候选演示”，检查示例词典、窗口和颜色。默认 LMT 未启用，英文模型列会显示词典兜底。
4. 退出演示后继续真实读取。关闭设置窗口默认留在托盘；显式退出会清理托管子进程。

诊断程序独立运行，默认不输出候选原文：

```powershell
./artifacts/pinyin-gloss-win-x64/reader/PinyinGloss.CandidateReader.exe --diagnose --seconds 60
# 只有需要检查中文对应关系时才显式输出文本：
./artifacts/pinyin-gloss-win-x64/reader/PinyinGloss.CandidateReader.exe --diagnose --include-text --seconds 60
```

提交诊断时注明 Windows build、应用、微软拼音设置、是否找到候选容器、顺序/关闭行为。原文诊断可能包含你输入的词，请先检查再公开。

## 离线模型

默认适配 [NiuTrans/LMT-60-1.7B](https://huggingface.co/NiuTrans/LMT-60-1.7B) 翻译检查点，使用 **Q8_0 8 位权重量化**，激活/KV 保持 FP16。不是 W8A8。模型权重没有包含在仓库或程序包中，也不会自动下载、降量化或转在线服务。

1. 用 [运行时准备脚本](scripts/Get-LlamaRuntime.ps1) 获取固定的 llama.cpp `b11077`（带官方工件 SHA256 校验），或自行准备该版本 `llama-server.exe`。
2. 按 [模型准备说明](docs/model-setup.md) 固定官方模型 revision，转换成 GGUF Q8_0，记录 SHA256 与许可。不要使用 Base 或其他大小的检查点。
3. 在“离线模型”填入绝对路径、revision / hash；选择 GPU 或 CPU，启用来源并保存。
4. 点击“加载已保存模型”。加载使用独立期限，词典可继续工作。没有模型文件时可以一直使用词典。

默认单模型、单生成槽、512 token 总上下文、448 token 输入预算、32 token 输出、temperature=0。同一实例服务所有语言。取消时 HTTP 没有硬取消确认，托管 Worker 在取消宽限后释放，下次再加载；这优先保证残留任务不会累积，但频繁变化可能增加冷启动，需要实机调参。

外挂通过 `/apply-template` 获取真实 chat template 后调用 `/tokenize` 检查预算，再使用流式 `/completion`；不会在窗口显示未经校验的 token。通用在线兼容接口没有可信的厂商模板计数，依赖服务端 token 限制，不能把字符数当作实际 token 数。

## 多语言与并排来源

“语言 / 提示词”中，每一列是 `来源 ID + 目标语言`。例如：

| 列 ID | 来源 ID | 语言 | 同语言兜底 | 颜色 |
| --- | --- | --- | --- | --- |
| lmt-en | lmt | en | dictionary | 蓝色 |
| lmt-ja | lmt | ja | 留空，或导入日文词典 ID | 蓝色 |
| dictionary-en | dictionary | en | 留空 | 绿色 |

添加 `lmt-ja` 即可并排显示英文与日文；没有日文词典时不会把英文填进日文列。默认模型语言列表只是初始启用方向，不宣称已验证所有方向。导入词典后按其 manifest 中的真实语言配置列。

提示词支持三种变量：`{{candidate}}`、`{{source_language_name}}`、`{{target_language_name}}`。每列可覆盖来源模板；留空则继承。默认逐词生成短释义：

```text
Translate this {{source_language_name}} word or short phrase into {{target_language_name}}.
{{source_language_name}}: {{candidate}}
{{target_language_name}}:
```

“响应 / 资源”可设置候选稳定阈值、快照期限、无进展时长、读取看门狗、取消宽限、词典查询期限、模型加载和空闲释放。快照期限包含排队时间。新增快照、隐藏、切换应用和改配置会使旧结果失效。

## 扩展词典与来源

词典 TSV 最少三列：`中文词头<Tab>目标语言代码<Tab>释义`，可追加词性与 `|` 分隔别名。使用 UTF-8；`#` 开头为注释。词性目前只用于兼容导入格式，尚不展示。参考 [demo.tsv](dictionaries/demo.tsv)。在“本地词典”填写 ID、许可与出处后导入，保存再在列中引用。

来源可在“高级配置”编辑；先“校验 JSON 并载入表单”，再“保存并应用”。完整例子见 [providers.md](docs/providers.md)。接口位于 `ITranslationProvider`，增加实现和注册不需要修改候选读取器或翻译窗。候选读取适配器与翻译来源是独立扩展点。

配置路径：`%LOCALAPPDATA%\PinyinGloss\settings.json`；词典同目录下 `dictionaries`。配置写入先校验再原子替换。密钥配置仅保存环境变量名；在线来源必须显式启用，离线失败不会自动联网。默认只有有界内存 LRU，没有应用正文、剪贴板、按键历史、微软用户词库访问或候选原文日志。

## 开发

```powershell
dotnet test tests/PinyinGloss.Tests -c Release
dotnet build PinyinGloss.sln -c Release
./scripts/Publish.ps1
```

Linux 可运行核心与适配器测试，也可加 `-p:EnableWindowsTargeting=true` 交叉编译；交叉编译不能检验 Windows 交互。工程划分：

- `PinyinGloss.Core`：快照、协议、配置、提示词、缓存、取消和调度。
- `PinyinGloss.Providers`：SQLite、本机/在线 HTTP、托管 llama.cpp 生命周期。
- `PinyinGloss.CandidateReader`：Windows MTA/UIA 读取、WinEvent、只读诊断。
- `PinyinGloss.Desktop`：WPF 设置、托盘、非激活翻译窗、读取器监控。
- `PinyinGloss.Tests`：身份/语言隔离、时间预算、取消、缓存、词典原子导入与模型接口测试。

[架构设计](docs/architecture.md) · [实现状态与验收](docs/windows-validation.md) · [贡献指南](CONTRIBUTING.md) · [安全问题](SECURITY.md)

## 许可证

本项目代码和原创示例词条采用 [MIT](LICENSE)。[第三方许可](THIRD_PARTY_NOTICES.md) 单独列明 .NET、SQLite 和可选 llama.cpp；模型和用户导入词典保留各自许可，项目 MIT 不覆盖它们。模型卡当前标注 Apache-2.0，准备和分发具体权重时应保留对应 revision 的通知。项目与微软无官方隶属关系。

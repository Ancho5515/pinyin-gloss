# Pinyin Gloss

微软拼音旁的多语言翻译外挂。微软拼音继续负责拼音、中文候选、排序、用户记忆和上屏；Pinyin Gloss 只观察可见候选，在独立窗口中并排显示释义。

当前为 0.1 实验开发版。此前版本已在本机普通权限 Notepad3 打通真实微软拼音候选与词典/模型释义，在微软记事本验证当前句和选区翻译。最新正文修复已通过 242 项后台回归；Edge 本机专用网页的普通输入框、网页编辑区末尾输入与退格、句子和选区、多行及只读段落选区已实测。Codex 聊天选区的深层接口查找及空对象崩溃已修复，正式程序和压缩包已更新；新版生产链路读到聊天选区，用户现场确认翻译正常。无活动选区时收回圆圈的修复已构建，待更新程序并复测。本机企业微信自绘输入区尚无可用的直接文本接口。原站、Notepad3 新正文路径及真实 IME 回归仍待验收。右下角正文框可拖动、缩放，并与候选框共用主题；跨屏和完整主题切换仍需分别验收。不会安装或注册新输入法。

| 能力 | 首版状态 |
| --- | --- |
| UIA 独立读取进程、只读 WinEvent 通知、进程看门狗 | 已实现，本机 Notepad3 真实输入通过 |
| 中文设置、系统托盘、关闭到托盘、登录启动 | 已实现，交互待 Windows 验收 |
| 非激活、点击穿透翻译窗、候选中文/编号对应 | 本机键盘输入与焦点通过；鼠标/DPI 待测 |
| 更新保留旧页、跟随 Windows 应用深浅色 | 已实现，离屏回归通过，现场显示待复测 |
| 多来源、多语言、各列颜色、列排序 | 已实现 |
| SQLite 词典、UTF-8 TSV 导入、同语言兜底 | 已实现；随包仅 15 条原创中英示例 |
| LMT-60-1.7B Q4_K_M / llama.cpp 托管 | 默认 Q4，GPU 优先；Q8/Q6/Q5 可明确选择 |
| 右下角句子 / 选区翻译 | 两个独立开关，默认关闭；需要本地模型 |
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

若 PowerShell 策略阻止脚本，可使用你通常的本地脚本执行方式；无需更改全机策略。`Publish.ps1` 先运行测试，再分别发布主程序与读取器。CI 只执行构建和测试，不生成安装包。只有明确需要打包时才手动运行 `release.yml`（Actions → Release → Run workflow），或在本地执行 `Publish.ps1`；推送代码和版本标签不会自动打包。

1. 打开“概览”，运行真实候选诊断；切换微软拼音，在记事本键入拼音并保持候选窗可见。
2. 若诊断没有识别候选，查看 [Windows 验收清单](docs/windows-validation.md)。候选读取只接受受保护的微软输入宿主和明确标识的候选容器。
3. 选择“模拟候选演示”，检查示例词典、窗口和颜色。默认 LMT 未启用，英文模型列会显示词典兜底。
4. 退出演示后继续真实读取。关闭设置窗口默认留在托盘；显式退出会清理托管子进程。

当前优先验证本地词典，不需要准备模型。可从 MDBG 官方下载 CC-CEDICT，转换后导入；准备命令、许可和实测结果见 [本地词典测试文档](docs/local-dictionary-testing.md)。完整词库不随程序包分发。

诊断程序独立运行，默认不输出候选原文：

```powershell
./artifacts/pinyin-gloss-win-x64/reader/PinyinGloss.CandidateReader.exe --diagnose --seconds 60
# 只有需要检查中文对应关系时才显式输出文本：
./artifacts/pinyin-gloss-win-x64/reader/PinyinGloss.CandidateReader.exe --diagnose --include-text --seconds 60
```

提交诊断时注明 Windows build、应用、微软拼音设置、是否找到候选容器、顺序/关闭行为。原文诊断可能包含你输入的词，请先检查再公开。

## 离线模型

默认适配 [NiuTrans/LMT-60-1.7B](https://huggingface.co/NiuTrans/LMT-60-1.7B) 翻译检查点，使用 Q4_K_M 权重量化、FP16 KV。程序交付是含主 exe、reader 与依赖文件的解压包，不是单文件 exe。权重不包含在程序包内。

1. 解压后运行主 exe 同级的 `Setup-LLM.cmd`；直接回车选 Q4_K_M（1.28 GB），也可选 Q8_0、Q6_K 或 Q5_K_M。在同级 `LLM/` 下载所选社区 GGUF 与固定版 llama.cpp CUDA、CPU 两套运行时，校验 SHA256。启动优先 GPU，初始化失败再回退 CPU，设置页显示实际设备与原因。
2. 完成后重启主程序。全新用户自动配置并预加载，已有手工配置或禁用状态会保留。
3. 自定义目录用 `./scripts/Setup-Llm.ps1 -ModelDirectory 'D:\模型\LMT'`；量化可加 `-Quantization Q6_K`，仅用 CPU 可加 `-Backend Cpu`。当前自动 GPU 后端为 NVIDIA CUDA；其他 GPU 尚未接入。较小量化的译文质量需单独验证。详细来源、许可、恢复下载及后台验收见[模型准备说明](docs/model-setup.md)。

默认单模型、单生成槽、512 token 总上下文、448 token 输入预算、32 token 输出、temperature=0。同一实例服务所有语言。取消后确认后端槽位空闲则保留模型，无法确认才释放 owned worker；分词前取消不重载。下载只在显式运行准备脚本时发生，运行中不自动转在线服务或降低量化。

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

“翻译窗 / 后台”默认勾选“跟随 Windows 应用主题”；关闭后可手动选择深色。高对比度使用系统颜色，列颜色对比度不足时回退为可读文字色。输入更新期间保留上一页，词典快查后用结果和模型等待状态组成新页并整体替换，模型译文随后原位补齐；选词、Esc 或切应用仍隐藏。

## 句子与选区翻译

准备并启用本地模型后，在“翻译窗 / 后台”分别开启句子与选区翻译。有效选区优先；没有选区时翻译活动编辑光标所在句。框默认固定在屏幕右下角，拖动标题移动、拖动边缘缩放，释放后保存位置与尺寸。没有活动目标时清空并收成圆圈，关闭两个开关后隐藏。

本机企业微信 5.0.12.6018 的聊天编辑区目前不支持：该控件未提供可用的直接文本接口，通用设置中也未发现可开启的无障碍选项。显示“当前控件不支持正文读取”表示读取被拒绝，不表示翻译已完成。

这两项功能通过统一 TextReader 读取当前活动目标中的获准范围；UIA 借用父文档时必须限定到当前子控件，caret/selection 越界立即拒绝。IA2、Scintilla 兼容路径已接入，具体应用支持仍待分别实测。密码框、不支持控件和失效目标拒绝读取，不访问剪贴板；正文仅送给本地模型，不进入候选缓存或默认日志。局部采样最多 514 个 UTF-16 字符，最终当前句或完整选区最多 256 字符，再按模型实际分词检查预算；超限、接口缺失和读取故障明确显示原因。兼容范围、复现步骤和尚未完成的验收见 [正文翻译测试](docs/text-translation-testing.md)。Desktop 与 reader 的 IPC 已升级为 3，须整包更新。

## 扩展词典与来源

词典 TSV 最少三列：`中文词头<Tab>目标语言代码<Tab>释义`，可追加词性与 `|` 分隔别名。使用 UTF-8；`#` 开头为注释。词性目前只用于兼容导入格式，尚不展示。参考 [demo.tsv](dictionaries/demo.tsv)。在“本地词典”填写 ID、许可与出处后导入，保存再在列中引用。

来源可在“高级配置”编辑；先“校验 JSON 并载入表单”，再“保存并应用”。完整例子见 [providers.md](docs/providers.md)。接口位于 `ITranslationProvider`，增加实现和注册不需要修改候选读取器或翻译窗。候选读取适配器与翻译来源是独立扩展点。

配置路径：`%LOCALAPPDATA%\PinyinGloss\settings.json`；词典同目录下 `dictionaries`。配置写入先校验再原子替换。密钥配置仅保存环境变量名；在线来源必须显式启用，离线失败不会自动联网。正文功能默认关闭；候选缓存只在有界内存中，不访问剪贴板、按键历史或微软用户词库，默认日志不含输入原文。

## 开发

```powershell
dotnet test tests/PinyinGloss.Tests -c Release
dotnet build PinyinGloss.sln -c Release
./scripts/Publish.ps1
```

正在使用桌面时，后台测试用 `dotnet test PinyinGloss.sln -c Release --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'`，后台打包用 `./scripts/Publish.ps1 -BackgroundOnly`。排除的窗口/桌面采集测试需另行验收，默认发布仍运行完整 Windows 测试。主程序与 reader 使用同一构建配套文件。

Linux 可运行核心与适配器测试，也可加 `-p:EnableWindowsTargeting=true` 交叉编译；交叉编译不能检验 Windows 交互。工程划分：

- `PinyinGloss.Core`：快照、协议、配置、提示词、缓存、取消和调度。
- `PinyinGloss.Providers`：SQLite、本机/在线 HTTP、托管 llama.cpp 生命周期。
- `PinyinGloss.CandidateReader`：Windows MTA/UIA 读取、WinEvent、只读诊断。
- `PinyinGloss.Desktop`：WPF 设置、托盘、非激活翻译窗、读取器监控。
- `PinyinGloss.Tests`：身份/语言隔离、时间预算、取消、缓存、词典原子导入与模型接口测试。

[架构设计](docs/architecture.md) · [实现状态与验收](docs/windows-validation.md) · [贡献指南](CONTRIBUTING.md) · [安全问题](SECURITY.md)

## 许可证

本项目代码和原创示例词条采用 [MIT](LICENSE)。[第三方许可](THIRD_PARTY_NOTICES.md) 单独列明 .NET、SQLite 和可选 llama.cpp；模型和用户导入词典保留各自许可，项目 MIT 不覆盖它们。模型卡当前标注 Apache-2.0，准备和分发具体权重时应保留对应 revision 的通知。项目与微软无官方隶属关系。

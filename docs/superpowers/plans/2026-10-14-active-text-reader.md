# 活动正文读取实施计划

> 执行约定：使用 `superpowers:executing-plans` 在当前会话逐项执行；每项先失败回归、再实现、再验证。按项目约定不提交、不推送、不重置工作区。

**目标：** 修复网页输入框错读区域，明确正文读取失败原因，并在统一采集接口后逐步验证和接入兼容 Provider。

**架构：** 保留独立 reader 进程与专用 MTA。正文由目标解析、能力路由、Provider scope 校验和有界快照组成，统一切句后继续使用现有会话与翻译调度；候选采集不参与此次重构。

**技术栈：** C#、.NET 10、WPF、原生 UIA COM、标准 IA2/MSAA、经过验证的 Win32 只读消息；不增加第三方运行依赖。

**设计：** [2026-10-14-active-text-reader-design.md](../specs/2026-10-14-active-text-reader-design.md)，用户已确认。

## 全局约束

- 在当前含未提交实现的工作区修改；先备份本计划涉及文件到 `artifacts/validation/active-text-reader-20261014/baseline/`，不创建丢失这些修改的新 checkout，不提交任何既有工作。
- 两种正文功能各有开关，默认关闭；暂停优先，候选宿主验证、候选窗口和模型单槽保持原行为。
- 只采集当前前台真实目标；密码、身份不确定和权限拒绝立即清理，不尝试提权、OCR、剪贴板、模拟复制、注入或远程内存写入。
- 当前句最终最多 256 个 UTF-16 字符；统一采样窗口最多 514 字符，包含两侧各最多 256 字符与最多 2 个句界确认字符；选区只取完整选区且最多 256 字符。
- 所有原生对象在专用 MTA 内使用。读取前后核对目标、scope、caret/selection；COM 不能硬取消，由现有独立进程看门狗收尾。
- 默认诊断不包含正文、Name、Value、网址、窗口标题、提示词和可能带正文的异常 Message。
- 每项记录命令、退出码、通过/过滤数量。桌面工具 `USER_CONTROL` 或拒绝目标校验时停止输入，不更换工具绕过。
- 发布前保留已有 exe、配置和模型；停止程序必须核对路径和进程身份。未通过现场验证的 Provider 不记作应用支持。

## 重点回归

- 同 HWND 切换输入框或标签页：目标版本变更，旧译文和迟到状态均拒绝（任务 1、2、4）。
- 搜索框为空或 caret 越界：不读取附近正文，不把 caret 夹到边缘猜测位置（任务 1）。
- 非 BMP 字符、CRLF、小数和网址：UTF-16 偏移可靠，句界及 256/514 上限准确（任务 3、6）。
- UIA 缺少模式与 COM 访问失败：前者可探测同目标兼容接口，后者不能扩大范围或吞掉原因（任务 2、4、5）。
- 高权限、失去响应或读取中修改：返回明确状态，在整体截止时间内退出且不发布混合文本（任务 5、6、7）。

## 文件与接口

优先复用 `UiaTextScanner`、`TextRangeTests`、`TextOverlayTests`、`Program`、`ReaderSupervisor` 和 `DesktopRuntime`。新增文件仅承担真实新职责：

- `Core/TextCapture.cs`：`TextReadStatus`、`TextReadDiagnostic`、`TextReadRequest`、`CapturedText` 等纯数据；不保存原生对象。
- `Core/SentenceExtractor.cs`：唯一的字符串切句逻辑。
- `CandidateReader/UiaActiveTextScope.cs`：焦点子控件 scope 与范围包含校验。
- `CandidateReader/TextReader.cs`、`TextTargetResolver.cs`、`TextProviderRouter.cs`：统一入口、目标解析、能力路由；其余 Provider 各在确有实现时增加文件。
- `Windows.Tests/UiaTextTestObjects.cs`：从现有私有范围替身提取的测试工具，不能放进生产代码。

共同接口固定如下，后续任务不能自行更名：

```csharp
TextReadRequest(bool Sentence, bool Selection, bool Composing);
CapturedText(string Text, int? CaretOffset, int? SelectionStart,
    int SelectionLength, bool AtScopeStart, bool AtScopeEnd,
    bool ReadOnly, bool Composing);
TextReadResult(InputFocus? Focus, TextSnapshot? Text, TextReadDiagnostic Diagnostic);
ValueTask<TextReadResult> TextReader.ReadAsync(TextReadRequest request, CancellationToken token);
(TextMode? Mode, string Text, TextReadStatus Status)
    SentenceExtractor.Extract(CapturedText capture, TextReadRequest request);
```

`TextReadDiagnostic` 包含状态、会话/版本、前台 HWND、目标身份、Provider、scope 来源、进程名、类名、角色、耗时和数值错误码；没有任何正文属性。`CapturedText` 只在采集链路使用，不写入 IPC、诊断或模型请求。

reader 内部的 `ResolvedTextTarget` 固定包含 `FocusedElement`、`ForegroundWindow`、`NativeFocusWindow`、`ProcessId`、`ProcessName`、`ClassName`、`FrameworkId`、`ControlType`、`Identity` 和 `Password`；原生节点只允许 MTA 使用。`ProviderCaptureResult` 固定包含 `Status`、可为空的 `Capture`、`Provider`、`ScopeKind` 和可为空的 `NativeError`。前者在 `TextTargetResolver.cs` 定义，后者与 `ITextProvider` 在 `TextProviderRouter.cs` 定义。

## 任务 1：活动 scope 与越界拒绝

**文件：** 新增 `src/PinyinGloss.CandidateReader/UiaActiveTextScope.cs`；修改 `UiaTextScanner.cs`、`tests/PinyinGloss.Windows.Tests/TextRangeTests.cs`；提取 `UiaTextTestObjects.cs`。

**接口：** `UiaActiveTextScope.Create(IUIAutomationTextPattern text, IUIAutomationElement target, bool ownsPattern)` 返回范围及 `OwnDocument` / `RangeFromChild` 来源；`bool Contains(IUIAutomationTextRange range, bool requireCaret)` 只比较端点。`(string Text, string? Status) ReadSentence(IUIAutomationTextRange caret, IUIAutomationTextRange scope)` 使用显式 scope，不再暗中获取父级 DocumentRange；任务 2 再将采集状态映射到统一枚举。

- [ ] 新增失败用例：父文档全文与焦点子范围不同，输入框首/中/尾只返回“十角馆杀人”；外部 caret、部分跨界选区、外部非空选区在 `GetText` 前拒绝；空框、RangeFromChild 失败/不可比较均不回退全文。
- [ ] 运行 `dotnet test tests/PinyinGloss.Windows.Tests -c Release -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~TextRangeTests'`，保留预期失败。
- [ ] 实现 own/parent scope 获取、caret 退化/活动验证、selection 完整包含验证；扩展和句界确认均夹紧到同一 scope。读取后重检 focus、caret/selection 与 scope 端点，变化则丢弃。
- [ ] 运行同一用例组，确认通过；再运行既有后台回归，确认候选关闭、组合期和只读选区未变。
- [ ] 更新 `docs/text-translation-testing.md` 和进度记录，区分范围替身与真实应用证据。

## 任务 2：正文状态、IPC 与界面反馈

**文件：** 新增 `src/PinyinGloss.Core/TextCapture.cs`；修改 `Contracts.cs`、`PipeProtocol.cs`、reader `Program.cs`、`UiaTextScanner.cs`、Desktop 的 `ReaderSupervisor.cs`、`DesktopRuntime.cs`、`TextOverlayWindow.cs`、`MainWindow.cs`；对应核心和 Windows 测试。

**接口：** `TextReadStatus` 包括设计列出的 Success/NoFocusedTarget/NoCaret/NoSelection/InvalidTextScope/UnsupportedControl/NoSuitableProvider/AccessDenied/Timeout/UiaError/Ia2Error/NativeProviderError，并明确 TooLong/MultipleSelections/Composing/PasswordTarget/TargetChanged。正文状态使用新增 `ReaderMessageKind.TextStatus` 和 `TextReadDiagnostic`，IPC 升至 3；最终 `TextSnapshot` 与翻译协议字段保留。

- [ ] 新增失败用例：无目标与不支持控件不同、E_ACCESSDENIED 不变成无目标、无效 scope 清理旧内容、迟到诊断拒绝、正文错误不覆盖候选状态、IPC 旧版本拒绝及诊断序列化不含原文。
- [ ] 分别运行相关核心测试和 `TextOverlayTests`，确认预期失败；仅创建离屏内容的测试不标成真实桌面通过。
- [ ] 将 scanner 返回改为 `TextReadResult`；按状态变化去重发布诊断，先清理失效文本再显示错误。新增 `PresentReadStatus(TextReadDiagnostic)`，有已确认文本目标的失败展开说明，正常无目标保持圆圈并提供“空闲”提示。
- [ ] 概览增加独立正文状态行；详细 Provider/scope/角色仅在诊断中显示。读取中只用于确实仍在执行的读取，快速轮询不得闪烁。
- [ ] 监督器超时发送正文 Timeout 并清除旧结果；禁用、退出、IPC 断开仍清理全部状态。运行相关测试及 IPC/禁用/迟到结果回归。

## 任务 3：唯一的句子提取器

**文件：** 新增 `src/PinyinGloss.Core/SentenceExtractor.cs`、`tests/PinyinGloss.Tests/SentenceExtractorTests.cs`；扩展任务 2 的 `TextCapture.cs`。

**接口：** 生产实现使用上方 `SentenceExtractor.Extract`；无效偏移返回 InvalidTextScope，不能以 0 替代未知 caret。

- [ ] 写表驱动失败测试：既有中英文句界、小数、网址、CRLF、句末 caret、空白、有效选区优先、只读无 caret、组合中、多选区、256/257 长度和未到 scope 边界的截断窗口。
- [ ] 补充 surrogate pair 首中尾偏移、514 窗口上限、scope 尾部句号、句号后一个空白仅作确认的测试；期望字符串使用人工确定的字面值。
- [ ] 运行该类确认失败，再实现纯字符串提取；Provider 不保留第二套句子算法，最终超长明确拒绝。
- [ ] 运行核心完整测试，核对原文换行保留及模型提示词换行转换继续独立。

## 任务 4：统一 TextReader 与 UIA Provider

**文件：** 新增 reader 的 `TextReader.cs`、`TextTargetResolver.cs`、`TextProviderRouter.cs`、`UiaTextProvider.cs`；迁移并清理 `UiaTextScanner.cs` 的重复逻辑；修改 `Program.cs`；新增 `TextReaderTests.cs` 和 `TextProviderRouterTests.cs`。

**接口：** `TextTargetResolver.Resolve()` 返回当前前台及真实编辑目标元数据；`ITextProvider.Capture(ResolvedTextTarget target, TextReadRequest request, CancellationToken token)` 返回 `ProviderCaptureResult`（采集状态、CapturedText、Provider、scope 来源及数值错误码）。`TextProviderRouter.Capture` 只向同一已验证目标路由。

- [ ] 写失败测试：禁用时不解析/采集、UIA 成功不再调用其他 Provider、模式缺失可继续能力探测、密码/AccessDenied/InvalidTextScope 为终止状态、同 HWND 换控件更新会话且拒绝旧结果。
- [ ] 实现目标解析的有界父链与明确关联子节点；只接受唯一目标，不扫描全页找任意 TextPattern。
- [ ] UIA Provider 创建任务 1 scope，读取两侧最多 256 字符及最多 2 个确认字符。通过有界前后片段长度确定局部 UTF-16 偏移；不假定 `CompareEndpoints` 的返回数值就是字符数。
- [ ] 统一入口在既有 MTA 串行调用 Provider 和任务 3 提取器，返回 `ValueTask<TextReadResult>`；不得使用线程池迁移 COM 对象。替换 `Program.Observe` 的正文调用，不改候选 scanner。
- [ ] 删除迁移产生的旧切句/旧采集路径，运行核心与 Windows 后台完整回归；检查默认诊断无正文。

## 任务 5：IA2 目标绑定与兼容路径

**文件：** 经探测确有能力后新增 `CandidateReader/Ia2Interop.cs`、`Ia2TextProvider.cs`、对应 `Ia2TextProviderTests.cs`；诊断工件放 `artifacts/validation/active-text-reader-20261014/ia2/`。

**接口：** IA2 Provider 实现任务 4 `ITextProvider`；COM 声明依据官方 IA2 IDL，声明和 vtable 顺序必须完整匹配实际使用的接口。

- [ ] 在独立 Chromium/Electron 测试输入目标做只读能力探测：MSAA 的真实 focus、IServiceProvider/QueryService、IAccessibleText、角色与密码状态、可比较的局部偏移及封送可用性；默认不取正文。
- [ ] 保存可用或不可用的具体 HRESULT 与目标元数据；不修改浏览器标志、系统注册或安全设置。不可用时记录限制并保留 NoSuitableProvider，不能创建伪成功实现。
- [ ] 能力可用时写失败回归：祖先/兄弟可访问对象拒绝、光标和选区越界、只读选区、同对象换文档、偏移与文本读取前后变化、COM 拒绝与超时。
- [ ] 实现有界 `IAccessibleText` 查询，转换为任务 3 的 CapturedText，读取前后验证同一焦点与范围；不使用全量 accValue/get_text(0,-1) 兜底。
- [ ] 在生产路由接入并测试；真实输入只在独立测试页/宿主执行。Codex 仅做已授权的只读接口诊断，不操作聊天界面。

## 任务 6：Scintilla 无指针消息采集

**文件：** 新增 reader 的 `ScintillaTextProvider.cs`，扩展 `Native.cs` 的 `SendMessageTimeout`；新增 `ScintillaTextProviderTests.cs`，独立测试工件放 `.../scintilla/`。

**接口：** 实现任务 4 `ITextProvider`。使用 scalar getter：SCI_GETCURRENTPOS/GETANCHOR/GETSELECTIONS/GETLENGTH/GETCODEPAGE/GETREADONLY，以及 SCI_POSITIONBEFORE/AFTER 与 SCI_GETCHARAT；消息编号按官方头文件核对。

- [ ] 先在本轮专用 Notepad3 文档验证这些 scalar 消息的跨进程返回、焦点 HWND 与实际 caret/selection；不传入远程缓冲区或函数指针。
- [ ] 写失败回归：中文 UTF-8 与 emoji 的字节→UTF-16 偏移、反向选区、多选区、未知编码拒绝、读取中 caret/长度变化、无响应整体超时、256/514 字符及最多 2056 字节采样上限。
- [ ] 首版只接受明确 UTF-8（65001），其他编码返回明确不支持；字符边界用 POSITIONBEFORE/AFTER，字节用 GETCHARAT 有界获取，严格解码并映射局部偏移。不得调用 GETTEXT、GETTEXTRANGE、GETDIRECTFUNCTION、VirtualAllocEx 或 WriteProcessMemory。
- [ ] 所有消息使用 SMTO_BLOCK/SMTO_ABORTIFHUNG；单次消息最多 25ms，整轮采集最多 200ms，任一超时丢弃全部结果。若现场性能无法满足预算，记录实际边界而不放宽全局看门狗。
- [ ] 通过后台回归后接入能力路由，真实验证 Notepad3 的当前句与选区；Notepad++ 只有独立实测通过才标支持。

## 任务 7：Win32 评估、最终回归与配套交付

**文件：** `docs/text-translation-testing.md`、`docs/implementation-status.md`、`docs/windows-validation.md`、`README.md`、`AGENTS.md`；只有发现可满足边界且现有 UIA/IA2 不覆盖的 Win32 控件才新增生产 Provider。

- [ ] 检查专用 EDIT/RichEdit 测试控件实际 UIA/IA2 覆盖。需要原生接口时先验证消息封送、密码样式、位数、局部范围与 SendMessageTimeout；禁止全量 WM_GETTEXT 或未经封送的 WM_USER 指针消息。
- [ ] 有可行路径则先写当前目标范围、密码拒绝、长行/大偏移、读取中变更和超时失败回归，再接入同一 Provider 接口；无可行路径保存诊断结论并保持明确不支持。
- [ ] 使用仓库隔离 SDK：设置 DOTNET_ROOT、PATH、DOTNET_CLI_HOME、NUGET_PACKAGES；执行完整后台 `dotnet test PinyinGloss.sln -c Release -m:1 -p:UseSharedCompilation=false --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'` 及 Release build，保存 TRX、日志和退出码。
- [ ] 桌面空闲且新状态有效时，运行完整测试并在独立页面验证搜索框首/中/尾、空框、两个输入框切换、网页旧选区、只读选区、按钮失焦、IME 组合和再次聚焦；保持真实前台与键盘焦点证据。
- [ ] 明确区分实际通过、过滤、工具阻塞与不支持的 Provider；更新文档，不使用历史通过数量替代新结果。
- [ ] 在核对本轮及正式进程退出、保留旧目录和模型后生成配套 Desktop/reader 包，按已验证方式复用并核对 Q4 大小/hash，zip 排除权重。不能只替换其中一方或移动仍运行的发布目录。

## 执行与审阅记录

计划已对照设计自查：每项有独立输出和失败回归；Provider 间共用 CapturedText/目标身份，只有统一提取器负责断句；跨进程消息路径不引入指针封送和远程内存写入。IA2 与 Win32 的真实接口可用性属于任务内验证，不提前宣称兼容。

用户已确认此计划，由当前会话顺序执行；现有未提交工作保留，没有提交、推送或重置。

| 任务 | 当前状态 |
| --- | --- |
| 1 活动 scope | 实现及范围替身回归通过；真实 Edge 待验证 |
| 2 状态与 IPC | 实现及后台回归通过，含失焦/采集失败后的迟到状态拒绝；真实窗口待验证 |
| 3 唯一句子提取 | 24 项纯数据用例通过；U+FFFC 明确拒绝 |
| 4 Reader 与 UIA | 实现及路由/目标身份回归通过，旧 scanner 路径已移除；候选独立 |
| 5 IA2 | 自身对象绑定与有界重采样已接入，偏移回归通过；真实编辑对象待验证 |
| 6 Scintilla | scalar、前置 UTF-16 预算、重采样和超时已接入，回归通过；真实 Notepad3 待验证 |
| 7 交付 | 202 后台通过、0 警告/0 错误、IPC 3 配套正式包及旧包备份完成；8 项桌面过滤，真实应用和 Win32 评估受工具政策阻塞 |

最终只读审查的重要问题已用失败回归修复，具体数量和偏差裁定见 `artifacts/validation/active-text-reader-20261014/progress.md` 及 [正文翻译测试](../../text-translation-testing.md)。真实应用验收未完成，不能把任务表的代码状态作为截图问题的现场结论。

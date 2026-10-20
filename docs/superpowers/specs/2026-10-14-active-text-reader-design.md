# 活动正文范围与多来源采集设计

日期：2026-10-14。状态：用户已确认设计及实施计划；统一读取器、scope、状态及审查修复已实现，202 项后台通过，IPC 3 配套包已生成。真实 Edge/兼容 Provider 验收受桌面工具阻止，未宣布通过。不提交或推送现有工作区。

## 目标与成功标准

修复两类问题：聚焦网页搜索框时读到其他区域的正文；当前控件不支持 UIA 文本接口时，界面只有圆圈且没有失败原因。在可验证的系统接口范围内逐步增加应用支持，不承诺任意输入框均可读取。

上层只有一个正文读取入口 `TextReader.ReadAsync`，不感知 UIA、IA2 或原生控件的选择。候选采集继续使用现有系统宿主认证链路，正文能力不能放宽候选读取范围。模型、单槽、提示词及候选窗口不属于本次重构范围。

必须通过以下验收：

- 浏览器搜索框中光标位于“十角馆杀人”的开头、中间或末尾，采集原文只属于该输入框；空框不读取相邻网页文字。
- 借用祖先文本接口时，其他输入框或网页正文的 caret/selection 被拒绝，拒绝前不调用该范围的 `GetText`。
- 同一应用切换输入框、标签页或文档后，旧快照和晚到译文不能恢复。
- 不支持、范围无效、权限拒绝和读取故障具有不同的诊断状态；正常无目标与读取失败不能都表现为无说明圆圈。
- 原有微软记事本当前句、选区优先、IME 组合、候选关闭与正文独立、非激活窗口行为继续通过。

## 设计形成时的证据

用户截图中的搜索框为“十角馆杀人”，正文框显示下载说明。真实浏览器的焦点/provider/caret 尚未采集，不能将某个 API 假设写成已确认现场根因。

上一轮仅修复 `ReadSentence` 扩展端点未限制在文本提供者 `DocumentRange` 内的问题。范围替身回归修复前 4 失败、17 通过，修复后 21 通过，退出码分别 1/0；证据在 `artifacts/validation/browser-caret-20261014/`。此修复不足以证明借用父级 Document 时已经隔离当前输入框。正式程序尚未更新，也未完成完整回归。

Windows-MCP 持续返回 `USER_CONTROL`，本轮未操作用户网页。浏览器连接曾返回 `fetch request failed`。这些工具状态不能记作项目 UIA 兼容性失败。

## 架构与线程

```text
TextReader.ReadAsync
  → TargetResolver
  → ProviderRouter
  → TextProvider：创建 ActiveTextScope 并读取有界快照
  → SentenceExtractor
  → 现有正文 IPC、会话校验与翻译调度
```

`ActiveTextScope` 是所有 Provider 必须满足的约束；其原生对象由对应 Provider 创建和使用。不能在选择 UIA/IA2 前就把所有 scope 固定为 UIA COM 对象。

保留独立 reader 进程及专用 COM MTA；UIA 对象、IA2 对象和原生范围不能传到 WPF 线程。异步入口在该 MTA 上串行执行，不对每次读取使用任意线程池 `Task.Run`。现有进程看门狗负责不可取消的 COM 阻塞；取消令牌不能被描述为能够硬中断 COM 调用。

## 目标与范围

`TargetResolver` 记录前台 HWND、焦点元素身份、PID、ClassName、FrameworkId、ControlType、密码/只读标记，以及确定目标所必需的有限父子关系。ClassName 和进程名只是能力探测提示，不能单独证明当前目标可读。

只检查当前前台窗口中的焦点节点、现有最多 8 层祖先，以及明确关联该焦点的编辑子节点；不遍历全页或其他标签寻找任意有 TextPattern 的节点。目标不唯一或身份变化时拒绝读取。

UIA scope 规则：

1. 焦点编辑目标自身有 TextPattern：采用该目标的 `DocumentRange`。
2. 仅祖先有 TextPattern：使用祖先 `RangeFromChild(真实焦点编辑目标)`；失败、无法比较或只得到无法解释的嵌入对象范围时，返回 `InvalidTextScope`，不得回退为整页 `DocumentRange`。
3. 只读文档选区允许使用已验证的活动文档范围；焦点在按钮、菜单、其他编辑框时不能使用该文档保留的选区。

在读取正文前验证：caret 为活动退化范围且包含于 scope；selection 的两个端点均包含于 scope。不能把范围外的 caret 夹到输入框边缘来猜位置，也不能把跨界选区裁短后当成用户完整选区。范围无效时清理旧结果，不以另一 Provider 扩大范围掩盖失败。

合法 caret 的两侧扩展、句点前后确认和最终文本读取都限制在 scope 内。读取结束后再次核对前台窗口、焦点、scope 身份和原始 caret/selection；读期间改变则丢弃本轮结果。

## 快照与统一切句

现有 `Core.TextSnapshot` 是送往翻译的最终快照，保留会话、版本、目标身份、模式、原文和状态。新增 Provider 内部的 `CapturedText`，避免用同名类型替换现有 IPC 契约。

内部快照包含 `Text`、可为空的 `CaretOffset`、`SelectionStart`、`SelectionLength`，以及是否到达 scope 两端、是否只读/组合中、目标和 scope 版本。偏移统一为相对于该局部 `Text` 的 UTF-16 偏移；未知光标不能用 0 冒充。

按本轮已确认的统一切句方案，Provider 可在当前 scope 内读取光标两侧各最多 256 个 UTF-16 字符及至多 2 个句界确认字符，总窗口最多 514 字符；仅短期内存保留，模型只接收提取出的当前句。该局部窗口可能含相邻句片段，比旧实现仅对最终句调用 `GetText` 的范围更宽；不能将此解释为允许读取整个 input、整页或整篇文档。

选区路径只读取完整选区，最多 256 个 UTF-16 字符，不顺带读取上下文。多个不连续选区、超限或无法映射偏移时明确拒绝。

`SentenceExtractor` 只处理局部文本和范围元数据，Provider 不各自实现断句。沿用当前中文标点、英文句点后空白、小数/网址、换行及句末光标行为。局部窗口触及采样上限却没有可靠句界时返回 `TooLong`，不能把截断片段当完整句。最终当前句最多 256 字符，模型仍执行独立真实 token 预算检查。

UIA 范围对象验证与端点夹紧始终属于 UIA Provider；统一字符串切句不能替代采集前的 scope 校验。

## Provider 路由与接入条件

优先使用目标自身已验证的 UIA 文本能力；缺少能力时再探测与同一目标绑定的 IA2 或特定原生接口。路由顺序由能力决定，不按“Chromium 一律 IA2”硬编码。Chrome 138 起默认支持原生 UIA；Electron/Codex 使用的内核、暴露接口和控件仍须本机验证。

IA2 只使用当前目标的标准可访问性文本、光标和选区接口；MSAA 辅助确认目标，不把整个 `accValue` 当正文兜底。没有接口或 COM 封送支持时返回明确状态，不修改浏览器开关、注册系统代理、提权或注入来强行开启。

Scintilla 必须先验证真实跨进程读取方式。`SCI_GETTEXTRANGEFULL` 等接收结构体和缓冲区指针，不能把本进程指针直接发给外部编辑器。Scintilla 的位置是字节位置，需按实际 code page 转换成 UTF-16 偏移。常见带指针的 `SCI_*` 消息高于 `WM_USER`，Windows 不会自动为它们完成跨进程封送。

当前方案不隐式授权 `VirtualAllocEx`、`WriteProcessMemory`、DLL 注入或远程执行。优先调查宿主现有可访问性接口及经过验证的系统封送通道；没有满足边界的方案时，保留 `NoSuitableProvider`，说明限制，不交付空实现冒充支持。

Win32 Edit/RichEdit 同样按实际消息、指针封送、位数、密码样式和超时能力验证；禁止 `WM_GETTEXT` 读取全文兜底。所有原生查询有明确时间和字节上限，不能卡住候选采集。

## 状态与诊断

采集状态使用独立类型，不与翻译的 Ready/Timeout 混用：`Success`、`NoFocusedTarget`、`NoCaret`、`NoSelection`、`InvalidTextScope`、`UnsupportedControl`、`NoSuitableProvider`、`AccessDenied`、`Timeout`、`UiaError`、`Ia2Error`、`NativeProviderError`，并保留已有组合、多选区及超长状态。

`NoSelection` 在句子功能开启且 caret 有效时可继续当前句；无效 scope、密码和权限拒绝是终止条件。取消与目标变更属于正常失效，不累计为 Provider 故障。只有没有可靠活动文本目标时才显示空闲，确认了文本目标但无法读取时显示原因。

正文窗显示空闲、读取中、翻译中、无法读取、翻译成功/失败。读取中只对实际待完成操作显示，快速轮询不反复展开窗口或闪烁；翻译成功保留完整原文与译文。密码目标不显示内容，失效首先清除旧原文和译文。暂停或关闭正文功能后隐藏。

诊断与候选状态分开，默认只记录去重后的状态变化和非正文元数据：进程名称、控件类/角色、Provider、scope 来源、耗时及错误码。不得记录 UIA Name/Value、网址、窗口标题、正文或提示词；包含文本的验收仍需显式 `--include-text`，并限专用测试页面/文档。默认错误日志不直接写可能包含正文的异常 Message。

## 分阶段交付

1. **范围修复与可见失败状态**：先保留现有 UIA 的最终句读取方式，加入真正子控件 scope、caret/selection 越界拒绝及正文独立状态。验证 own/parent provider、输入框首中尾、空框、其他区域遗留选区、只读选区、密码、失焦和故障。该阶段不依赖新的局部文本窗口授权。
2. **统一采集与切句**：在本设计的局部窗口边界被接受后，提取 TextReader、目标解析、Provider 路由和统一 SentenceExtractor；仅迁移现有正文逻辑，保留已验证候选行为。
3. **IA2**：先在专用 Chromium/Electron 编辑目标验证目标绑定、范围、封送和偏移，再接入生产路由。Codex 只做授权范围内的只读接口诊断，不操作其聊天界面；需要输入的真实用例使用独立测试宿主。
4. **Scintilla 与 Win32**：先完成跨进程安全可行性结论，再实现可满足边界的 Provider；不能根据类名或 getter 返回成功就宣称正文兼容。

每阶段先保留失败回归，再做最小实现及完整相关回归，记录命令、退出码、通过与过滤数量。真实桌面使用独立测试页面/文档，先读取新状态；`USER_CONTROL`、目标校验拒绝或输入结果不确定时立即停止当轮，不换工具绕过。

界面和真实应用未测项继续明确列出；范围替身、接口存在、编译和后台测试不能替代实际应用验收。

## 接口依据

- [Microsoft RangeFromChild](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-rangefromchild)：返回包围子元素的范围，无文本时可返回退化范围。
- [Chrome 原生 UIA](https://developer.chrome.com/blog/windows-uia-support-update)：Chrome 138 起默认启用原生 UIA，并保留 IA2/MSAA 支持。
- [Microsoft SendMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessage)：跨进程的系统消息封送范围与自定义消息限制。
- [Scintilla 文档](https://scintilla.org/ScintillaDoc.html#SCI_GETTEXTRANGEFULL)：范围结构、缓冲区、位置及编码约定。

这些资料证明接口契约，不证明本机某个应用已经通过。

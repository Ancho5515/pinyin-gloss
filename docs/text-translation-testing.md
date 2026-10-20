# 句子与选区翻译测试

更新：2026-10-20。右下角正文框与微软候选释义框独立。本文区分真实桌面、真实模型与自动回归；未测项不计通过。

## 2026-10-20 只读浏览无活动选区时收回圆圈

用户报告知乎页面正常浏览、没有选择文字时，右下角持续展开“没有活动选区”，预期收回小圆圈。调用链已确认：DesktopRuntime.Receive 收到 NoSelection 时先清空 `_textSnapshot` 并取消正文任务，再调用 TextOverlayWindow.PresentReadStatus；后者原未把 NoSelection 归入空闲状态，因而进入通用错误展开分支。修复仅在该显示方法的 Collapse 条件加入 NoSelection，诊断本身保留，读取范围与 Provider 不变，范围无效、接口不支持、拒绝访问与超时仍显示各自原因。

新回归 `No_active_selection_collapses_the_window_and_clears_the_previous_translation` 覆盖初始浏览和已有译文两场景。连续三次无选区必须保持 28×28 DIP、内容为空；迟到 UpdateResult 不恢复旧译文；新有效选区仍可展开并显示译文。独立只读审查另核对 Runtime/Coordinator 的任务取消、epoch 与快照校验，无必须修项。

| 命令（本轮 Run-Tests.ps1） | 结果 |
| --- | --- |
| `-Name no-selection-before -Project tests/PinyinGloss.Windows.Tests/PinyinGloss.Windows.Tests.csproj -Filter FullyQualifiedName~No_active_selection_collapses_the_window_and_clears_the_previous_translation` | 2 失败、0 通过，退出码 1；期望宽度 28，实际 420 |
| `-Name no-selection-after` | 核心 99、Windows 后台 143，共 242 通过，0 跳过、退出码 0；8 桌面项过滤 |
| `Check-And-Publish.ps1` | Release 与 Desktop win-x64 自包含发布退出码 0，0 警告/0 错误 |

证据位于 `artifacts/validation/no-selection-idle-20261020/`。新 Desktop DLL SHA256 为 `5562E245BEF2598E60B91CF41E0F4A383334487D8C68923F6FA2D31E350292D5`，EXE、Core、Providers、deps 和 runtimeconfig 与当前正式程序一致。独立更新包保留已修复 Codex 选区的 reader（SHA256 `3BEA70F61AE633D1A16CF5C32F842B21AE0C74EDE7A4ABFFE91EE0CF5984E597`），排除模型及本机安装清单。

准备正常退出正式程序更新时，Computer Use 报告用户按物理 Esc 停止，已停止所有桌面调用，未用其他工具继续。当前运行中的 Desktop 未替换，真实知乎收圈和清除选区后的现场复测尚未完成；恢复后从正常退出、替换 Desktop DLL/PDB、重启和网页复测继续。独立包核对保存为 `package-check.json`，不将其记为正式程序更新成功。

## 2026-10-19 Codex 与企业微信

### 2026-10-20 聊天选区的深层接口查找

本轮分阶段诊断将接口查找、RangeFromChild、选区端点包含和 enclosing element 归属分别记录。真实 Codex 聊天选中文字时，聚焦节点为 Group（50026），第 16 层 Raw 祖先为具有 TextPattern 的 Document；`RangeFromChild(focusedGroup)` 成功，选区数量 1、非空且完整位于原 Group 子范围，enclosing element 沿 3 层回到该 Group。稳态元数据与范围核对约 12–17ms，首次约 67ms；这轮能力诊断不调用 GetText，不记录聊天正文、Name 或 Value。

实际生产阻碍是 UiaTextProvider 原只查找 8 层，未到达提供接口的 Document。此前 `selection-parent-metadata.jsonl` 中未分阶段的 InvalidOperationException 不能单独归因为 RangeFromChild 不支持；本轮 `probe-initial.jsonl` 的分阶段成功结果纠正该判定。

最小生产修复只将 Selection 开启且聚焦 Group 的祖先接口查找预算设为 64 层，其他角色仍 8 层。途中密码拒绝、前台原生根停止及超预算拒绝不变。查得接口后必须建立原 Group 的 RangeFromChild，整个选区读前必须包含于该范围，读取最多 257 UTF-16 单元以验证最终 256 上限，读后重建子范围和选区并比较；失败不退回文档全文。Group 空选区仍返回 NoSelection，不进入 caret 或句子读取。

`A_deep_group_selection_keeps_the_original_child_scope` 覆盖有效深层选区、跨界、空选区、密码、前台根、关闭选区、Edit、超预算和无子范围 9 场景；有效场景的每次 GetText 都核对位于原 Group 子范围，拒绝场景为 0 次 GetText。

| 命令（本轮 Run-Tests.ps1） | 结果 |
| --- | --- |
| `-Name deep-group-before -Project tests/PinyinGloss.Windows.Tests/PinyinGloss.Windows.Tests.csproj -Filter FullyQualifiedName~A_deep_group_selection_keeps_the_original_child_scope` | 5 失败、4 通过，退出码 1 |
| `-Name deep-group-after` | 核心 99 通过；Windows 140 通过、1 失败，旧 IA2 替身未实现新增访问的 CurrentControlType |
| `-Name deep-group-after-complete` | 补齐替身后核心 99、Windows 141，共 240 通过，0 跳过，退出码 0；8 桌面项过滤 |
| `Check-And-Publish.ps1` | Release 与 reader win-x64 自包含发布退出码 0，0 警告/0 错误 |

独立只读审查未发现必须修项。工件位于 `artifacts/validation/codex-chat-selection-20261020/`。已从设置窗口正常退出旧程序，`Update-Package.ps1` 先确认正式目录无进程后备份旧 reader/zip 并更新；退出码 0，正式目录及 zip 内 reader SHA256 均为 `3BEA70F61AE633D1A16CF5C32F842B21AE0C74EDE7A4ABFFE91EE0CF5984E597`。Core 与原 Desktop 匹配，Q4 保留为 1282439264 字节、完整 SHA256 与固定清单一致，zip 不含模型及本机清单。旧文件位于本轮 `before-update`，核对记录为 `package-check.json`。

已正常重启正式程序，设置窗口实际显示 GPU 29/29 就绪。用户完成本聊天记录选区测试并回复“正常了”；生产计时宿主 `selection-live-window2.jsonl` 在完整 120 秒观察中采到 3 次 Group（50026）经 UIA/RangeFromChild 返回 Selection Success，选区长度 47 UTF-16 单元、读取最大 77.326ms；另有 5 次 Group 空选区返回 NoSelection，无异常记录。自动日志只含长度、状态和计时，不包含原文或译文，因此对应翻译内容的现场确认来自用户；不将后台回归代替该确认。`formal-processes.jsonl` 多次记录 Desktop=24668、reader=6184 及创建时间未变化，从启动到最后核对约 6 分钟，没有重启证据。最终包内外 reader、README 和相关验收文档的一致性由 `Finish-Package.ps1` 核对并写入 `final-check.json`。

### 2026-10-20 聊天选区导致空引用与冷却

用户选中 Codex 聊天中的已知短句后，真实只读计时宿主捕获 `NullReferenceException`。堆栈为 `Ia2TextProvider.Capture:17 → TextProviderRouter.Capture:22 → TextReader.ReadAsync:36`，最后一个接口调用为 `LegacyIAccessiblePattern.GetIAccessible`。该接口返回 null，旧实现直接使用它查询 role/state。异常超出预期接口错误的处理范围，读取线程退出，主程序重启读取器；一分钟内已启动两次后（包含首次），再次启动进入 30 秒冷却，必要时再次等待。冷却是读取器重启保护，与模型冷却不同；不是聊天文字选择本身受限。

同轮普通编辑读取记录为约 27–90ms，现场捕获的是空引用崩溃，不能只按默认 WatchdogMs=500 推断该轮超时。计时诊断仅记录接口名、状态、长度及耗时，不记录聊天内容、Name 或 Value，也未修改选区。

生产修复仅在空 IAccessible 返回时结束 IA2 路径，状态为 NoSuitableProvider，Capture=null，不建立新范围、不读其他正文、不改变重启/超时配置。新增 `A_missing_legacy_accessible_object_does_not_terminate_the_reader` 走真实 UIA→IA2→TextReader，连续读取两次；修前 1 失败、0 通过，退出码 1，堆栈与现场一致。修后完整后台核心 99、Windows 132，共 231 通过，0 跳过，退出码 0；8 桌面项过滤。Release 与 reader 发布退出码 0，0 警告/0 错误，独立只读审查无必须修项。

日志、TRX 和计时工件位于 `artifacts/validation/codex-selection-watchdog-20261020/`。已从真实主窗口正常退出程序并更新正式目录和 zip，reader SHA256 为 `6D13C0CF311F93D159E1AB51BF2EFEC38E74AA3F9E18E855320BB5DE50024740`，包内外一致；旧 reader/zip 保留于本目录 `before-update`，Core 匹配，模型大小及完整 SHA256 正确，zip 不含本机模型或清单。已启动正式新版；`live-processes-after.jsonl` 的 40 秒观察中 Desktop=30840、reader=36632 保持不变。

第一轮修后 `selection-after.jsonl` 有 136 次读取，均为编辑框句子 Success，最大约 142ms，无异常；尚未采到用户的聊天选区，因此原选区修后现场验收仍待完成。消除读取器崩溃不等于具体聊天节点已经提供可用文本接口，选区翻译支持需单独以实测结果确认。

第二轮 `selection-after-window2.jsonl` 已进入与原故障一致的 Group/IA2 路径，连续 105 次均返回 NoSuitableProvider，无异常，最大约 120ms；之后核对正式 Desktop/reader 仍为 30840/36632，创建时间也未变化。说明空对象路径不再导致读取器退出，但该阶段尚未取得可翻译选区。`selection-parent-metadata.jsonl` 在第 16 层发现 Document/TextPattern 后，一次未分阶段诊断抛 InvalidOperationException、未调用 GetText。后续已按上节分阶段复查，确认子范围可建立，生产根因是原 8 层查找预算；该次异常不能证明范围映射持续不支持。

### 前台身份与能力诊断

Codex 本机窗口/进程名为 ChatGPT，编辑区为 Chrome 框架的 ProseMirror Edit，自带 TextPattern，无 TextPattern2。前台原生窗口位于 Raw 父链第 24 层，顶层窗口第 31 层；旧 `InForeground` 只核对 12 层，读前返回 TargetChanged。只读诊断在确认完整父链属于当前前台后，聚焦 Edit 自身 scope 的生产 Capture 返回 Success。修复仅将窗口身份核对预算设为 64 层，仍要求 GA_ROOT 等于当前前台，不扩大正文范围或借用父级接口的层数。

本机企业微信版本 5.0.12.6018。聚焦 UIA 节点为 Window / WeWorkWindow，无 TextPattern 或 TextPattern2；GetGUIThreadInfo 的 focus/caret/foreground 相同，存在原生编辑光标。MSAA Client 无可用编辑对象，IAccessible2 QueryService 返回 0x80070057，TOM 返回 0x80004005。上述查询只记录接口、角色、句柄及数值返回码，不读聊天、Name 或 Value。当前直接只读接口证据不足以支持该输入区；不能把候选释义或错误提示显示等同于正文翻译通过。

为避免该场景仍静默收圈，只有 UIA Window、原生 focus 和 caret 都指向当前同一个前台窗口时，解析失败改为 UnsupportedControl。该分类不创建可读目标；无 caret、不同窗口、按钮及 GUI 查询失败仍为 NoFocusedTarget。用户仅授权查看企业微信菜单与设置，未授权更改设置或发送消息。

### 自动回归与发布状态

2026-10-20 本机企业微信 5.0.12.6018 的追加现场调查已完成。用户打开设置后，仅查看并滚动“通用”页到底，功能、隐私、会话、外观、语言、自动翻译、文档、字体及软件更新区域均未发现无障碍/屏幕阅读器开关；未更改选项。自动翻译选项针对收到的聊天消息，未启用，不能当作当前编辑句或选区接口。

回到原输入框后，`GetGUIThreadInfo` 确认原生 focus/caret 为当前前台；企业微信与诊断程序完整性级别均为 8192。UIA 焦点和经当前窗口身份核对的光标顶部点均返回 WeWorkWindow / Window，无 TextPattern/TextPattern2。`AccessibleChildren` 成功返回两个对象，均为滚动条（role=9），并非可读编辑对象；焦点客户端和光标的 IA2、IAccessibleText、IAccessibleEx QueryInterface 返回 0x80004002，QueryService 返回 0x80070057。没有可用于当前正文的文本对象，非权限高低不同导致。光标中点可能落在窗外，因此仅将已确认仍在同一前台窗口内的顶部点作为有效接口证据，不用阴影窗口的结果判断编辑能力。

新增能力诊断没有任何正文 getter，不读 Name/Value 或聊天记录；构建退出码 0，0 警告/0 错误。有效输入框调查 `editor-interfaces-final.jsonl`、`editor-point-checked.jsonl` 均退出码 0；后者 4 次采样、4 次存在原生 caret，无异常。证据和设置局部截图 `settings-bottom.png` 在 `artifacts/validation/wecom-interface-20261020/`。本轮仅诊断及文档更新，没有修改生产代码，不新增回归通过数量，不把不支持提示记为翻译修复。当前直接只读边界下，该版本企业微信聊天编辑区仍不支持正文翻译。

`artifacts/validation/cross-app-text-20261019/Run-Tests.ps1` 保存以下日志和 TRX：

| 回归 | 结果 |
| --- | --- |
| foreground-before | 1 失败、3 通过，退出码 1；覆盖深层同窗口、其他窗口及预算越界 |
| opaque-caret-before | 1 失败、5 通过，退出码 1；覆盖三句柄一致及各拒绝条件 |
| final-after-gui-guard | 核心 99、Windows 后台 131，共 230 通过，0 跳过，退出码 0；8 桌面项过滤 |

`Check-And-Publish.ps1` 的 Release 构建为 0 警告/0 错误，reader win-x64 自包含发布退出码 0。独立只读审查指出的 GetGUIThreadInfo 成功守卫已落实后重新执行上述最终回归。正式目录及压缩包已于 2026-10-20 更新，尚未启动新程序，Codex 悬浮窗真实验收尚待执行。诊断位于同目录 `probe-depth.jsonl`、`probe-wecom-complete.jsonl`，默认无原文。

自动审批拒绝 Codex 整窗采集，避免读取会话内容；后续改用编辑区局部截图与获准 scope 诊断。本轮已知两句测试草稿未发送，已局部核对后清空，截图确认输入框恢复空白。托盘点击因无法确认程序身份被拒绝，需用户打开设置窗口后再正常退出更新，不能强制结束用户程序。

企业微信更多按钮的早期代点因控件身份不可确认而被自动审批拒绝，已停止该动作；2026-10-20 用户手动打开设置后，设置查看已完成，未更改设置。无原文诊断 `probe-wecom-final.jsonl` 确认生产 resolver 返回 UnsupportedControl、Target/Capture 均为空；用户后续确认正式窗口显示“当前控件不支持正文读取”。上述结果仅证明失败反馈生效，不等于正文翻译已支持。

正式目录 `artifacts/pinyin-gloss-win-x64`、正式 zip 及独立更新包的 reader SHA256 均为 `B42CF6A8C8557FB27290107D75ABDDACB52AAF7A6A284141FBEEFC9EC55D8004`。2026-10-20 已确认该目录无运行进程，`Update-Package.ps1` 退出码 0；旧 reader/zip 保留在 `cross-app-text-20261019/before-update`。Core 与原 Desktop 一致，Q4 大小 1282439264 字节及完整 SHA256 保持正确，zip 不含模型或本机清单。核对记录 `package-check.json`；本轮未启动程序，当前句/选区现场验收仍待执行。

## 2026-10-19 网页范围修复与实测

### 网页编辑区末尾

在同一本机测试页的 contenteditable 中真实复现：光标位于“欢迎使用本地翻译。我喜欢吃苹果”末尾时，生产 UIA 返回 InvalidTextScope；左移一格返回 Success。自身 DocumentRange、退化 caret 和末尾间隙的 enclosing element 均为编辑区内部 Text，而非 focused Edit；范围父链仍到达原 Edit。末尾 caret 比 scope.end 大，间隙 GetText(1) 长度为 0。只读诊断未输出正文，见 `artifacts/validation/web-editable-20261019/probe-before.jsonl` 和 `probe-gap-before.jsonl`。

修复只调整原空边界对齐的归属校验：限 OwnDocument 和聚焦 Edit，最多 8 层 Raw 父链验证 enclosing 节点；途中密码、另一个 Edit 或超出深度立即拒绝。scope、原范围及间隙分别校验，仍需间隙两次为空，并夹紧回原 scope。不扩到父级全文，不修改网页光标或选区。

新增真实调用生产 Capture 的 5 条后台回归：文字子节点成功；嵌套编辑框、其他编辑框、密码父节点、过深父链读前拒绝。命令为 `artifacts/validation/web-text-scope-20261016/Run-Tests.ps1 -Name editable-ownership-before -Project tests/PinyinGloss.Windows.Tests/PinyinGloss.Windows.Tests.csproj -Filter 'FullyQualifiedName~Editable_boundary_ownership'`，修前 1 失败/4 通过、退出码 1。修后同一脚本 `-Name editable-ownership-after` 完整后台核心 99、Windows 121，共 220 通过，0 跳过、退出码 0；8 项桌面测试过滤，不计通过。日志/TRX 位于该脚本对应名称路径。`web-editable-20261019/Check-And-Publish.ps1` 构建和 reader 自包含发布退出码均为 0，Release 0 警告/0 错误。独立只读审查无必须修项。

用户明确交出桌面并允许正常重启后，通过主窗口控件树确认“退出程序”按钮，正常退出并核对 Desktop/reader/模型均已结束，再备份旧 reader/zip、更新 reader 并启动正式 App。新 reader 的 SHA256 为 `645520749B4C538C313FDB0DFE70D84D64B0218EAA6CD129023C931CE847EE1D`，与包内一致；Core 与原 Desktop 一致，Q4 完整大小/SHA256 保持正确，zip 不含模型及本机清单。备份为 `web-editable-20261019/before-update/`，校验结果 `package-check.json`。

| 修后真实 Edge 用例 | 结果 |
| --- | --- |
| 保持原末尾位置 | 同样 caret 比 scope.end 大、enclosing 为 Text，生产 Capture 和切句均 Success；正式悬浮窗显示“我喜欢吃苹果 / I like to eat apples”，不需点击中间 |
| 末尾追加句号 | 保持框尾立即显示“我喜欢吃苹果。”及对应译文，无 InvalidTextScope；capture 长度由 15 变为 16 |
| 退格与 Home | 删除新增句号后恢复原内容；Home 只显示“欢迎使用本地翻译。”及对应译文 |
| 完整选区 | Ctrl+A 原文两句蓝色高亮，悬浮窗显示“选中文本”及双句完整译文 |
| 非文本焦点及恢复 | 点击按钮收成圆圈；重新聚焦编辑区并 End 后恢复末句译文 |
| 普通按键 | Windows-MCP Shortcut 的 p 仅上屏拉丁字符，末尾仍正常读取；随后一次 Backspace 已实测清除 p，原文恢复 |

修后只读诊断为 `web-editable-20261019/probe-after.jsonl`，截图 `editable-end-screen.png` 保存了恢复后的原测试内容、末尾光标及成功译文。正式 App 留在运行状态。首键/退格/换页/空格提交的真实微软拼音组合仍未通过；上述普通按键及浏览器插字不能代替 IME 组合验收。

### 普通输入框与只读选区

网页输入框末尾的 UIA 光标可能排在自身 `DocumentRange` 之后，但光标、边界间隙的 enclosing element 都是同一个聚焦 Edit，间隙没有文字。2026-10-16 原问题输入框的只读诊断确认：自身范围精确为“测试”，长度 2，末尾间隙 `GetText(1)` 长度 0。修复只在 OwnDocument 和上述身份确认成立、间隙两次读取均为空时，将范围副本对齐回原边界；不修改原始范围，不扩大 scope。借用父级模式、其他控件身份和非空间隙仍拒绝。`boundary-before-desktop` 为 4 失败、3 通过；修后相关 54 项通过。

用户确认汉字已确定上屏且候选关闭，Edge 仍返回历史 `GetActiveComposition` 范围，旧判断会永久保持“输入中”。UIA 现与 IA2、Scintilla 一样，采用 reader 已识别或正在确认的微软候选状态。该信号不是所有输入法、所有过渡瞬间的真实 composition 状态；首键、退格、换页与提交后的完整 IME 回归仍须实测，不能由普通网页填值代替。

本机专用页面 `http://127.0.0.1:8766/` 通过真实前台 Edge、生产 ReaderSupervisor/IPC/Runtime、Q4 模型和悬浮窗进行以下验证。GPU 实际就绪 29/29 层；不是注入快照或 DOM 翻译结果。

| 用例 | 真实结果 |
| --- | --- |
| 输入框首尾 | “测试”在框尾、Home 后均可读取，实际悬浮窗显示 test |
| 同框移动光标 | 第二框 End 仅取“今天的天气很好。”，Home 仅取“我喜欢苹果。”；长度与已知测试句 ID 均一致 |
| 选区优先 | 选中两句后 Mode=Selection、长度 14，悬浮窗显示完整双句译文；截图 `selection-screen.png` |
| 多行选区 | textarea 两句及换行完整读取，Mode=Selection、长度 13；原文保留换行 |
| 非文本焦点 | 点击按钮后清空会话和正文，窗口恢复 28 DIP 圆圈 |
| 只读网页选区 | 鼠标双击选中“中文”后，聚焦节点为无自身 TextPattern 的 Group。新增仅选区入口后经 `RangeFromChild` 读取 2 字，悬浮窗实际显示中文与 Chinese |

Group 的失败证据为 `readonly-group-before-20261019`：1 失败、4 通过。现在只有选区开关开启时允许该角色；借用父接口必须限定到当前 Group 的子范围，空选区直接返回 NoSelection，不能进入句子/caret 路径。按钮和菜单仍拒绝。

最新完整后台 `readonly-group-after-20261019`：核心 99、Windows 后台 116，共 215 项通过，0 跳过、退出码 0；8 项桌面测试被过滤，不计通过。Release 构建为 0 警告、0 错误，reader 自包含发布退出码 0。角色范围和组合判定均经独立只读审查，没有必须修项。

本轮日志位于 `artifacts/validation/web-text-scope-20261019/live.jsonl`、`live2.jsonl`；验收宿主新增 `--known-web-text`，只记录已知测试句 ID、长度及无原文诊断，未启用 `--include-text`。测试页面、隔离配置、构建日志和截图保留在同目录；失败回归及 TRX 位于 `artifacts/validation/web-text-scope-20261016/` 下对应名称。自建宿主、模型子进程和静态服务已按 PID/命令行核对后结束。

原站复测与真实拼音按键尚未通过：浏览器自动审批拒绝读取原 Z-Library 页面，认为超出测试授权且可能涉及私有内容；仅继续了本机专用页面。随后 Computer Use 对专用 Edge 窗口也因无法可靠确定当前 URL 而终止。此后停止全部 GUI 输入，没有换工具执行按键。不能把本机输入框和选区通过写成原站或真实 IME 回归通过。

## 范围与准备

句子与选区两个开关默认关闭；开启后，当前前台活动文本目标的非空选区优先，其次为编辑光标所在句。没有活动目标时清空为 28 DIP 圆圈；外挂暂停或两个开关都关闭时隐藏。空编辑框仍有活动光标时显示“等待输入”，不请求模型。

使用统一 TextReader 的 UIA/IA2/Scintilla 路径。局部采样窗口最多 514 个 UTF-16 字符，最终当前句或完整选区最多 256 字符。密码、不支持的控件、后台标签页和失效目标拒绝读取。不使用 Value/Name、全文、剪贴板、按键历史、输入钩子或 OCR 兜底；正文不进入候选 LRU、默认日志或在线来源。采集原文保留换行，模型提示词单独将换行替换为空格，避免 LMT 行式模板漏译后续行。

1. 使用配套发布的 Desktop 与 reader，准备固定 Q4_K_M 本地模型，确认设置显示实际 GPU 层数或 CPU 回退原因。
2. 在“翻译窗 / 后台”开启句子和选区翻译，选本地来源及英文，保留默认 300ms 稳定、1000ms 合并上限和 3000ms 请求期限。
3. 只使用专用新建空白测试文档；不要改动原有未保存文档。本机系统 notepad.exe 被替换为 Notepad3，正文已验证的真实应用是微软记事本 11.2607.14.0，不能混记。
4. 自动化前读取当前桌面。Windows-MCP 返回 USER_CONTROL 时停止输入并等待；输入返回结果不确定时重新观察，不能直接重发。出现异常立即停止当轮操作，诊断、回归、修复后重测。

## 桌面用例与当前结果

| 用例 | 操作及预期 | 当前证据 |
| --- | --- | --- |
| 当前句 | 输入“我喜欢苹果。今天的天气很好。”，等待 2 秒；原文只显示末句，得到对应译文 | 微软记事本真实通过，live3/live7 |
| 选区优先 | 鼠标选中两句，等待 2 秒；显示“选中文本”，原文和译文对应整个选区 | 微软记事本真实通过，live4/live7 |
| 取消选区 | 按右方向键；回到光标所在句，不保留选区模式的旧结果 | 微软记事本真实通过，live7 |
| 拖动 | 有选区时拖正文标题；位置改变，编辑框焦点和蓝色选区不变 | 真实通过，live7 中前台 EE0E9C、键盘焦点 9509AA 均未变化；句柄仅为该次证据 |
| 缩放 | 拖正文右下角；尺寸改变，焦点和选区不变，释放后设置保存新几何 | 真实通过，live7，420×180 变为约 486.67×246.67 DIP |
| 冷启动 | 先有选区，再启动宿主；显示“模型加载中”，GPU 就绪后无需重新选择即返回译文 | 修复后真实通过，live7，约 7.2 秒 GPU 就绪、7.7 秒显示译文 |
| 无光标/无选区 | 点击非文本控件或切到不支持的应用；清空正文和未完成任务，收成圆圈 | live8 微软记事本菜单两轮清空通过；其他不支持应用待补齐 |
| 再次聚焦 | 从圆圈回到测试编辑框；重新读取当前句，不崩溃、不恢复失效结果 | live8 连续两轮菜单/编辑框恢复真实通过，无崩溃 |
| 组合输入 | 逐键输入真实拼音，未确认期间保留上次完整译文并显示“输入中”；上屏后重读 | live8 逐键 p→pi→pin→ping→pingg→pinggu→pingguo 触发真实候选，7 行编号核对一致；正文保留旧译文，空格提交苹果后候选隐藏，正文保持展开并更新为 apple |
| 句界 | 移动光标、退格，覆盖中文标点、英文句号后空白、小数、网址和换行 | 有界 UIA 范围替身覆盖中文、英文、小数、换行；具体应用完整场景待补齐 |
| 只读选区 | 在专用只读网页/文档中鼠标选区，无输入光标也可翻译；换页面清除 | 尚未验证：浏览器专用接口连接失败，Computer Use 随后因无法可靠确认 Edge 当前网址而终止；停止操作，未绕过限制 |
| 开关/暂停 | 句子单开、选区单开、两项全关及外挂暂停/恢复；禁用时无正文请求 | 设置/全局禁用回归通过；设置界面操作待补齐 |
| 范围与隐私 | 空白/退化选区不查询，多范围和超长明确报错，密码拒绝，失焦迟到结果拒绝 | 密码/前台元数据及超长范围、迟到结果回归通过；真实目标边界待补齐 |
| 主题 | 两框深浅色、字体字号、圆角一致；系统主题变化原位重着色，不抢焦点 | 共用调色与字体回归通过；真实深色正文已观察，完整主题切换待补齐 |
| 多屏 | 150% 主屏与 100% 负坐标副屏拖动，折叠展开、重启和显示器移除后可见 | 主屏 150% 已实测；跨屏仍待验证 |

WPF `IsActive` 在原生拖动中可能为 true，不能据此断言键盘焦点已转移。实际验收同时采样 GetForegroundWindow、GetGUIThreadInfo 和 UIA 活动目标，并检查原选区高亮。live7 的拖动及缩放均未改变这些实际输入目标。

真实日志在 `artifacts/validation/text-feature-20261013/`，含本轮自建测试原文，仅保留本机。宿主限时退出后日志不再代表实时状态；开始输入前核对 PID、启动参数及日志更新时间。

## 自动回归与修复证据

### 2026-10-14 活动 scope 与统一读取器

用户确认问题截图来自 Microsoft Edge：光标在搜索框的“十角馆杀人”，右下角正文显示页面下载说明。当前实现已经将 own `DocumentRange` 与借用父级 `RangeFromChild` 分开，读取前拒绝外部 caret、非退化 caret、跨界选区；读取窗口只能在 scope 内扩展。`TextReader` 统一解析目标、路由 Provider、提取句子及管理会话，候选扫描不放宽。范围替身的首/中/尾、空框和跨界测试证明策略，不证明 Edge 提供的实际范围。

| 改动 | 失败回归 | 通过证据 |
| --- | --- | --- |
| ActiveTextScope 与越界拒绝 | scope-before：11 失败、21 通过 | scope-after：32 通过；scope-full-after：137 后台通过 |
| 独立失败状态、诊断、IPC 3 | status-before：核心 2 失败、窗口 4 失败；reading-before：1 失败 | status-after：核心 2、窗口 12 通过；status-sentence-full-after：166 后台通过 |
| 统一字符串句子提取 | sentence-before：22 失败 | sentence-after：22 通过，中英文、CRLF、小数/网址、emoji、预算和组合状态 |
| 统一 Reader / Provider 路由 | router-window-before：38 失败、1 通过；reader-contract-before：1 失败、5 通过 | router-window-after2：39 通过；reader-full-after：173 后台通过；无 Capture 的假成功已拒绝 |
| IA2 有界偏移查询 | ia2-before：5 失败 | ia2-after：IA2 5 项、Reader 6 项共 11 通过；对象嵌入符及偏移单位不一致拒绝 |
| Scintilla scalar 字节读取 | scintilla-before：6 失败 | scintilla-after：6 通过，中文/emoji、反向选区、未知编码、多选区、目标变化和超时 |

以上命令由 `artifacts/validation/active-text-reader-20261014/Run-Tests.ps1` 保存为同名日志/TRX，before 退出码 1、所列 after 退出码 0。提供者接入后 `providers-full-after` 为 190 项通过；独立审查补齐空文本缓存、同长度编辑和 Scintilla 的前置 UTF-16 预算，`review-findings-before` 核心 2 + Windows 7 = 9 失败，修后相关 87 项通过。迟到状态从旧生产判定提取后 `stale-diagnostic-repro-before` 为 2 失败/1 通过，修后仅当前目标和已确认快照可接收 Reading。

最终 `final-reviewed-background` 与 `Publish.ps1 -BackgroundOnly` 均为核心 99 + Windows 后台 103 = 202 项通过、退出码 0；8 项桌面测试被过滤，不计通过。`build-reviewed-final` 为 0 警告/0 错误、退出码 0。统一窗口仍最多 514 UTF-16 字符，但会在同一个有限范围内重采样确认，不能把重复确认次数当作新增全文范围；该策略没有跨任意第三方 Provider 的原子性保证，范围替身证明所覆盖的同长度修改会拒绝。

配套 IPC 3 正式 exe/zip 已生成，旧程序及旧 zip 保留在 `artifacts/pinyin-gloss-win-x64-before-active-text-20261014`。113 个 LLM 文件硬链接复用，Q4 大小 1282439264、完整 SHA256 `54c6244f100330264cef607188413ee82cb8a254f0cf950011aaa9cee74f7eb5` 一致；zip 不含权重和本机安装清单，本轮没有启动新 App。扩展 Provider 的失败状态必须携带空 Capture；统一入口对“失败但非空 Capture”的额外契约校验为审查保留的低优先级项，当前三个生产 Provider 没有这种返回路径。

IA2 只读能力探测确认当前 Electron 节点能查询 IServiceProvider、IAccessible2 和 IAccessibleText，但该节点为非编辑角色，生产路径会拒绝；不能据此标记 Codex 输入框已支持。Scintilla 只使用无指针 getter，单次 25ms、整轮 200ms，首版仅接受明确 UTF-8；尚未对真实 Notepad3 的新路径验收。Win32 EDIT/RichEdit 的现场接口评估尚未执行，未增加全文 WM_GETTEXT 兜底。

本轮桌面状态读取已恢复；Edge 标签创建仍返回 `fetch request failed`。新一轮 Computer Use 在绑定/激活现有 Edge 时因无法可靠识别当前浏览器 URL 而终止。已停止全部桌面输入，没有换用 Windows-MCP 绕过。Edge 搜索框、网页选区、Notepad3 新正文路径及本轮真实 IME 回归均未完成；旧版本记事本及候选的历史实测不是此次架构的现场通过证据。

### 2026-10-14 跨应用兼容性调查

在新架构实施前的调查中，用户报告光标在句子中或选中文字后正文框持续缩成圆圈，涉及 Notepad3 和 Codex。当时已核对句子与选区均开启，实际 reader 带有 `--sentence --selection`，主程序与本地模型进程运行。持续圆圈不能解释为模型翻译中；有效正文进入窗口后会展开显示状态。

- 真实 Notepad3 编辑区的 UIA 角色为 Pane，类为 Scintilla，未提供 TextPattern 或 TextPattern2；生产 `UiaTextScanner.Scan` 返回无活动正文目标。该应用的候选释义曾通过，不代表正文读取也支持。
- 真正微软记事本 11.2607.14.0 的专用新标签暴露 Document / RichEditD2DPT、TextPattern 和 TextPattern2；生产读取器接受活动目标，空文档返回长度 0，输入两句样例后返回光标所在末句长度 8。这只证明本轮真实采集，对正式程序显示和翻译结果尚未完成新的独立确认。
- 旧实现未读取到目标、接口不支持等路径都只清空为圆圈，缺少明确的失败原因提示；不能将无状态反馈作为正常等待。新 TextStatus 已区分这些结果，具体现场显示待复测。

诊断工件位于 `artifacts/validation/text-reader-20261014/`，只记录进程、控件角色/类、接口可用性、状态与文本长度，不记录正文。用户已要求扩展输入框和可选文字的跨应用支持；剪贴板兼容路径仍须明确现有隐私边界，Codex 直接读取原因继续调查。

本机 .NET SDK 环境见 [AGENTS.md](../AGENTS.md)。后台回归不会创建可见测试窗口：

```powershell
dotnet test PinyinGloss.sln -c Release -m:1 -p:UseSharedCompilation=false --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'
```

| 修复 | 失败证据 | 修复后证据 |
| --- | --- | --- |
| 展开/收起/再展开崩溃、组合期丢失旧译文 | collapse-composition-before，2 失败，退出码 1 | collapse-composition-after，98 项通过，退出码 0 |
| 英文句号紧邻光标时混入下一句 | sentence-boundary-before，1 失败、7 通过，退出码 1 | sentence-boundary-after，105 项后台通过，退出码 0 |
| 跨换行选区只翻首句 | model-multiline-before 真实失败；multiline-prompt-before 单测失败 | multiline-prompt-after，108 项通过；model-text-after 真实通过，退出码均 0 |
| 正文排队被已等候候选抢占 | priority-before，顺序失败 | priority-after 及 priority-full-after 通过 |
| 冷启动状态及自动刷新 | cold-text-before，核心/窗口各 1 失败 | cold-text-after，核心 73、Windows 后台 37，共 110 项通过，退出码 0；live7 现场确认 |
| 聚焦按钮/菜单误用祖先遗留选区、窗口超过工作区导致标题离屏 | review-boundaries-before，6 失败、6 通过，退出码 1 | review-boundaries-after，核心 73、Windows 后台 49，共 122 项通过，退出码 0；真实网页焦点及异 DPI 跨屏仍待补齐 |

2026-10-14 审查修复后的完整发布回归 `publish-final-resume-20261014.log`：核心 73、Windows 57，共 130 项通过，0 跳过，退出码 0；Release 构建 `build-reviewed-20261014.log` 退出码 0，0 警告/0 错误；Windows PowerShell 5.1 安装脚本 12 项通过、退出码 0（scripts-final-20261014.log）。后台回归 review-boundaries-after 为 122 项通过。

首次最终发布在 CandidatePageTests.Literal_pinyin 用例的 AutomationElement.FromHandle 处出现 COMException，核心 73 通过、Windows 56 通过/1 失败，退出码 1（publish-final-20261014.log）。未改代码直接复查该类 4 项全部通过，随后完整发布回归通过；偶发根因尚未确认，不能将重跑通过等同于已修复该异常。

最新版自包含 exe 目录及 zip 均已生成。旧程序和模型完整保留于 artifacts/pinyin-gloss-win-x64-before-final-text-20261014；113 个 LLM 文件硬链接恢复，Q4 大小及完整 SHA256 正确。zip 不含本机权重或安装清单。

后台命令排除 7 项可见窗口测试和 1 项桌面诊断，不计通过；上述完整回归包含这 8 项。TextRangeTests 的范围替身只验证端点与读取上限，不代表目标应用 UIA 兼容。

## 真实 Q4 正文模型验收

```powershell
dotnet run --project tests/PinyinGloss.Model.Acceptance -c Release -- artifacts/pinyin-gloss-win-x64 artifacts/validation/text-feature-20261013/model-text text-probe
```

使用固定 revision/hash 的 LMT-60-1.7B Q4_K_M、llama.cpp b11077、RTX 3050 Laptop，29/29 层卸载、FP16 KV、512 context、单槽。正文独立预留 128 输出、16 控制，实际输入最多 368 token；候选仍为 32 输出、448 输入。

修复后 `model-text-after` 退出码 0：5 个整句/跨句样例 ×3 轮，共 15 个 Ready，跨行选区包含 first 和 second 两部分；p50 97.329ms、p95/max 148.509ms。3 轮观察到真实槽忙后取消，均复用 PID，槽位清理约 32–48ms。256 字符内但真实分词超预算的输入明确返回 prompt_token_budget；没有静默截断。

这些耗时是生产正文查询到完整结果，不含 UIA、300ms 合并等待和屏幕；样本不足以报告可靠 p99。模型进程工作集约 1.55GB，不等于显存。少量基本词义通过不能代替所有语言或完整语义质量评估；专名与歧义词仍有模型限制。

界面、模型、范围和权限全部通过后才根据分阶段耗时优化，不能通过缩短等待掩盖错配或漏译。

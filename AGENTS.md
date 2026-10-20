# pinyin-gloss 工作约定与续接记录

## 工作约定

- 始终用简体中文回复和撰写文档，每次回复开头称呼用户为 Ancho。
- 开始工作先阅读 `README.md`、`docs/architecture.md`、`docs/implementation-status.md`、`docs/windows-validation.md` 和本文件。
- 保护已有未提交修改；未经用户要求，不提交、推送或重置工作区。
- 先定位问题，再作最小修复；每轮修复执行相应回归，记录命令、退出码、通过数量和实测边界。
- 只按实际证据宣布通过。单元测试、注入快照、交叉编译不能代替真实微软拼音或模型验收。
- 后续有影响续接的架构决定、实测结论或阻塞变化时，更新本文件；详细记录放入对应验收文档，避免复制整段对话。
- 踩坑一经证实就及时记录：写清触发条件、根因、有效处理方式及验证边界，不等到交付才补记。
- 用户于 2026-10-12 已恢复 Computer Use 和完整桌面验收授权；可以在专用空白测试文档中输入，启动自建可见测试宿主，按清单验收主题/焦点/隐藏等。异常立即停止当轮控制，定位、修复和回归后重测；不改用户原有未保存文档。最新授权覆盖此前暂停约束。
- `mcp__codex_app__consume_usage_reset` 永久禁用，不得调用或绕过。

## 项目边界

### 2026-10-20 只读浏览无选区时收回圆圈

- 用户报告知乎正常浏览、没有选中文字时仍展开“没有活动选区”。根因是 TextOverlayWindow.PresentReadStatus 将 NoSelection 落入通用失败展开分支；DesktopRuntime 已取消旧任务并清快照，无需改读取器。仅将 NoSelection 加入现有 Collapse 分支，诊断状态保留，其他读取失败仍显示原因。
- 新增初始浏览/已有译文两场景，反复 NoSelection 必须为 28×28 DIP 且清空内容，迟到结果不恢复，重新选区可展开。修前 2 失败/0 通过、退出码 1；修后核心 99 + Windows 143 = 242 通过、0 跳过、退出码 0，8 桌面项过滤。Release 与 Desktop 自包含发布退出码 0、0 警告/0 错误，独立只读审查无必须修项。新 Desktop DLL SHA256 为 5562E245BEF2598E60B91CF41E0F4A383334487D8C68923F6FA2D31E350292D5；EXE、Core、Providers、deps/runtimeconfig 与正式程序一致，仅需正常退出后更新 Desktop DLL/PDB。
- 准备正式更新时 Computer Use 报告用户按物理 Esc 停止，已停止桌面调用，未换工具继续。当前正式 Desktop 尚未更新，不能宣称知乎收圈已实测；Codex 选区修复 reader 仍是已更新版本。本轮独立更新包与证据位于 artifacts/validation/no-selection-idle-20261020/，恢复前先正常退出，再更新并现场复测，不覆盖运行中 DLL。

### 2026-10-20 Codex 聊天选区接口深度修复

- 分阶段只读诊断确认，当前聊天选区聚焦 Group 的第 16 层 Raw 祖先提供 Document/TextPattern；RangeFromChild(原 Group) 成功，单个非空选区完全位于该子范围，enclosing element 沿 3 层父链回到同一 Group。稳态元数据与范围核对约 12–17ms。上一轮未分阶段的 InvalidOperationException 不能据此归因为 RangeFromChild 不支持，以 probe-initial.jsonl 的分阶段结果为准。
- 生产根因是 UiaTextProvider 原仅查 8 层。现仅在 Selection 开启且聚焦 Group 时将接口查找上限改为 64，Edit/其他角色仍 8；密码与前台原生根停止条件不变。仍必须映射原 Group 的 RangeFromChild，完整选区读前包含、256 UTF-16 上限及读后重建范围核对不变，不读祖先全文。
- 新增 9 场景生产 Capture 回归，修前 5 失败/4 通过、退出码 1；首次全量回归仅旧 IA2 替身漏实现 CurrentControlType 失败，补齐替身后核心 99 + Windows 141 = 240 通过、0 跳过、退出码 0，8 桌面项过滤。Release/reader 发布均退出码 0、0 警告/0 错误，独立只读审查无必须修项。证据 artifacts/validation/codex-chat-selection-20261020/。
- 已正常退出旧程序并更新正式目录/zip，包内外 reader SHA256 均为 3BEA70F61AE633D1A16CF5C32F842B21AE0C74EDE7A4ABFFE91EE0CF5984E597；Core 匹配，Q4 完整大小/hash 正确，zip 不含模型或本机清单。新版已正常启动，GPU 29/29 就绪。用户在本聊天完成选区测试并回复“正常了”；生产计时宿主也已采到 Group/UIA/RangeFromChild/Selection Success，正文未写入诊断。正式进程 Desktop=24668、reader=6184，多次核对未变化。
- --background 首启未创建可见主窗口，需从托盘打开设置后正常退出。一次 Sky 控件索引点击落到“退出演示”，进程仍运行，更新脚本安全拒绝覆盖；刷新截图后按明确“退出程序”按钮坐标正常退出。click 返回成功不能替代进程退出核对，不覆盖运行中 DLL。

### 2026-10-20 Codex 聊天选区触发读取器冷却

- 真实无原文计时诊断已复现 NullReferenceException：UIA 无可用 Provider 后进入 IA2，Legacy.GetIAccessible() 返回 null，Ia2TextProvider.Capture 原第 17 行直接解引用。普通编辑读取为约 27–90ms，该次故障为崩溃，不能仅凭“冷却”归因为 500ms 超时。ReaderSupervisor 一分钟最多两次启动（含首次），后续启动每轮等待 30 秒，异常退出也会触发，不只看门狗超时。
- 最小生产修复仅在 GetIAccessible 后拒绝空对象，返回 NoSuitableProvider/Capture=null；不吞掉全部 NullReferenceException，不放宽范围或调整冷却。新增生产 UIA→IA2→TextReader 连续两次读取回归，修前 1 失败/0 通过，空引用堆栈与现场一致；修后完整后台核心 99、Windows 132，共 231 通过、退出码 0，8 桌面项过滤。Release 0 警告/0 错误，reader 自包含发布成功，独立只读审查无必须修项。
- 证据 artifacts/validation/codex-selection-watchdog-20261020/selection-timing.jsonl、null-accessible-before/after 日志及 TRX。本轮已通过真实主窗口退出按钮正常退出，备份并更新正式目录/zip，reader SHA256 为 6D13C0CF311F93D159E1AB51BF2EFEC38E74AA3F9E18E855320BB5DE50024740，Core 匹配、模型大小及完整 SHA256 正确。已启动新版；40 秒观察 Desktop=30840、reader=36632 持续同 PID。第一轮修后 136 次为编辑框句子 Success；第二轮真实 Group/IA2 故障路径连续 105 次均安全返回 NoSuitableProvider，无异常，随后再次核对仍为上述 PID。
- 该阶段 Group/IA2 路径无崩溃，但仍不能取得选区。父链发现第 16 层 Document/TextPattern，一次未分阶段诊断抛 InvalidOperationException；后续分阶段结果已确认 RangeFromChild 可以成功，实际生产阻碍为 8 层接口查找上限，见上节。不得据该次异常断言 Codex 没有接口或回退全文。

### 2026-10-19 Codex 与企业微信正文诊断

- 2026-10-20 企业微信复查已完成：用户手动打开设置后，仅查看并滚动通用页至底部，未改任何选项，未发现无障碍/屏幕阅读器开关。用户返回原输入框后，原生光标与 focus/foreground 一致；WXWork 与诊断程序完整性级别均为 8192，非权限高低不同。AccessibleChildren 的两个对象均为滚动条；原生光标顶部命中同一 WeWorkWindow，UIA 仍无 TextPattern/TextPattern2，MSAA/IA2/IAccessibleEx 均无可用文本对象。该版本（5.0.12.6018）在当前直接只读边界内仍不支持正文，不能再将菜单检查列为未完成或建议仅升权重试。本轮未改生产代码，没有可发布的企业微信兼容修复；诊断及设置局部截图见 artifacts/validation/wecom-interface-20261020/。
- Codex（本机进程/窗口名 ChatGPT）的聚焦 ProseMirror Edit 自带 TextPattern，但 Raw 父链直到第 24 层才到原生窗口。前台归属原限 12 层，导致 TargetChanged。现仅将窗口身份核对限额改为 64 层，仍要求 GA_ROOT 等于当前前台；正文 scope 和借用父级的 8 层上限不变。失败回归 foreground-before 为 1 失败/3 通过。
- 本机企业微信 5.0.12.6018 的编辑光标由 WeWorkWindow 自绘，UIA 仅 Window，无 TextPattern；MSAA 为 Client，IA2/TOM 查询失败。不能宣称已支持正文。新增仅用于失败分类的三句柄核对：当前 Window、原生 focus、caret 均为前台同一窗口时显示 UnsupportedControl，不建立读取目标、不读 Name/Value。opaque-caret-before 为 1 失败/5 通过。
- 最终 final-after-gui-guard 后台核心 99、Windows 131，共 230 通过，0 跳过、退出码 0；8 桌面项过滤。Release 0 警告/0 错误，reader 自包含发布退出码 0，已补 GetGUIThreadInfo 成功守卫。正式目录及 zip 已于 2026-10-20 更新，尚未启动新程序，真实 Codex 验收待执行，证据 artifacts/validation/cross-app-text-20261019/。
- 用户仅授权查看企业微信更多菜单/设置，不得更改选项、切换会话或发送消息。Codex 整窗采集被自动审批拒绝，后续仅用编辑区局部截图与无原文诊断；本轮已知两句测试草稿未发送，已通过局部截图核对后清空并确认输入框恢复空白。托盘点击因身份无法确认被拒绝，不能沿用截图坐标反复尝试；已请用户手动打开 Pinyin Gloss 设置窗口以便正常退出更新。
- 企业微信更多按钮的早期代点因缺少可识别控件身份被自动审批拒绝，未绕过。2026-10-20 用户手动打开设置后，设置页查看已完成；仍未更改任何选项。probe-wecom-final.jsonl 已确认生产 resolver 返回 UnsupportedControl 且无 Capture，用户也反馈正式窗口显示同一不支持提示；这仅证明失败反馈生效，不等于企业微信正文翻译已支持。
- 2026-10-20 确认正式目录无运行进程后已执行 Update-Package.ps1，退出码 0；正式目录、正式 zip 及独立更新包的 reader SHA256 均为 B42CF6A8C8557FB27290107D75ABDDACB52AAF7A6A284141FBEEFC9EC55D8004。旧 reader/zip 保留于 cross-app-text-20261019/before-update，Core 匹配，Q4 完整 SHA256 及大小正确，zip 不含模型和本机清单；未启动程序，下一步从启动及真实 Codex 验收继续，无需再为本次更新要求用户打开设置窗口。

### 2026-10-19 网页修复续接

- contenteditable 末尾故障已真实复现：OwnDocument 的 scope、caret 和空隙归属内部 Text 子节点，不等于聚焦 Edit；caret 比 scope.end 大，左移一格恢复。身份判断现沿最多 8 层 Raw 父链验证原 Edit，遇到密码或其他 Edit 立即拒绝；空隙双确认和原 scope 夹紧不变。生产 Capture 回归修前 1 失败/4 通过，修后完整后台核心 99 + Windows 121 = 220 通过，退出码 0，8 桌面项过滤；Release 0 警告/0 错误。独立审查无必须修项。用户交出桌面后通过主窗口明确退出按钮正常重启，正式 reader 已更新，真实 Edge 编辑区末尾、标点追加/退格、开头、完整选区、失焦清空和末尾恢复均通过，原测试内容已恢复。证据 artifacts/validation/web-editable-20261019/，截图 editable-end-screen.png；真实 IME 组合验收尚未完成，Shortcut 单字母仅上屏拉丁 p，已核对退格清除。
- Windows-MCP 的纯截图会报告无窗口，不能作为控件树目标证据；退出或输入前用 use_ui_tree=true 确认前台标题、URL、控件名及坐标。截图标签编号不能从文字树顺序推定，使用树中明确的按钮坐标。USER_CONTROL 后先等待约 10 秒，再刷新状态确认后操作；不能只等完就沿用旧坐标。上述自动审批拒绝通过目标核对及用户桌面授权解决，未绕过保护。
- 同一聚焦 Edit 的 UIA caret 可位于 OwnDocument 末尾之外，但中间没有文字。仅在 scope、原范围及间隙的 enclosing element 经有限父链确认属于当前 Edit 且间隙两次为空时，将副本对齐回原边界；借用模式及其他控件越界仍拒绝。原问题“测试”只读诊断与失败回归已保留。
- Edge 可保留已提交的 `GetActiveComposition` 范围，不能只按非 null 永久标记输入中。各 Provider 统一用 reader 当前已认证/正在确认的微软候选状态；首键、退格、换页及提交的真实 IME 回归仍待完成。
- 只读网页段落可为无自身 TextPattern 的 Group。仅选区开关开启时接受 Group，父级模式必须 RangeFromChild 当前组；空选区直接 NoSelection，不能进入句子/caret。
- 普通输入框与只读选区阶段后台核心 99、Windows 后台 116，共 215 项通过，退出码 0，8 桌面项过滤；Release 0 警告/0 错误。Edge 本机专用页当前句、两句选区、多行选区、按钮清空及只读“中文”选区已通过真实生产链路与 Q4 窗口，GPU 29/29；日志默认仅已知测试句 ID/长度，无任意正文。
- 原站读取本轮被浏览器自动审批拒绝；本机页随后在真实按键前又被 Computer Use URL 识别保护终止。已停止 GUI 输入，不能宣称原站或真实 IME 已通过。详情与证据见 docs/text-translation-testing.md、artifacts/validation/web-text-scope-20261019/。
- 正式目录及 zip 的 reader 已更新；Core SHA256 与原 Desktop 相同，zip 内外 reader SHA256 一致。旧 reader/zip 保留在上述验收目录的 before-update。Q4 完整 SHA256 及 1282439264 字节大小核对正确，zip 不含模型或本机安装清单；未重新启动正式 App。

- 保持微软拼音外挂路线，不开发新输入法或自己的拼音候选引擎。
- `CandidateReader` 负责独立 MTA 线程上的只读 UIA/WinEvent 采集；`Desktop` 是 WPF 托盘及非激活、点击穿透的释义窗；`Core` 调度，`Providers` 提供词典及模型来源。
- 候选链路仅采集经身份验证的系统输入宿主中的候选，不扩大到应用正文、剪贴板、按键历史或私有词库，不注入或提交候选。用户于 2026-10-13 明确允许新增可独立关闭的句子功能，通过 UIA 读取当前聚焦编辑框中光标所在句，并进一步要求翻译当前前台应用内鼠标选中的文本；两种正文触发方式各有开关，仅内存处理、目标失效清除、密码框和不支持应用拒绝读取。该例外只用于开启的正文功能，不能放宽原候选扫描。
- 在线来源默认禁用；App 启动/运行不自动下载，模型仅在用户显式运行准备脚本时下载；不降低量化等级、不暗中联网兜底。
- 用户已明确将默认模型改为 LMT-60-1.7B Q4_K_M GGUF；保留 Q8_0/Q6_K/Q5_K_M 的明确选择，不自动换精度。FP16 KV，单推理槽由各语言共享；不是 Base 或 W8A8。上下文 512、输入上限 448、输出上限 32、temperature 0。
- 2026-10-13 正式 App 首启已通过：真实 exe 同级 Q4 清单自动配置、GPU 29/29 层，Notepad3 首键 p 的微软候选与模型释义可见。随即发现微软有 7 个候选而外挂仅显示 5 个，已按停止规则停止输入，正在定位默认采集上限；不能把这一轮记为完整候选验收通过。
- 已确认上述截断根因：CaptureSettings 与独立 reader CLI 的默认 MaxCandidates=5，扫描器验证全页后 Take(maxCandidates)，并非 UIA 漏读。默认改为读取器支持的上限 10，仍只读取当前可见、身份验证后的候选；已有用户显式较小上限不自动覆盖。新增失败回归已确认旧值 5；真实复测应依据当页实际数量核对所有编号。
- 候选上限修复后完整 95 项回归通过（核心 63、Windows 32，0 跳过），正式 App 真实 p/pingguo 均完整显示 7 行并对应编号。观察到 Q4 对“平果”的疑似译文问题，已停止当轮输入，按官方提示词与完整 CC-CEDICT 对照调查；词典有“平果→参见平果市→Pingguo”条目，不能用格式校验通过代替译文正确。
- 已完成上述真实 Q4 提示词对照：GPU 29/29 层，36 个中英/中日请求采集退出码 0。旧短词模板将平果/平果市译为 Fogu，官方模板仍为 Fangui，日文专名也未正确；这不是候选错配，不能宣称更换提示词就修好。常见词苹果/保险/银行/你好等正常。记录为专名质量限制；完整词典命中优先显示，不换精度、不硬编码、不把英文直接填入日文。证据 full-desktop-20261013/prompt-probe/results.jsonl。
- Computer Use 本机曾在仅 include_text、不请求截图后点 WPF 按钮报 coordinate input geometry is unavailable；输入动作未确认成功，不能继续假定窗口已退出。下一次点击前获取含截图的新状态，并重新绑定当前窗口。发布/移动目录前必须核对进程实际退出；若仍运行，CU 的 app 路径会随目录移动而改变，而 CIM 原启动路径可能仍旧，不能使用旧绑定或仅凭原路径判断。
- 当前示例词典仅有 15 条中英记录；多语言缺词必须显示不可用，不能拿英文填充其他语言。

## 正文悬浮翻译开发（2026-10-13，执行中）

- 2026-10-14 用户已确认 ActiveTextScope、统一 TextReader、Provider 路由和失败状态设计及实施计划（docs/superpowers/specs/2026-10-14-active-text-reader-design.md、docs/superpowers/plans/2026-10-14-active-text-reader.md），并授权当前工作区顺序实施。现已接入 Resolver/Router/UIA/IA2/Scintilla 和唯一切句器；父级模式必须 RangeFromChild，caret/selection 越界读前拒绝。局部窗口 514、最终文本 256 UTF-16 单元；同范围有界重采样拒绝同长度混合，不将重复确认扩为全文。不能扩大为剪贴板、OCR、远程内存写入或注入，Scintilla 仅 UTF-8 无指针 scalar，单次 25ms、整轮 200ms。
- 新架构独立审查修复空输入框 Success 恢复旧缓存、同长度编辑混合、Scintilla 先读取超限等缺陷；review-findings-before 9 失败 → after 相关 87 通过。迟到目标状态的旧生产判定 2 失败/1 通过；现仅当前目标和已确认快照可接受 Reading。U+FFFC 对象占位符拒绝。扩展 Provider 失败但带非空 Capture 的额外契约校验仍为低优先级项，当前三条生产路径失败均返回 null。
- 最终 final-reviewed-background 核心 99、Windows 后台 103，共 202 通过，退出码 0，8 桌面项过滤；build-reviewed-final 0 警告/0 错误。Publish.ps1 -BackgroundOnly 同样 202 通过，IPC 3 Desktop/reader 正式包已更新。旧目录与旧 zip 保留为 artifacts/pinyin-gloss-win-x64-before-active-text-20261014；113 LLM 硬链接、Q4 大小/SHA256 核对通过，zip 不含权重或本机安装清单，本轮未启动新 App。
- 用户确认截图来自 Microsoft Edge。新一轮 Snapshot 成功，但 Edge 创建标签 fetch request failed，随后 Sky 激活当前 Edge 因无法可靠确定 URL 的政策保护终止；立即停止所有桌面输入，未用 Windows-MCP 绕过。新架构 Edge/Codex/Notepad3、Win32 评估及真实 IME 回归未完成；IA2 非编辑角色的接口可查询不等于 Codex 编辑支持。详细证据见 docs/text-translation-testing.md 与 artifacts/validation/active-text-reader-20261014/，后续真实复测必须重新取得可用的工具状态。

- 2026-10-14 用户要求正文翻译覆盖任意输入框与可选文字，并报告 Notepad3/Codex 持续圆圈。真实只读诊断确认 Notepad3 的 Scintilla 编辑区为 Pane，未暴露 TextPattern/TextPattern2，生产 scanner 返回无目标；此前 Notepad3 候选通过不等于正文支持。真正微软记事本的新标签仍能由生产 scanner 读取当前句（长度 8），本轮正式窗口结果尚未独立确认。接口不支持与无目标都静默圆圈是状态反馈缺口。详细证据见 docs/text-translation-testing.md 的跨应用调查；新的选区复制兜底尚待用户明确是否允许，不从通用兼容目标自行推定剪贴板授权。

- 当前目标集中于右下角正文悬浮框：句子与选区翻译分别有开关，选区优先。候选释义恢复原来的实时页和关闭隐藏，不保留跨候选页历史；相关历史栏、保留开关、排队延续与测试已清理。
- 用户已明确同意 UIA 读取光标所在句，并新增鼠标选中文本翻译。共用独立正文框，默认屏幕右下角，可拖动/缩放；有效非空文本选区优先于编辑光标所在句；两者都没有时清空并收成空白圆圈。只读网页/文档选区可以触发，不要求输入光标；当前活动文本目标必须验证，不能扫描其他页面的遗留选区。
- 鼠标与键盘选择都按 UIA 文本选区处理，不安装输入钩子、不模拟 Ctrl+C、不访问剪贴板；范围上限和 token 预算超出时明确报错，不静默截断。拖动缩放正文框必须不抢输入焦点、不取消原选区，不能照搬候选框的整窗点击穿透样式。两种正文开关独立，默认均关闭；已在本轮授权的测试配置可开启。
- 当前设计为 `docs/superpowers/specs/2026-10-13-text-overlay-design.md`，实施计划为 `docs/superpowers/plans/2026-10-13-text-overlay.md`。用户已确认并要求执行到完成，后续新增要求和范围收缩直接同步文档，在当前会话执行，不提交/推送。新功能尚未完成，不能拿旧回归替代现场验收。
- 已加入设置、正文单槽调度、独立窗口、UIA 读取与 Runtime 接线。最新 text-only-after 回归核心 68、Windows 33 项通过，1 项桌面诊断被 filter 排除，退出码 0；证据 `artifacts/validation/text-feature-20261013/`。真实正文接口与拖动尚未验收。
- 用户已安装 Windows-MCP，2026-10-13 当前会话已暴露并成功调用工具。优先使用其 Snapshot 读取桌面后操作；USER_CONTROL 表示用户正在操作且需等待约 10 秒空闲，不是程序失败，不绕过控制权保护。Sky 会自动激活输入目标，已改用 Windows-MCP 检查非激活拖动，不能把工具主动激活当作程序抢焦点。
- 最新首次新功能发布退出码 0，核心 68、Windows 完整 35 共 103 项通过，0 跳过；`text-feature-20261013/publish-first.log`。旧发布目录在确认所有本轮程序退出后整体保留于 `artifacts/pinyin-gloss-win-x64-before-text-20261013/`。新目录 Q4/CUDA/CPU 通过 113 个硬链接复用，完整 Q4 SHA256 再次核对成功。
- 沙箱内创建 NTFS 硬链接会返回拒绝访问；PowerShell 非终止错误仍可能退出码 0。创建模型复用文件时必须 ErrorActionPreference=Stop，并实际核对文件大小、hash 和运行时存在；本轮已在授权的工作区外层执行成功，不能凭枚举文件数量宣称链接完成。
- 已确认模型原输出检验仅看 stop=true，会把 stop_type=limit 当完整结果。新增失败回归后明确拒绝输出截断，正文可用独立 128 输出预算；这些参数仍需真实整句质量/延迟验收。
- `CandidatesClosed` 仅清除候选任务和窗口，不能连带清掉仍聚焦的句子；`Hidden` 表示 reader/IPC 故障，清空候选和正文。活动目标失效同样清除正文。不能简单忽略所有 Hidden。
- 新增正文后旧全局暂停只更新候选调度器，正文 API 仍会返回 Ready。pause-before 失败回归已确认，现已修复为全局禁用优先，并停止 reader；恢复重建正文会话，已有模型可以继续复用。
- 两框必须共用 OverlayTheme/DisplaySettings、背景边框、高对比度和应用主题；字体统一 Microsoft YaHei UI、14 DIP，展开圆角 8 DIP。typography-before 已确认候选默认 12 与正文 14 不一致，现已统一，真实主题切换仍需现场验收。
- 正文实测已改用真正的微软记事本 11.2607.14.0（从已安装包启动，系统 notepad.exe 入口仍被 Notepad3 替换）。专用新标签输入“我喜欢苹果。今天的天气很好。”，生产 UIA 只取末句并得到 The weather is nice today.，失焦清空圆圈已观察。重新聚焦时宿主崩溃：TextOverlayWindow.Present 重建父容器却复用仍有逻辑父节点的 TextBlock，stderr 已确认 InvalidOperationException。现已复用同一内容面板，展开/收起/再展开及组合保留回归先 2 失败，再完整后台核心 69、Windows 29 共 98 项通过（collapse-composition-before/after，退出码 1/0）；真实恢复仍须复测。
- 正文优先排队修复已完整后台回归：核心 69、Windows 后台 27 共 96 项通过，退出码 0；priority-full-after.log。7 项可见窗口测试及 1 项桌面诊断过滤，不计通过。Windows-MCP 连续 USER_CONTROL 时停止桌面输入，后台工作继续。
- live4 中真正微软记事本的鼠标整行选区已得到完整对应译文，正文框原生标题拖动和几何保存也已执行。拖动时 WPF IsActive=true，但采样前台始终为记事本，reader 未发布新焦点/选区；本轮随即停止。必须进一步核对实际键盘焦点及拖动后的选区，不能仅凭 WPF 标志断言抢焦点，也不能直接记作非激活验收通过。
- 句界替身回归确认光标紧跟英文句号、尚未跨后续空格时会读取下一句（sentence-boundary-before：1 失败、7 通过）。现用有界 FindText 多确认一个字符的句号后空白，句号本身作为边界，文档末尾句号单独确认，不额外 GetText；中文、小数、换行和过长范围同时回归。修复后核心 69、Windows 后台 36 共 105 项通过，退出码 0；不能将范围替身当具体应用兼容验收。
- 真实 Q4 正文对照确认官方行式模板对“这是第一句。\n这是第二句。”只输出第一句；同样两句用空格连接后完整输出。这是模型提示词输入格式问题，不能仅凭 stop/eos 就宣称完整翻译。正文提示词将 CRLF/CR/LF 转为空格，UIA 原选区、界面原文与结果身份保留换行；候选提示词不改。model-multiline-before 真实失败与 multiline-prompt-before 单测失败已保留，修复后须复测跨句内容。
- 句子需要独立输出预算：现有候选配置为 512 上下文/448 输入/32 输出；若句子输出改为 128，输入最多 368（另留 16 控制 token），不能沿用 448 输入。句子不能进入通用候选 LRU、默认日志或在线来源；UIA caret/组合接口存在不等于具体应用已通过。
- live6 真实冷启动确认正文先返回 model_loading 却显示“需要启用本地模型”，随后 GPU 29/29 就绪仍保持旧状态。已加入加载状态及完成后仅刷新仍有效加载中快照，cold-text-before 核心/窗口各 1 失败，cold-text-after 共 110 项后台通过。live7 真实验证无需重新选区：约 7.2 秒 GPU 就绪、7.7 秒显示完整译文。
- live7 标题拖动及右下角缩放真实通过：GetForegroundWindow 与 GetGUIThreadInfo 的 active/focus 都持续指向微软记事本及其编辑框；原选区蓝色高亮保持，正文 Mode/Session/Version 未改变。位置和约 486.67×246.67 DIP 尺寸写入隔离配置。WPF IsActive=true 不代表键盘焦点转移，后续验收需使用同样多项证据。
- 最新 Q4 正文真实后台 model-text-after 退出码 0：15 次请求全部 Ready，跨行两句均输出，p50 97.329ms、p95/max 148.509ms；3 轮真实忙槽取消均同 PID、清理 32–48ms，真实分词超预算明确拒绝。仅后台查询口径、不含 UIA/合并等待/屏幕；不是完整语言质量。
- Windows-MCP Shortcut 的单字母动作在本轮微软记事本中只插入拉丁字母，即使任务栏显示“中”；不能据此把该轮当作真实 IME 组合验收。需重新验证正确的物理按键方法，不改生产候选校验来迁就工具。上一轮 Backspace 因自动审批服务用量限制未执行，测试文档末尾仍有本轮字母 p；不得默认已删除。
- 2026-10-14 完整回归 full-final-20261014：核心 73、Windows 45 共 118 项通过，0 跳过、退出码 0；build-final-20261014.log 为 Release 0 警告/0 错误；scripts-final-20261014.log 为 PS5.1 安装脚本 12 项通过、退出码 0。正文验收详情已移入 docs/text-translation-testing.md；README、架构与模型默认 Q4 说明已同步。

### 2026-10-14 发布前边界审查

- 正文焦点不能仅按“祖先存在 TextPattern”判断。文档内按钮/菜单获得焦点时，祖先可能仍保留旧选区；原实现会继续读取并请求翻译。现于正文读取入口限定 Edit/Text/Document 可查有界父链，Custom/Pane 仅在自身提供 TextPattern 时可读，其他角色拒绝；该限制不修改候选扫描。角色策略回归只证明入口策略，真实网页按钮切换仍须现场验收。
- 仅夹紧窗口右下锚点不能保证标题可见。1600×1200 DIP 正文框在 1280×688 DIP 工作区恢复后会产生负的 Left/Top；现先限制实际宽高，再校正位置，原生缩放结束后也执行约束并保存。纯几何回归覆盖较小工作区与负坐标，不代替异 DPI 跨屏实测。
- 两项修复的失败证据为 `text-feature-20261013/review-boundaries-before.log`：6 失败、6 通过，退出码 1；修后 `review-boundaries-after.log` 核心 73、Windows 后台 49，共 122 项通过，退出码 0。8 项桌面测试被过滤；最终完整回归和发布结果以随后记录为准。
- 首次最终发布在完整 Windows 测试中停止：CandidatePageTests 的 Literal_pinyin 用例在 AutomationElement.FromHandle 处抛 COMException，核心 73 通过、Windows 56 通过/1 失败，退出码 1。未改代码立即复查该类 4 项全部通过（review-desktop-recheck，退出码 0）；暂不能确定偶发原因，不将失败隐去或宣称已修。旧发布目录已整体保留于 `artifacts/pinyin-gloss-win-x64-before-final-text-20261014/`，续跑仅从此备份恢复，不能再次移动/删除权重。
- 续跑最终发布 `publish-final-resume-20261014.log` 退出码 0：核心 73、Windows 57，共 130 项通过，0 跳过；113 个本机 LLM 文件硬链接恢复完成，Q4 完整大小/SHA256 正确，zip 不包含权重或本机安装清单。最新构建 `build-reviewed-20261014.log` 0 警告/0 错误。正式发布根目录已是最新代码。
- live8 已在最新代码的真实微软记事本完成两轮菜单失焦清空/编辑框恢复，无再次展开崩溃；选区优先及完整双句译文通过。Sky press_key 的逐键 p/i/n/g/g/u/o 确实触发微软拼音（不同于此前 Windows-MCP Shortcut 字母直接插入的现象），组合期间保留完整旧译文，空格提交后候选隐藏但正文保持展开并译出 apple；7 个候选编号按现场核对。之前不确定的退格经新截图确认已执行，末尾 p 已清除，不得重复补发。
- 本轮只读网页验收尚未执行：CUA Edge 创建标签接口返回 fetch request failed；随后 Sky 激活现有 Edge 时被工具终止，原文为 could not determine the current browser URL on Windows with enough confidence to enforce policy。立即停止所有 GUI 输入，不用 Windows-MCP 或其他路径绕过。只整理后台证据并释放本轮宿主。继续前需要新一轮可用的工具状态；不要将该问题当作本项目网页 UIA 失败，也不能宣称网页已验收。

## 当前实测状态（2026-10-12，后台修复完成，桌面验收暂停）

以下是本机验证记录，续接时仍需核对当前代码和环境。README 与验收文档已同步；不要沿用旧的“UIA 无法读取”结论。

- Windows build 为 `26200.9457`；不要仅依据注册表旧的 ProductName 判断系统版本。
- 系统原有 SDK 9 不能构建本项目。.NET SDK 10.0.302 已校验官方 SHA512 并解压至 `artifacts/toolchain/dotnet`，未更改项目目标框架。
- Windows Release 解决方案构建成功，0 警告、0 错误。
- UIA 稳定页修复后曾完整通过 40 项（核心 30、Windows 10），退出码 0，记录在 `artifacts/validation/uia-stability-after-fix-20261012/`。随后新增词典延迟回归确认 `NotFound` 等待模型稳定时间；现已修复，核心 32 项通过，退出码 0，证据 `dictionary-delay-after-fix-20261012/`。Windows 可见窗口测试现已标记 `InteractiveDesktop`，读取桌面测试标记 `DesktopCapture`，暂停期必须排除两类。
- 最新后台回归 85 项通过（核心 61、Windows 后台 24），退出码 0；证据 artifacts/validation/gpu-20261012/test-final.log 和 final/ 下两个 TRX。8 项桌面测试排除，不计通过；另 11 项 PS5.1 安装脚本用例通过。
- 最新 Release 构建退出码 0、0 警告/0 错误；Publish.ps1 -BackgroundOnly 退出码 0，85 项后台回归通过，生成自包含 win-x64 目录/zip，包含 GPU 优先与量化选择脚本。证据 artifacts/validation/gpu-20261012/build-final.log、publish.log。主 App 未启动。
- 真实 UIA 读取已成功。普通权限 Notepad3 专用空文档中，生产 reader/IPC/调度器/释义窗闭环通过逐键输入、退格、翻页、空格和数字选词、Esc、切应用隐藏及不抢焦点；不是注入候选。鼠标选词、DPI/多屏和完整 App 启动尚未验收。
- 已下载校验真实 LMT Q8_0/Q4_K_M 与固定 CUDA/CPU 包，RTX 3050 上两种量化实际卸载 29/29 层、后台验收退出码 0；11 次请求分别 44–138ms / 34–78ms，5 轮取消均复用同 PID，清理分别 32–64ms / 31–46ms；三轮双语言各 30/30 Ready。隔离缺 DLL 故障真实回退独立 CPU（0xC0000135）通过。证据 artifacts/validation/gpu-20261012/；完整语言质量、Q6/Q5 推理及 GUI 首启仍未验收，详见 docs/model-setup.md。
- 优化前真实 32 页“主进程接收快照到自身文本树无等待”p50 127.924ms、p95 147.743ms，含 20ms 采样误差，不是按键到屏幕延迟。词典缺词等待已移除；新实现的真实屏幕性能仍待恢复桌面验收。
- 最新后台完整词库验收退出码 0：纯查词 n=40 p50 0.329ms、p95 0.374ms；三列七候选无缓存、StableMs=2000 时整页调度 p50 14.295ms、p95 15.335ms、max 17.706ms，40 页 840 个终态无缺失/重复。`artifacts/validation/dictionary-background-20261012.log`。调度口径不含 UIA/显示，不能直接与屏幕采样基线计算提速比例。
- 本地词典基础功能仍是已验证基线；用户现已要求继续调整延迟并准备新用户模型下载/自动配置，只开展后台模型验收。Computer Use 暂停约束不变。词典用例见 docs/local-dictionary-testing.md。

### 已实现并回归的修复

- `OverlayWindow.Position()`：空间不足隐藏后，候选移到有空间的位置可重新显示；恢复窗口继续使用非激活行为。
- reader 的 `--diagnose` 输出明确采用 UTF-8，修复中文诊断日志重定向后无法严格按 UTF-8 解码的问题；默认诊断不包含候选原文。
- 新增 `tests/PinyinGloss.Windows.Tests`：验证真实 WPF 窗口隐藏恢复、前台句柄及窗口样式、候选编号和过期结果对应关系、诊断输出编码。候选映射用的是注入快照，不是真实 IME 读取。
- 核心测试新增真实 SQLite 词典来源集成验证：禁用模型时英文双来源可回落词典，缺少日文记录时保持不可用；不代表模型推理通过。
- 发布脚本在 Windows 上执行 Windows 测试；续接时先用 `git status` 核对当前工作区和提交状态。
- 本地词典主来源原来忽略 `FallbackSourceIds`；已用失败回归确认，再修复为先查主词典、缺词再查同语言兜底。34 项回归和完整 CC-CEDICT 验收均通过。
- 已从 MDBG 官方下载 CC-CEDICT（2026-10-10T02:36:17Z，125251 原始记录，CC BY-SA 4.0）。转换为 125261 行 TSV，含别名去重后 SQLite 203053 条；未随项目 MIT 或发布包分发。转换脚本 `scripts/Convert-Cedict.ps1`；可重复验收 `tests/PinyinGloss.Dictionary.Acceptance`，数据在 `artifacts/dictionaries/cc-cedict/`。

### 真实候选读取调查

- 用户历史截图是 1 保险、2 保鲜、3 包线；本机最新真实输入是 1 保鲜、2 保险、3 包线。必须依据当前微软候选及真实编号，不能硬编码词序。
- 本机启动 `notepad.exe` 后实际出现的是 Notepad3；不得将这次输入试验记为微软记事本验收。
- 真宿主未被 `EnumWindows` / `EnumChildWindows` 枚举到。必须先在独立 MTA 上订阅原生 COM UIA3 的 desktop subtree `MenuOpened`，再重开微软候选；沿事件节点的有限 Raw 父链获得真实 CoreWindow HWND，再复用托管扫描器。旧 TextInputHost 全屏根矩形不能当候选矩形，旧的“无事件”阴性不能证明 UIA 不可行。
- `IME_Candidate_Window` 的 HWND 可为 0；不能直接要求候选容器本身有 HWND。仍必须校验同 PID 父链、受保护系统路径和前台应用。
- 编号与文字是 `TEMPLATE_PART_CandidateItemIndex/Text` 独立节点，`ControlElement=false`、`ContentElement=false`；只用 ControlView 会漏掉，候选项内部需有界 RawView 查询。
- 候选页可含拉丁串、emoji、罗马数字、圈号字符；“只允许汉字”会拒绝整个真实页。显式编号必须有效且唯一，不得按位置猜序号。
- UIA 页更新并非原子操作，会短暂混合新旧候选。连续两次相同页、间隔至少 50ms 后发布；确认期间与真正关闭必须区分，否则旧框会闪烁。Updating/Hidden 区分和整页缓冲已修复、离屏回归通过；实际显示效果待恢复后验收。
- 本机 `ChsIME.exe` 的 `Process.MainModule` 查询失败；用 `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName` 已验证有效。不要升权或放宽宿主路径白名单。
- 不要硬编码进程 PID、窗口 HWND，或为通过验收而放宽系统宿主身份校验。
- 用户已授权使用 Computer Use；真实读取前置失败后立即停止后续自动输入。2026-10-12 用户明确选择继续调查 UIA、暂不增加 OCR；不得实现或启用 OCR，也不要重复请求这项选择。调查计划和实测记录见 `docs/candidate-reading-next-step.md`。

### 后台开发与验收避坑

- 验收宿主限时结束后，旧日志不再代表实时状态；先核对 PID 和日志 LastWriteTime，再判断后续输入是否被采集。
- 不要从 `src/bin` 常驻运行 reader：会锁定构建 DLL，触发 MSB3027/MSB3021。运行使用 `artifacts` 副本；只在核对 PID/命令行后停止本轮自建进程，不能杀用户进程。
- Computer Use 点击编辑器中的微软候选会跨到 TextInputHost，工具拒绝；若目标应用列表未暴露可选宿主，立即结束当轮，不绕过目标校验。鼠标选词仍待用户恢复后手动验收。
- WPF `OverlayWindowTests` / UIA `CandidatePageTests` 会 `Window.Show()`，即使叫单元测试也会干扰桌面。当前暂停期必须过滤；不要直接运行旧发布脚本中的完整 Windows 测试。
- Windows 测试项目同时启用 WPF 和 WinForms，隐式 `System.Drawing` 会与 WPF 的 `SystemColors` 等同名类型冲突（CS0104）；测试里使用明确别名/完整限定名，不通过修改框架或关闭既有能力规避。
- 不用正式 App 启动路径做隔离词典验收，以免改用户设置、开机项或预加载模型。已有验收宿主复用生产 Runtime，但数据及配置在 `artifacts` 独立目录，禁止 `SaveAsync`。
- PowerShell `switch ($_.kind)` 内的 `$_` 是匹配值，不再是日志对象；统计脚本使用显式 `$row`。沙箱的 FileSystem 初始化乱码不能代替退出码判定。
- 当前界面需求：更新期间保留旧页、完成后替换；默认跟随 Windows 应用深浅色，主题变更只更新配色，不抢焦点；深色背景必须使用可读浅色文字。暂停期间只验证代码与离屏组件，实际闪烁/主题切换效果待恢复后复测。
- OverlayContent 不创建 HWND。上一页保留到词典快查后新页各格收到结果或模型 Pending，再整体替换；模型完整译文原位补齐。Pending 不是终态，期限必须收尾；Updating 撤销未完成页、拒绝旧结果，Hidden 清空全部状态。
- 纯词典 NotFound 原来绕过快速发布、等模型 StableMs；现在主词典及兜底结束后立即发布终态，纯词典列不进入模型等待。完成集合需包含所有终态，不能在截止时把已经发布的缺词覆盖成超时。
- 未创建/禁用的来源也会发布 `SourceVersion=unavailable` 的失败终态。Runtime 若一概拒绝不存在的 provider，缓冲页可能永远无法提交；只接受其失败终态，绝不接受其 Ready 或伪版本。
- 系统“Windows 模式”和“应用模式”可独立配置。主题应只读 `AppsUseLightTheme`，不能取 `SystemUsesLightTheme` 代替。WM_SETTINGCHANGE/THEMECHANGED/SYSCOLORCHANGE 仅就地重设颜色；高对比度优先系统颜色，自定义列色对比度不足回退可读文字色。
- `Updating` 是 reader/桌面配套新增的 IPC 消息，旧包不要混用新旧 reader。后台发布用 `scripts/Publish.ps1 -BackgroundOnly`，默认发布仍运行完整 Windows 测试；跳过项不能记作通过。
- 日文并排列使用 2 条原创验收词头，仅证明独立语言路由和缺词行为，不是完整日文词库，也不能用英文冒充日文。
- 已修复无词典模型列拖住整页：调度器快查后发布 Pending，等待状态即可提交新页，完整结果后续补齐；已通过离屏及真实模型后台注入快照测试，新交互尚未现场复测。缺失 provider 只接受失败终态，不能接受 Pending 冒充可用来源；新增失败回归已确认并修复。
- 取消不再无条件停止模型：先释放 SSE，宽限内认证 /slots 确认单槽空闲则复用，失败才停止 owned worker；分词前取消不重载。固定 b11077 的 HTTP_POLLING_SECONDS=1，原 300ms 在真实 CPU 上不足，默认已改 1500ms；实测 454–529ms，不宣称即时硬取消。确认空闲不能把无进展故障状态改成就绪，相关失败回归已修复。

## 复用验证环境

在仓库根目录使用 PowerShell 7。以下本地 SDK 和缓存位于被 Git 忽略的 `artifacts`；其他机器或清理后需重新准备 .NET 10 SDK，不能假设文件存在。

```powershell
$env:DOTNET_ROOT = (Resolve-Path 'artifacts/toolchain/dotnet').Path
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'artifacts/toolchain/cli-home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) 'artifacts/nuget'
dotnet test PinyinGloss.sln -c Release -m:1 -p:UseSharedCompilation=false --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'
dotnet build PinyinGloss.sln -c Release -m:1 -p:UseSharedCompilation=false
./scripts/Publish.ps1 -BackgroundOnly
```

以上命令遵守当前暂停桌面约束。测试、构建和发布分别确认退出码；不要在前一步失败后仍把后一步输出作为通过依据。恢复授权后才去掉 filter 或 BackgroundOnly 执行完整桌面测试。

历史证据位置：

- `artifacts/validation/background-final-20261012/`：最新两个 TRX，核心 32、Windows 后台 22 项通过；8 项桌面测试被过滤。
- `artifacts/validation/dictionary-after-fix/`：早期 30 项核心及 4 项 Windows 回归；`dictionary-before-fix/` 保留兜底缺陷失败回归。
- `artifacts/validation/`：隐藏恢复与诊断编码的修复前失败、修复后通过记录。
- `artifacts/reader-computer-use.log`：候选可见期间 reader 未识别容器的诊断。
- `artifacts/uia-visible-candidate.log`：系统输入宿主与 Raw UIA 调查。
- `artifacts/validation/dictionary-uia-events.log`、`uia3-thread-windows.log`：早期入口失败记录，不能作为当前 UIA 不可行的结论；当前入口和证据见 `docs/candidate-reading-next-step.md`。
- `artifacts/validation/desktop-dictionary-20261012/run2.jsonl`、`run3.jsonl`：新界面修复前的真实双来源/三列/故障恢复证据，不能代替新交互复测。

这些文件不随 Git 分发；不得仅靠临时文件保存关键结论。包含候选原文的诊断日志应留在本机，分享前检查内容。

## 后续验收顺序

### 2026-10-12 模型部署后台开发补充

- 用户已要求提供模型下载/自动配置脚本并进行真实后台推理调试；该授权不恢复桌面或 Computer Use 测试。
- `Pending` 是模型等待状态，词典快查结束即可参与新页提交，但不是终态，期限必须继续收尾为超时。新增回归修复前 2 项失败，修复后核心 34、Windows 后台 23 共 57 项通过，退出码 0；`artifacts/validation/llm-waiting-before-fix/`、`llm-waiting-after-fix/`。新显示仍未现场验收。
- 官方 LMT 仓库提供 safetensors；准备脚本固定社区 `mradermacher/LMT-60-1.7B-GGUF` revision `f03d51b43463d8a457eee1cd765220de69e9bac8`、Q8_0 SHA256 `18ff357223b2ee8e43f965d92cc72d37287575660d134aaff2835a5fe5697ac1`，大小 2165039200 字节。模型卡明确 base_model 为非 Base 的 `NiuTrans/LMT-60-1.7B`，但未说明原始 safetensors revision，不能伪称官方发布的 GGUF 或已证明对应原始 commit。
- GitHub 匿名 API 会限流；固定版本下载可直接使用 release 工件 URL，源码可用固定 tag 的 raw URL，不需要查询 latest API。
- 本机 RTX 3050 Laptop 4GB 在本轮开始时仅约 1994MiB 空闲，先用 CPU 调试，不改其他进程或系统设置。下载单连接约 300KB/s，已改固定分块并行，最终必须做完整 SHA256 校验；下载开始不代表推理已通过。
- `InvalidDataException` 不能由 `catch (IOException)` 捕获。安装清单校验失败必须显式捕获，保持词典启动可用；新回归已复现这一点。
- NTFS 下载中目录枚举得到的 Length/LastWriteTime 可能到写句柄关闭才更新。分块长时间显示 16KB 不代表停滞；已用 FileShare.ReadWrite 的只读 FileStream.Length 确认实际持续增长。检查实时字节后再决定停止自建下载进程。
- 自动安装路径解析成绝对路径后若保存，搬动程序目录会留下旧路径。`UseInstalledModel` 标记自动来源，启动时重新解析当前清单；手工修改路径/revision/hash/GPU 后退出自动管理，显式禁用状态始终保留。
- 新用户默认模型准备策略已改为 Auto：CUDA GPU 优先，失败一次后回退独立 CPU 包。CPU 基线仅是历史实测，不能继续当产品默认；旧 Cpu 清单和手工 CPU 配置保留，需重跑默认脚本才能更新自动策略。当前自动 GPU 后端仅 NVIDIA CUDA，不宣称 AMD/Intel 已支持。
- `health` 成功与 UseGpu=true 不能证明 GPU 实际推理。必须核对固定版启动日志的 `offloaded X/Y layers to GPU` 且 X>0；UI 显示实际层数或 CPU 回退原因。加载日志只在内存中有界保存，健康后停止采集，不保留请求文本。模型身份/hash 错误、用户取消及已识别端口冲突不应当作 GPU 初始化失败重试。
- 含中文的 PowerShell 脚本在 Windows PowerShell 5.1 上必须保存为 UTF-8 BOM；无 BOM 被按 ANSI 读取，中文字节可能误成引号并出现伪“缺少右括号/引号”语法错误。生产下载脚本已有 BOM，本轮扩展安装测试时复现并补齐 BOM；不要仅用 PowerShell 7 通过代替 PS5.1 包内脚本验收。
- 用户新增要求量化选择：Setup-LLM.cmd 无参数先显示四选一菜单；Setup-Llm.ps1 的 -Quantization 默认 Q8_0、可明确选 Q6_K/Q5_K_M/Q4_K_M。固定同一 GGUF revision 的大小/hash，各量化文件并存，仅成功后更新清单。运行时必须按所选精度校验 file_type=7/18/17/15；改界面量化不会下载或转换，必须对应文件/hash。较小量化的发布者评价不能代替项目质量验收；Q6/Q5 尚无真实推理验收。
- 大型 CUDA 运行时下载不应放在随机临时文件后中断就从头下载。固定包存于模型目录 .runtime-downloads，curl --continue-at - 续传，完整 SHA256 与固定官方 digest 一致后才展开；坏 hash 删除该包且拒绝安装。检查正在下载文件仍用共享读 FileStream.Length，不能凭目录缓存 Length=0 就杀进程。
- 固定 b11077 的默认日志 verbosity=3 只显示 server 概要；common_log_get_verbosity 将底层 GGML INFO 映射为 TRACE=4，实际 offloaded 层数会被过滤。不能把缺少该行直接诊断为无 GPU/显存不足。GPU 加载必须显式 --log-verbosity 4，健康后停止本机日志采集；新增参数失败回归已确认并修复。验收宿主可显式落盘加载期 gpu-startup.log，不收集真实输入正文。
- 在本机显式从 powershell.exe -File 调用、不传 InstallDirectory 时，param 默认表达式中的 $PSScriptRoot 会为空，Split-Path 失败；cmd 入口一直显式传路径，此前未覆盖此调用。已将默认目录计算移到 param 后的脚本主体；验收必须同时覆盖 cmd 的显式根目录和直接脚本不传目录。
- 本机曾用验收进程 CUDA_VISIBLE_DEVICES=-1 尝试模拟不可用 GPU，但实际仍卸载 29/29 层，不能据该变量假定故障注入成功。最终用隔离的官方 GPU 启动器副本且故意不带实现 DLL 实测 0xC0000135，再成功回退正常独立 CPU 包；原运行时和系统不修改。DLL 错误应显示可读原因，不只输出十进制退出码。

1. 当前只进行后台构建、回归和文档同步，及时记录确认的坑，不自动恢复桌面测试。
2. 用户恢复授权后优先复测旧页保留/整页替换、真正关闭隐藏、系统主题切换和深色可读性，再完成鼠标选词、完整 App 启动/设置、DPI/多屏及其他应用。
3. 保留已验证的真实 UIA/编号/翻页/键盘提交/焦点/词典并排及隔离证据；新改动必须重新验收，不能拿更早记录替代。日文 2 词条仍仅是验收夹具。
4. 模型 Q8/Q4 的 CPU/CUDA 基础后台验收及独立 CPU 回退已通过；继续补 Q6/Q5 推理、其他 GPU/驱动、完整语言质量、原始权重对照和真实桌面模型闭环，不能用当前少量样例替代。
5. 在正确性通过后采集延迟、CPU、内存及模型资源数据，按实测瓶颈优化；同步 `docs/implementation-status.md` 和 `docs/windows-validation.md`。

需要用户实际输入或观察时，给出明确的应用、输入内容、保持时间及预期结果。若 UIA 路线确有不可解决的限制，先提出有明确边界的替代设计，不自行扩展为候选引擎或应用正文采集。

- 默认 exe 同级 LLM 已实际配置于 artifacts/pinyin-gloss-win-x64/，当前为 Auto GPU 优先；Q8/Q4 通过硬链接复用已验证下载，CPU/CUDA 两套 runtime 配套。包内脚本在 Windows PowerShell 5.1 上直接不传目录重跑退出码 0，证据 gpu-20261012/setup-default-exe-fixed.log；主 App 未启动。旧 CPU 发布目录及模型已整体保留于 artifacts/pinyin-gloss-win-x64-cpu-baseline-20261012/，不要删除权重。
- 新发布根目录清单的真实模型验收已退出码 0，确认 Q8_0 GPU 29/29 层、6 组基本词义、5 轮取消同 PID、三页 30/30 Ready 和释放；gpu-20261012/acceptance-default-exe.log。这仍不是正式 App/GUI 首启或真实微软候选模型闭环。zip 仅包含程序与脚本，不包含本机 LLM 或清单。
- 该发布目录现在有 LLM 和清单，Publish.ps1 会为保护权重拒绝清理它。以后重发先把整个旧发布目录移到明确备份位置，不要递归删除模型。下载原件另在 artifacts/LLM/。
- CPU 阶段 67 项后台回归证据为 llm-20261012/publish-final.log；当前 GPU/量化选择阶段 85 项见 gpu-20261012/test-final.log。此前 llm-final-regression/ 的66项是加入超时状态回归前的记录，不能混淆。

### 2026-10-12 GPU 切换前的后台只读调查

- 本机 NVIDIA RTX 3050 Laptop 4096MiB，驱动 576.52。16:39 至 16:44 的总占用从约 2580MiB 变为 1830MiB，最后空闲 2135MiB；这是动态快照，CUDA 验收前必须重新读取。没有启动模型、结束进程或修改应用/系统设置，发布清单仍为 Cpu。
- Windows `GPUProcessMemory` 在本机出现明显失真的进程计数：4GB 卡上 DWM 报约 6GB，进程合计超过 10GB。不能累加各进程计数或把它当作退出应用后的可释放量；总占用交叉核对 `nvidia-smi` 与 `GPUAdapterMemory`。微软也记录了该类计数错误及跨进程共享重复计数，不能据此认定应用显存泄漏。参考 https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/gpu-process-memory-counters-report-wrong-value 和 https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/ 。
- CIM 与 PnP 均只枚举到 NVIDIA 和 OrayIddDriver 虚拟显示设备，未看到 AMD 核显；不能假定“节能”一定切到核显，也不能仅据此断言 BIOS/混合模式设置。联想机型为 82JW，混合模式需用户在空闲时另行确认。
- Excel 在最后快照中已退出；Visual Studio、Codex、企业微信、Clash Verge、CC Switch 仍在。WebView2 应按可执行路径及父链归属宿主，不要批量结束同名进程，以免中断正在使用的应用或代理。
- 证据为 `artifacts/validation/gpu-memory-20261012/snapshot.json`、`snapshot-latest.json`；包含本机进程路径，不随 Git 分发。当前仅给出手动调整建议，GPU 推理仍未验收，Computer Use 暂停不变。

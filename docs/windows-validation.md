# Windows 验收与当前边界

2026-10-26 默认目录发布：已修复中文管道乱码及已有模型时拒绝更新的问题。GBK 调用环境下真实发布退出码 0，核心 99 + Windows 后台 191 = 290 项通过，8 桌面项过滤；发布/Python/原生编码脚本回归共 27 项通过。113 个 LLM 文件、安装清单和旧 ZIP 共 115 个文件的大小及完整 SHA256 均不变，Desktop/reader 的 Core hash 一致。正式 exe、reader 与 CUDA 模型进程已启动，原主界面控件状态为“GPU 就绪（29/29 层）”。Computer Use 的截图显示桌面且窗口激活失败，已停止输入；不将该次启动核对视为窗口显示或完整正文/IME 验收。证据见 `artifacts/validation/publish-fix-20261026/` 及 [实现记录](implementation-status.md)。

2026-10-26 正文显示规则：仅当前快照的非空成功译文展开，其余读取、翻译、组合、加载、空文本及失败全部收成 28×28 DIP 小圆点。详情与最近提示复用原主界面状态栏，失焦后仍保留；未新增产品界面。最终核心 99 + Windows 后台 191 = 290 通过、0 跳过、退出码 0，8 桌面项过滤；Release 0 警告/0 错误，独立只读审查无剩余问题。Windows-MCP 已核对隔离已知状态下的生产原主界面状态栏与真实圆点显示；此项仅证明窗口呈现，不证明真实 Provider/IME/模型完整链路。默认目录已包含此修复，正文现场验收边界未扩大，证据及命令见 [正文翻译测试](text-translation-testing.md)。

2026-10-20 无选区收圈修复：用户报告知乎正常浏览、没有选中文字时正文框仍展开“没有活动选区”。已定位为显示层将 NoSelection 当通用错误，现归入 Collapse。修前 2 项失败；修后完整后台核心 99 + Windows 143 = 242 通过、0 跳过、退出码 0，8 桌面项过滤；Release 与 Desktop 自包含发布 0 警告/0 错误、退出码 0。独立只读审查无必须修项。更新前用户按 Esc 停止桌面控制，未继续操作；运行中的 Desktop 尚未替换，真实网页收圈复测待执行，不能将后台窗口回归等同于知乎实测。新代码已打包于 `artifacts/validation/no-selection-idle-20261020/`，详情见 [正文翻译测试](text-translation-testing.md)。

2026-10-20 Codex 聊天选区接口深度修复：分阶段真实诊断确认，聚焦 Group 的第 16 层 Document 提供 TextPattern，RangeFromChild 成功且选区完整位于原 Group 子范围；原生产只查 8 层。现仅 Group/Selection 查找扩至 64 层，读取范围、密码与前台根拒绝条件不变。新增 9 场景修前 5 失败/4 通过；修后核心 99 + Windows 141 = 240 通过、0 跳过、退出码 0，8 桌面项过滤；Release/reader 发布 0 警告/0 错误。正式程序与 zip 已更新，正常重启后 GPU 29/29 就绪；新版生产链路实际读到 Group/RangeFromChild/Selection，用户完成聊天选区测试并确认“正常了”。正式 reader 多次核对保持同 PID。自动诊断不记录正文，翻译内容的现场确认来自用户。

前一阶段已修复 Legacy.GetIAccessible 返回 null 后的空引用崩溃，后台 231 项通过，正式包更新重启后真实 Group/IA2 路径连续 105 次安全返回 NoSuitableProvider、reader 同 PID。此前一次未分阶段的 InvalidOperationException 不能证明 RangeFromChild 不支持，已由上述分阶段结果纠正。崩溃防护与选区实际翻译分别验收，详情见 [正文翻译测试](text-translation-testing.md)。

2026-10-20 企业微信 5.0.12.6018 追加实测：通用设置已查看到底，无相关无障碍开关，未更改选项。输入区焦点及原生光标存在，双方均为普通权限；光标处 UIA/MSAA/IA2/IAccessibleEx 未提供可用文本对象。当前直接只读范围内仍不支持该编辑区。本轮只有诊断与文档更新，没有新的生产修复或回归计数；用户已确认新版窗口显示不支持提示，不能记为正文翻译通过。

2026-10-20 正式包已同步 10-19 的跨应用修复：后台 230 项通过（核心 99、Windows 131），退出码 0，8 桌面项过滤；Release 0 警告/0 错误，reader 自包含发布退出码 0。更新前确认无运行进程，旧 reader/zip 已备份，正式目录和压缩包内 reader SHA256 均与最新发布一致，模型大小及完整 SHA256 正确。尚未启动新程序，Codex 实际悬浮窗仍待验证。企业微信本机自绘输入区未暴露可用的直接文本接口，只补充明确的不支持提示，不记为翻译通过。详情见 [正文翻译测试](text-translation-testing.md)。

2026-10-19 contenteditable 末尾修复：真实只读诊断确认文字子节点归属误拒绝，修后完整后台 220 项通过（核心 99、Windows 121），退出码 0，8 桌面项过滤；Release 0 警告/0 错误。正式 reader 与 zip 已更新，正常重启后真实 Edge 编辑区末尾、追加标点/退格、开头、完整选区、失焦清空及末尾恢复均通过。原测试内容已恢复；真实 IME 组合验收尚未完成。详细证据见 [正文翻译测试](text-translation-testing.md)。

2026-10-19 普通输入框与只读选区阶段：后台回归核心 99 + Windows 后台 116 = 215 项通过，退出码 0；8 桌面项过滤。Release 0 警告/0 错误。Edge 本机专用页已真实验证当前句、输入框双句选区、多行选区、非文本焦点清空和只读 Group 选区，Q4 GPU 29/29 层。原站读取被自动审批拒绝，真实拼音按键受 Computer Use URL 识别保护终止，均未验收。证据、范围及复现见 [正文翻译测试](text-translation-testing.md)。

以下为 2026-10-14 统一读取器阶段的记录。

最新活动 TextReader 的 `final-reviewed-background` 与后台发布均为核心 99 + Windows 后台 103 = 202 项通过，退出码 0；8 项桌面测试被过滤，不计通过。Release `build-reviewed-final` 为 0 警告/0 错误。IPC 3 配套 exe/zip 已更新，113 个 LLM 文件硬链接复用，Q4 完整大小/hash 正确；旧包保留于 `artifacts/pinyin-gloss-win-x64-before-active-text-20261014`。

本轮未启动新 App。Edge 新标签连接失败后，Computer Use 激活现有 Edge 又因无法可靠确定 URL 而终止，已停止桌面输入，未换工具绕过。新 TextReader 的 Edge/Codex/Notepad3、Win32 评估及真实 IME 验收均未完成；不能将下列旧版本实测或范围替身当作新架构现场通过。

以下为此前版本的实测与验收记录。

截至 2026-10-14，本机普通权限 Notepad3 已打通真实微软候选、词典/模型释义及完整 7 行编号；正式 App Q4 首启已验收。微软记事本另已验证当前句和鼠标选区翻译、非激活拖动/缩放与几何保存、冷启动自动刷新。其他应用、完整主题切换和跨屏仍待测，不标稳定版。

此前完整发布回归核心 73 项、Windows 57 项，共 130 项通过、0 跳过、退出码 0（publish-final-resume-20261014）；Release 构建 0 警告/0 错误，PS5.1 安装脚本 12 项通过。后台回归 122 项通过，另过滤 7 项可见窗口测试及 1 项桌面诊断，不计通过。完整 CC-CEDICT 曾独立验收通过。正文用例、首次发布偶发 UIA COM 异常及复查、全部证据和待测项集中在 [正文翻译测试](text-translation-testing.md)。

live8 已验证真实微软拼音组合时保留正文旧译文、空格提交后候选隐藏但正文保持展开，以及两轮菜单失焦/编辑框恢复。只读网页仍未验证：Computer Use 无法可靠确认 Edge 当前网址而终止，已停止界面操作，未切换工具绕过。最新 exe/zip 已生成；本机模型复用校验通过，zip 不含权重和本机安装清单。

用户已授权真实桌面验收，使用专用测试文档，Windows-MCP 报告真人控制或操作异常时停止当轮输入。词典用例见 [本地词典基础功能测试](local-dictionary-testing.md)。真实 Q8/Q4 CPU/CUDA 加载、基本词义、取消复用及缺 DLL 后 CPU 回退已验收，见[模型准备与实测](model-setup.md)；完整语言质量与其他 GPU/驱动兼容未验收。

后台命令：`dotnet test PinyinGloss.sln -c Release --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'`；目录发布用 `./scripts/Publish.ps1 -BackgroundOnly`，不生成 ZIP。暂停期间不得直接运行不带 filter 的完整测试、默认发布或可见验收宿主。新旧 Desktop/reader 不混用。

## 首先验证候选读取

记录 Windows build、微软拼音版本/设置、显示语言、应用及权限级别。运行 --diagnose，切换微软拼音输入，检查：

- 当前页中文、序号/顺序、候选窗边界；选中项未暴露时不得假造。
- 快速键入、删除、翻页、数字选择、鼠标选择、候选关闭。
- 切换应用立即隐藏，不复用旧窗身份；同文本重新开窗也重新识别。
- 记事本、浏览器、Office、Electron 和终端分别测试。
- 失败时明确记录“未识别候选容器 / 无可见文本”，不扩大读取到应用正文。

实现只接受受保护 Windows 路径下的 TextInputHost/InputApp/MicrosoftIme/ChsIME，匹配带 candidate 标识或明确“候选”列表的 UIA 子树。这是保守启发式，不是已知稳定的微软协议，可能完全无法读取某些 build。若失败，先依据匿名结构调整只读识别；可另行评估局部 OCR，不改成自有输入法。

本机入口为原生 desktop subtree MenuOpened 事件及其有界 Raw 父链，顶层窗口枚举遗漏的宿主也能定位；候选项编号/文字使用有界 RawView。50ms 一致页确认防止混合过渡页。用户当前明确不增加 OCR，继续 UIA 方向；已确认坑见 [AGENTS.md](../AGENTS.md)。

## 输入与窗口

- 数字键、空格、翻页、鼠标与中文提交始终由微软拼音处理。
- 翻译窗显示时键盘焦点仍在原应用；鼠标点击穿透，不遮盖候选。
- 100%/150%/200% DPI、跨显示器、负坐标、屏幕上下/左右边缘。
- 浅色/深色、高对比度，列标题始终说明语言与来源。
- 连续输入/退格/翻页时旧页完整保留，新页中文、编号及释义一次替换，无隐藏再显示；新结果不能与旧候选混合。Esc/选词/切应用是真正隐藏，不受缓冲影响。
- 默认跟随 Windows 的应用深浅色（AppsUseLightTheme），区别于 Windows shell 模式。关闭跟随后手动深色有效；主题变更不重载来源或抢焦点，背景/边框/候选/释义均可读；低对比度自定义列色回退可读色。
- 设置关闭继续托管；暂停、退出、强制结束主进程后，原输入法正常，owned 子进程无残留。

## 模型 / 多语言 / 故障

- 默认 LMT Q4_K_M 路径、revision、SHA256、实际 token 预算和冷/热启动；其他量化分别验收。
- 同源 en/ja 并排、同语言不同源颜色、列排序、提示词覆盖。
- 只具英文词典时日文超时绝不显示英文兜底。
- 故意慢模型、断开 SSE、空/思考/损坏输出、冷却、正常取消。
- UIA 卡住时看门狗清空翻译窗并重启 reader；设置窗口保持响应。
- 显存不足、模型进程退出、端口占用、SHA256/量化不匹配。
- 未显式启用在线来源时没有网络推理；在线凭据不进入配置导出。

## 首版与设计稿的差距

设计稿是完整目标，本次为实验开发版。尚未完成应用启停名单、持久缓存、全面 Health/Metrics、批量/外部插件进程、原始 token 数展示、完整多语言词库及质量评估和全部 Windows 界面测试。OCR 未实现且当前不开展。取消先确认槽位空闲，确认成功复用 worker，失败才停止；固定版轮询为 1 秒，默认取消宽限据实测改为 1500ms。保存设置会重建组件；系统主题广播仅就地重着色，无需保存或重载来源。

稳定版门槛是先在目标系统可靠读取真实候选，再完成上面的焦点、映射、取消和性能验收。诊断/模拟结果不能替代这些条件。

## 本机验证记录

| 项目 | 结果与范围 |
| --- | --- |
| 系统/工具链 | Windows build 26200.9457；本机隔离 .NET SDK 10.0.302，官方 SHA512 已校验；普通权限 Notepad3 |
| 后台回归 | 54 项通过，退出码 0；artifacts/validation/background-final-20261012/；8 项桌面测试被排除 |
| Release 构建 | 退出码 0、0 警告/0 错误；artifacts/validation/background-build-20261012.log |
| 自包含打包 | Publish.ps1 -BackgroundOnly 退出码 0，54 项后台测试通过；artifacts/pinyin-gloss-win-x64/ 及同名 zip，Desktop/reader 配套；background-publish-20261012.log；未运行包内 exe |
| 完整词库 | CC-CEDICT 203053 条 SQLite 记录；双英文来源+原创日文 2 词条；语言隔离、缺词、兜底和原子回滚通过 |
| 真实输入 | run2/run3 证据在 artifacts/validation/desktop-dictionary-20261012/；在新增页面缓冲/主题修改前执行，新交互待现场复测 |
| 后台调度性能 | n=40、三列七候选、无缓存：p50 14.295ms、p95 15.335ms；840 个终态无缺失/重复，不含 UIA/屏幕 |
| 量化门槛 | 2 秒读取、500ms 隐藏尚未取得完整可信按键计时；不因观察行为正确而直接宣布时限通过 |

后台发布不代表新的窗口交互已验收，恢复后按 W04/W05 及剩余清单验证。

## 真实模型后台验收（2026-10-12）

此前固定 Q8_0/revision/hash 下载校验、CPU 加载与 6 组基本词义、5 轮忙槽取消/同 PID 复用、默认期限收尾及显式释放均通过，Model.Acceptance 退出码 0。该 CPU 基线在 1500ms 内完成 19/30 译文，其余明确超时。后续 GPU 结果见下节。新模型与界面的真实微软拼音闭环、GUI 首启和完整质量仍待验收。

此前 CPU 阶段构建/发布退出码 0，构建 0 警告/0 错误；67 项后台回归（核心 43、Windows 24）通过，8 项桌面测试过滤不计通过。发布 zip 核对 8 个必须文件、3 个 DLL 与目录一致，包含下载脚本和许可，不包含模型权重或本机安装清单。历史证据在 artifacts/validation/llm-20261012/。

## GPU 优先与量化选择验收（2026-10-12）

默认准备 CUDA+CPU 两套运行时；真实 RTX 3050 / CUDA 12.4 对 Q8_0 和 Q4_K_M 均卸载 29/29 层，6 组基本词义、5 轮忙槽取消/同 PID 复用、释放通过。三页双语言各 30/30 Ready，0 超时，原 1500ms 期限不变。短词请求 Q8 为 44–138ms、Q4 为 34–78ms，仅后台口径。独立 CPU 回退使用隔离缺 DLL 启动器故障注入，实际捕获 0xC0000135 并回退成功，Model.Acceptance 退出码 0。

最终回归 85 项通过（核心 61、Windows 24），8 项桌面测试排除；PS5.1 脚本 11 项通过，Release 0 警告/0 错误，后台发布退出码 0。证据 artifacts/validation/gpu-20261012/test-final.log、build-final.log、publish.log、script-tests-final.log 及 q8-gpu-fixed/q4-gpu-final/cpu-fallback-confirmed 的 results.jsonl。详细命令、性能和量化选择见[模型准备文档](model-setup.md)。

Q6/Q5 选择、清单与文件精度校验通过回归，未真实推理；AMD/Intel GPU 后端未接入。不得把少量基本词义推广为完整语言质量，或把后台结果代替现场窗口/焦点/主题/新用户 GUI 首启验收。

# 0.1 实现与验证记录

更新：2026-10-20。保持微软拼音外挂路线，不增加 OCR。用户已恢复真实桌面验收授权；Windows-MCP 检测到真人输入时暂停操作。验收结论以本机记录为准，不将历史远程 CI 结果当作当前代码验证。

## 最新无选区显示修复（2026-10-20）

- 正常浏览只读网页时，NoSelection 原落入正文框的通用错误展开分支，持续显示“没有活动选区”。现仅将其归入收回圆圈的分支；Runtime 原有取消任务、清快照与迟到结果校验继续生效，真正读取失败仍显示原因。
- 初始浏览与已有译文两场景修前 2 失败/0 通过，退出码 1；修后核心 99、Windows 后台 143，共 242 通过，0 跳过、退出码 0，8 桌面项过滤。Release 与 Desktop 自包含发布退出码 0，0 警告/0 错误，独立只读审查无必须修项。
- 新 Desktop 已构建并准备独立更新包。准备正式替换时用户按 Esc 停止桌面控制，已停止桌面调用；运行中的 Desktop 尚未更新，真实知乎无选区收圈尚未复测。当前正式 reader 的 Codex 选区修复仍保留。证据见 [正文翻译测试](text-translation-testing.md)。

## 最新跨应用正文诊断（2026-10-20）

- Codex 聊天 Group 的 TextPattern 位于第 16 层，原生产接口查找只有 8 层。分阶段真实诊断确认 RangeFromChild(原 Group) 成功且选区完整位于子范围，纠正此前一次未分阶段异常的归因。现仅在 Group/Selection 模式查找最多 64 层，仍限定原子范围；其他角色及跨界、密码拒绝条件不变。新增 9 场景修前 5 失败/4 通过，修后完整后台核心 99 + Windows 141 = 240 通过、退出码 0，8 桌面项过滤；Release 与 reader 发布 0 警告/0 错误。正式程序和 zip 已更新，新版正常重启、GPU 29/29 就绪；生产链路已实际采到 Group/RangeFromChild/Selection Success，用户现场确认聊天选区翻译“正常了”。证据见 [正文翻译测试](text-translation-testing.md)。
- 前一阶段 IA2 空 IAccessible 导致读取器崩溃与 30 秒冷却已修复：仅空对象→NoSuitableProvider，不调整冷却。该阶段修前 1 失败/0 通过，修后后台 231 通过（99+132）；正式程序已更新该防护。真实 Group/IA2 路径连续 105 次安全返回 NoSuitableProvider、无异常，不能将无崩溃等同于选区翻译通过。
- Codex 聚焦 Edit 到原生前台窗口有 24 层 Raw 父链，原 12 层身份核对导致 TargetChanged；已仅将窗口身份核对限额调整为 64，正文范围不扩大。企业微信当前自绘输入区未提供可用的直接文本接口，新增明确的 UnsupportedControl 失败提示，不将其记为翻译支持。
- 2026-10-20 企业微信 5.0.12.6018 现场复查：通用设置已查看到底，未发现无障碍开关，未更改选项。原输入光标处 UIA/MSAA/IA2/IAccessibleEx 无可用文本对象，双方均为普通权限级别；当前直接只读边界内仍不支持，未新增生产修复。详情及证据见 [正文翻译测试](text-translation-testing.md)。
- 最终后台核心 99 + Windows 131 = 230 项通过，退出码 0，8 桌面项过滤；Release 0 警告/0 错误，reader 自包含发布退出码 0。2026-10-20 确认正式目录无运行进程后，备份旧 reader/zip 并更新正式目录及压缩包，包内外 reader SHA256 与已发布版本一致，Q4 大小及完整 SHA256 正确。尚未启动新程序，Codex 现场验收待执行；企业微信菜单仅获准查看。详情见 [正文翻译测试](text-translation-testing.md)。

## 最新网页修复与证据（2026-10-19）

- contenteditable 末尾已复现为文字子节点归属导致误拒绝。现以有界父链核对原 Edit，密码、其他 Edit、过深父链仍拒绝；生产 Capture 回归修前 1 失败/4 通过，修后完整后台 220 通过（核心 99、Windows 121），退出码 0，8 桌面项过滤。Release 0 警告/0 错误，正式 reader 与 zip 已更新。正式 App 正常重启后的真实 Edge 编辑区末尾、追加标点/退格、开头、完整选区、失焦清空与末尾恢复均通过；测试内容已恢复。真实拼音组合验收尚未完成，不能用本轮普通字符输入替代。
- 修复同一聚焦 Edit 的空边界位置被误判越界；原 scope、原始 caret 和跨控件拒绝仍保留。
- UIA 与其他 Provider 统一使用已验证微软候选状态，避免浏览器历史组合范围导致一直“输入中”。只读 Group 仅允许当前非空选区，并限定子范围，不能触发句子读取。
- 普通输入框与只读选区阶段后台为核心 99 + Windows 后台 116 = 215 项通过，退出码 0，8 项桌面测试过滤；Release 0 警告/0 错误，reader 自包含发布退出码 0。
- Edge 本机专用页的当前句、双句选区、多行选区、非文本焦点清空、只读段落选区已通过真实生产链路与 Q4 悬浮窗验证；GPU 29/29。原站受自动审批拒绝，真实拼音按键又受 URL 识别保护终止，两项均未计通过。详情见 [正文翻译测试](text-translation-testing.md)。

## 统一读取器阶段证据（2026-10-14）

- 最新统一 `TextReader.ReadAsync` 已接入目标解析、UIA 活动 scope、能力路由和唯一 `SentenceExtractor`；UIA、IA2、Scintilla 返回同一种有界快照。借用父级模式必须用 `RangeFromChild`，越界 caret/selection 读取前拒绝；最终文本最多 256 UTF-16 字符，局部窗口最多 514。
- 正文失败具有独立状态、默认无原文诊断和 IPC 3；目标失效清空，旧目标的迟到状态及无有效快照的 Reading 不得重新展开。独立审查发现的空文本恢复旧译文、同长度混合采集及 Scintilla 前置 UTF-16 预算缺口均已用失败回归修复；U+FFFC 对象占位符明确拒绝。
- 最终 `final-reviewed-background`：核心 99 + Windows 后台 103 = 202 项通过，0 跳过、退出码 0；8 项桌面测试过滤。`build-reviewed-final`：0 警告/0 错误、退出码 0。`Publish.ps1 -BackgroundOnly` 配套发布再次 202 项通过，退出码 0。
- 正式目录 `artifacts/pinyin-gloss-win-x64` 与 zip 已更新；旧目录及旧 zip 保留为 `pinyin-gloss-win-x64-before-active-text-20261014`。113 个 LLM 文件硬链接复用，Q4 大小 1282439264、完整 SHA256 与固定清单一致；zip 不含权重/本机安装清单。本轮未启动新 App。
- 新一轮 Edge 连接仍失败，Computer Use 又因无法可靠识别浏览器 URL 而终止；停止桌面输入，未换工具绕过。新架构真实 Edge/Codex/Notepad3、Win32 评估和 IME 回归未完成。IA2 的非编辑节点接口探测及范围替身不等于这些应用已支持。详细记录见 [正文翻译测试](text-translation-testing.md)。

以下正文实测属于此前架构及相应发布，不作为新 TextReader 的现场通过证据。

- 已实现默认关闭的句子/选区开关、右下角独立正文框、选区优先、失效清空圆圈、非激活拖动缩放及几何保存；两框共享主题、Microsoft YaHei UI、14 DIP 字号和展开圆角。
- 微软记事本 11.2607.14.0 的当前句、鼠标选区、取消选区、冷启动自动刷新、拖动/缩放及输入焦点保持真实通过。live7 中键盘焦点始终在原编辑框，选区保持蓝色高亮，尺寸保存成功。未将 Notepad3 或范围替身测试冒充该结论。
- 修复了再次展开的 WPF 逻辑父节点崩溃、组合期旧译文清除、英文句界混读、跨换行漏译、全局暂停遗漏正文及冷启动模型就绪后不刷新。
- 此前完整发布回归 `publish-final-resume-20261014`：核心 73、Windows 57，共 130 项通过，0 跳过、退出码 0；当时后台回归 122 项通过、另 8 项过滤。Release 构建 0 警告/0 错误，PS5.1 安装脚本 12 项通过、退出码 0。真实 Q4 正文 15 次请求 p50 97ms、p95 149ms，3 轮取消保留同 PID；仅后台口径，不含 UIA/合并等待/屏幕。
- 当前默认 Q4_K_M、GPU 优先，已有正式首启和真实 7 候选完整编号证据。专名“平果”等模型质量限制仍存在，不能用格式通过替代语义质量。
- 审查补齐正文焦点角色限制及超工作区尺寸约束；最新版 exe/zip 已生成，本机 Q4 哈希核对正确。live8 真实通过两轮菜单清空/再次聚焦恢复及微软拼音 pingguo 组合/空格提交；提交后候选隐藏、正文仍展开。
- 只读网页选区因 Computer Use 无法可靠确认 Edge 当前网址而停止，未完成；系统主题切换、跨屏及完整开关操作仍待验收。完整用例、首次完整回归中偶发 COM 异常及复查结果见 [正文翻译测试](text-translation-testing.md)。

以下为此前分阶段记录，命令、数量与桌面暂停状态仅属于相应历史日期。

## 已实现

- 独立 MTA/UIA reader：原生 desktop 事件发现宿主、有界 Raw 候选编号/文字读取、受保护路径校验、50ms 一致页确认、只读 WinEvent、当前用户有界 IPC、看门狗和重启。
- WPF 中文设置、托盘、关闭后台、登录启动、暂停/词典模式；释义窗非激活、点击穿透。
- Updating 保留旧页，词典快查后用结果或模型 Pending 组成新页并整体替换，随后原位补齐完整模型译文；Hidden 清空旧页及未完成页，旧结果严格按快照/世代/配置/候选/语言过滤。
- 默认跟随 Windows 应用深浅色，支持手动选择；主题消息就地重着色，高对比度用系统颜色，低对比度列色回退可读前景。
- 多来源、多语言列、同语言词典兜底、来源标记、颜色、列顺序及提示词；有界内存缓存、取消、单槽和期限。
- SQLite TSV 导入及原子包替换，随包只有 15 条原创中英示例。独立 CC-CEDICT：125251 原始记录、125261 TSV 行、203053 SQLite 记录（含繁简别名去重），CC BY-SA 4.0，不随 MIT 包分发。
- 模型下载、固定版本/hash、exe 同级 LLM 默认目录及自定义路径、首次自动配置已实现。默认 GPU 优先、初始化失败回退独立 CPU 包；Q8_0/Q6_K/Q5_K_M/Q4_K_M 可明确选择。真实 Q8/Q4 的 RTX 3050 CUDA 基本词义、取消复用及缺 DLL 后 CPU 回退通过，在线默认关闭。

## 自动验证

| 验证 | 实际结果与证据 |
| --- | --- |
| UIA 稳定页修复后的完整回归 | 历史 40 项通过：核心 30、Windows 10；退出码 0，artifacts/validation/uia-stability-after-fix-20261012/ |
| 词典缺词延迟修复 | 新回归修复前超时失败，修复后核心 32 项通过；Ready/NotFound 均不等待模型 StableMs；dictionary-delay-before-fix-20261012/ 与 dictionary-delay-after-fix-20261012/ |
| 更新状态回归 | 原逻辑把更新当 Hidden，2 项失败；修复后通过，updating-before-fix-20261012/ |
| 页面缓冲与主题回归 | 修复前离屏测试 8 失败、10 通过；修复后 18 通过，overlay-theme-before-fix-20261012/ 与 overlay-theme-after-fix-20261012/ |
| 界面修复历史后台回归 | 核心 32、Windows 后台 22，共 54 项通过，退出码 0；artifacts/validation/background-final-20261012/。8 项桌面测试被 filter 排除，不计作通过 |
| 完整词库验收 | 繁简、多义、缺词、语言隔离、损坏导入回滚、双来源/兜底、原创日文列均 PASS，退出码 0；artifacts/validation/dictionary-background-20261012.log |

后台命令（本机 SDK 环境见 AGENTS.md）：

```powershell
dotnet test PinyinGloss.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture' --logger trx --results-directory artifacts/validation/background-final-20261012
dotnet build PinyinGloss.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
./scripts/Publish.ps1 -BackgroundOnly
```

InteractiveDesktop 包含 3 项窗口测试、4 项自建 UIA 页测试；DesktopCapture 包含 1 项 reader 诊断。暂停期间不执行，不启动可见验收宿主。最终构建/打包结果见 [Windows 验收](windows-validation.md)。历史 NU1900 表示漏洞源连接失败，不能声称已完成漏洞审计。

## 真实输入已验证范围

Windows build 26200.9457、普通权限 Notepad3 专用空文档，使用生产 ReaderSupervisor、IPC、Runtime、调度器和释义窗；未注入候选、覆盖用户配置或更改登录启动。

- 逐键 p → pi → pin → ping → pingg → pinggu → pingguo，退格再补字母，PageDown/PageUp 翻页及恢复，原文、编号和释义对应。
- 本次 pingguo 为 1 苹果、2 平果、3 平锅、4 评过、5 🍎、6 平过、7 平；baoxian 为 1 保鲜、2 保险、3 包线。排序以现场为准，不能沿用历史截图词序。
- CC-CEDICT 英文与原创对照英文并排，主源缺词回落同语言词典并标记实际来源；包线无释义。
- 原创日文 2 词条列显示 苹果→りんご、保险→保険；日文缺词不填英文。仅证明路由/隔离，不是完整日文词库或翻译质量验收。
- 空格提交苹果、数字 2 提交保险，Esc 取消，切应用隐藏且返回不恢复旧结果；释义窗 IsActive=false，前台始终是输入应用。
- 本轮拥有的 reader 结束后旧窗清空、监督器重启，重新输入恢复。

证据：artifacts/validation/desktop-dictionary-20261012/run2.jsonl（双来源）及 run3.jsonl（三列/故障恢复）。以上在新增界面缓冲和主题修改之前执行；新交互尚未现场复测。2 秒读取、500ms 隐藏门槛没有完整可信按键计时，不将可见正确行为等同于量化时限通过。

## 性能与余项

- 最新完整词库纯查询 n=40：p50 0.329ms、p95 0.374ms、max 0.513ms。
- 修复后后台三列七候选、关闭缓存，模型 StableMs 故意设为 2000ms：40 页共 840 个终态，无重复/缺失；整页调度 p50 14.295ms、p95 15.335ms、max 17.706ms。仅调度与词典，不含 UIA/屏幕显示。
- 修改前真实 32 页“主进程收到快照→自身文本树无等待”p50 127.924ms、p95 147.743ms、max 148.782ms，20ms 采样。口径不同，不能直接宣称屏幕性能提升倍数。
- 鼠标选词受 Computer Use 跨进程目标校验限制，未完成；恢复后手动验收。DPI/多屏、其他应用、设置保存/托盘/开机项、完整 App 启动、新页面稳定显示和主题切换待现场验证。
- 真实 Q8/Q4 CPU/CUDA 加载及少量基本词义已验收；完整语言质量、原始权重对照、Q6/Q5 推理与其他 GPU/驱动兼容仍未验收。UIA 本机通过不代表所有 Windows build 兼容。

详细用例和恢复操作见 [本地词典测试](local-dictionary-testing.md)，已确认坑及时维护于仓库 [AGENTS.md](../AGENTS.md)。

## 模型后台接入（2026-10-12）

真实下载和生产默认模型验收退出码均为 0，来源/命令/身份及详细结果见[模型准备与实测](model-setup.md)。加载含 hash 检查 6.029 秒，11 次请求 172–381ms；5 轮取消复用同一 PID，清理 454–529ms。默认取消宽限据实测从 300 改为 1500ms，旧已保存自定义设置保持原值。5 候选×2 语言、无缓存、仍用 1500ms 页期限：19/30 译文完成、11/30 超时，全部格有终态。等待页组成 0.339–18.364ms，仅后台调度，不含 UIA/屏幕。

此前 CPU 安装阶段 Windows PowerShell 5.1 脚本幂等校验退出码 0，2 项路径保护回归通过。新增 GPU/量化选择阶段已扩展为 10 项，结果见下节。新用户 GUI 首启和完整语言质量仍未验收。

此前 CPU 阶段后台发布回归：核心 43、Windows 后台 24，共 67 项通过，退出码 0；8 项桌面测试排除。Release 构建 0 警告/0 错误，Publish.ps1 -BackgroundOnly 退出码 0。证据：artifacts/validation/llm-20261012/build-final.log、publish-final.log、package-check.json。主程序未启动。

## GPU 优先与量化选择（2026-10-12）

默认 Auto 准备 CUDA/CPU 两套固定运行时，GPU 初始化失败只回退一次；hash/身份错误、取消及已识别端口冲突不作为 GPU 回退。实际 GPU 就绪必须确认卸载层数大于 0，CPU 状态保留回退原因；当前仅接入 NVIDIA CUDA。安装菜单及 -Quantization 支持四种明确精度，各文件并存、成功后才更新清单，不自动降低精度。

Q8/Q4 均完整下载与 hash 校验，真实 CUDA 卸载 29/29 层，11 次请求分别 44–138ms、34–78ms，5 轮取消均保留进程，三轮双语言各 30/30 Ready。缺 DLL 故障注入真实回退独立 CPU 通过，原因显示 0xC0000135；单槽、FP16 KV、512 context、1500ms 页期限保持固定。详细口径和命令见[模型验收](model-setup.md)，后台调度不能替代屏幕延迟。

本轮定位并修复默认日志过滤 GPU 卸载信息：b11077 底层 INFO 被映射 TRACE=4，加载显式使用 --log-verbosity 4；健康后停止日志采集。失败证据 gpu-before.log（4 失败）、quant-before.log（3 失败）、log-before.log（1 失败）、dependency-before.log（1 失败）均保留在 artifacts/validation/gpu-20261012/ 或 llm-20261012/。

最终后台回归 85 项通过（核心 61、Windows 24），退出码 0，8 项桌面测试过滤；PS5.1 安装脚本 11 项通过，包含直接不传目录调用。Release 0 警告/0 错误；Publish.ps1 -BackgroundOnly 退出码 0。证据 artifacts/validation/gpu-20261012/test-final.log、build-final.log、publish.log、script-tests-final.log。Q6/Q5 仅完成选择/精度校验回归，未真实推理；完整质量、GUI 首启及新模型桌面闭环待恢复授权后验收。

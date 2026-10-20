# 本地词典基础功能测试

更新：2026-10-12。目标是在不启用任何模型或在线来源的情况下，验证“真实微软拼音候选 → 读取器 → 本地词典 → 独立释义窗”。本机 Notepad3 已取得真实闭环证据；鼠标、完整 App、DPI 和新界面效果尚未完成。模拟候选、离屏回归与真实输入分别记录。

当前用户暂停所有桌面控制。以下 Computer Use/主题/DPI 步骤是恢复授权后的验收清单；现在只运行后台测试，不自动操作用户桌面。

## 词典选择与来源

主要验收词典采用 [MDBG 官方 CC-CEDICT](https://www.mdbg.net/chinese/dictionary?page=cedict)。它提供可下载的中英数据，官方列出 Pleco、Hanping 等采用者；这是选择成熟开放词典的依据，不声称有证明“多数用户最喜欢”的投票统计。相比规模更大、结构更复杂的 [Wiktionary/Kaikki](https://kaikki.org/dictionary/Chinese/index.html)，CC-CEDICT 更适合本轮中文词头的精确查词。

- 官方下载：<https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz>。
- 下载文件声明：2026-10-10T02:36:17Z，125251 条原始记录。
- 压缩文件 SHA256：`a2391e1d599fab524acea58c2437ea0bb07e77f3152c2dbb7182813d288ef435`。
- 解压文本 SHA256：`b7188f3f214aa26bcc501a67b0dff5b2a72eb3519ea1a24136dbaf8847f037ea`。
- 作者：CC-CEDICT contributors；发布者：MDBG；许可：[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)，以本次原始文件声明为准。格式转换后的数据继续保留署名、来源、许可和转换说明，不归入项目 MIT。
- 原始文件、转换后的 TSV、SQLite 和清单保存在 `artifacts/dictionaries/cc-cedict/`，不提交完整词库。
- 第二来源使用项目原创 MIT 示例或验收对照词典，用于同语言并排对照；缺词应明确显示无释义。其他语言先测试隔离行为，不能把英文当成其他语言释义。

CC-CEDICT 中“苹果”既含 Apple 公司也含水果义项；不能只因释义不是唯一的 `apple` 判失败。“包线”本次原始词典未检索到，预期允许无释义，不能杜撰。

## 环境与准备

1. 记录 Windows build、实际编辑器名称及权限、微软拼音模式、显示缩放和显示器数量。启动 `notepad.exe` 后如果出现 Notepad3，按 Notepad3 记录。
2. 按 `AGENTS.md` 选择 .NET 10 SDK，运行 Release 测试、构建和发布，分别记录退出码。
3. 从官方地址下载并校验文件，转换为项目 UTF-8 TSV，再经 `DictionaryStore.ImportAsync` 导入。核对清单、记录数量、繁简体、重复词头多义项、缺词及语言隔离。
4. 配置只启用 LocalDictionary，关闭模型、在线来源和预加载。优先使用 `tests/PinyinGloss.Windows.Acceptance` 隔离宿主（不加入 CI），从 artifacts 读取专用配置和数据，复用生产 Runtime；不调用正式 App.OnStartup 或 SaveAsync，不覆盖用户设置/开机项。完整 App 启动另行验收。
5. 恢复桌面验收后才启动 GUI 宿主和有时限的 reader。只对固定测试词显式使用 `--include-text`，日志留本机；每次核对 PID 和日志写入时间，限时进程结束后不能拿旧日志判断新输入。

完整词库的程序化验收可以单独执行，无需修改用户设置或启用模型。下载并解压为 `cedict.txt` 后，在仓库根目录执行：

```powershell
./scripts/Convert-Cedict.ps1 -InputPath artifacts/dictionaries/cc-cedict/cedict.txt -OutputPath artifacts/dictionaries/cc-cedict/cc-cedict.tsv
./tests/scripts/Convert-Cedict.Tests.ps1
dotnet test PinyinGloss.sln -c Release -m:1 -p:UseSharedCompilation=false --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'
dotnet run --project tests/PinyinGloss.Dictionary.Acceptance -c Release -- "$PWD/artifacts/dictionaries/cc-cedict/cc-cedict.tsv" "$PWD/artifacts/dictionaries/cc-cedict/acceptance"
```

最后一个项目显式执行，不加入普通测试/CI 下载流程。它使用生产 `DictionaryStore`、`ProviderFactory` 和 `TranslationCoordinator`，输出三列配置：完整 CC-CEDICT 英文、原创对照英文、原创日文（2 个词头）。两个原创词典仅作验收夹具，不冒充其他完整第三方词库。程序还验证关闭缓存的三列七候选调度，不创建界面或读取桌面。

后台发布使用 `./scripts/Publish.ps1 -BackgroundOnly`，默认发布脚本会运行窗口测试。被 filter 排除的 7 项可见窗口测试和 1 项桌面诊断不计通过；恢复后才能运行不带 filter 的完整测试。

## Computer Use 执行和停止规则

- 只在专用空白测试文档中输入，不接管用户已有未保存文档。先观察并确认编辑区焦点，再逐键使用 `press_key` 输入拼音；`type_text` 的字面粘贴不能证明触发了真实 IME。
- 每次操作后重新观察截图及相关状态。先通过第一个真实候选读取用例，才能继续选词、翻页、隐藏等后续用例。
- 一旦出现与预期不符的情况，立即停止当前 Computer Use 操作序列，不继续键入、不盲目重试、不继续后续用例。记录失败步骤、观察及诊断，再转回代码分析。
- 修复后先执行针对该问题的回归；通过后重新开始失败用例。用户物理 Esc 停止或工具报告停止时，停止桌面输入并按工具要求结束操作。
- 这里“退出 Computer Use”指停止自动控制，不是强制关闭编辑器或卸载插件，不丢弃用户文档。

## 用例与通过标准

| ID | 操作 | 预期与核对方式 |
| --- | --- | --- |
| D01 | 下载、转换和导入完整 CC-CEDICT | 哈希与清单一致；原始行有明确去向，无静默截断；导入成功，无空词头或错误语言 |
| D02 | 查 苹果/蘋果、保险/保險、保鲜/保鮮、支持 | 繁简体均命中对应英文；词头和释义不串行；保留多义项 |
| D03 | 查缺词及同词的 ja | 返回无释义；不返回相似词、不以英文填日文 |
| D04 | 同一候选并排显示 CC-CEDICT 与示例词典 | 列名、来源和颜色清晰；各列独立；不合并成假装单一来源 |
| D05 | 导入损坏 TSV 后再次查词 | 失败可解释，原有词典仍可查询，数据与 manifest 不分离 |
| R00 | 编辑区输入 `p`，候选保持至少 2 秒 | 微软候选可见时 reader 必须输出对应候选快照；前置失败即停止输入 |
| R01 | 编辑区逐键输入 `pingguo`，候选稳定保持 2 秒 | 微软候选可见；reader 在 2 秒内给出对应快照；释义窗显示当前页中文、编号和正确英文；禁止用模拟快照补齐 |
| R02 | Esc 后逐键输入 `baoxian` | 快照与屏幕当前候选逐项一致；实际顺序以本机截图为准，不硬编码用户词库排序 |
| R03 | 输入后按 BackSpace，再补字母 | 只显示最新世代；不存在上一拼音的迟到释义 |
| R04 | 按微软拼音翻页键，返回上一页 | 显示当前可见页；序号对应真实页面；未知页码/选中项不编造 |
| R05 | 分别用数字键、空格、鼠标选词 | 原应用正确上屏；外挂不拦截、不重复提交；候选消失后释义窗 500ms 内隐藏 |
| R06 | 候选可见时按 Esc | 原输入取消；释义窗 500ms 内隐藏；同一词重新输入可恢复 |
| R07 | 候选可见时切换到另一个测试应用 | 释义窗 500ms 内隐藏，不在新应用显示旧结果；返回并重新输入可恢复 |
| W01 | 释义窗显示期间继续输入和选词 | 焦点始终在编辑器，释义窗不激活，鼠标点击穿透 |
| W02 | 候选靠近屏幕四边、无空间后再移回 | 不遮盖原候选；无空间可隐藏，有空间可恢复，不抢焦点 |
| W03 | 100%/150%/200% DPI、多屏和负坐标 | 位置对应，无明显漂移；未实际切换的缩放/屏幕只能记未执行 |
| W04 | 已有释义时连续输入、退格和翻页 | Updating 期间旧页保持完整；新页中文/编号/释义整体替换，无隐藏再显示、无混合页；选词/Esc/切应用仍隐藏 |
| W05 | 候选可见时改变 Windows 应用深浅色，再测试手动模式/高对比度 | 背景、边框、候选及列文字一致切换，深色文字可读；不重开窗口、不抢焦点、不重载词典；自定义低对比度列色回退可读色 |
| F01 | 暂停/恢复、设置关闭到托盘、退出 | 暂停即隐藏，恢复可读；设置关闭仍工作；退出后微软拼音正常 |
| F02 | reader 退出/正常 EOF/超时 | 清空旧窗口，主设置响应；仅重启本程序拥有的 reader |
| P01 | 正确性通过后记录查词和端到端延迟 | 分开记录冷/热查询和观察口径，至少 30 个样本；不把截图调用耗时当产品延迟 |

R03 的“最新世代”指提交新页后不能混入旧结果；W04 等待期间允许完整保留上一页。以上时间是验收阈值，不是未经测量的性能声明；截图调用不能证明毫秒期限。

恢复后先在专用空白编辑器完成 W04：逐键输入 pingguo，退格再补 o，翻页再返回；观察旧页是否完整保持、新页是否一次替换，再 Esc 核对隐藏。W05 先开候选，再到 Windows“个性化 → 颜色”改变“默认应用模式”，返回编辑器重开候选并核对；如需验证候选持续可见时的系统广播，由用户选择方便的系统切换方式。不要把切应用导致的正常隐藏误记为主题闪烁。

## 执行记录

每轮记录：时间、代码状态、测试配置、用例 ID、预期、实际、结论、证据路径、修复及对应回归。结论只用“通过 / 失败 / 阻塞 / 未执行”。

| 轮次 | 用例 | 结果 | 实际证据 |
| --- | --- | --- | --- |
| 准备 | 官方下载 | 通过 | 已下载 125251 条版本；文件头与上述 SHA256 已核对 |
| 词库 | D01/D02/D03/D05 | 通过 | 125251 原始记录转换为 125261 行 TSV，10 条长释义分行；含繁简别名去重后 SQLite 203053 条；词典清单 TSV SHA256 为 `f3db1e625d189b66980b833a137e6982dc6d0eeb9d4e8cbd34b67d15033715fb` |
| 修复前 | D04 来源链 | 失败 | 主来源为词典时忽略配置的兜底链；新增回归期望 Ready，实际 NotFound，退出码 1；证据 `artifacts/validation/dictionary-before-fix/` |
| 修复后 | D04 来源链 | 通过 | 主词典优先，缺词时查询同语言兜底，实际来源正确；完整词库验收程序退出码 0。列颜色等 GUI 部分未执行 |
| 回归 | 自动测试 | 通过 | 核心 30/30、Windows 4/4，共 34 项，退出码 0；`artifacts/validation/dictionary-after-fix/` |
| 回归 | 转换脚本 | 通过 | 繁简别名、长释义完整分行、损坏源不覆盖原 TSV、拒绝同路径；退出码 0 |
| 发布 | Windows 自包含包 | 通过 | 修复后 `scripts/Publish.ps1` 退出码 0，重新生成 `artifacts/pinyin-gloss-win-x64.zip`；词库仍单独留在本机 |
| 真实输入第 1 轮 | R00 | 失败 | Computer Use 在专用空白 Notepad3 输入 `p`，微软候选可见但 reader 无快照；立即停止后续输入。`artifacts/validation/dictionary-round1-reader.log` |
| 真实输入第 2 轮 | R00 | 失败 | 先监听托管 UIA 事件，再 Esc/输入 `p`；仍无候选事件/快照，停止输入。`artifacts/validation/dictionary-uia-events.log` |
| 只读诊断 | COM UIA3、线程窗口枚举 | 失败 | 系统输入宿主仍只有根节点，没有可读候选子树；`uia3-visible.log`、`uia3-thread-windows.log`；详情见 [下一步设计](candidate-reading-next-step.md) |
| UIA 修复 | 原生事件、Raw 编号、非汉字候选 | 通过 | desktop MenuOpened 有真实事件，父链找到枚举遗漏的 HWND；编号/文字节点仅 RawView 暴露；真实页含拉丁串/emoji。见 [UIA 调查](candidate-reading-next-step.md) |
| UIA 回归 | 页稳定确认 | 通过 | 真实 run1 曾出现新旧混合页；增加 50ms 一致页确认后全 40 项通过，退出码 0，`artifacts/validation/uia-stability-after-fix-20261012/` |
| 真实输入 | R00/R01/R02/R03/R04 | 通过 | 普通权限 Notepad3，生产 reader→IPC→词典→释义窗；逐键 pingguo、baoxian、退格和翻页对应。`desktop-dictionary-20261012/run2.jsonl`；量化读取时限待测 |
| 真实输入 | R05 键盘、R06/R07、W01 键盘焦点 | 通过 | 空格提交苹果，数字 2 提交保险；Esc/切应用隐藏，返回无旧结果；Overlay.IsActive=false，前台始终输入应用；500ms 隐藏门槛未严格计时 |
| 真实输入 | R05/W01 鼠标部分 | 未执行 | 鼠标目标落到 TextInputHost，Computer Use 跨进程校验拒绝；立即结束当轮。待恢复后由用户手动点选 |
| 真实多列 | D04、多语言隔离 | 通过 | 双英文来源及回落标记；三列中原创日文 苹果→りんご、保险→保険，日文缺词无英文；`desktop-dictionary-20261012/run3.jsonl` |
| 故障恢复 | F02 reader 退出 | 通过 | 核对命令行后结束本轮 owned reader，旧窗清空、监督器重启，重新输入恢复；非真正 UIA 卡死测试 |
| 设置交互 | F01、完整 App 启动/保存 | 未执行 | 隔离 Runtime 不覆盖用户配置，主程序正式启动流程仍待验收 |
| 新界面 | W04/W05 | 未执行 | 更新保留旧页和系统主题已实现并通过离屏回归；用户暂停桌面控制后未真机复测 |
| 显示环境 | W03 | 未执行 | 未切换 DPI 或显示器，不作兼容性声明 |
| 性能 | P01 最新纯查词 | 通过 | 40 次 p50 0.329ms、p95 0.374ms、max 0.513ms；`artifacts/validation/dictionary-background-20261012.log` |
| 性能基线 | 主进程接收→自身文本树 | 通过 | 修改前真实 32 页，20ms 采样：p50 127.924ms、p95 147.743ms、max 148.782ms；`desktop-dictionary-20261012/latency.json`；不含按键/UIA/屏幕合成 |
| 词典延迟修复 | 缺词不等模型 StableMs | 通过 | 修复前 NotFound 回归等待超时；修复后 Ready/NotFound 核心 32 项通过，退出码 0；`dictionary-delay-after-fix-20261012/` |
| 后台界面回归 | 缓冲/主题/身份隔离/失败终态 | 通过 | 核心 32 + Windows 后台 22 = 54 项，退出码 0；`artifacts/validation/background-final-20261012/`；8 项桌面测试被排除 |
| 最新构建/发布 | Release / 自包含包 | 通过 | build 退出码 0、0 警告/0 错误；Publish.ps1 -BackgroundOnly 退出码 0，54 项后台测试通过；`background-build-20261012.log` / `background-publish-20261012.log`；未启动 exe |
| 后台性能 | 三列七候选、无缓存 | 通过 | 40 页 840 个终态，无缺失/重复；模型 StableMs=2000 时整页调度 p50 14.295ms、p95 15.335ms、max 17.706ms；不含 UIA/显示，不能与前项直接算提速比例 |

早期构建有 NU1900（漏洞数据源连接失败），不能据此声称漏洞审计完成。没有应用或覆盖用户设置；真实词典 Runtime 闭环已验证，GUI 导入/保存及完整 App 启动仍待完成。

基础功能完整通过门槛：D01–D05、R00–R07、W01/W02/W04/W05、F01/F02 在目标环境通过，量化时限另行采样；W03 按实际支持环境列出。当前真实读取和词典关键闭环已打通，尚未达到完整稳定版门槛。模型完全不作为前置条件。

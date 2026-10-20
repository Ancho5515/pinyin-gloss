# LMT 下载、自动配置与后台验收

程序交付为可解压的 Windows x64 文件夹/zip，主入口是 PinyinGloss.Desktop.exe，配套 reader/ 和依赖 DLL。不是单文件 exe；自包含包无需预装 .NET。不要单独移动主 exe。

## 新用户准备

解压到可写目录，运行根目录 Setup-LLM.cmd，先选择量化，直接回车选 Q4_K_M；或在该目录执行（脚本不指定量化仍默认 Q4_K_M）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-Llm.ps1
# 指定下载位置；安装清单仍写在主 exe 同级目录：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-Llm.ps1 -ModelDirectory 'D:\模型\LMT'
# 需要明确只用 CPU 时：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-Llm.ps1 -Backend Cpu
# 明确选择较小量化，GPU 优先策略不变：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-Llm.ps1 -Quantization Q6_K
```

源码开发时脚本默认安装根目录是仓库根；可用 -InstallDirectory '<主exe所在目录>' 指定目标。默认 -Backend Auto，准备 CUDA 12.4 和独立 CPU 包；加载优先 GPU，初始化失败只回退一次。-Backend Cuda 兼容旧命令，同样准备 CPU 备用；-Backend Cpu 明确只用 CPU。-Connections 1..8 控制模型并行下载，默认 4。只下载固定版本，不安装系统服务、不改全机执行策略、不启用在线推理或降低量化。

当前支持 NVIDIA CUDA GPU；AMD/Intel GPU 后端尚未接入，CUDA 不可用时明确回退 CPU。GPU 使用固定版 llama.cpp 的自动层数适配空闲显存，512 context、FP16 KV 和单槽保持，量化使用用户所选文件。只有加载日志报告实际卸载层数大于 0 才显示 GPU 就绪，部分卸载显示实际层数；不能用“检测到显卡”或 health 成功证明 GPU 已启用。GPU 尝试最多占总加载期限的一半且不超过 30 秒，CPU 继续使用剩余期限；取消、模型 hash/身份错误和已识别端口冲突不触发 GPU 回退。

默认目录：

```text
PinyinGloss.Desktop.exe
reader/
Setup-LLM.cmd
scripts/Setup-Llm.ps1
scripts/Get-LlamaRuntime.ps1
llm-installation.json
LLM/
  LMT-60-1.7B.Q4_K_M.gguf
  LICENSE-LMT.txt
  SOURCES.txt
  llama-b11077-Cuda/          # 默认主运行时（含配套 CUDA DLL）
    llama-server.exe
    *.dll
    pinyin-gloss-runtime.json
  llama-b11077-Cpu/           # 独立 CPU 备用运行时
    llama-server.exe
    *.dll
    LICENSE-llama.cpp.txt
    pinyin-gloss-runtime.json
```

默认模型大小 1282439264 字节，下载/拼接期间建议留出至少 7 GB 空闲空间（含 CUDA/CPU 配套文件），完成后删除模型分块。中断后重跑同一命令即可复用完整块；最终完整 SHA256 校验成功才安装。已有模型校验失败时拒绝覆盖。运行时校验官方 zip SHA256，重复运行会校验安装文件后跳过。仅全部文件准备成功后原子写入安装清单，不修改用户的 %LOCALAPPDATA%/PinyinGloss/settings.json。

运行时 zip 暂存于模型目录的 .runtime-downloads，下载中断后续传；已完整校验的包可复用，hash 错误会删除该损坏包并拒绝安装。此目录是下载缓存，成功安装且不再需要恢复时可以由用户手动删除；模型及运行时目录须保留。

完成后重启程序。全新用户自动填入主/CPU 备用 server、模型、revision/hash 和 GPU 优先策略，启用 LMT 并后台预加载；加载期间词典仍可用。已有手工路径不会覆盖，已有显式禁用保持禁用，可在“离线模型”检查并启用后保存。自动路径配置标记 UseInstalledModel，保存后也能随当前清单刷新；手工修改路径、hash、revision 或 GPU 会退出自动路径管理。旧 Cpu/Cuda 清单兼容，旧 Cpu 清单不会自行改成 GPU；重跑默认脚本才能更新自动安装策略。默认清单使用相对路径，自定义外部目录使用绝对路径。

启动不自动联网下载，下载是显式运行脚本的一次性准备动作。加载前生产托管器重新检查 GGUF general.file_type 必须匹配所选精度及模型 SHA256，不因 GPU 失败改换精度。模型服务仅监听回环地址，每次启动随机认证令牌、单槽、FP16 KV、512 context、448 输入预算、32 输出 token、temperature 0。端口占用不结束其他软件进程。

## 可选择的 1.7B 量化

以下均是同一非 Base 的 LMT-60-1.7B 社区 GGUF，固定 revision 与下载来源相同。大小以实际 LFS 元数据为准，GB 为十进制；文件大小不等于总显存，仍需 KV、计算缓冲及桌面余量。[发布者模型卡](https://huggingface.co/mradermacher/LMT-60-1.7B-GGUF)中的质量标注是发布者评价，不是本项目的语言质量验收。

| 选择 | 文件大小 | 用途与边界 | GGUF file_type |
| --- | --- | --- | --- |
| Q8_0 | 2165039200 字节 / 2.17 GB | 优先保留精度，作为现有实测基线；不保证无损 | 7 |
| Q6_K | 1673007200 字节 / 1.67 GB | 减少显存占用、兼顾精度的候选；完整翻译质量未验收 | 18 |
| Q5_K_M | 1471805536 字节 / 1.47 GB | 更小的折中选项；完整翻译质量未验收 | 17 |
| Q4_K_M（默认） | 1282439264 字节 / 1.28 GB | 优先适应小显存；精度损失须实际评估 | 15 |

固定 SHA256：

```text
Q8_0    18ff357223b2ee8e43f965d92cc72d37287575660d134aaff2835a5fe5697ac1
Q6_K    2e6446baba3a62141ecedf0a869afda01fe3b26551389d1d2cbfe6efe4095815
Q5_K_M  f7291217a413cf8e159173fd0ba8b45154ca67285cd5fa1feb8f601f0f02703b
Q4_K_M  54c6244f100330264cef607188413ee82cb8a254f0cf950011aaa9cee74f7eb5
```

可多次运行准备脚本下载不同量化，各文件并存；仅本次全部成功后更新安装清单指向所选文件，不自动删除以前模型。App 只加载当前所选一份，各语言共用一个实例。手工改量化必须同时选择对应 GGUF/hash；更改界面量化不会下载、转换或自动寻找文件。当前未开放 Q2/Q3，避免给翻译质量未验证的极低精度默认入口；未接入其他基座模型。

## 固定来源与许可

| 项目 | 固定身份 |
| --- | --- |
| 原模型 | [NiuTrans/LMT-60-1.7B](https://huggingface.co/NiuTrans/LMT-60-1.7B)，翻译版，非 Base |
| 下载 GGUF | [mradermacher/LMT-60-1.7B-GGUF](https://huggingface.co/mradermacher/LMT-60-1.7B-GGUF)，社区静态 Q4_K_M（默认），Apache-2.0 |
| GGUF revision | f03d51b43463d8a457eee1cd765220de69e9bac8 |
| 默认 Q4 SHA256 | 54c6244f100330264cef607188413ee82cb8a254f0cf950011aaa9cee74f7eb5 |
| llama.cpp | b11077 / 982a3329af8401772087d67c54d5948a5b270943，MIT |

官方仓库提供 safetensors，本脚本下载的 GGUF 是社区量化文件。模型卡明确原模型身份，但未注明原始 safetensors 的具体 revision，不能把 GGUF revision 当作官方检查点 revision。当前方式避免新用户额外下载约 4 GB 原始权重和安装 Python 转换环境。原始权重与量化的一致性质量评估仍需独立执行。Q4_K_M 是用户明确选择的权重量化默认值；保留 Q8_0 的明确选择，不自动换精度。

希望自己转换时仍可使用 scripts/Convert-Lmt.ps1，固定官方原始 revision 与上述 llama.cpp commit，先转 F16 再 Q8_0，并在界面手工配置产物与 SHA256。权重不包含在源码、MIT 许可或应用 zip 中；上游、模型和 NVIDIA 许可分别保留。

## 延迟与取消行为

词典快查后，新页由完整候选、词典结果或模型“等待”状态组成并整体替换旧页；模型完整输出校验后逐单元格原位更新。Pending 不是终态，快照期限到达仍收尾为超时。真正关闭候选始终隐藏；等待期间不抢焦点或阻塞输入。

模型等待候选稳定 120ms，默认总期限 1500ms 包含排队，单槽优先选中候选并轮转语言。默认短期限不能保证 CPU 完成整页所有语言；词典兜底与等待状态保持可见。无进展默认 800ms，加载独立默认 60 秒。

取消时先释放 HTTP/SSE，再在默认 1500ms 宽限内通过固定版 /slots 确认唯一槽位空闲；确认成功保留模型，无法确认才停止 owned worker。调度器保留信号量直到 provider 清理真正结束。分词前取消不重载模型。外部 HTTP 服务无 owned worker，不承诺相同的硬取消能力。参考[固定版服务端文档](https://github.com/ggml-org/llama.cpp/blob/b11077/tools/server/README.md)。

## 后台验收命令与边界

```powershell
# 模型下载到独立目录；不覆盖用户设置，不启动桌面 App：
./scripts/Setup-Llm.ps1 -InstallDirectory artifacts/validation/llm-20261012/install -ModelDirectory artifacts/LLM -Connections 8
dotnet run --project tests/PinyinGloss.Model.Acceptance -c Release -- artifacts/validation/llm-20261012/install artifacts/validation/llm-20261012/acceptance
dotnet test PinyinGloss.sln -c Release --filter 'Category!=InteractiveDesktop&Category!=DesktopCapture'
./scripts/Publish.ps1 -BackgroundOnly
```

验收宿主复用生产安装解析、ManagedLlamaServer、HttpModelProvider、TranslationCoordinator，记录真实加载、少量中英/中日/中法词义、观察忙槽后取消 5 轮、同 PID 复用、默认期限的 5 候选×2 语言以及资源。注入候选快照只验证模型后端，不能代替真实微软拼音、窗口或完整语言质量验收。当前暂停桌面测试，禁止借此启动正式 App。

开发发布会清理目标文件夹，因此检测到已有 LLM/ 或安装清单时拒绝覆盖。先把旧发布目录整体移动，再重新发布；不要丢失已下载权重。

## 2026-10-12 CPU 基线实测

环境：Windows build 26200.9457，Ryzen 7 5800H、16GB 内存；CPU 运行时，未使用 GPU。脚本真实下载退出码 0，GGUF 完整 SHA256 与固定值一致，CPU 运行时官方 zip 校验成功。首次模型加载包含完整 hash 检查，未清除操作系统文件缓存，不称为冷盘启动。

最终命令不带诊断覆盖参数，使用生产默认设置，退出码 0。证据：artifacts/validation/llm-20261012/acceptance-final/results.jsonl 与同级 summary.json。

| 检查 | 实际结果 |
| --- | --- |
| 加载与文件身份 | 6.029 秒；Q8_0、固定 revision/hash、FP16 KV、CPU、512 context、单槽 |
| 基本词义 | 苹果→apple/リンゴ，保险→insurance/保険，你好→Hello/Bonjour；6 组全部通过，不代替完整语义评估 |
| 独立翻译请求 | 11 次，172–381ms，含模板、分词、生成、完整输出校验；未含 UIA/显示/稳定等待 |
| 真实忙槽取消 | 5 轮，同 PID 15844，槽位均空闲，后续翻译成功；取消清理 454–529ms，无重载 |
| 5 候选×2 语言、无缓存 | 3 页，默认 1500ms 总期限；19/30 个模型译文完成、11/30 个超时，30 个单元格均有终态，无缺失 |
| 新页首状态组成 | 首页 18.364ms，随后 0.603/0.339ms；首个完整模型译文 246–318ms；仅后台调度，不是屏幕延迟 |
| 资源 | server 峰值 WorkingSet 1987063808 字节（约 1.85GiB）；PrivateMemory 249200640 字节；累计 CPU 时间 84.05 秒 |
| 释放 | 显式 Stop 后未就绪，owned worker 退出，未遗留本轮模型进程 |

原 300ms 取消宽限的两轮真实验收均失败：worker 被释放。禁用 HTTP 响应排空后，300ms 仍不足。固定服务端源码 HTTP_POLLING_SECONDS=1，本机诊断五轮需要 438–484ms；据此将默认宽限改为 1500ms，再以最终默认配置通过上表五轮。不能宣称毫秒级硬取消；用户已保存的自定义宽限不会自动覆盖，旧 300ms 设置建议改为 1500ms。

该 CPU/Q8 基线在默认短期限内未完成无缓存的全部并排列，超时是明确记录的能力边界。默认内存缓存与词典兜底可覆盖重复词；没有暗中放宽 1500ms 总期限。后续 GPU 实测见下节；200 词量化一致性、完整多语言质量、桌面新界面及新用户 GUI 首启仍未验收。

发布目录默认路径实跑：Windows PowerShell 5.1 调用包内 scripts/Setup-Llm.ps1，不传 ModelDirectory，退出码 0；复用已下载并校验的模型（硬链接节省磁盘）与运行时缓存，生成相对路径 LLM/LMT-60-1.7B.Q8_0.gguf、LLM/llama-b11077-Cpu/llama-server.exe。随后 Model.Acceptance 直接读取该发布清单，完整后台验收退出码 0。此用例验证默认布局和缓存重跑，首次网络下载另在独立自定义目录实跑通过。证据：setup-default-exe-winps51.log、acceptance-default-exe.log，均在 artifacts/validation/llm-20261012/；没有启动桌面 App。

## 2026-10-12 GPU 优先与量化选择实测

同机 RTX 3050 Laptop 4GB、驱动 576.52，官方 CUDA 12.4 包及 cudart 的完整 hash 校验通过。Q8_0 与 Q4_K_M 均完整下载并核对固定 SHA256；GPU 两种量化均实际卸载 29/29 层、上下文 512、FP16 KV、单槽，未降低参数。命令退出码均为 0：

```powershell
dotnet run --project tests/PinyinGloss.Model.Acceptance -c Release -- artifacts/validation/gpu-20261012/install-q8-auto artifacts/validation/gpu-20261012/q8-gpu-fixed
dotnet run --project tests/PinyinGloss.Model.Acceptance -c Release -- artifacts/validation/gpu-20261012/install-q4-auto artifacts/validation/gpu-20261012/q4-gpu-final
# 故障注入：在隔离目录只复制 GPU 启动器、故意缺少 DLL；正常安装和系统保持原样。
dotnet run --project tests/PinyinGloss.Model.Acceptance -c Release -- artifacts/validation/gpu-20261012/install-q8-auto artifacts/validation/gpu-20261012/cpu-fallback-confirmed missing-gpu-runtime
```

| 检查 | Q8_0 / CUDA | Q4_K_M / CUDA |
| --- | --- | --- |
| 含 hash 检查加载 | 7.371 秒 | 4.588 秒 |
| 独立请求 11 次 | 44.49–138.07ms | 34.19–77.59ms |
| 忙槽取消 5 轮 | 同 PID 38340；清理 31.78–63.65ms，无重载 | 同 PID 4936；清理 30.50–46.29ms，无重载 |
| 5 候选×2 语言、3 页、无调度缓存 | 默认 1500ms；30/30 Ready，0 超时；627–672ms/页 | 默认 1500ms；30/30 Ready，0 超时；573–703ms/页 |
| 首个完整模型译文（含稳定等待） | 172.75–193.69ms | 156.68–185.11ms |
| 后端 CUDA model buffer | 1743.77 MiB | 1050.43 MiB |
| 后端 CUDA KV / compute buffer | 56.00 / 48.51 MiB | 56.00 / 48.51 MiB |

以上是后台模板/分词/完整生成校验与调度口径，不含 UIA、按键或屏幕刷新；11 次含重复词及模型内 KV 复用，不称为冷请求统计。加载未清理操作系统缓存，不称冷盘启动。buffer 数值来自实际后端加载日志，不是用户整卡可用显存估计。两次运行显存空闲会变化，不能据少量样本推导固定提速比例。

两种 GPU 量化的 6 组基本词义、5 轮取消后继续翻译、默认期限收尾和释放均通过。Q4 的“你好”可返回合法的 hi，验收允许 hello/hi；不强制模型输出同一字面词。Q4_CPU 对照也通过：11 次 120–277ms，30/30 Ready；证据 q4-cpu/results.jsonl。Q6_K/Q5_K_M 仅完成固定文件身份查询、选择/清单/精度校验回归，尚未真实加载或评估质量。

真实故障注入中 GPU 启动器因依赖 DLL 缺失退出（0xC0000135），一次加载流程内自动使用独立 CPU 包，显示明确回退原因；全部基本词义、5 轮取消复用、30 个页面终态及释放通过，退出码 0。CPU 页内 20/30 Ready、10/30 Timeout，保持原期限。此用例验证依赖缺失，不替代所有驱动错误、OOM、不同 GPU/Windows 版本的兼容验收。

GPU 检测必须使用 --log-verbosity 4：固定版把底层 GGML INFO 映射到 TRACE=4，默认 3 会缺失 offloaded 行。已定位并修复误回退，新增失败回归后通过。加载日志仅内存有界捕获，健康后停止；本验收显式保留 gpu-startup.log。全部证据在 artifacts/validation/gpu-20261012/。

最新后台回归核心 61、Windows 24，共 85 项通过，8 项桌面测试被过滤；PS5.1 安装脚本 11 项通过（含直接无参数默认路径），Release 构建退出码 0、0 警告/0 错误；后台发布退出码 0。默认 exe 同级 LLM 实际重新配置为 Auto，旧 CPU 包保留在 artifacts/pinyin-gloss-win-x64-cpu-baseline-20261012/。证据 setup-default-exe-fixed.log / publish.log。用户仍暂停桌面操作，GPU 与真实微软候选的新闭环、UI 状态可见性及完整质量尚未现场验收。

新发布根目录直接读取相对路径清单的模型验收也退出码 0：Q8_0 GPU 29/29 层，6 组基本词义、5 轮同 PID 取消复用、三页 30/30 Ready 和释放通过，证据 acceptance-default-exe.log。zip 只包含程序/脚本/文档/许可，排除本机 LLM 和安装清单；正式 Desktop.exe 没有启动。

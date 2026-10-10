# Pinyin Gloss 初版实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax.

**Goal:** 提供微软拼音翻译外挂的可构建首版、候选诊断、离线词典、可配置模型、多语言并排显示和开源工程。
**Architecture:** Core 契约与调度，Providers 词典/HTTP/模型进程，独立 CandidateReader UIA 读取，Desktop WPF 主窗口/托盘/翻译窗。原输入法完全负责中文输入。
**Tech Stack:** C# / .NET 10 LTS / WPF / UI Automation / SQLite / llama.cpp HTTP。
**Spec:** docs/architecture.md

## Global Constraints

- 不注册 IME、不拦截键盘、不注入 DLL、不读取应用正文或剪贴板。
- Windows 现场候选读取验证在当前 Linux 环境无法完成；提供诊断程序和测试清单，明确实验状态，不宣称已兼容。
- 首版完成可运行工程与模拟闭环；真实 UIA 兼容性是发布稳定版的门槛。
- 词典始终按语言匹配；在线默认关闭，必须显式配置启用；不打包模型或未授权词典。
- 实际上传需 GitHub 连接具备写权限；当前连接返回 403，继续完成可审查代码。

## Review Focus

UIA 读取范围与进程身份；候选隐藏和前台切换；IPC 边界；异步旧结果及配置变更；取消与子进程清理；语言隔离；在线启用；令牌预算；配置原子写入；Win32 DPI/不夺焦点；CI 工件包含读取器。

### Task 1: 契约与可靠调度

Files: src/PinyinGloss.Core/{Contracts,Settings,PromptTemplate,TranslationCoordinator,PipeProtocol}.cs; tests/PinyinGloss.Tests/*.
Interfaces: CandidateSnapshot、ITranslationProvider、TranslationCell、AppSettings。
- [ ] 先写有意义的测试：新快照淘汰忽略取消的旧请求、隐藏/配置变更阻止回写、语言兜底隔离、选中词多语言轮转、总期限包含排队、模板变量及配置边界、IPC 超长消息。
- [ ] 运行测试观察失败。Expected: 未实现行为失败。
- [ ] 实现契约、参数校验、白名单模板、有界 IPC、内存缓存及调度。
- [ ] 运行完整核心测试。Expected: 全部通过。

### Task 2: 本地词典和模型适配器

Files: src/PinyinGloss.Providers/{DictionaryStore,DictionaryProvider,HttpModelProvider,ManagedLlamaServer,ProviderFactory}.cs; dictionaries/*.
Consumes: Task 1 provider contracts. Produces: Dictionary/llama.cpp/compatible HTTP providers.
- [ ] 测试词典导入失败不替换有效数据库、日文未命中不返回英文、HTTP 输出过滤和真实模板 token 上限、禁止隐式联网。
- [ ] 观察失败再实现；词典样例自行编写 MIT 许可。
- [ ] 测试和构建。Expected: 全部通过。

### Task 3: Windows 读取与主程序

Files: src/PinyinGloss.CandidateReader/*; src/PinyinGloss.Desktop/*.
Consumes: Snapshot IPC / validated settings / providers.
- [ ] 实现限定宿主和候选子树 UIA、MTA 线程、WinEvent 只读辅助发现、心跳、诊断、进程看门狗。
- [ ] 实现 WPF 不激活点击穿透翻译窗、中文设置、托盘、语言列/颜色/提示词、导入词典、模型生命周期、登录启动。
- [ ] 提供显式演示模式，固定人工候选，绝不假装真实采集成功。
- [ ] 交叉编译 Windows。Expected: 0 errors；现场 UIA 与焦点/DPI 仍待 Windows 手工验收。

### Task 4: 开源交付与验证

Files: README.md, LICENSE, THIRD_PARTY_NOTICES.md, CONTRIBUTING.md, SECURITY.md, .github/*, scripts/*, docs/*.
- [ ] 中文 README 写清试验状态、运行/模型/词典/多语言/诊断步骤、真实测试边界。
- [ ] CI Linux 核心测试 + Windows build/publish 工件；许可与模型/词典独立。
- [ ] 完整测试、发布交叉构建、独立代码审查和必要修复。
- [ ] 上传授权仓库；写权限失败时交付源码和 Windows 包并给出具体授权步骤。

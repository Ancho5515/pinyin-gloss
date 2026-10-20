# 句子与选区悬浮翻译实施计划

> 当前会话直接执行 executing-plans、TDD、verification-before-completion；用户已确认并要求执行到完成，不重复确认，不提交/推送。

目标：固定右下角正文框，选区优先、句子其次、无目标为空白圆圈；拖动缩放不抢焦点；与候选框主题一致。

设计：[2026-10-13-text-overlay-design.md](../specs/2026-10-13-text-overlay-design.md)。

约束：原候选显示与隐藏保持；正文开关默认关闭，只读获准范围、不读全文/剪贴板/密码；同源单槽 Q4、FP16 KV、512 上下文；正文 368 输入/128 输出/16 控制；合并 300/1000ms、期限 3000ms；420×180 DIP、28 DIP 圆圈、16 DIP 右下边距。

## 1 契约、设置与调度

文件：Core/Contracts.cs、Settings.cs、TranslationCoordinator.cs、TextTranslationCoordinator.cs；Tests/TextTranslationTests.cs；Providers/HttpModelProvider.cs。

- [x] 独立开关与旧配置、输出截断失败回归；新增设置和请求独立输出预算。
- [x] 正文来源槽共用、不进入候选 LRU、失焦过期拒绝；对应失败/通过记录。
- [x] 全局暂停正文请求回归；暂停停止 reader，恢复重建正文会话。
- [ ] 合并、模式转换、公平调度、输入预算的完整边界回归与真实模型验收。

## 2 UIA 正文读取

文件：CandidateReader/UiaTextScanner.cs、Program.cs、Native.cs；Desktop/ReaderSupervisor.cs；Core/PipeProtocol.cs；Windows.Tests/TextRangeTests.cs。

- [x] 活动目标身份、密码/前台拒绝、有界父链、选区与 caret 接口；默认关闭不创建正文扫描器。
- [x] 句界 FindText、有界 GetText、组合状态、IPC 2 和 watchdog 接线；自动回归。
- [ ] 专用真实文本目标验证句界、选区、IME 组合和只读文档能力；异常停输入后修复。

## 3 正文窗、主题与设置

文件：Desktop/TextOverlayWindow.cs、DesktopRuntime.cs、MainWindow.cs、OverlayTheme.cs、OverlayWindow.cs；Windows.Tests/TextOverlayTests.cs。

- [x] 空白圆圈、展开、会话过滤、非激活、鼠标命中区和几何保存；对应回归。
- [x] 两正文开关、来源/语言/合并与期限、重置位置、旧表单几何合并。
- [ ] 两框主题、字体字号、深浅色与高对比度一致；对应回归和现场切换。
- [x] 真实主屏拖动、缩放、焦点与选区保持及几何保存（live7，150%）。
- [ ] 150%/100% 跨屏及可见几何。

## 4 完整验收与交付

文件：Windows.Acceptance/Program.cs、README.md、docs/architecture.md、implementation-status.md、windows-validation.md、AGENTS.md。

- [x] Release、完整测试与安装脚本回归，独立核对退出码与跳过项（2026-10-14：118 项完整回归、12 项 PS5.1 脚本；均退出码 0）。
- [ ] 配套发布、保留旧目录和模型，已运行程序退出后才替换；Q4 全 hash。
- [ ] 正文真实模型闭环、错误恢复、同 PID/单槽及分阶段延迟与资源统计。
- [ ] 根据实际证据同步文档和 AGENTS，diff 检查，不提交/推送。

# 贡献指南

欢迎通过 issue / pull request 贡献。先阅读 README 和 docs/architecture.md，保持项目为微软拼音外挂：不接管中文输入，不引入自有 IME、键盘拦截、应用正文读取或 DLL 注入。

开发依赖 .NET 10 SDK。提交前运行：

```powershell
dotnet test tests/PinyinGloss.Tests -c Release
dotnet build PinyinGloss.sln -c Release
```

修复可测试行为时，先增加失败测试，再实现。Windows 读取变更应附 Windows build、目标应用、匿名结构诊断以及 docs/windows-validation.md 的对应检查结果。只上传经你检查、愿意公开的诊断；不要提交密钥、候选历史、模型权重或未授权词库。

新增翻译源实现 ITranslationProvider，声明支持的中文→目标语言方向，保留来源版本。在线源需要明确启用，不得改成自动联网兜底。新增词典提供来源和许可；示例数据应为原创或明确允许分发。

提交的代码按本仓库 MIT 许可证提供。保留第三方版权与许可通知；更换依赖版本时检查 THIRD_PARTY_NOTICES.md 和 third-party 中的文本。

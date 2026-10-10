# 配置来源与语言列

主窗口“高级配置”可编辑完整 JSON。保留 schemaVersion=1；编辑后校验载入表单，再保存。来源 ID 唯一，列 ID 唯一。列顺序就是 columns 数组顺序。

以下为可添加的来源片段（替换路径、地址、模型名和支持方向），不是可直接覆盖整个配置的文件：

```json
{
  "id": "other-local",
  "name": "其他本机模型",
  "kind": "LocalModel",
  "enabled": true,
  "managed": false,
  "llamaApi": false,
  "endpoint": "http://127.0.0.1:1234/",
  "model": "your-model-id",
  "modelRevision": "your-version",
  "supportedTargets": ["en", "ja"],
  "prompt": "Translate {{candidate}} from Chinese into {{target_language_name}}. Return a short gloss only."
}
```

`llamaApi=true` 使用 llama.cpp `/apply-template`、`/tokenize`、`/completion`。`false` 使用根地址下的 `/v1/chat/completions`（请勿把 `/v1` 重复放入 endpoint）。后者要求 SSE 流式响应，不承诺所有 OpenAI 兼容服务完全兼容。

在线来源需明确添加和启用：

```json
{
  "id": "my-online",
  "name": "我的在线模型",
  "kind": "OnlineModel",
  "enabled": false,
  "managed": false,
  "llamaApi": false,
  "endpoint": "https://your-service.example/",
  "apiKeyEnvironmentVariable": "PINYIN_GLOSS_ONLINE_KEY",
  "model": "your-model-id",
  "modelRevision": "your-version",
  "supportedTargets": ["en", "ja"],
  "prompt": "Translate {{candidate}} into {{target_language_name}}. Return only a short gloss."
}
```

自行设置密钥环境变量，并重新启动外挂读取更新后的环境。将 enabled 改为 true 即表示你允许向该 endpoint 发送可见候选及语言提示词。禁用 HTTP 重定向，在线 endpoint 要求 HTTPS。在线模型不能作为隐式词典兜底。不要将密钥直接写入 JSON。

日文列示例：

```json
{
  "id": "lmt-ja",
  "enabled": true,
  "sourceId": "lmt",
  "targetLanguage": "ja",
  "fallbackSourceIds": [],
  "promptOverride": null,
  "colorLight": "#2266CC",
  "colorDark": "#8DBBFF",
  "width": 180
}
```

英文列可引用只支持英文的 dictionary；日文列必须引用日文词典或留空。导入器把 TSV 来源、许可、SHA256、版本、语言方向一同写入 SQLite 包，索引完成后原子替换。示例词典规模很小，实际可用性依赖你选择的合法词库。

扩展代码实现 `ITranslationProvider` 的 Describe、StartAsync、StopAsync、QueryAsync，并在 ProviderFactory 注册。现有接口一次 Query 对应一个候选/目标语言；调度器统一拆分多语言任务。批量适配器、健康指标和外部进程协议是后续接口演进，不需要改变微软拼音。

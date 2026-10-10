# LMT Q8_0 准备与验证

本项目不随包分发模型，当前未实测模型转换、词义质量或显存。以下为可复现准备路径，不代表量化已经通过验收。

1. 固定官方 [NiuTrans/LMT-60-1.7B](https://huggingface.co/NiuTrans/LMT-60-1.7B) 翻译模型的 40 位 commit revision。保存对应许可证、配置、tokenizer 和 chat template。不要选择 Base、0.6B、4B 或 8B。
2. 在你的模型准备环境中，使用官方 Hugging Face 下载工具按该 revision 下载完整检查点。
3. 克隆 llama.cpp 并检出 `982a3329af8401772087d67c54d5948a5b270943`（`b11077`），按该仓库转换器说明安装 Python 依赖。
4. `Get-LlamaRuntime.ps1` 下载同版本 Windows 运行时和 quantize 工具。CUDA 默认包为 12.4，CPU 可选；GPU 驱动兼容性需自行验证。
5. 运行：

```powershell
./scripts/Get-LlamaRuntime.ps1 -Backend Cuda
./scripts/Convert-Lmt.ps1 -ModelDirectory 'D:\models\official-LMT-60-1.7B' `
  -Revision '<模型的40位commit SHA>' `
  -LlamaCppDirectory 'D:\tools\llama.cpp' `
  -QuantizeExecutable 'D:\tools\llama-quantize.exe'
```

脚本固定转换器 revision，先 F16 再 Q8_0，生成权重 SHA256 manifest。原始检查点身份需你核验，脚本不根据文件夹名认证官方模型。程序托管该 LMT 来源时检查 GGUF 的 `general.file_type=7`，拒绝未知或其他量化；可填写 SHA256 进一步校验文件。

主设置中启用本地模型来源、填写 server / GGUF 绝对路径、revision/hash，保存后加载。默认本机 `127.0.0.1:18089`、每次启动随机认证令牌、单槽、FP16 KV、512 context。端口需空闲；程序不会结束其他软件的服务。没有 GPU 时可手动选 CPU。启用失败时查看设置状态并继续词典；不会改用在线模型。

至少用 200 个普通词、短语、多义词、专有名词和词典未命中项对比原始权重与 Q8_0。每种目标语言独立评估。记录热/冷启动、首候选与整页 p50/p95/p99、取消残留和显存峰值。短词翻译无上下文，不能保证多义词语境判断。

API 与参数参考：[固定版本 llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11077/tools/server/README.md)。运行时获取脚本校验官方 release 工件的 SHA256，但脚本本身尚未在 Windows 运行。

# Third-party notices

Pinyin Gloss original code and authored demo.tsv entries: MIT, Copyright (c) 2026 Ancho5515 and contributors.

| Component | License / copyright | Distribution |
| --- | --- | --- |
| .NET / WPF / Windows Forms | MIT and runtime third-party notices; Microsoft and .NET contributors | Self-contained Windows package includes runtime dependencies. Runtime packs' notice files are copied when present. |
| Microsoft.Data.Sqlite 10.0.12 | MIT; © Microsoft Corporation; .NET Foundation contributors | NuGet, redistributed managed assemblies; full [license](third-party/Microsoft.Data.Sqlite-LICENSE.txt). |
| Interop.UIAutomationClient 10.19041.0 | MIT；Copyright (c) 2019 Roman；[上游](https://github.com/FlaUI/UIAutomation-Interop) | 读取器使用的原生 UIA COM 接口声明；不引入 FlaUI 自动操作库。完整[许可](third-party/Interop.UIAutomationClient-LICENSE.txt)随包保留。 |
| IAccessible2 AccessibleText.idl | BSD；Copyright (c) 2007, 2013 Linux Foundation、2006 IBM Corporation、2000, 2006 Sun Microsystems, Inc.；[上游](https://github.com/LinuxA11y/IAccessible2/blob/master/api/AccessibleText.idl) | IA2 COM 接口声明依照上游 IDL 顺序编写，仅调用读取成员；完整[许可](third-party/IAccessible2-LICENSE.txt)随源码及发布包保留。 |
| SQLitePCLRaw 2.1.12 | Apache-2.0; Copyright 2014-2024 SourceGear, LLC | NuGet bridge/native bundles; full [license](third-party/SQLitePCLRaw-LICENSE.txt). |
| SQLite | Public domain, [copyright statement](https://www.sqlite.org/copyright.html) | Native e_sqlite3 bundled via SQLitePCLRaw. |
| llama.cpp b11077 (982a3329af8401772087d67c54d5948a5b270943) | MIT, ggml / llama.cpp contributors; CUDA distribution may include additional NVIDIA notices | Optional separate user download, not bundled by Publish.ps1. Preserve downloaded notices. |
| NiuTrans/LMT-60-1.7B | Apache-2.0 per official model card, [model](https://huggingface.co/NiuTrans/LMT-60-1.7B) | Not redistributed here. Retain exact checkpoint revision, tokenizer, chat template, license and conversion provenance. |
| xUnit.net / Visual Studio runner / Microsoft.NET.Test.Sdk | Respective upstream licenses, development/test dependencies | Not part of the application publish output. |
| Imported dictionaries / other model checkpoints | Their own licenses | User supplied. A recorded manifest is provenance, not permission to redistribute. |
| CC-CEDICT（可选本地词典） | CC-CEDICT contributors，MDBG 发布；本次 2026-10-10 数据声明 [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)；[官方来源](https://www.mdbg.net/chinese/dictionary?page=cedict) | 完整数据仅下载到本机 artifacts，不随源码或发布包分发。转换 TSV 保留署名、许可、出处和转换说明；数据不适用项目 MIT。 |

No third-party input-method source or Microsoft candidate dictionary is copied into this repository.

首次准备脚本显式下载 mradermacher 的 LMT-60-1.7B 社区量化文件（默认 Q8_0，可明确选 Q6_K、Q5_K_M、Q4_K_M；Apache-2.0，来源及固定 revision/hash 见 docs/model-setup.md），不随应用 zip 分发权重。[模型许可文本](third-party/LMT-APACHE-2.0.txt)和[固定版 llama.cpp MIT 许可](third-party/llama.cpp-LICENSE.txt)随准备脚本保留到安装目录。社区模型卡未给出原始 safetensors revision，不作原始 commit 一致性保证。

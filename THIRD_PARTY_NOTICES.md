# Third-party notices

Pinyin Gloss original code and authored demo.tsv entries: MIT, Copyright (c) 2026 Ancho5515 and contributors.

| Component | License / copyright | Distribution |
| --- | --- | --- |
| .NET / WPF / Windows Forms | MIT and runtime third-party notices; Microsoft and .NET contributors | Self-contained Windows package includes runtime dependencies. Runtime packs' notice files are copied when present. |
| Microsoft.Data.Sqlite 10.0.12 | MIT; © Microsoft Corporation; .NET Foundation contributors | NuGet, redistributed managed assemblies; full [license](third-party/Microsoft.Data.Sqlite-LICENSE.txt). |
| SQLitePCLRaw 2.1.12 | Apache-2.0; Copyright 2014-2024 SourceGear, LLC | NuGet bridge/native bundles; full [license](third-party/SQLitePCLRaw-LICENSE.txt). |
| SQLite | Public domain, [copyright statement](https://www.sqlite.org/copyright.html) | Native e_sqlite3 bundled via SQLitePCLRaw. |
| llama.cpp b11077 (982a3329af8401772087d67c54d5948a5b270943) | MIT, ggml / llama.cpp contributors; CUDA distribution may include additional NVIDIA notices | Optional separate user download, not bundled by Publish.ps1. Preserve downloaded notices. |
| NiuTrans/LMT-60-1.7B | Apache-2.0 per official model card, [model](https://huggingface.co/NiuTrans/LMT-60-1.7B) | Not redistributed here. Retain exact checkpoint revision, tokenizer, chat template, license and conversion provenance. |
| xUnit.net / Visual Studio runner / Microsoft.NET.Test.Sdk | Respective upstream licenses, development/test dependencies | Not part of the application publish output. |
| Imported dictionaries / other model checkpoints | Their own licenses | User supplied. A recorded manifest is provenance, not permission to redistribute. |

No third-party input-method source or Microsoft candidate dictionary is copied into this repository.

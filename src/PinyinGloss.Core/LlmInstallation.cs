using System.Text.Json;

namespace PinyinGloss.Core;

// 安装脚本的完成清单；不下载文件，不覆盖已有手工模型配置。
public static class LlmInstallation
{
    private sealed record Manifest(int SchemaVersion, string Model, string Quantization, string ModelRepository,
        string ModelRevision, string ModelSha256, string ModelPath, string ExecutablePath, string Backend,
        string RuntimeTag, string RuntimeRevision, string? CpuExecutablePath = null);

    public static bool TryApply(AppSettings settings, string installDirectory, bool newProfile, out string? error)
    {
        error = null;
        var provider = settings.Providers.FirstOrDefault(p => p.Id == "lmt" && p.Kind == ProviderKind.LocalModel && p.Managed && p.Model == "NiuTrans/LMT-60-1.7B");
        if (provider == null || !provider.UseInstalledModel && (provider.ModelPath.Length != 0 || provider.ExecutablePath.Length != 0)) return false;
        var path = Path.Combine(installDirectory, "llm-installation.json");
        if (!File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("模型安装清单过大");
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), SettingsJson.Options)
                ?? throw new InvalidDataException("模型安装清单为空");
            if (manifest.SchemaVersion != 1 || manifest.Model != provider.Model || manifest.Quantization is not ("Q8_0" or "Q6_K" or "Q5_K_M" or "Q4_K_M") ||
                manifest.Backend is not ("Cpu" or "Cuda" or "Auto") || manifest.RuntimeTag != "b11077" ||
                manifest.RuntimeRevision != "982a3329af8401772087d67c54d5948a5b270943" ||
                !IsHex(manifest.ModelRevision, 40) || !IsHex(manifest.ModelSha256, 64))
                throw new InvalidDataException("模型安装清单版本或身份无效");
            var model = Path.GetFullPath(manifest.ModelPath, installDirectory);
            var executable = Path.GetFullPath(manifest.ExecutablePath, installDirectory);
            var cpu = string.IsNullOrWhiteSpace(manifest.CpuExecutablePath) ? "" : Path.GetFullPath(manifest.CpuExecutablePath, installDirectory);
            if (!File.Exists(model) || !File.Exists(executable) || !Path.GetFileName(executable).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("安装文件缺失，请重新运行模型准备脚本");
            if (manifest.Backend == "Auto" && cpu.Length == 0 || cpu.Length != 0 && (!File.Exists(cpu) || !Path.GetFileName(cpu).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase)))
                throw new FileNotFoundException("CPU 备用运行时缺失，请重新运行模型准备脚本");
            provider.ModelPath = model; provider.ExecutablePath = executable;
            provider.Quantization = manifest.Quantization; provider.Name = "LMT " + manifest.Quantization;
            provider.CpuExecutablePath = cpu;
            provider.ModelRevision = manifest.ModelRepository + "@" + manifest.ModelRevision;
            provider.ModelSha256 = manifest.ModelSha256; provider.UseGpu = manifest.Backend != "Cpu";
            provider.UseInstalledModel = true;
            if (newProfile) { provider.Enabled = true; settings.Startup.PreloadModel = true; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        { error = "本地模型自动配置失败：" + ex.Message; return false; }
    }
    private static bool IsHex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);
}

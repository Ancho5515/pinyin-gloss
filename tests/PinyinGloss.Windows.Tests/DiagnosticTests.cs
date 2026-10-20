using System.Diagnostics;
using System.IO;
using System.Text;

namespace PinyinGloss.Windows.Tests;

[Trait("Category", "DesktopCapture")]
public class DiagnosticTests
{
    [Fact]
    public async Task Redirected_diagnostic_output_is_utf8_and_omits_candidate_text_by_default()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "PinyinGloss.CandidateReader.dll"), "--diagnose", "--seconds", "1" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        try
        {
            using var bytes = new MemoryStream();
            var output = process.StandardOutput.BaseStream.CopyToAsync(bytes);
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await output;
            Assert.True(process.ExitCode == 0, await error);
            var text = new UTF8Encoding(false, true).GetString(bytes.ToArray());
            Assert.Contains("text=False", text);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}

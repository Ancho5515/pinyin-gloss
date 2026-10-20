using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using PinyinGloss.Core;
using PinyinGloss.Providers;

namespace PinyinGloss.Desktop;
public partial class App : Application
{
    private Mutex? _mutex;
    internal DesktopRuntime? Runtime { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        _mutex = new(true, $"Local\\PinyinGloss-{key}-{Process.GetCurrentProcess().SessionId}", out var created);
        if (!created) { MessageBox.Show("Pinyin Gloss 已在本次登录会话中运行，请从托盘打开设置。"); Shutdown(); return; }
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinyinGloss");
            Directory.CreateDirectory(directory);
            var settingsPath = Path.Combine(directory, "settings.json");
            var newProfile = !File.Exists(settingsPath);
            var settings = newProfile ? AppSettings.CreateDefault() : SettingsJson.Load(settingsPath);
            LlmInstallation.TryApply(settings, AppContext.BaseDirectory, newProfile, out var installationError);
            var demoDb = Path.Combine(directory, "dictionaries", "demo.db");
            if (!File.Exists(demoDb)) await DictionaryStore.ImportAsync(Path.Combine(AppContext.BaseDirectory, "dictionaries", "demo.tsv"), demoDb, "示例中英词典", "MIT", "Pinyin Gloss authors", default);
            foreach (var p in settings.Providers.Where(p => p.DictionaryPath == "dictionaries/demo.db")) p.DictionaryPath = demoDb;
            Runtime = new(settings, settingsPath, directory);
            var main = new MainWindow(Runtime); MainWindow = main; Runtime.Attach(main);
            await Runtime.StartAsync();
            if (installationError != null) Runtime.ReportInstallationError(installationError);
            if (!e.Args.Contains("--background")) main.Show();
            if (e.Args.Contains("--demo")) Runtime.ShowDemo();
        }
        catch (Exception ex) { MessageBox.Show("启动失败，未修改微软拼音。\n" + ex.Message, "Pinyin Gloss"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Runtime?.Dispose(); _mutex?.Dispose(); base.OnExit(e);
    }
}

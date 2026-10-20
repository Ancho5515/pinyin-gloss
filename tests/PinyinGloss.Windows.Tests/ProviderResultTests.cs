using PinyinGloss.Core;
using PinyinGloss.Desktop;

namespace PinyinGloss.Windows.Tests;

public class ProviderResultTests
{
    [Theory]
    [InlineData(ResultStatus.Unavailable, true)]
    [InlineData(ResultStatus.Timeout, true)]
    [InlineData(ResultStatus.NotFound, true)]
    [InlineData(ResultStatus.Ready, false)]
    [InlineData(ResultStatus.Pending, false)]
    public void Missing_provider_accepts_only_failure_status_so_pending_page_can_complete(ResultStatus status, bool expected)
    {
        var cell = new TranslationCell("page", 1, 1, "en", "en", "disabled", "unavailable", new("word", "苹果", "en", status));
        Assert.Equal(expected, DesktopRuntime.AcceptsProviderResult(new Dictionary<string, ITranslationProvider>(), cell));
        Assert.False(DesktopRuntime.AcceptsProviderResult(new Dictionary<string, ITranslationProvider>(), cell with { SourceVersion = "stale" }));
    }
}

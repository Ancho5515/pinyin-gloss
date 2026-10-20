using PinyinGloss.CandidateReader;

namespace PinyinGloss.Windows.Tests;

public class CandidatePageStabilityTests
{
    [Fact]
    public void Transitional_page_is_never_published_and_new_page_requires_separated_matching_reads()
    {
        var page = new CandidatePageStability();
        Assert.False(page.Observe("pi:皮,屁,批", TimeSpan.Zero));
        Assert.True(page.Observe("pi:皮,屁,批", TimeSpan.FromMilliseconds(50)));
        Assert.False(page.Observe("mixed:皮,品,聘", TimeSpan.FromMilliseconds(100)));
        Assert.True(page.IsConfirming);
        Assert.False(page.Observe("pin:拼,品,聘", TimeSpan.FromMilliseconds(120)));
        Assert.False(page.Observe("pin:拼,品,聘", TimeSpan.FromMilliseconds(130)));
        Assert.True(page.Observe("pin:拼,品,聘", TimeSpan.FromMilliseconds(170)));
        Assert.False(page.IsConfirming);
    }

    [Fact]
    public void Hidden_or_changed_foreground_requires_confirmation_even_for_same_text()
    {
        var page = new CandidatePageStability();
        page.Observe("same-window-and-text", TimeSpan.Zero);
        Assert.True(page.Observe("same-window-and-text", TimeSpan.FromMilliseconds(50)));
        page.Reset();
        Assert.False(page.Observe("same-window-and-text", TimeSpan.FromMilliseconds(500)));
        Assert.True(page.Observe("same-window-and-text", TimeSpan.FromMilliseconds(550)));
    }
}

using PinyinGloss.Core;
using PinyinGloss.CandidateReader;

namespace PinyinGloss.Windows.Tests;

public class TextProviderRouterTests
{
    [Theory]
    [InlineData(TextReadStatus.NoSuitableProvider, true)]
    [InlineData(TextReadStatus.UnsupportedControl, true)]
    [InlineData(TextReadStatus.InvalidTextScope, false)]
    [InlineData(TextReadStatus.AccessDenied, false)]
    [InlineData(TextReadStatus.PasswordTarget, false)]
    [InlineData(TextReadStatus.Success, false)]
    [InlineData(TextReadStatus.NoCaret, false)]
    public void A_fallback_cannot_override_target_or_scope_rejection(TextReadStatus status, bool expected)
    {
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.TextProviderRouter");
        Assert.NotNull(type);
        var method = type.GetMethod("CanFallback", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, [status]));
    }
}

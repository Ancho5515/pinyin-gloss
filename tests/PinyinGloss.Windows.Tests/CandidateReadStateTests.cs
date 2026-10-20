using PinyinGloss.CandidateReader;
using PinyinGloss.Core;

namespace PinyinGloss.Windows.Tests;

public class CandidateReadStateTests
{
    private static CandidateSnapshot Page(long generation) => new("session", $"page-{generation}", generation,
        "identity", new(10, 10, 100, 30), [new("word", "苹果", 1)]);

    [Fact]
    public void Changing_page_is_updating_until_confirmed_and_real_closure_still_hides()
    {
        var state = new CandidateReadState();
        Assert.Equal(ReaderMessageKind.Snapshot, state.Update(Page(1), false)!.Kind);
        Assert.Equal(ReaderMessageKind.Updating, state.Update(null, true)!.Kind);
        Assert.Null(state.Update(null, true));
        Assert.Equal(ReaderMessageKind.Snapshot, state.Update(Page(2), false)!.Kind);
        Assert.Equal(ReaderMessageKind.Hidden, state.Update(null, false)!.Kind);
        Assert.Null(state.Update(null, false));
    }

    [Fact]
    public void Closing_during_confirmation_hides_and_first_confirmation_does_not_invent_page()
    {
        var state = new CandidateReadState();
        Assert.Equal(ReaderMessageKind.Updating, state.Update(null, true)!.Kind);
        Assert.Equal(ReaderMessageKind.Hidden, state.Update(null, false)!.Kind);
        Assert.Null(state.Update(null, false));
    }
}

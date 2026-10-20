using PinyinGloss.Core;

namespace PinyinGloss.CandidateReader;

internal sealed class CandidateReadState
{
    private bool _visible, _updating;
    internal ReaderMessage? Update(CandidateSnapshot? snapshot, bool confirming)
    {
        if (snapshot != null) { _visible = true; _updating = false; return new(ReaderMessageKind.Snapshot, snapshot); }
        if (confirming)
        {
            if (_updating) return null;
            _updating = true;
            return new(ReaderMessageKind.Updating);
        }
        if (!_visible && !_updating) return null;
        _visible = _updating = false;
        return new(ReaderMessageKind.Hidden);
    }
}

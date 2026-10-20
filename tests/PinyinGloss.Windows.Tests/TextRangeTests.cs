using PinyinGloss.Core;
using PinyinGloss.CandidateReader;
using UIA = Interop.UIAutomationClient;

namespace PinyinGloss.Windows.Tests;

public class TextRangeTests
{
    [Theory]
    [InlineData("我喜欢苹果。今天的天气很好。", 14, "今天的天气很好。")]
    [InlineData("我喜欢苹果。今天的天气很好。", 3, "我喜欢苹果。")]
    [InlineData("First sentence. Second sentence.", 15, "First sentence.")]
    [InlineData("First sentence. Second sentence.", 20, "Second sentence.")]
    [InlineData("价格是3.14元。下一句。", 6, "价格是3.14元。")]
    [InlineData("第一句。\n", 5, "")]
    public void Only_the_caret_sentence_is_read(string value, int caret, string expected)
    {
        var document = new TextDocument(value);
        var result = ReadSentence(new TextRange(document, caret, caret), document.DocumentRange);
        Assert.Null(result.Status); Assert.Equal(expected, result.Text);
        Assert.All(document.ReadLimits, limit => Assert.InRange(limit, 1, 514));
    }

    [Fact]
    public void A_sentence_beyond_the_range_budget_is_rejected_without_unbounded_text_read()
    {
        var document = new TextDocument(new string('甲', 600));
        var result = ReadSentence(new TextRange(document, 300, 300), document.DocumentRange);
        Assert.Equal("too_long", result.Status); Assert.Equal("", result.Text);
        Assert.All(document.ReadLimits, limit => Assert.InRange(limit, 1, 514));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void An_embedded_input_sentence_stays_inside_its_own_document_range(int caretOffset)
    {
        const string prefix = "网页前文。", input = "十角馆杀人", suffix = "网页后文。";
        var document = new TextDocument(prefix + input + suffix, prefix.Length, prefix.Length + input.Length);
        var caret = prefix.Length + caretOffset;

        var result = ReadSentence(new TextRange(document, caret, caret), document.DocumentRange);

        Assert.Null(result.Status);
        Assert.Equal("十角馆杀人", result.Text);
        Assert.All(document.ReadRanges, range =>
        {
            Assert.InRange(range.Start, prefix.Length, prefix.Length + input.Length);
            Assert.InRange(range.End, prefix.Length, prefix.Length + input.Length);
        });
    }

    [Fact]
    public void An_empty_embedded_input_does_not_read_the_surrounding_page()
    {
        var document = new TextDocument("网页前文。网页后文。", 5, 5);

        var result = ReadSentence(new TextRange(document, 5, 5), document.DocumentRange);

        Assert.Null(result.Status);
        Assert.Equal("", result.Text);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(11, 11)]
    [InlineData(6, 8)]
    public void A_caret_outside_the_scope_or_not_collapsed_is_rejected_before_text_read(int start, int end)
    {
        var document = new TextDocument("网页前文。十角馆杀人网页后文。", 5, 10);
        var result = ReadSentence(new TextRange(document, start, end), document.DocumentRange);
        Assert.Equal("invalid_text_scope", result.Status);
        Assert.Equal("", result.Text);
        Assert.Empty(document.ReadLimits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void A_parent_text_pattern_uses_the_child_range_instead_of_the_page(int offset)
    {
        var document = new TextDocument("网页前文。十角馆杀人网页后文。");
        document.ChildRange = new TextRange(document, 5, 10);
        var scope = ActiveScope(document, false);
        var result = ReadSentence(new TextRange(document, 5 + offset, 5 + offset), scope);
        Assert.Null(result.Status);
        Assert.Equal("十角馆杀人", result.Text);
    }

    [Theory]
    [InlineData("selection", TextReadStatus.Success)]
    [InlineData("cross-boundary", TextReadStatus.InvalidTextScope)]
    [InlineData("no-selection", TextReadStatus.NoSelection)]
    [InlineData("password", TextReadStatus.PasswordTarget)]
    [InlineData("window-boundary", TextReadStatus.NoSuitableProvider)]
    [InlineData("disabled", TextReadStatus.NoSuitableProvider)]
    [InlineData("edit", TextReadStatus.NoSuitableProvider)]
    [InlineData("over-budget", TextReadStatus.NoSuitableProvider)]
    [InlineData("no-child-range", TextReadStatus.InvalidTextScope)]
    public void A_deep_group_selection_keeps_the_original_child_scope(string scenario, TextReadStatus expected)
    {
        const string prefix = "页面前文。", input = "测试。", suffix = "页面后文。";
        var document = new TextDocument(prefix + input + suffix);
        if (scenario != "no-child-range") document.ChildRange = new TextRange(document, prefix.Length, prefix.Length + input.Length);
        document.Selection = new TextRange(document, scenario == "cross-boundary" ? prefix.Length - 1 : prefix.Length,
            scenario == "no-selection" ? prefix.Length : prefix.Length + input.Length);
        var providerDepth = scenario == "over-budget" ? 64 : 16;
        var parents = new Dictionary<UIA.IUIAutomationElement, UIA.IUIAutomationElement>();
        UIA.IUIAutomationElement Node(int level) => Stub<UIA.IUIAutomationElement>((name, _) => name switch
        {
            "GetCurrentPattern" => level == providerDepth ? document : null,
            "get_CurrentControlType" => level == 0 && scenario == "edit" ? 50004 : level == providerDepth ? 50030 : 50026,
            "get_CurrentIsPassword" => scenario == "password" && level == 10 ? 1 : 0,
            "get_CurrentNativeWindowHandle" => scenario == "window-boundary" && level == 8 ? (nint)1 : (nint)0,
            _ => throw new NotSupportedException(name)
        });
        var focused = Node(providerDepth);
        for (var level = providerDepth - 1; level >= 0; level--)
        { var child = Node(level); parents[child] = focused; focused = child; }
        var walker = Stub<UIA.IUIAutomationTreeWalker>((name, args) => name == "GetParentElement"
            ? parents.GetValueOrDefault((UIA.IUIAutomationElement)args![0]!) : throw new NotSupportedException(name));
        var automation = Stub<UIA.IUIAutomation>((name, _) => name == "get_RawViewWalker" ? walker : throw new NotSupportedException(name));
        var target = new ResolvedTextTarget(focused, 1, 0, 1, "ChatGPT", "", "Chrome", scenario == "edit" ? 50004 : 50026, "test", false);

        var result = new UiaTextProvider(automation).Capture(target, new(true, scenario != "disabled", false), default);

        Assert.Equal(expected, result.Status);
        if (expected == TextReadStatus.Success)
        {
            Assert.Equal("RangeFromChild", result.ScopeKind);
            Assert.Equal(input, result.Capture?.Text);
            Assert.All(document.ReadRanges, range => { Assert.True(range.Start >= prefix.Length); Assert.True(range.End <= prefix.Length + input.Length); });
        }
        else { Assert.Null(result.Capture); Assert.Empty(document.ReadLimits); }
    }

    [Theory]
    [InlineData(0, 4, "invalid_text_scope", "")]
    [InlineData(4, 8, "invalid_text_scope", "")]
    [InlineData(7, 11, "invalid_text_scope", "")]
    [InlineData(5, 10, null, "十角馆杀人")]
    public void A_selection_must_be_completely_inside_the_active_scope(int start, int end, string? status, string expected)
    {
        var document = new TextDocument("网页前文。十角馆杀人网页后文。", 5, 10);
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.UiaTextProvider");
        Assert.NotNull(type);
        var method = type.GetMethod("ReadSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var result = ((string Text, string? Status))method.Invoke(null, [new TextRange(document, start, end), document.DocumentRange])!;
        Assert.Equal(status, result.Status);
        Assert.Equal(expected, result.Text);
        if (status != null) Assert.Empty(document.ReadLimits);
    }

    [Fact]
    public void An_unavailable_child_range_never_falls_back_to_the_parent_document()
    {
        var document = new TextDocument("网页全文。");
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => ActiveScope(document, false));
        Assert.IsType<System.Runtime.InteropServices.COMException>(error.InnerException);
        Assert.Empty(document.ReadLimits);
    }

    private static UIA.IUIAutomationTextRange ActiveScope(TextDocument document, bool ownsPattern)
    {
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.UiaActiveTextScope");
        Assert.NotNull(type);
        var method = type.GetMethod("Create", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var value = method.Invoke(null, [document, null, ownsPattern])!;
        return (UIA.IUIAutomationTextRange)type.GetProperty("Range", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(value)!;
    }

    [Fact]
    public void Password_and_nonforeground_target_are_rejected_before_text_access()
    {
        var type = typeof(PinyinGloss.CandidateReader.CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.TextTargetResolver");
        Assert.NotNull(type);
        var method = type.GetMethod("CanReadTarget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.False((bool)method.Invoke(null, [true, true, true])!);
        Assert.False((bool)method.Invoke(null, [false, false, true])!);
        Assert.True((bool)method.Invoke(null, [false, true, true])!);
    }

    [Theory]
    [InlineData(50000, true, false)] // Button 的祖先文档即使保留选区，也不是活动文本目标。
    [InlineData(50011, true, false)] // MenuItem
    [InlineData(50004, false, true)] // Edit 可以使用有界父链上的文本 provider。
    [InlineData(50020, false, true)] // Text
    [InlineData(50030, false, true)] // Document
    [InlineData(50025, false, false)] // Custom/Pane 必须自身提供 TextPattern。
    [InlineData(50025, true, true)]
    [InlineData(50033, false, false)]
    [InlineData(50033, true, true)]
    public void Only_active_text_roles_can_use_a_text_provider(int controlType, bool ownTextPattern, bool expected)
    {
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.TextTargetResolver");
        Assert.NotNull(type);
        var method = type.GetMethod("CanReadTextFocus", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, [controlType, ownTextPattern, false]));
    }

    [Theory]
    [InlineData(50026, true, true)] // Group 的当前选区可使用 RangeFromChild 限定。
    [InlineData(50026, false, false)]
    [InlineData(50000, true, false)]
    [InlineData(50011, true, false)]
    [InlineData(50033, true, false)]
    public void A_group_can_borrow_a_child_range_only_for_enabled_selection_reading(int type, bool selection, bool expected)
    {
        Assert.Equal(expected, TextTargetResolver.CanReadTextFocus(type, false, selection));
    }

    [Theory]
    [InlineData(50032, 10, 10, 10, 10, TextReadStatus.UnsupportedControl)]
    [InlineData(50032, 10, 10, 0, 10, TextReadStatus.NoFocusedTarget)]
    [InlineData(50032, 10, 11, 10, 10, TextReadStatus.NoFocusedTarget)]
    [InlineData(50032, 10, 10, 10, 20, TextReadStatus.NoFocusedTarget)]
    [InlineData(50032, 0, 0, 0, 0, TextReadStatus.NoFocusedTarget)]
    [InlineData(50000, 10, 10, 10, 10, TextReadStatus.NoFocusedTarget)]
    public void A_native_caret_without_an_exposed_editor_reports_unsupported_instead_of_idle(
        int type, long elementWindow, long nativeFocus, long caretWindow, long foreground, TextReadStatus expected)
    {
        Assert.Equal(expected, TextTargetResolver.UnreadableFocusStatus(type, elementWindow, nativeFocus, caretWindow, foreground));
    }

    private static (string Text, string? Status) ReadSentence(UIA.IUIAutomationTextRange caret, UIA.IUIAutomationTextRange scope)
    {
        var type = typeof(CandidateReadState).Assembly.GetType("PinyinGloss.CandidateReader.UiaTextProvider");
        Assert.NotNull(type);
        var method = type.GetMethod("CaptureWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var capture = ((CapturedText? Capture, TextReadStatus Status))method.Invoke(null, [caret, scope, false])!;
        if (capture.Capture == null) return ("", capture.Status == TextReadStatus.InvalidTextScope ? "invalid_text_scope" : "too_long");
        var result = SentenceExtractor.Extract(capture.Capture, new(true, false, false));
        return (result.Text, result.Status == TextReadStatus.Success ? null : result.Status == TextReadStatus.InvalidTextScope ? "invalid_text_scope" : "too_long");
    }

    [Fact]
    public void A_same_length_edit_between_left_and_right_reads_is_rejected()
    {
        var document = new TextDocument("甲乙");
        document.BeforeRead = count => { if (count == 2) document.Value = "丙丁"; };
        var result = UiaTextProvider.CaptureWindow(new TextRange(document, 1, 1), document.DocumentRange, false);
        Assert.Equal(TextReadStatus.TargetChanged, result.Status);
        Assert.Null(result.Capture);
    }

    [Fact]
    public void A_selection_changed_without_moving_its_endpoints_is_rejected()
    {
        var document = new TextDocument("甲乙");
        document.BeforeRead = count => { if (count == 2) document.Value = "丙丁"; };
        var result = UiaTextProvider.ReadSelection(document.DocumentRange, document.DocumentRange);
        Assert.Equal("target_changed", result.Status);
        Assert.Equal("", result.Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Empty_boundary_positions_inside_the_same_input_are_aligned_to_its_scope(bool atStart, bool selection)
    {
        var document = atStart ? new TextDocument("\0测试", 1, 3) : new TextDocument("测试\0", 0, 2);
        document.EmptyPosition = atStart ? 0 : 2;
        var original = atStart ? new TextRange(document, 0, selection ? 2 : 0) : new TextRange(document, selection ? 1 : 3, 3);
        var scope = new UiaActiveTextScope(document.DocumentRange, "OwnDocument");

        var normalized = Normalize(scope, original, _ => true);

        Assert.NotNull(normalized);
        Assert.True(scope.Contains(normalized, !selection));
        Assert.False(scope.Contains(original, false)); // 不修改浏览器返回的原始范围。
        if (selection) Assert.Equal(atStart ? "测" : "试", UiaTextProvider.ReadSelection(normalized, scope.Range).Text);
        else Assert.Equal("测试", ReadSentence(normalized, scope.Range).Text);
    }

    [Theory]
    [InlineData("OwnDocument", false)]
    [InlineData("RangeFromChild", true)]
    public void An_unverified_input_or_inherited_scope_cannot_normalize_an_external_range(string kind, bool sameInput)
    {
        var document = new TextDocument("测试\0", 0, 2) { EmptyPosition = 2 };
        var scope = new UiaActiveTextScope(document.DocumentRange, kind);

        Assert.Null(Normalize(scope, new TextRange(document, 3, 3), _ => sameInput));
        Assert.Empty(document.ReadLimits);
    }

    [Fact]
    public void A_nonempty_gap_is_rejected_without_reading_the_external_selection()
    {
        var document = new TextDocument("测试外部文字", 0, 2);
        var scope = new UiaActiveTextScope(document.DocumentRange, "OwnDocument");

        Assert.Null(Normalize(scope, new TextRange(document, 2, 6), _ => true));
        Assert.All(document.ReadLimits, limit => Assert.Equal(1, limit));
    }

    [Fact]
    public void A_boundary_gap_changed_between_confirmations_is_rejected()
    {
        var document = new TextDocument("测试\0", 0, 2) { EmptyPosition = 2 };
        document.BeforeRead = count => { if (count == 2) { document.Value = "测试外"; document.EmptyPosition = null; } };
        var scope = new UiaActiveTextScope(document.DocumentRange, "OwnDocument");

        Assert.Null(Normalize(scope, new TextRange(document, 3, 3), _ => true));
        Assert.Equal(new[] { 1, 1 }, document.ReadLimits);
    }

    [Theory]
    [InlineData("text-child", TextReadStatus.Success)]
    [InlineData("nested-input", TextReadStatus.InvalidTextScope)]
    [InlineData("other-input", TextReadStatus.InvalidTextScope)]
    [InlineData("password-parent", TextReadStatus.InvalidTextScope)]
    [InlineData("deep-child", TextReadStatus.InvalidTextScope)]
    public void Editable_boundary_ownership_is_verified_through_the_production_provider(string hierarchy, TextReadStatus expected)
    {
        var document = new TextDocument("测试\0", 0, 2) { EmptyPosition = 2 };
        var parents = new Dictionary<UIA.IUIAutomationElement, UIA.IUIAutomationElement>();
        var focused = Stub<UIA.IUIAutomationElement>((name, args) => name switch
        {
            "get_CurrentControlType" => 50004,
            "get_CurrentIsPassword" => 0,
            "get_CurrentHasKeyboardFocus" => 1,
            "GetCurrentPattern" => (int)args![0]! == 10014 ? document : null,
            _ => throw new NotSupportedException(name)
        });
        UIA.IUIAutomationElement Node(int role, bool password = false) => Stub<UIA.IUIAutomationElement>((name, _) => name switch
        {
            "get_CurrentControlType" => role,
            "get_CurrentIsPassword" => password ? 1 : 0,
            _ => throw new NotSupportedException(name)
        });
        var text = Node(50020);
        document.EnclosingElement = text;
        document.Selection = new TextRange(document, 3, 3);
        var parent = focused;
        if (hierarchy == "nested-input") { parent = Node(50004); parents[parent] = focused; }
        if (hierarchy == "other-input") parent = Node(50004);
        if (hierarchy == "password-parent") { parent = Node(50026, true); parents[parent] = focused; }
        if (hierarchy == "deep-child")
            for (var i = 0; i < 8; i++) { var next = Node(50026); parents[next] = parent; parent = next; }
        parents[text] = parent;
        var walker = Stub<UIA.IUIAutomationTreeWalker>((name, args) => name == "GetParentElement"
            ? parents.GetValueOrDefault((UIA.IUIAutomationElement)args![0]!) : throw new NotSupportedException(name));
        var automation = Stub<UIA.IUIAutomation>((name, args) => name switch
        {
            "get_RawViewWalker" => walker,
            "CompareElements" => ReferenceEquals(args![0], args[1]) ? 1 : 0,
            _ => throw new NotSupportedException(name)
        });
        var target = new ResolvedTextTarget(focused, 1, 0, 1, "msedge", "", "", 50004, "test", false);

        var result = new UiaTextProvider(automation).Capture(target, new(true, true, false), default);

        Assert.Equal(expected, result.Status);
        if (expected == TextReadStatus.Success) Assert.Equal("测试", result.Capture?.Text);
        else { Assert.Null(result.Capture); Assert.Empty(document.ReadLimits); }
    }

    private static T Stub<T>(Func<string, object?[]?, object?> invoke) where T : class
    {
        var value = System.Reflection.DispatchProxy.Create<T, UiaStub>();
        ((UiaStub)(object)value).InvokeMember = invoke;
        return value;
    }

    [Theory]
    [InlineData(4, true, true)]
    [InlineData(24, true, true)] // Codex 编辑区到原生宿主的真实深度。
    [InlineData(24, false, false)]
    [InlineData(64, true, false)]
    public void Foreground_ownership_accepts_deep_controls_but_rejects_other_windows_and_over_budget_chains(int depth, bool sameWindow, bool expected)
    {
        // 测试专用 HWND 不显示、不激活；只验证元数据父链，不读取真实桌面。
        var window = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var otherWindow = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.NotEqual(0, window); Assert.NotEqual(0, otherWindow);
        try
        {
            UIA.IUIAutomationElement Node(nint hwnd) => Stub<UIA.IUIAutomationElement>((name, _) =>
                name == "get_CurrentNativeWindowHandle" ? hwnd : throw new NotSupportedException(name));
            var parents = new Dictionary<UIA.IUIAutomationElement, UIA.IUIAutomationElement>();
            var element = Node(window);
            for (var i = 0; i < depth; i++) { var child = Node(0); parents[child] = element; element = child; }
            var parentCalls = 0;
            var walker = Stub<UIA.IUIAutomationTreeWalker>((name, args) =>
            {
                Assert.Equal("GetParentElement", name); parentCalls++;
                return parents.GetValueOrDefault((UIA.IUIAutomationElement)args![0]!);
            });
            var automation = Stub<UIA.IUIAutomation>((name, _) =>
                name == "get_RawViewWalker" ? walker : throw new NotSupportedException(name));
            var resolver = new TextTargetResolver();
            typeof(TextTargetResolver).GetField("<Automation>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(resolver, automation);

            Assert.Equal(expected, resolver.InForeground(element, sameWindow ? window : otherWindow));
            Assert.InRange(parentCalls, 0, 64);
        }
        finally { DestroyWindow(window); DestroyWindow(otherWindow); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint CreateWindowEx(int extendedStyle, string className, string title, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    public class UiaStub : System.Reflection.DispatchProxy
    {
        internal Func<string, object?[]?, object?> InvokeMember = null!;
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => InvokeMember(method!.Name, args);
    }

    private static UIA.IUIAutomationTextRange? Normalize(UiaActiveTextScope scope, UIA.IUIAutomationTextRange range,
        Func<UIA.IUIAutomationTextRange, bool> belongsToInput)
    {
        return scope.Normalize(range, belongsToInput);
    }

    // 有限的 UIA 范围替身，只模拟端点语义；不启动窗口或读取真实桌面。
    private sealed class TextDocument(string value, int scopeStart = 0, int? scopeEnd = null) : UIA.IUIAutomationTextPattern
    {
        internal string Value { get; set; } = value;
        internal Action<int>? BeforeRead;
        internal int? EmptyPosition;
        internal List<int> ReadLimits { get; } = [];
        internal List<(int Start, int End)> ReadRanges { get; } = [];
        internal UIA.IUIAutomationTextRange? ChildRange { get; set; }
        internal UIA.IUIAutomationElement? EnclosingElement;
        internal UIA.IUIAutomationTextRange? Selection;
        public UIA.IUIAutomationTextRange DocumentRange => new TextRange(this, scopeStart, scopeEnd ?? Value.Length);
        public UIA.SupportedTextSelection SupportedTextSelection => UIA.SupportedTextSelection.SupportedTextSelection_Single;
        public UIA.IUIAutomationTextRange RangeFromPoint(UIA.tagPOINT pt) => throw new NotSupportedException();
        public UIA.IUIAutomationTextRange RangeFromChild(UIA.IUIAutomationElement child) => ChildRange ?? throw new System.Runtime.InteropServices.COMException("No child text range", unchecked((int)0x80004002));
        public UIA.IUIAutomationTextRangeArray GetSelection() => Selection is { } value ? new TextRanges(value) : throw new NotSupportedException();
        public UIA.IUIAutomationTextRangeArray GetVisibleRanges() => throw new NotSupportedException();
    }
    private sealed class TextRanges(UIA.IUIAutomationTextRange range) : UIA.IUIAutomationTextRangeArray
    {
        public int Length => 1;
        public UIA.IUIAutomationTextRange GetElement(int index) => index == 0 ? range : throw new ArgumentOutOfRangeException(nameof(index));
    }
    private sealed class TextRange(TextDocument document, int start, int end) : UIA.IUIAutomationTextRange
    {
        private int _start = start, _end = end;
        private int Endpoint(UIA.TextPatternRangeEndpoint point) => point == UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start ? _start : _end;
        private void Set(UIA.TextPatternRangeEndpoint point, int value)
        {
            if (point == UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) { _start = value; _end = Math.Max(_end, value); }
            else { _end = value; _start = Math.Min(_start, value); }
        }
        public UIA.IUIAutomationTextRange Clone() => new TextRange(document, _start, _end);
        public int Compare(UIA.IUIAutomationTextRange range) => range is TextRange other && _start == other._start && _end == other._end ? 1 : 0;
        public int CompareEndpoints(UIA.TextPatternRangeEndpoint endpoint, UIA.IUIAutomationTextRange range, UIA.TextPatternRangeEndpoint targetEndpoint) => Endpoint(endpoint) - ((TextRange)range).Endpoint(targetEndpoint);
        public UIA.IUIAutomationTextRange? FindText(string text, int backward, int ignoreCase)
        {
            var value = document.Value.Substring(_start, _end - _start);
            var comparison = ignoreCase != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var index = backward != 0 ? value.LastIndexOf(text, comparison) : value.IndexOf(text, comparison);
            return index < 0 ? null : new TextRange(document, _start + index, _start + index + text.Length);
        }
        public string GetText(int maxLength)
        {
            document.ReadLimits.Add(maxLength); document.ReadRanges.Add((_start, _end)); document.BeforeRead?.Invoke(document.ReadLimits.Count); Assert.True(maxLength > 0);
            var value = document.Value.Substring(_start, _end - _start);
            if (document.EmptyPosition is { } empty && empty >= _start && empty < _end) value = value.Remove(empty - _start, 1);
            return value[..Math.Min(value.Length, maxLength)];
        }
        public int MoveEndpointByUnit(UIA.TextPatternRangeEndpoint endpoint, UIA.TextUnit unit, int count)
        {
            Assert.Equal(UIA.TextUnit.TextUnit_Character, unit);
            var old = Endpoint(endpoint); var value = Math.Clamp(old + count, 0, document.Value.Length); Set(endpoint, value); return value - old;
        }
        public void MoveEndpointByRange(UIA.TextPatternRangeEndpoint endpoint, UIA.IUIAutomationTextRange range, UIA.TextPatternRangeEndpoint targetEndpoint) => Set(endpoint, ((TextRange)range).Endpoint(targetEndpoint));
        public void ExpandToEnclosingUnit(UIA.TextUnit unit) => throw new NotSupportedException();
        public UIA.IUIAutomationTextRange FindAttribute(int attributeId, object value, int backward) => throw new NotSupportedException();
        public object GetAttributeValue(int attributeId) => attributeId == 40015 ? false : throw new NotSupportedException();
        public double[] GetBoundingRectangles() => throw new NotSupportedException();
        public UIA.IUIAutomationElement GetEnclosingElement() => document.EnclosingElement ?? throw new NotSupportedException();
        public int Move(UIA.TextUnit unit, int count) => throw new NotSupportedException();
        public void Select() => throw new NotSupportedException();
        public void AddToSelection() => throw new NotSupportedException();
        public void RemoveFromSelection() => throw new NotSupportedException();
        public void ScrollIntoView(int alignToTop) => throw new NotSupportedException();
        public UIA.IUIAutomationElementArray GetChildren() => throw new NotSupportedException();
    }
}

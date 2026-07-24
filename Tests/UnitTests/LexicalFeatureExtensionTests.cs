using System.Text.Json;
using Blazor.Lexical;
using Microsoft.AspNetCore.Components;

// These tests set [Parameter] properties directly, which BL0005 flags because the
// framework normally owns them. That is the point here: the assertions are about what a
// given parameter combination produces, so the tests set them the way the renderer would
// and then read the descriptor, with no renderer in the loop.
#pragma warning disable BL0005 // Component parameter should not be set outside of its component

namespace Tests.UnitTests;

/// <summary>
/// Locks the C# half of the built-in feature extensions added alongside mentions and
/// tables — the TOC, marks, and stats. Three things matter here and nowhere else:
/// the option payload each one hands JS (a renamed property silently disables a
/// feature, because the JS side just reads <c>undefined</c>), the opt-in interop gate
/// (invariant #1 — no delegate, no JS→.NET calls), and single-instance enforcement.
/// The behaviour itself is covered end-to-end by Tests.Integration.
/// </summary>
public class LexicalFeatureExtensionTests
{
    /// <summary>An <see cref="EventCallback{T}"/> with a delegate, to arm a gate.</summary>
    private static EventCallback<T> Wired<T>() =>
        EventCallback.Factory.Create<T>(new object(), (T _) => { });

    /// <summary>The option payload an extension hands the JS factory.</summary>
    private static JsonElement Options(LexicalExtension extension) =>
        extension.ToDto().Options ?? throw new InvalidOperationException("no options");

    // --- TOC -------------------------------------------------------------

    [Fact] // the JS side reads these exact camelCase keys; a rename disables the feature
    public void Toc_options_serialize_every_knob()
    {
        var options = Options(new LexicalToc
        {
            TargetSelector = "#outline",
            MinLevel = 2,
            MaxLevel = 4,
            AnchorPrefix = "doc-",
            ScrollSpy = false,
            SmoothScroll = false,
            FocusOnClick = true,
        });

        Assert.Equal("#outline", options.GetProperty("targetSelector").GetString());
        Assert.Equal(2, options.GetProperty("minLevel").GetInt32());
        Assert.Equal(4, options.GetProperty("maxLevel").GetInt32());
        Assert.Equal("doc-", options.GetProperty("anchorPrefix").GetString());
        Assert.False(options.GetProperty("scrollSpy").GetBoolean());
        Assert.False(options.GetProperty("smoothScroll").GetBoolean());
        Assert.True(options.GetProperty("focusOnClick").GetBoolean());
    }

    [Fact] // headings 1..3 rendered nowhere is the zero-config shape
    public void Toc_options_default_to_h1_through_h3_with_scrollspy()
    {
        var options = Options(new LexicalToc());

        Assert.Equal(1, options.GetProperty("minLevel").GetInt32());
        Assert.Equal(3, options.GetProperty("maxLevel").GetInt32());
        Assert.True(options.GetProperty("scrollSpy").GetBoolean());
        // Null leaves are omitted, so an unset selector is simply absent.
        Assert.False(options.TryGetProperty("targetSelector", out _));
    }

    [Fact] // a JS-rendered outline is a pure client-side feature: zero interop
    public void Toc_reports_no_invoke_handler_without_a_callback()
    {
        Assert.False(new LexicalToc { TargetSelector = "#outline" }.ToDto().HasInvokeHandler);
    }

    [Fact] // subscribing is what arms the channel
    public void Toc_reports_an_invoke_handler_once_OnTocChanged_is_wired()
    {
        var toc = new LexicalToc { OnTocChanged = Wired<IReadOnlyList<LexicalTocEntry>>() };

        Assert.True(toc.ToDto().HasInvokeHandler);
    }

    [Fact] // the built-in tier is named by BuiltIn, never by a module URL
    public void Toc_is_a_built_in_module()
    {
        var dto = new LexicalToc().ToDto();

        Assert.Equal("toc", dto.BuiltIn);
        Assert.Null(dto.ModuleUrl);
    }

    // --- Marks -----------------------------------------------------------

    [Fact] // a purely decorative highlighter must not call into .NET at all
    public void Marks_reports_no_invoke_handler_without_a_callback()
    {
        Assert.False(new LexicalMarks().ToDto().HasInvokeHandler);
    }

    [Fact] // either callback arms it; neither is required for the .NET→JS methods
    public void Marks_reports_an_invoke_handler_for_either_callback()
    {
        Assert.True(
            new LexicalMarks { OnMarkClicked = Wired<IReadOnlyList<string>>() }
                .ToDto().HasInvokeHandler);
        Assert.True(
            new LexicalMarks { OnActiveMarksChanged = Wired<IReadOnlyList<string>>() }
                .ToDto().HasInvokeHandler);
    }

    [Fact]
    public void Marks_is_a_built_in_module_with_no_options()
    {
        var dto = new LexicalMarks().ToDto();

        Assert.Equal("marks", dto.BuiltIn);
        Assert.Null(dto.Options);
    }

    // --- Comment composer ------------------------------------------------

    [Fact] // OpenAsync is C#→JS; with nothing that needs JS→.NET, no handler is reported
    public void CommentComposer_reports_no_invoke_handler_by_default()
    {
        Assert.False(new LexicalCommentComposer().ToDto().HasInvokeHandler);
    }

    [Fact] // either push arms it — and so does a factory (the add button's compose request)
    public void CommentComposer_reports_an_invoke_handler_for_a_callback_or_a_factory()
    {
        Assert.True(
            new LexicalCommentComposer { OnSubmit = Wired<CommentComposition>() }
                .ToDto().HasInvokeHandler);
        Assert.True(
            new LexicalCommentComposer
            {
                OnCancel = EventCallback.Factory.Create(new object(), () => { }),
            }.ToDto().HasInvokeHandler);
        Assert.True(
            new LexicalCommentComposer { NewMarkId = () => "id" }.ToDto().HasInvokeHandler);
    }

    [Fact]
    public void CommentComposer_is_a_built_in_module()
    {
        Assert.Equal("comments", new LexicalCommentComposer().ToDto().BuiltIn);
    }

    [Fact] // the JS side reads these exact camelCase keys off the options payload
    public void CommentComposer_options_carry_the_factory_flag_and_click_away()
    {
        var withFactory = Options(new LexicalCommentComposer
        {
            NewMarkId = () => "id",
            CloseOnClickAway = false,
        });
        Assert.True(withFactory.GetProperty("hasMarkIdFactory").GetBoolean());
        Assert.False(withFactory.GetProperty("closeOnClickAway").GetBoolean());

        var bare = Options(new LexicalCommentComposer());
        Assert.False(bare.GetProperty("hasMarkIdFactory").GetBoolean());
        Assert.True(bare.GetProperty("closeOnClickAway").GetBoolean());
    }

    // --- Stats -----------------------------------------------------------

    [Fact]
    public void Stats_options_serialize_every_knob()
    {
        var options = Options(new LexicalStats
        {
            TargetSelector = "#count",
            Template = "{words}w",
            WordsPerMinute = 250,
        });

        Assert.Equal("#count", options.GetProperty("targetSelector").GetString());
        Assert.Equal("{words}w", options.GetProperty("template").GetString());
        Assert.Equal(250, options.GetProperty("wordsPerMinute").GetInt32());
    }

    [Fact]
    public void Stats_options_default_to_a_word_count_at_200wpm()
    {
        var options = Options(new LexicalStats());

        Assert.Equal("{words} words", options.GetProperty("template").GetString());
        Assert.Equal(200, options.GetProperty("wordsPerMinute").GetInt32());
    }

    [Fact] // a word counter written into the page is client-side only
    public void Stats_reports_no_invoke_handler_without_a_callback()
    {
        Assert.False(new LexicalStats { TargetSelector = "#count" }.ToDto().HasInvokeHandler);
    }

    [Fact]
    public void Stats_reports_an_invoke_handler_once_OnStatsChanged_is_wired()
    {
        var stats = new LexicalStats { OnStatsChanged = Wired<LexicalDocumentStats>() };

        Assert.True(stats.ToDto().HasInvokeHandler);
    }

    // --- Horizontal rule -------------------------------------------------

    [Fact]
    public void HorizontalRule_is_a_built_in_module_with_no_options()
    {
        var dto = new LexicalHorizontalRule().ToDto();

        Assert.Equal("hr", dto.BuiltIn);
        Assert.Null(dto.Options);
    }

    [Fact] // inserting a rule is a JS command; nothing calls back into .NET
    public void HorizontalRule_reports_no_invoke_handler()
    {
        Assert.False(new LexicalHorizontalRule().ToDto().HasInvokeHandler);
    }

    // --- Tab indent ------------------------------------------------------

    [Fact]
    public void TabIndent_is_a_built_in_module()
    {
        Assert.Equal("tabIndent", new LexicalTabIndent().ToDto().BuiltIn);
    }

    [Fact]
    public void TabIndent_options_carry_the_indent_cap()
    {
        var options = Options(new LexicalTabIndent { MaxIndent = 5 });

        Assert.Equal(5, options.GetProperty("maxIndent").GetInt32());
    }

    [Fact] // no cap is the default, and null is omitted rather than sent
    public void TabIndent_options_omit_an_unset_cap()
    {
        var options = Options(new LexicalTabIndent());

        Assert.False(options.TryGetProperty("maxIndent", out _));
    }

    [Fact] // rebinding Tab is a keyboard-behavior change, never a .NET round trip
    public void TabIndent_reports_no_invoke_handler()
    {
        Assert.False(new LexicalTabIndent().ToDto().HasInvokeHandler);
    }

    // --- Single-instance -------------------------------------------------

    [Theory] // a duplicate would register nodes and listeners twice, so it fails loudly
    [InlineData(typeof(LexicalToc))]
    [InlineData(typeof(LexicalMarks))]
    [InlineData(typeof(LexicalCommentComposer))]
    [InlineData(typeof(LexicalStats))]
    [InlineData(typeof(LexicalHorizontalRule))]
    [InlineData(typeof(LexicalTabIndent))]
    public void A_second_instance_in_one_editor_throws(Type extensionType)
    {
        var editor = new LexicalEditor();
        editor.RegisterExtension((LexicalExtension)Activator.CreateInstance(extensionType)!);

        var second = (LexicalExtension)Activator.CreateInstance(extensionType)!;

        Assert.Throws<InvalidOperationException>(() => editor.RegisterExtension(second));
    }
}

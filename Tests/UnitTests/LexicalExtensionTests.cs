using Blazor.Lexical;

namespace Tests.UnitTests;

/// <summary>
/// Locks the extension SDK's opt-in semantics at the C# level — the half that decides
/// what the editor tells JS. The wire payload and both call directions are covered
/// end-to-end by Tests.Integration's <c>ExtensionTests</c> (the badge extension).
/// </summary>
public class LexicalExtensionTests
{
    /// <summary>A pure client-side extension: it never overrides the invoke hook.</summary>
    private sealed class QuietExtension : LexicalExtension
    {
        protected override string? ModuleUrl => "./_content/Sample/quiet.mjs";

        /// <summary>Surfaces the protected gate for assertions.</summary>
        public bool InvokeHandlerReported => HasInvokeHandler;

        /// <summary>Surfaces the protected multi-instance flag for assertions.</summary>
        public bool MultipleAllowed => AllowMultiple;
    }

    /// <summary>An extension that opts into JS→.NET by overriding the hook.</summary>
    private class TalkingExtension : LexicalExtension
    {
        protected override string? ModuleUrl => "./_content/Sample/talking.mjs";

        protected override Task<string?> OnInvokeAsync(string method, string argsJson) =>
            Task.FromResult<string?>($"\"{method}\"");

        public bool InvokeHandlerReported => HasInvokeHandler;
    }

    /// <summary>Narrows the gate further, the way a consumer-facing callback would.</summary>
    private sealed class ConditionalExtension : TalkingExtension
    {
        public bool Enabled { get; set; }

        protected override bool HasInvokeHandler => Enabled;
    }

    /// <summary>Multi-instance extensions declare themselves so the editor allows duplicates.</summary>
    private sealed class MultiExtension : LexicalExtension
    {
        // protected *internal* on the base — and this assembly now sees the library's
        // internals (InternalsVisibleTo), so the override has to match both halves.
        protected internal override bool AllowMultiple => true;

        public bool MultipleAllowed => AllowMultiple;
    }

    /// <summary>Inherits the override without redeclaring it — the vtable case.</summary>
    private sealed class InheritingExtension : TalkingExtension;

    /// <summary>An extension with no JS half at all — it declares nothing.</summary>
    private sealed class BareExtension : LexicalExtension
    {
        public string? DeclaredModuleUrl => ModuleUrl;
    }

    [Fact] // not overriding OnInvokeAsync must report no handler — JS then never calls back
    public void HasInvokeHandler_IsFalse_WhenOnInvokeAsyncIsNotOverridden()
    {
        Assert.False(new QuietExtension().InvokeHandlerReported);
    }

    [Fact] // overriding the hook is the opt-in; no second flag to remember
    public void HasInvokeHandler_IsTrue_WhenOnInvokeAsyncIsOverridden()
    {
        Assert.True(new TalkingExtension().InvokeHandlerReported);
    }

    [Fact] // the gate resolves the override through the vtable, not the declaring type
    public void HasInvokeHandler_IsTrue_WhenTheOverrideIsInherited()
    {
        Assert.True(new InheritingExtension().InvokeHandlerReported);
    }

    [Fact] // an extension may narrow the gate to its own consumer-wired callback
    public void HasInvokeHandler_HonoursAFurtherOverride()
    {
        var extension = new ConditionalExtension();

        Assert.False(extension.InvokeHandlerReported);
        extension.Enabled = true;
        Assert.True(extension.InvokeHandlerReported);
    }

    [Fact] // a JS half is optional: an extension that is pure C# declares no module
    public void ModuleUrl_DefaultsToNull()
    {
        Assert.Null(new BareExtension().DeclaredModuleUrl);
    }

    [Fact] // single-instance is the default; multi-instance extensions opt in explicitly
    public void AllowMultiple_DefaultsToFalse()
    {
        Assert.False(new QuietExtension().MultipleAllowed);
        Assert.True(new MultiExtension().MultipleAllowed);
    }
}

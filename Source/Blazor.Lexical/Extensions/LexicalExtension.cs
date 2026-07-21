using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Base class for a consumer-authored editor extension: a component nested inside a
/// <see cref="LexicalEditor"/> that contributes its own Lexical nodes, commands and
/// plugins on the JS side, and (optionally) its own two-way channel to .NET.
/// </summary>
/// <remarks>
/// <para>
/// An extension is used exactly like any other editor child — the editor cascades
/// itself, and the extension registers with it during <c>OnInitialized</c>, which runs
/// before the editor's JS <c>create</c>. That ordering is what lets an extension add
/// custom nodes: Lexical requires every node class up front, so the declaration window
/// closes once the editor exists.
/// </para>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;MyExtension /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// The JS half is a separately-built ES module served by URL — typically a static web
/// asset of the extension's own Razor Class Library
/// (<c>_content/&lt;Assembly&gt;/my-extension.mjs</c>) — named by <see cref="ModuleUrl"/>
/// and <c>import()</c>ed at runtime. Its default export is a factory that receives this
/// extension's <see cref="GetOptions"/> payload and returns the module contract
/// (<c>nodes</c> / <c>register</c> / <c>invoke</c>); see <c>js/src/extension.ts</c> for
/// the typed contract.
/// </para>
/// <para>
/// Interop is opt-in, like every other channel in this library: an extension that does
/// not override <see cref="OnInvokeAsync"/> reports no invoke handler, and its JS half's
/// <c>invokeDotNet</c> throws rather than calling back — so a purely client-side
/// extension performs zero JS→.NET calls.
/// </para>
/// <para>
/// The component renders nothing by default. Override <see cref="ComponentBase.BuildRenderTree"/>
/// (or supply a <c>.razor</c> subclass with <c>@inherits</c>) to render toolbar buttons or
/// overlay containers; they land inside the editor root like any other child content.
/// </para>
/// </remarks>
public abstract class LexicalExtension : ComponentBase, IDisposable
{
    /// <summary>
    /// The editor this extension belongs to, supplied by <see cref="LexicalEditor"/>'s
    /// cascade. <c>null</c> when the component is placed outside an editor, in which
    /// case the extension is inert.
    /// </summary>
    [CascadingParameter] protected LexicalEditor? Editor { get; set; }

    /// <summary>
    /// Stable id for this extension instance, generated once. Routes calls in both
    /// directions (<c>InvokeExtensionAsync</c> from JS, <c>invokeExtension</c> to JS).
    /// </summary>
    internal string ExtensionId { get; } = $"ext-{Guid.NewGuid():N}";

    /// <summary>
    /// URL of the extension's ES module, <c>import()</c>ed by the editor before the
    /// underlying Lexical editor is created. Typically an RCL static web asset, e.g.
    /// <c>"./_content/My.Extension/my-extension.mjs"</c>. <c>null</c> (the default) is
    /// an extension with no JS half.
    /// </summary>
    protected virtual string? ModuleUrl => null;

    // Names a module compiled into the library's own bundle, for the features that ship
    // here (table, mentions). It is the *other* bundling tier of the same contract —
    // built-in modules are reached by a literal import() in create(), external ones by
    // the runtime ModuleUrl above — which is why this is internal: it is deliberately
    // not a seam consumers can reach into, and an internal virtual cannot be overridden
    // outside this assembly. Wins over ModuleUrl when both are set.
    internal virtual string? BuiltIn => null;

    /// <summary>
    /// Whether more than one instance of this extension type may be nested in the same
    /// editor. <c>false</c> (the default) is the common case — a second
    /// <c>&lt;MyExtension /&gt;</c> in one editor is almost always a mistake, and the
    /// editor throws rather than silently running it twice. Override to <c>true</c> for
    /// extensions that are genuinely multi-instance (e.g. a toolbar that can be placed
    /// both at the top and down the left side).
    /// </summary>
    protected internal virtual bool AllowMultiple => false;

    /// <summary>
    /// Per-instance configuration handed to the JS module's factory. The shape is
    /// entirely the extension's own; it travels as opaque JSON. Returning a
    /// pre-serialized <see cref="JsonElement"/> (e.g. from a source-generated context)
    /// keeps the payload reflection-free; any other object is serialized with the
    /// default serializer. <c>null</c> (the default) sends no options.
    /// </summary>
    protected virtual object? GetOptions() => null;

    /// <summary>
    /// Whether this extension accepts JS→.NET calls. Defaults to "the concrete type
    /// overrides <see cref="OnInvokeAsync"/>", so interop is opt-in by construction.
    /// Override to narrow it further — e.g. <c>=&gt; OnSomething.HasDelegate</c> — so an
    /// instance whose consumer wired no callback still does zero interop.
    /// </summary>
    // Binding the virtual method to a delegate resolves it through the vtable, so the
    // delegate's Method is whichever override actually runs — the same question the
    // reflection form asked, but without a name lookup that a trimmer can turn into
    // null (which would fail *open*, reporting an invoke handler that isn't there).
    protected virtual bool HasInvokeHandler =>
        ((Func<string, string, Task<string?>>)OnInvokeAsync).Method.DeclaringType
            != typeof(LexicalExtension);

    /// <summary>
    /// Handles a call from this extension's JS half. <paramref name="argsJson"/> is the
    /// JSON array of arguments the JS side passed; the return value is JSON handed back
    /// to it (<c>null</c> for no result). Overriding this method is what opts the
    /// extension into JS→.NET interop — see <see cref="HasInvokeHandler"/>.
    /// </summary>
    /// <param name="method">The method name chosen by the extension's JS half.</param>
    /// <param name="argsJson">The arguments, as a JSON array string.</param>
    protected virtual Task<string?> OnInvokeAsync(string method, string argsJson) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Calls this extension's JS half (its module's <c>invoke</c> handler) — the .NET→JS
    /// direction. Returns the handler's result as JSON, or <c>null</c> when the editor is
    /// not created yet, the extension declares no <see cref="ModuleUrl"/>, or the module
    /// exposes no <c>invoke</c> handler.
    /// </summary>
    /// <param name="method">The method name the extension's JS half understands.</param>
    /// <param name="argsJson">The arguments, as a JSON array string.</param>
    protected Task<string?> InvokeJsAsync(string method, string argsJson = "[]") =>
        Editor?.InvokeExtensionJsAsync(ExtensionId, method, argsJson)
        ?? Task.FromResult<string?>(null);

    /// <summary>
    /// Called once, after the editor has been created and every extension module loaded
    /// — the first moment <see cref="InvokeJsAsync"/> reaches a live JS half, and the
    /// natural place for a C#-only extension to do its start-up work. Extensions are
    /// called in registration order and each call is isolated: one that throws is logged
    /// and skipped rather than taking the editor down.
    /// </summary>
    protected virtual Task OnEditorReadyAsync() => Task.CompletedTask;

    /// <summary>Runs the ready hook. Not part of the public API.</summary>
    internal Task NotifyEditorReadyAsync() => OnEditorReadyAsync();

    /// <inheritdoc />
    protected override void OnInitialized() => Editor?.RegisterExtension(this);

    /// <summary>Unregisters this extension from its editor.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources held by this extension. The base implementation unregisters it
    /// from the cascaded editor; always call it from an override.
    /// </summary>
    /// <param name="disposing">
    /// <c>true</c> when called from <see cref="Dispose()"/> (managed state may be touched).
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Editor?.UnregisterExtension(this);
        }
    }

    /// <summary>Routes a JS→.NET call to <see cref="OnInvokeAsync"/>, honouring the opt-in gate.</summary>
    internal Task<string?> DispatchInvokeAsync(string method, string argsJson) =>
        HasInvokeHandler ? OnInvokeAsync(method, argsJson) : Task.FromResult<string?>(null);

    /// <summary>Builds the interop wire model for this extension.</summary>
    internal ExtensionDescriptorDto ToDto() => new()
    {
        Id = ExtensionId,
        BuiltIn = BuiltIn,
        ModuleUrl = ModuleUrl,
        Options = ToOptionsElement(GetOptions()),
        HasInvokeHandler = HasInvokeHandler,
    };

    /// <summary>
    /// Normalizes the extension-owned options object into a <see cref="JsonElement"/>.
    /// A <see cref="JsonElement"/> travels as-is (reflection-free); any other shape is
    /// serialized with the default serializer, since its runtime type is arbitrary.
    /// </summary>
    private static JsonElement? ToOptionsElement(object? options) => options switch
    {
        null => null,
        JsonElement element => element,
        _ => JsonSerializer.SerializeToElement(options),
    };
}

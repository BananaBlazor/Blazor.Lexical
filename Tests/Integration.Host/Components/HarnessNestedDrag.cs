using Blazor.Lexical;

namespace Tests.Integration.Host.Components;

/// <summary>
/// Harness-only extension whose JS half installs a <c>ctx.blockDrag</c> policy that makes
/// list items (<c>listitem</c>) individually draggable and yields both in-list and
/// top-level drop gaps. It proves the nested-block drag seam end to end without shipping a
/// public sample. No interop, no nodes — just the policy.
/// </summary>
public sealed class HarnessNestedDrag : LexicalExtension
{
    /// <inheritdoc />
    protected override string? ModuleUrl => "./harness-nested-drag.mjs";
}

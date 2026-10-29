using Blazor.Lexical;
using Microsoft.AspNetCore.Components;

namespace Tests.Integration.Host.Components;

/// <summary>
/// Harness-only extension whose JS half registers a block <c>DecoratorNode</c> made atomic
/// through <c>setup.primitives.atomicBlock</c>. <see cref="Variant"/> <c>indentable</c>
/// opts the node into indenting (<c>IndentableBlockNode</c>); <c>plain</c> does not.
/// </summary>
public sealed class HarnessBlockDecorator : LexicalExtension
{
    /// <summary><c>indentable</c> (node type <c>harness-block</c>) or <c>plain</c> (<c>harness-block-plain</c>).</summary>
    [Parameter] public string Variant { get; set; } = "indentable";

    // One instance per variant, so an editor can hold both nodes.
    protected override bool AllowMultiple => true;

    protected override string? ModuleUrl => "./harness-block-decorator.mjs";

    protected override object? GetOptions() => new { variant = Variant };
}

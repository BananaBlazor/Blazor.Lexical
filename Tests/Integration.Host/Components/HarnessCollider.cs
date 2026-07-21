using Blazor.Lexical;
using Microsoft.AspNetCore.Components;

namespace Tests.Integration.Host.Components;

/// <summary>
/// Harness-only extension whose JS half deliberately collides with a sibling — by name,
/// by node type, or by a declared conflict — so the integration tests can prove the host
/// skips the offender and keeps the editor alive.
/// </summary>
public sealed class HarnessCollider : LexicalExtension
{
    /// <summary>Which colliding personality the JS module should adopt.</summary>
    [Parameter] public string Variant { get; set; } = "name-a";

    // The whole point is putting two of these in one editor.
    protected override bool AllowMultiple => true;

    protected override string? ModuleUrl => "./harness-collider.mjs";

    protected override object? GetOptions() => new { variant = Variant };
}

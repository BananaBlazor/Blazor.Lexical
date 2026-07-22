namespace Samples.Extensions.ReferenceFields;

/// <summary>
/// One entity a <see cref="ReferenceFieldsExtension"/> field can resolve to — the shape
/// handed to the JS half as its candidate pool (see <c>GetOptions</c>). A stable
/// <paramref name="Id"/> and the <paramref name="Text"/> the query is matched against.
/// </summary>
/// <param name="Id">Stable identifier the field commits to.</param>
/// <param name="Text">Display and match text.</param>
public readonly record struct ReferenceCandidate(string Id, string Text);

/// <summary>
/// A commit surfaced by <see cref="ReferenceFieldsExtension.OnEntityCommitted"/>. Carries
/// the entity the field resolved to and the two lifecycle flags from the primitive:
/// <paramref name="Created"/> (the entity was minted by the create-if-missing path) and
/// <paramref name="Provisional"/> (this is the optimistic placeholder fired before the
/// real create resolved — a later <see cref="ReferenceFieldsExtension.OnEntityResolved"/>
/// swaps in the real id).
/// </summary>
/// <param name="Id">The committed entity's id (a <c>provisional:*</c> id while provisional).</param>
/// <param name="Text">The committed entity's text.</param>
/// <param name="Created">Whether the entity was newly created.</param>
/// <param name="Provisional">Whether this is the optimistic, not-yet-resolved placeholder.</param>
public readonly record struct ReferenceCommit(string Id, string Text, bool Created, bool Provisional);

/// <summary>
/// The resolution of an optimistic create, surfaced by
/// <see cref="ReferenceFieldsExtension.OnEntityResolved"/>: the provisional id first
/// reported by <see cref="ReferenceFieldsExtension.OnEntityCommitted"/> and the real id to
/// swap in for it.
/// </summary>
/// <param name="ProvisionalId">The provisional id previously committed.</param>
/// <param name="RealId">The real id the backing store assigned.</param>
public readonly record struct ReferenceResolved(string ProvisionalId, string RealId);

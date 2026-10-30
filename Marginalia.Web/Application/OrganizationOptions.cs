namespace Marginalia.Web.Application;

/// <summary>
/// Bounded organization names available for filtering library articles.
/// </summary>
public sealed record OrganizationOptions(
    IReadOnlyList<CollectionOption> Collections,
    IReadOnlyList<string> Tags);

/// <summary>
/// A collection identifier and its display name.
/// </summary>
public sealed record CollectionOption(Guid Id, string Name);

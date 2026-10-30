namespace Marginalia.Web.Application;

/// <summary>
/// Metadata extracted from an untrusted public HTML response.
/// </summary>
public sealed record PageMetadata(string? Title, string? Description);

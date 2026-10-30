using System.Net;

namespace Marginalia.Web.Application;

/// <summary>
/// Holds bounded response metadata and bytes fetched from a public source.
/// </summary>
public sealed record SafeHttpFetchResponse(
    HttpStatusCode StatusCode,
    Uri FinalUri,
    string? ContentType,
    byte[] Body,
    string? EntityTag,
    DateTimeOffset? LastModified);

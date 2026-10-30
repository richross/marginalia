namespace Marginalia.Web.Application;

/// <summary>
/// Returns manual capture status along with any safely extracted page metadata.
/// </summary>
public sealed record ManualUrlCaptureResult(
    ManualCaptureResult Capture,
    PageMetadata? Metadata,
    SafeHttpFetchFailure? MetadataFailure);

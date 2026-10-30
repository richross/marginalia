namespace Marginalia.Web.Application;

/// <summary>
/// Coordinates a pasted URL, safe public metadata retrieval, and direct Saved capture.
/// </summary>
public sealed class ManualUrlCaptureService(
    LibraryCommandService libraryCommandService,
    ISafePublicHttpFetcher httpFetcher)
{
    /// <summary>
    /// Captures a pasted URL directly as unread Saved after using available metadata when needed.
    /// </summary>
    /// <param name="url">The owner-supplied original URL.</param>
    /// <param name="title">An optional owner-entered title.</param>
    /// <param name="description">An optional owner-entered description.</param>
    /// <param name="captureRequestId">A stable idempotency key for this capture action.</param>
    /// <param name="confirmPurgedIdentity">Whether the owner confirmed fresh capture after a purged match.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The capture result and any safe metadata retrieval outcome.</returns>
    public async Task<ManualUrlCaptureResult> CaptureAsync(
        string url,
        string? title,
        string? description,
        Guid captureRequestId,
        bool confirmPurgedIdentity = false,
        CancellationToken cancellationToken = default)
    {
        var initial = await libraryCommandService.CaptureAsync(
            url,
            title,
            description,
            captureRequestId,
            confirmPurgedIdentity,
            cancellationToken);
        if (initial.Outcome != ManualCaptureOutcome.TitleRequired)
        {
            return new ManualUrlCaptureResult(initial, null, null);
        }

        SafeHttpFetchResponse response;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target))
            {
                throw new SafeHttpFetchException(SafeHttpFetchFailure.UnsafeTarget);
            }

            response = await httpFetcher.FetchAsync(target, cancellationToken: cancellationToken);
        }
        catch (SafeHttpFetchException exception)
        {
            return new ManualUrlCaptureResult(initial, null, exception.Failure);
        }

        var metadataIsHtml = string.Equals(response.ContentType, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response.ContentType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
        PageMetadata metadata;
        SafeHttpFetchFailure? metadataFailure = SafeHttpFetchFailure.UnsupportedContentType;
        if (metadataIsHtml)
        {
            try
            {
                metadata = HtmlMetadataParser.Extract(System.Text.Encoding.UTF8.GetString(response.Body));
                metadataFailure = metadata.Title is null
                    ? SafeHttpFetchFailure.MetadataUnavailable
                    : null;
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                metadata = new PageMetadata(null, null);
                metadataFailure = SafeHttpFetchFailure.MetadataUnavailable;
            }
        }
        else
        {
            metadata = new PageMetadata(null, null);
            if (response.ContentType?.ToLowerInvariant() is
                "application/rss+xml" or "application/atom+xml" or "application/xml" or "text/xml")
            {
                try
                {
                    var feed = RssFeedParser.Parse(response.Body, response.FinalUri);
                    metadata = new PageMetadata(feed.Title, feed.Description);
                    metadataFailure = metadata.Title is null
                        ? SafeHttpFetchFailure.MetadataUnavailable
                        : null;
                }
                catch (FeedParseException)
                {
                    metadataFailure = SafeHttpFetchFailure.MetadataUnavailable;
                }
            }
        }

        var result = await libraryCommandService.CaptureAsync(
            url,
            string.IsNullOrWhiteSpace(title) ? metadata.Title : title,
            string.IsNullOrWhiteSpace(description) ? metadata.Description : description,
            captureRequestId,
            confirmPurgedIdentity,
            cancellationToken);

        return new ManualUrlCaptureResult(
            result,
            metadata,
            metadataFailure);
    }
}

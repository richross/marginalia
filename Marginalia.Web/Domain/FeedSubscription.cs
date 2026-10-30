namespace Marginalia.Web.Domain;

/// <summary>
/// Stores a feed endpoint and the persisted state needed for safe scheduled refresh.
/// </summary>
public sealed class FeedSubscription
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(24)
    ];

    private FeedSubscription()
    {
    }

    /// <summary>
    /// Gets the feed subscription identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the normalized feed URL.
    /// </summary>
    public string FeedUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional title returned by the feed.
    /// </summary>
    public string? FeedTitle { get; private set; }

    /// <summary>
    /// Gets whether automatic and manual refresh are enabled.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Gets the most recent ETag validator.
    /// </summary>
    public string? EntityTag { get; private set; }

    /// <summary>
    /// Gets the most recent Last-Modified validator.
    /// </summary>
    public DateTimeOffset? LastModifiedUtc { get; private set; }

    /// <summary>
    /// Gets the most recent refresh attempt time.
    /// </summary>
    public DateTimeOffset? LastAttemptUtc { get; private set; }

    /// <summary>
    /// Gets the most recent successful refresh time.
    /// </summary>
    public DateTimeOffset? LastSuccessUtc { get; private set; }

    /// <summary>
    /// Gets when the first successful feed snapshot was recorded.
    /// </summary>
    public DateTimeOffset? InitialSnapshotUtc { get; private set; }

    /// <summary>
    /// Gets the oldest publication time included in the initial snapshot, when available.
    /// </summary>
    public DateTimeOffset? InitialCutoffUtc { get; private set; }

    /// <summary>
    /// Gets the feed-order boundary used to avoid importing older undated entries.
    /// </summary>
    public string? InitialBoundaryOccurrenceKey { get; private set; }

    /// <summary>
    /// Gets the next persisted refresh time.
    /// </summary>
    public DateTimeOffset? NextDueUtc { get; private set; }

    /// <summary>
    /// Gets a safe, user-visible health message without response data or the feed URL.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Gets the optimistic concurrency revision.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Gets the consecutive refresh failure count.
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// Gets when the subscription was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>
    /// Gets when the subscription was last updated.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; private set; }

    /// <summary>
    /// Creates an enabled subscription due for its initial refresh.
    /// </summary>
    /// <param name="feedUrl">The HTTP(S) feed URL.</param>
    /// <param name="now">The current time.</param>
    /// <returns>A new enabled subscription.</returns>
    public static FeedSubscription Create(string feedUrl, DateTimeOffset now)
    {
        var normalizedUrl = UrlIdentity.Normalize(feedUrl);
        var uri = new Uri(normalizedUrl);
        if (normalizedUrl.Length > 2_048
            || (uri.Scheme == Uri.UriSchemeHttp && uri.Port != 80)
            || (uri.Scheme == Uri.UriSchemeHttps && uri.Port != 443))
        {
            throw new ArgumentException("The feed URL is not an allowed HTTP(S) endpoint.", nameof(feedUrl));
        }

        return new FeedSubscription
        {
            Id = Guid.NewGuid(),
            FeedUrl = normalizedUrl,
            IsEnabled = true,
            NextDueUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    /// <summary>
    /// Records that a refresh attempt has begun.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void MarkAttempt(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("A disabled feed cannot be refreshed.");
        }

        LastAttemptUtc = now;
        Touch(now);
    }

    /// <summary>
    /// Records a successful refresh and schedules the next daily poll.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="feedTitle">The optional title returned by the feed.</param>
    /// <param name="entityTag">The response ETag, if supplied.</param>
    /// <param name="lastModifiedUtc">The response Last-Modified validator, if supplied.</param>
    /// <param name="notModified">Whether the response was HTTP 304.</param>
    /// <param name="initialSnapshotUtc">The time of the first successful snapshot, if being recorded.</param>
    /// <param name="initialCutoffUtc">The earliest published time included in the first snapshot.</param>
    /// <param name="initialBoundaryOccurrenceKey">A stable feed-order boundary for undated items.</param>
    public void MarkSuccess(
        DateTimeOffset now,
        string? feedTitle,
        string? entityTag,
        DateTimeOffset? lastModifiedUtc,
        bool notModified,
        DateTimeOffset? initialSnapshotUtc = null,
        DateTimeOffset? initialCutoffUtc = null,
        string? initialBoundaryOccurrenceKey = null)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("A disabled feed cannot be refreshed.");
        }

        if (notModified)
        {
            EntityTag = entityTag ?? EntityTag;
            LastModifiedUtc = lastModifiedUtc ?? LastModifiedUtc;
        }
        else
        {
            FeedTitle = feedTitle;
            EntityTag = entityTag;
            LastModifiedUtc = lastModifiedUtc;
            if (!InitialSnapshotUtc.HasValue)
            {
                InitialSnapshotUtc = initialSnapshotUtc ?? now;
                InitialCutoffUtc = initialCutoffUtc;
                InitialBoundaryOccurrenceKey = initialBoundaryOccurrenceKey;
            }
            else if (!InitialCutoffUtc.HasValue
                     && InitialBoundaryOccurrenceKey is null
                     && (initialCutoffUtc.HasValue || initialBoundaryOccurrenceKey is not null))
            {
                InitialCutoffUtc = initialCutoffUtc;
                InitialBoundaryOccurrenceKey = initialBoundaryOccurrenceKey;
            }
        }

        LastSuccessUtc = now;
        NextDueUtc = now.AddDays(1);
        LastError = null;
        ConsecutiveFailures = 0;
        Touch(now);
    }

    /// <summary>
    /// Records a safe refresh failure and schedules a bounded retry delay.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="safeError">A fixed, safe message without URL or response content.</param>
    public void MarkFailure(DateTimeOffset now, string safeError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeError);
        if (!IsEnabled)
        {
            return;
        }

        ConsecutiveFailures = Math.Min(ConsecutiveFailures + 1, RetryDelays.Length);
        LastError = safeError[..Math.Min(safeError.Length, 256)];
        NextDueUtc = now.Add(RetryDelays[ConsecutiveFailures - 1]);
        Touch(now);
    }

    /// <summary>
    /// Disables a feed without deleting its subscription identity or occurrence history.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void Disable(DateTimeOffset now)
    {
        IsEnabled = false;
        NextDueUtc = null;
        Touch(now);
    }

    /// <summary>
    /// Re-enables a feed and makes it eligible for an immediate refresh.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        NextDueUtc = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedUtc = now;
        Revision++;
    }
}

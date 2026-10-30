namespace Marginalia.Web.Application;

/// <summary>
/// Presents feed configuration and refresh health without response bodies.
/// </summary>
public sealed record FeedSubscriptionSummary(
    Guid Id,
    string FeedUrl,
    string? FeedTitle,
    bool IsEnabled,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    DateTimeOffset? NextDueUtc,
    string? LastError,
    int ConsecutiveFailures);

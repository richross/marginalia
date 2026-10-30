namespace Marginalia.Web.Application;

/// <summary>
/// Describes the next explicit decision required for a manual URL capture.
/// </summary>
public enum ManualCaptureOutcome
{
    /// <summary>
    /// A new article was saved.
    /// </summary>
    Created,

    /// <summary>
    /// The URL already belongs to an active Inbox or Saved article.
    /// </summary>
    AlreadyPresent,

    /// <summary>
    /// Retained dismissed or trashed article details are available for a recovery decision.
    /// </summary>
    RecoveryDecisionRequired,

    /// <summary>
    /// The prior metadata has been purged and explicit confirmation is required.
    /// </summary>
    PurgedIdentityConfirmationRequired,

    /// <summary>
    /// A title must be supplied before an article can be saved.
    /// </summary>
    TitleRequired
}

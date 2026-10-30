namespace Marginalia.Web.Domain;

/// <summary>
/// Describes an article's relationship to the active library.
/// </summary>
public enum ArticleState
{
    /// <summary>
    /// An article delivered by a feed that awaits individual review.
    /// </summary>
    Inbox,

    /// <summary>
    /// An article the owner has chosen to retain.
    /// </summary>
    Saved,

    /// <summary>
    /// An Inbox article the owner chose not to save.
    /// </summary>
    Dismissed,

    /// <summary>
    /// A saved article removed from the active library but recoverable for a limited time.
    /// </summary>
    Trashed
}

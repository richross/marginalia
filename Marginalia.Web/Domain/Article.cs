namespace Marginalia.Web.Domain;

/// <summary>
/// A metadata-only reference to one original web article.
/// </summary>
public sealed class Article
{
    private Article()
    {
    }

    /// <summary>
    /// Gets the article identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the canonical identity used to prevent duplicate articles.
    /// </summary>
    public string IdentityKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 identity fingerprint retained after article purge.
    /// </summary>
    public string IdentityFingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the URL the owner or feed originally supplied.
    /// </summary>
    public string OriginalUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the article title, which may be absent while an Inbox item awaits review.
    /// </summary>
    public string? Title { get; private set; }

    /// <summary>
    /// Gets the optional article description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the owner's optional personal notes.
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Gets the current review and retention state.
    /// </summary>
    public ArticleState State { get; private set; }

    /// <summary>
    /// Gets whether the saved article has been explicitly marked read.
    /// </summary>
    public bool IsRead { get; private set; }

    /// <summary>
    /// Gets whether the saved article is a favorite.
    /// </summary>
    public bool IsFavorite { get; private set; }

    /// <summary>
    /// Gets the creation time.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the publication time reported by a source, when available.
    /// </summary>
    public DateTimeOffset? PublishedUtc { get; private set; }

    /// <summary>
    /// Gets the last user-visible metadata change time.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; private set; }

    /// <summary>
    /// Gets when the article was dismissed from the Inbox, if applicable.
    /// </summary>
    public DateTimeOffset? DismissedUtc { get; private set; }

    /// <summary>
    /// Gets when the article was placed in Trash, if applicable.
    /// </summary>
    public DateTimeOffset? TrashedUtc { get; private set; }

    /// <summary>
    /// Gets the deadline after which recoverable metadata may be purged.
    /// </summary>
    public DateTimeOffset? PurgeAfterUtc { get; private set; }

    /// <summary>
    /// Gets the read state to restore when a trashed article is recovered.
    /// </summary>
    public bool? PreTrashIsRead { get; private set; }

    /// <summary>
    /// Gets the favorite state to restore when a trashed article is recovered.
    /// </summary>
    public bool? PreTrashIsFavorite { get; private set; }

    /// <summary>
    /// Gets the collection memberships.
    /// </summary>
    public ICollection<ArticleCollection> Collections { get; private set; } = new List<ArticleCollection>();

    /// <summary>
    /// Gets the tag memberships.
    /// </summary>
    public ICollection<ArticleTag> Tags { get; private set; } = new List<ArticleTag>();

    /// <summary>
    /// Gets the retained source occurrences.
    /// </summary>
    public ICollection<SourceOccurrence> SourceOccurrences { get; private set; } = new List<SourceOccurrence>();

    /// <summary>
    /// Creates an article discovered through a feed for individual review.
    /// </summary>
    /// <param name="identityKey">The conservative canonical identity.</param>
    /// <param name="identityFingerprint">The hash used for retention tombstones.</param>
    /// <param name="originalUrl">The originally supplied URL.</param>
    /// <param name="title">The optional feed title.</param>
    /// <param name="description">The optional feed description.</param>
    /// <param name="now">The current time.</param>
    /// <returns>A new Inbox article.</returns>
    public static Article CreateInbox(
        string identityKey,
        string identityFingerprint,
        string originalUrl,
        string? title,
        string? description,
        DateTimeOffset now,
        DateTimeOffset? publishedUtc = null) =>
        Create(identityKey, identityFingerprint, originalUrl, title, description, null, ArticleState.Inbox, now, publishedUtc);

    /// <summary>
    /// Creates an article saved directly by an explicit manual capture.
    /// </summary>
    /// <param name="identityKey">The conservative canonical identity.</param>
    /// <param name="identityFingerprint">The hash used for retention tombstones.</param>
    /// <param name="originalUrl">The originally supplied URL.</param>
    /// <param name="title">The required article title.</param>
    /// <param name="description">The optional article description.</param>
    /// <param name="now">The current time.</param>
    /// <returns>A new unread Saved article.</returns>
    public static Article CreateSaved(
        string identityKey,
        string identityFingerprint,
        string originalUrl,
        string title,
        string? description,
        DateTimeOffset now) =>
        Create(identityKey, identityFingerprint, originalUrl, RequireTitle(title), description, null, ArticleState.Saved, now);

    /// <summary>
    /// Creates a Saved article from a validated library import.
    /// </summary>
    public static Article CreateImportedSaved(
        string identityKey,
        string identityFingerprint,
        string originalUrl,
        string title,
        string? description,
        string? notes,
        bool isRead,
        bool isFavorite,
        DateTimeOffset createdUtc,
        DateTimeOffset? publishedUtc,
        DateTimeOffset importedUtc)
    {
        var article = Create(
            identityKey,
            identityFingerprint,
            originalUrl,
            RequireTitle(title),
            description,
            notes,
            ArticleState.Saved,
            createdUtc,
            publishedUtc);
        article.IsRead = isRead;
        article.IsFavorite = isFavorite;
        article.UpdatedUtc = importedUtc;
        return article;
    }

    /// <summary>
    /// Saves an Inbox article after an individual keep decision.
    /// </summary>
    /// <param name="title">The required title, including a user-entered fallback when needed.</param>
    /// <param name="now">The current time.</param>
    public void SaveFromInbox(string title, DateTimeOffset now)
    {
        EnsureState(ArticleState.Inbox);
        Title = RequireTitle(title);
        State = ArticleState.Saved;
        IsRead = false;
        IsFavorite = false;
        UpdatedUtc = now;
    }

    /// <summary>
    /// Dismisses an Inbox article for recoverable retention.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void Dismiss(DateTimeOffset now)
    {
        EnsureState(ArticleState.Inbox);
        State = ArticleState.Dismissed;
        DismissedUtc = now;
        PurgeAfterUtc = now.AddDays(30);
        UpdatedUtc = now;
    }

    /// <summary>
    /// Restores a dismissed article to the Inbox before its purge deadline.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void RestoreDismissedToInbox(DateTimeOffset now)
    {
        EnsureRecoverable(ArticleState.Dismissed, now);
        State = ArticleState.Inbox;
        DismissedUtc = null;
        PurgeAfterUtc = null;
        UpdatedUtc = now;
    }

    /// <summary>
    /// Moves a saved article to recoverable Trash while preserving its prior read/favorite state.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void MoveToTrash(DateTimeOffset now)
    {
        EnsureState(ArticleState.Saved);
        PreTrashIsRead = IsRead;
        PreTrashIsFavorite = IsFavorite;
        State = ArticleState.Trashed;
        TrashedUtc = now;
        PurgeAfterUtc = now.AddDays(30);
        UpdatedUtc = now;
    }

    /// <summary>
    /// Restores a trashed article to its prior saved read/favorite state.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void RestoreFromTrash(DateTimeOffset now)
    {
        EnsureRecoverable(ArticleState.Trashed, now);
        State = ArticleState.Saved;
        IsRead = PreTrashIsRead ?? false;
        IsFavorite = PreTrashIsFavorite ?? false;
        TrashedUtc = null;
        PurgeAfterUtc = null;
        PreTrashIsRead = null;
        PreTrashIsFavorite = null;
        UpdatedUtc = now;
    }

    /// <summary>
    /// Restores a retained dismissed or trashed match as an unread Saved article after explicit user choice.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void RestoreDuplicateAsUnreadSaved(DateTimeOffset now)
    {
        if (State == ArticleState.Dismissed)
        {
            EnsureRecoverable(ArticleState.Dismissed, now);
            DismissedUtc = null;
        }
        else if (State == ArticleState.Trashed)
        {
            EnsureRecoverable(ArticleState.Trashed, now);
            TrashedUtc = null;
        }
        else
        {
            throw new InvalidOperationException("Only a dismissed or trashed duplicate can be restored by manual capture.");
        }

        State = ArticleState.Saved;
        IsRead = false;
        PurgeAfterUtc = null;
        PreTrashIsRead = null;
        PreTrashIsFavorite = null;
        UpdatedUtc = now;
    }

    /// <summary>
    /// Marks a saved article read or unread explicitly.
    /// </summary>
    /// <param name="isRead">The new read state.</param>
    /// <param name="now">The current time.</param>
    public void SetRead(bool isRead, DateTimeOffset now)
    {
        EnsureState(ArticleState.Saved);
        IsRead = isRead;
        UpdatedUtc = now;
    }

    /// <summary>
    /// Marks a saved article as a favorite or removes favorite status.
    /// </summary>
    /// <param name="isFavorite">The new favorite state.</param>
    /// <param name="now">The current time.</param>
    public void SetFavorite(bool isFavorite, DateTimeOffset now)
    {
        EnsureState(ArticleState.Saved);
        IsFavorite = isFavorite;
        UpdatedUtc = now;
    }

    /// <summary>
    /// Replaces user-controlled article metadata without changing state or organization.
    /// </summary>
    /// <param name="title">The required title.</param>
    /// <param name="description">The optional description.</param>
    /// <param name="notes">The optional personal notes.</param>
    /// <param name="now">The current time.</param>
    public void UpdateMetadata(string title, string? description, string? notes, DateTimeOffset now)
    {
        if (State is ArticleState.Trashed)
        {
            throw new InvalidOperationException("Trashed article metadata cannot be edited.");
        }

        Title = RequireTitle(title);
        Description = NormalizeOptionalText(description);
        Notes = NormalizeOptionalText(notes);
        UpdatedUtc = now;
    }

    /// <summary>
    /// Creates a tombstone containing only the retained identity fingerprint after retention expires.
    /// </summary>
    /// <param name="purgedUtc">The purge time.</param>
    /// <returns>A minimal identity tombstone.</returns>
    public IdentityTombstone CreateTombstone(DateTimeOffset purgedUtc) =>
        IdentityTombstone.Create(IdentityFingerprint, purgedUtc);

    private static Article Create(
        string identityKey,
        string identityFingerprint,
        string originalUrl,
        string? title,
        string? description,
        string? notes,
        ArticleState state,
        DateTimeOffset now,
        DateTimeOffset? publishedUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalUrl);

        return new Article
        {
            Id = Guid.NewGuid(),
            IdentityKey = identityKey,
            IdentityFingerprint = identityFingerprint,
            OriginalUrl = originalUrl,
            Title = NormalizeOptionalText(title),
            Description = NormalizeOptionalText(description),
            Notes = NormalizeOptionalText(notes),
            State = state,
            CreatedUtc = now,
            PublishedUtc = publishedUtc,
            UpdatedUtc = now
        };
    }

    private static string RequireTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return title.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void EnsureRecoverable(ArticleState requiredState, DateTimeOffset now)
    {
        EnsureState(requiredState);
        if (PurgeAfterUtc is null || now >= PurgeAfterUtc.Value)
        {
            throw new InvalidOperationException("The article's recovery period has expired.");
        }
    }

    private void EnsureState(ArticleState requiredState)
    {
        if (State != requiredState)
        {
            throw new InvalidOperationException($"Article state must be {requiredState} for this operation.");
        }
    }
}

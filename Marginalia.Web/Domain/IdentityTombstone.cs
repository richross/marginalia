namespace Marginalia.Web.Domain;

/// <summary>
/// Retains only a URL identity fingerprint after article metadata and provenance are purged.
/// </summary>
public sealed class IdentityTombstone
{
    private IdentityTombstone()
    {
    }

    /// <summary>
    /// Gets the SHA-256 identity fingerprint.
    /// </summary>
    public string IdentityFingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// Gets when the source metadata was purged.
    /// </summary>
    public DateTimeOffset PurgedUtc { get; private set; }

    /// <summary>
    /// Creates a minimal identity tombstone.
    /// </summary>
    /// <param name="identityFingerprint">The hashed canonical identity.</param>
    /// <param name="purgedUtc">The purge time.</param>
    /// <returns>A tombstone containing no URL, title, notes, or provenance.</returns>
    public static IdentityTombstone Create(string identityFingerprint, DateTimeOffset purgedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityFingerprint);
        return new IdentityTombstone
        {
            IdentityFingerprint = identityFingerprint,
            PurgedUtc = purgedUtc
        };
    }
}

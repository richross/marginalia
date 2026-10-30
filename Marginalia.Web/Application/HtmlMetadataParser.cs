using System.Net;
using System.Text.RegularExpressions;

namespace Marginalia.Web.Application;

/// <summary>
/// Extracts bounded title and description fields without retaining page content.
/// </summary>
public static partial class HtmlMetadataParser
{
    private const int MaximumTitleLength = 500;
    private const int MaximumDescriptionLength = 4_000;

    /// <summary>
    /// Extracts conventional metadata from a bounded HTML document.
    /// </summary>
    /// <param name="html">The untrusted HTML document.</param>
    /// <returns>Optional title and description values.</returns>
    public static PageMetadata Extract(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTagRegex().Matches(html))
        {
            var attributes = ParseAttributes(tag.Value);
            var key = attributes.TryGetValue("property", out var property)
                ? property
                : attributes.GetValueOrDefault("name");
            var value = attributes.GetValueOrDefault("content");
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                metadata.TryAdd(key.Trim(), value);
            }
        }

        var title = FirstNonEmpty(
            metadata.GetValueOrDefault("og:title"),
            metadata.GetValueOrDefault("twitter:title"),
            ExtractTitleElement(html));
        var description = FirstNonEmpty(
            metadata.GetValueOrDefault("description"),
            metadata.GetValueOrDefault("og:description"),
            metadata.GetValueOrDefault("twitter:description"));

        return new PageMetadata(
            CleanPlainText(title, MaximumTitleLength),
            CleanPlainText(description, MaximumDescriptionLength));
    }

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(tag))
        {
            var value = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["unquoted"].Value;
            attributes.TryAdd(match.Groups["name"].Value, value);
        }

        return attributes;
    }

    private static string? ExtractTitleElement(string html)
    {
        var match = TitleRegex().Match(html);
        return match.Success ? match.Groups["title"].Value : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Decodes and bounds untrusted text while removing markup.
    /// </summary>
    /// <param name="value">The untrusted text.</param>
    /// <param name="maximumLength">The maximum resulting text length.</param>
    /// <returns>Normalized plain text or <see langword="null"/>.</returns>
    public static string? CleanPlainText(string? value, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var withoutTags = TagRegex().Replace(decoded, " ");
        var normalized = WhitespaceRegex().Replace(withoutTags, " ").Trim();
        return normalized.Length == 0 ? null : normalized[..Math.Min(normalized.Length, maximumLength)];
    }

    [GeneratedRegex("<meta\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(
        "(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<unquoted>[^\\s>]+))",
        RegexOptions.CultureInvariant,
        100)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("<title\\b[^>]*>(?<title>.*?)</title\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<[^>]*>", RegexOptions.Singleline | RegexOptions.CultureInvariant, 100)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant, 100)]
    private static partial Regex WhitespaceRegex();
}

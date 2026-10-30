using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Marginalia.Web.Domain;

namespace Marginalia.Web.Application;

/// <summary>
/// Parses bounded RSS and Atom XML while prohibiting DTD and external entity access.
/// </summary>
public static class RssFeedParser
{
    private const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private const int MaximumItems = 10_000;

    /// <summary>
    /// Parses RSS 2.0, RSS 1.0, or Atom metadata without extracting article bodies.
    /// </summary>
    /// <param name="body">The bounded XML response bytes.</param>
    /// <param name="feedUri">The final validated feed URI used to resolve relative links.</param>
    /// <returns>The feed title, description, and valid item links.</returns>
    /// <exception cref="FeedParseException">The document is unsafe, malformed, or unsupported.</exception>
    public static FeedDocument Parse(ReadOnlyMemory<byte> body, Uri feedUri)
    {
        ArgumentNullException.ThrowIfNull(feedUri);
        if (body.Length > MaximumDocumentBytes)
        {
            throw new FeedParseException();
        }

        try
        {
            using var stream = new MemoryStream(body.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumDocumentBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || root.Name.LocalName is not ("rss" or "RDF" or "feed"))
            {
                throw new FeedParseException();
            }

            var isAtom = root.Name.LocalName == "feed";
            var itemElements = root.Descendants()
                .Where(element => element.Name.LocalName == (isAtom ? "entry" : "item"))
                .Take(MaximumItems + 1)
                .ToArray();
            if (itemElements.Length > MaximumItems)
            {
                throw new FeedParseException();
            }

            var items = new List<FeedItemMetadata>(itemElements.Length);
            foreach (var item in itemElements)
            {
                var linkText = isAtom ? GetAtomLink(item) : ElementValue(item, "link");
                if (!TryResolvePublicWebUrl(feedUri, linkText, out var itemUrl))
                {
                    continue;
                }

                var identity = UrlIdentity.Normalize(itemUrl);
                var sourceIdentity = FirstNonEmpty(ElementValue(item, "guid"), ElementValue(item, "id")) ?? identity;
                var occurrenceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdentity)))
                    .ToLowerInvariant();
                var published = FirstNonEmpty(
                    ElementValue(item, "pubDate"),
                    ElementValue(item, "published"),
                    ElementValue(item, "updated"),
                    ElementValue(item, "date"));
                DateTimeOffset? publishedUtc = DateTimeOffset.TryParse(
                    published,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate)
                    ? parsedDate
                    : null;

                items.Add(new FeedItemMetadata(
                    itemUrl,
                    HtmlMetadataParser.CleanPlainText(ElementValue(item, "title"), 500),
                    HtmlMetadataParser.CleanPlainText(
                        isAtom
                            ? ElementValue(item, "summary")
                            : ElementValue(item, "description"),
                        4_000),
                    publishedUtc,
                    occurrenceKey));
            }

            var titleContainer = isAtom
                ? root
                : root.Descendants().FirstOrDefault(element => element.Name.LocalName == "channel") ?? root;
            var feedTitle = HtmlMetadataParser.CleanPlainText(ElementValue(titleContainer, "title"), 500);
            var feedDescription = HtmlMetadataParser.CleanPlainText(
                ElementValue(titleContainer, isAtom ? "subtitle" : "description"),
                4_000);
            return new FeedDocument(feedTitle, items, feedDescription);
        }
        catch (XmlException)
        {
            throw new FeedParseException();
        }
        catch (InvalidOperationException)
        {
            throw new FeedParseException();
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            throw new FeedParseException();
        }
    }

    private static string? GetAtomLink(XElement item)
    {
        var link = item.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "link"
            && (element.Attribute("rel")?.Value is null
                || string.Equals(element.Attribute("rel")?.Value, "alternate", StringComparison.OrdinalIgnoreCase)));
        return link?.Attribute("href")?.Value;
    }

    private static string? ElementValue(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool TryResolvePublicWebUrl(Uri feedUri, string? link, out string absoluteUrl)
    {
        absoluteUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(link)
            || !Uri.TryCreate(feedUri, link.Trim(), out var resolved)
            || !resolved.IsAbsoluteUri
            || (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(resolved.UserInfo))
        {
            return false;
        }

        absoluteUrl = resolved.AbsoluteUri;
        return true;
    }
}

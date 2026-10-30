using System.Text;
using Marginalia.Web.Application;

namespace Marginalia.Tests;

[TestClass]
public sealed class RssFeedParserTests
{
    [TestMethod]
    public void ParsesRssMetadataAndSkipsArticleBody()
    {
        const string xml = """
            <rss version="2.0">
              <channel><title>Example &amp; Feed</title>
                <description>Feed &amp; description</description>
                <item>
                  <guid>entry-1</guid>
                  <title>First &amp; latest</title>
                  <link>/article/1?mode=full</link>
                  <description>Summary text</description>
                  <content:encoded xmlns:content="http://purl.org/rss/1.0/modules/content/"><![CDATA[Full article body]]></content:encoded>
                  <pubDate>Fri, 30 Oct 2026 12:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var document = RssFeedParser.Parse(
            Encoding.UTF8.GetBytes(xml),
            new Uri("https://feeds.example.test/feed.xml"));

        Assert.AreEqual("Example & Feed", document.Title);
        Assert.AreEqual("Feed & description", document.Description);
        Assert.HasCount(1, document.Items);
        Assert.AreEqual("https://feeds.example.test/article/1?mode=full", document.Items[0].Url);
        Assert.AreEqual("First & latest", document.Items[0].Title);
        Assert.AreEqual("Summary text", document.Items[0].Description);
        Assert.AreNotEqual("Full article body", document.Items[0].Description);
        Assert.AreEqual(64, document.Items[0].OccurrenceKey.Length);
    }

    [TestMethod]
    public void ParsesAtomAlternateLinksAndSummary()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom feed</title>
              <subtitle>Atom feed description</subtitle>
              <entry>
                <id>tag:example.test,2026:item-1</id>
                <title>Atom entry</title>
                <link rel="self" href="https://feeds.example.test/self.xml" />
                <link rel="alternate" href="https://example.test/article" />
                <summary>Short summary</summary>
                <content>Do not persist article body</content>
                <updated>2026-10-30T12:00:00Z</updated>
              </entry>
            </feed>
            """;

        var document = RssFeedParser.Parse(
            Encoding.UTF8.GetBytes(xml),
            new Uri("https://feeds.example.test/feed.xml"));

        Assert.AreEqual("Atom feed", document.Title);
        Assert.AreEqual("Atom feed description", document.Description);
        Assert.HasCount(1, document.Items);
        Assert.AreEqual("https://example.test/article", document.Items[0].Url);
        Assert.AreEqual("Short summary", document.Items[0].Description);
    }

    [TestMethod]
    public void RejectsMalformedXmlAndDtdEntities()
    {
        var malformed = Encoding.UTF8.GetBytes("<rss><channel>");
        var dtd = Encoding.UTF8.GetBytes(
            "<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///private.txt'>]><rss><channel><title>&x;</title></channel></rss>");
        var feedUri = new Uri("https://feeds.example.test/feed.xml");

        Assert.ThrowsExactly<FeedParseException>(() => RssFeedParser.Parse(malformed, feedUri));
        Assert.ThrowsExactly<FeedParseException>(() => RssFeedParser.Parse(dtd, feedUri));
    }
}

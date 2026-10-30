using Marginalia.Web.Domain;

namespace Marginalia.Tests;

[TestClass]
public sealed class FeedDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FeedRefreshPersistsValidatorsDailyScheduleAndBoundedRetryHealth()
    {
        var feed = FeedSubscription.Create(" HTTPS://Example.COM:443/feed.xml#section ", Now);
        Assert.AreEqual("https://example.com/feed.xml", feed.FeedUrl);
        Assert.AreEqual(Now, feed.NextDueUtc);

        feed.MarkAttempt(Now);
        feed.MarkSuccess(
            Now.AddMinutes(1),
            "Example feed",
            "\"v1\"",
            Now.AddHours(-1),
            notModified: false);

        Assert.AreEqual(Now.AddDays(1).AddMinutes(1), feed.NextDueUtc);
        Assert.AreEqual("\"v1\"", feed.EntityTag);
        Assert.AreEqual(Now.AddHours(-1), feed.LastModifiedUtc);
        Assert.IsNull(feed.LastError);

        feed.MarkFailure(Now.AddDays(1), "safe error");
        Assert.AreEqual(Now.AddDays(1).AddMinutes(1), feed.NextDueUtc);
        Assert.AreEqual(1, feed.ConsecutiveFailures);
        Assert.AreEqual("safe error", feed.LastError);

        for (var index = 0; index < 10; index++)
        {
            feed.MarkFailure(Now.AddDays(2 + index), "safe error");
        }

        Assert.AreEqual(6, feed.ConsecutiveFailures);
        Assert.AreEqual(Now.AddDays(12), feed.NextDueUtc);
    }

    [TestMethod]
    public void DisablingFeedRetainsIdentityAndEnablingMakesItDueImmediately()
    {
        var feed = FeedSubscription.Create("https://example.com/feed.xml", Now);
        feed.MarkSuccess(Now, "Feed", "\"v1\"", null, notModified: false);
        var revision = feed.Revision;

        feed.Disable(Now.AddHours(1));

        Assert.IsFalse(feed.IsEnabled);
        Assert.IsNull(feed.NextDueUtc);
        Assert.AreEqual("\"v1\"", feed.EntityTag);
        Assert.AreEqual(revision + 1, feed.Revision);

        feed.Enable(Now.AddHours(2));
        Assert.IsTrue(feed.IsEnabled);
        Assert.AreEqual(Now.AddHours(2), feed.NextDueUtc);
    }
}

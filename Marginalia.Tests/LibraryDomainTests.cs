using Marginalia.Web.Domain;

namespace Marginalia.Tests;

[TestClass]
public sealed class LibraryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void TagTrimsOuterWhitespaceAndPreservesFirstDisplaySpelling()
    {
        var tag = Tag.Create("  Data Science  ", Now);

        Assert.AreEqual("Data Science", tag.DisplayName);
        Assert.AreEqual("DATA SCIENCE", tag.NormalizedName);
        Assert.AreEqual(tag.NormalizedName, Tag.Normalize("data science"));
    }

    [TestMethod]
    public void TagRejectsAnEmptyName()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Tag.Create("  ", Now));
    }

    [TestMethod]
    public void UrlIdentityNormalizesOnlySchemeHostDefaultPortAndFragment()
    {
        var normalized = UrlIdentity.Normalize(
            " HTTPS://Example.COM:443/CaseSensitive?item=1&mode=full#section ");

        Assert.AreEqual("https://example.com/CaseSensitive?item=1&mode=full", normalized);
    }

    [TestMethod]
    public void UrlIdentityPreservesMeaningfulQueryAndPathDifferences()
    {
        var first = UrlIdentity.Normalize("https://example.com/a?x=1&y=2");
        var reordered = UrlIdentity.Normalize("https://example.com/a?y=2&x=1");
        var differentPathCase = UrlIdentity.Normalize("https://example.com/A?x=1&y=2");

        Assert.AreNotEqual(first, reordered);
        Assert.AreNotEqual(first, differentPathCase);
    }

    [TestMethod]
    public void UrlIdentityRejectsNonHttpSchemesAndEmbeddedCredentials()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UrlIdentity.Normalize("javascript:alert(1)"));
        Assert.ThrowsExactly<ArgumentException>(() => UrlIdentity.Normalize("https://user:pass@example.com/"));
    }

    [TestMethod]
    public void InboxArticleRequiresTitleBeforeSaving()
    {
        var article = CreateInbox();

        Assert.ThrowsExactly<ArgumentException>(() => article.SaveFromInbox("  ", Now));

        article.SaveFromInbox(" A title ", Now);

        Assert.AreEqual(ArticleState.Saved, article.State);
        Assert.AreEqual("A title", article.Title);
        Assert.IsFalse(article.IsRead);
        Assert.IsFalse(article.IsFavorite);
    }

    [TestMethod]
    public void DismissedArticleCanBeRestoredBeforeThirtyDaysButNotAtDeadline()
    {
        var article = CreateInbox();
        article.Dismiss(Now);

        Assert.AreEqual(Now.AddDays(30), article.PurgeAfterUtc);
        article.RestoreDismissedToInbox(Now.AddDays(30).AddTicks(-1));

        Assert.AreEqual(ArticleState.Inbox, article.State);
        Assert.IsNull(article.PurgeAfterUtc);

        article.Dismiss(Now);
        Assert.ThrowsExactly<InvalidOperationException>(() => article.RestoreDismissedToInbox(Now.AddDays(30)));
    }

    [TestMethod]
    public void TrashedArticleRestoresItsSavedReadAndFavoriteState()
    {
        var article = CreateSaved();
        article.SetRead(true, Now);
        article.SetFavorite(true, Now);

        article.MoveToTrash(Now);
        article.RestoreFromTrash(Now.AddDays(29));

        Assert.AreEqual(ArticleState.Saved, article.State);
        Assert.IsTrue(article.IsRead);
        Assert.IsTrue(article.IsFavorite);
    }

    [TestMethod]
    public void ManualRestoreAfterDuplicateDecisionMakesArticleUnreadAndPreservesUserMetadata()
    {
        var article = CreateSaved();
        article.UpdateMetadata("Title", "Description", "Personal note", Now);
        article.SetFavorite(true, Now);
        article.MoveToTrash(Now);

        article.RestoreDuplicateAsUnreadSaved(Now.AddDays(1));

        Assert.AreEqual(ArticleState.Saved, article.State);
        Assert.IsFalse(article.IsRead);
        Assert.IsTrue(article.IsFavorite);
        Assert.AreEqual("Personal note", article.Notes);
        Assert.IsNull(article.PurgeAfterUtc);
    }

    [TestMethod]
    public void ReadAndFavoriteCanOnlyChangeForSavedArticles()
    {
        var article = CreateInbox();

        Assert.ThrowsExactly<InvalidOperationException>(() => article.SetRead(true, Now));
        Assert.ThrowsExactly<InvalidOperationException>(() => article.SetFavorite(true, Now));
    }

    private static Article CreateInbox() =>
        Article.CreateInbox(
            "https://example.com/article",
            IdentityFingerprint.Create("https://example.com/article"),
            "https://example.com/article",
            null,
            null,
            Now);

    private static Article CreateSaved() =>
        Article.CreateSaved(
            "https://example.com/article",
            IdentityFingerprint.Create("https://example.com/article"),
            "https://example.com/article",
            "Article title",
            null,
            Now);
}

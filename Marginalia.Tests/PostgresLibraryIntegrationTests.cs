using System.Net;
using System.Text;
using System.Text.Json;
using Marginalia.Web.Application;
using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marginalia.Tests;

[TestClass]
public sealed class PostgresLibraryIntegrationTests
{
    private readonly List<Guid> _articleIds = [];
    private readonly List<Guid> _collectionIds = [];
    private readonly List<Guid> _feedIds = [];
    private readonly List<Guid> _tagIds = [];
    private readonly List<string> _fingerprints = [];
    private LibraryDbContext _dbContext = null!;
    private DateTimeOffset _now;
    private string? _connectionString;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("MARGINALIA_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            Assert.Inconclusive(
                "Set MARGINALIA_TEST_CONNECTION_STRING to an isolated local Marginalia PostgreSQL database to run integration tests.");
        }

        _now = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _dbContext = new LibraryDbContext(options);

        Assert.IsTrue(await _dbContext.Database.CanConnectAsync(), "The configured PostgreSQL test database is unavailable.");
        Assert.IsFalse(
            (await _dbContext.Database.GetPendingMigrationsAsync()).Any(),
            "Apply the current EF migrations before running PostgreSQL integration tests.");
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_dbContext is null)
        {
            return;
        }

        if (_articleIds.Count > 0)
        {
            await _dbContext.Articles
                .Where(article => _articleIds.Contains(article.Id))
                .ExecuteDeleteAsync();
        }

        if (_fingerprints.Count > 0)
        {
            await _dbContext.IdentityTombstones
                .Where(tombstone => _fingerprints.Contains(tombstone.IdentityFingerprint))
                .ExecuteDeleteAsync();
        }

        if (_collectionIds.Count > 0)
        {
            await _dbContext.Collections
                .Where(collection => _collectionIds.Contains(collection.Id))
                .ExecuteDeleteAsync();
        }

        if (_feedIds.Count > 0)
        {
            await _dbContext.FeedSubscriptions
                .Where(feed => _feedIds.Contains(feed.Id))
                .ExecuteDeleteAsync();
        }

        if (_tagIds.Count > 0)
        {
            await _dbContext.Tags
                .Where(tag => _tagIds.Contains(tag.Id))
                .ExecuteDeleteAsync();
        }

        await _dbContext.DisposeAsync();
    }

    [TestMethod]
    public async Task OrganizationUsesCaseInsensitiveTagsAndSearchReturnsStableServerPages()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var article = CreateSavedArticle(
            $"https://example.test/{suffix}?source=search",
            $"title-{suffix}",
            $"description-{suffix}",
            $"notes-{suffix}");
        var second = CreateSavedArticle(
            $"https://example.test/second-{suffix}",
            $"second-{suffix}",
            null,
            null);
        var third = CreateSavedArticle(
            $"https://example.test/third-{suffix}",
            $"third-{suffix}",
            null,
            null);
        await _dbContext.Articles.AddRangeAsync(article, second, third);
        await _dbContext.SaveChangesAsync();

        var commandService = CreateCommandService(_dbContext);
        await commandService.SetOrganizationAsync(
            article.Id,
            [$"Collection-{suffix}", $"Other-{suffix}"],
            [$" Tag-{suffix} ", $"tag-{suffix}", $"Topic-{suffix}"]);
        await commandService.SetOrganizationAsync(second.Id, [], []);
        await commandService.SetOrganizationAsync(
            third.Id,
            [$"Collection-{suffix}"],
            [$"tag-{suffix}"]);

        var queryService = new LibraryQueryService(_dbContext);
        foreach (var query in new[]
                 {
                     $"title-{suffix}",
                     $"description-{suffix}",
                     $"example.test/{suffix}",
                     $"notes-{suffix}",
                     $"TAG-{suffix}",
                     $"collection-{suffix}"
                 })
        {
            var result = await queryService.SearchAsync(new ArticleSearchRequest(Query: query));
            Assert.IsTrue(result.Items.Any(item => item.Id == article.Id), $"Search did not find article by '{query}'.");
            Assert.AreEqual(result.Items.Count, result.Items.Select(item => item.Id).Distinct().Count());
        }

        var tag = await _dbContext.Tags.SingleAsync(tag => tag.NormalizedName == Tag.Normalize($"tag-{suffix}"));
        Assert.AreEqual($"Tag-{suffix}", tag.DisplayName);
        Assert.AreEqual(
            3,
            await _dbContext.Set<ArticleTag>().CountAsync(membership =>
                membership.ArticleId == article.Id || membership.ArticleId == third.Id));

        var firstPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: suffix, PageNumber: 1, PageSize: 2));
        var secondPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: suffix, PageNumber: 2, PageSize: 2));
        Assert.AreEqual(3, firstPage.TotalCount);
        Assert.HasCount(2, firstPage.Items);
        Assert.HasCount(1, secondPage.Items);
        Assert.IsFalse(firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)).Any());
    }

    [TestMethod]
    public async Task TargetScaleLibrarySearchPagesTenThousandSavedArticlesAndFiftyFeeds()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var searchTerm = $"scale-{suffix}";
        var articles = Enumerable.Range(0, 10_000)
            .Select(index => CreateSavedArticle(
                $"https://scale.example.test/{suffix}/{index}",
                $"{searchTerm} article {index}",
                $"Synthetic scale description {index}",
                null))
            .ToArray();
        var feeds = Enumerable.Range(0, 50)
            .Select(index => FeedSubscription.Create(
                $"https://scale.example.test/feeds/{suffix}/{index}.xml",
                _now))
            .ToArray();
        _feedIds.AddRange(feeds.Select(feed => feed.Id));

        await _dbContext.Articles.AddRangeAsync(articles);
        await _dbContext.FeedSubscriptions.AddRangeAsync(feeds);
        await _dbContext.SaveChangesAsync();

        var queryService = new LibraryQueryService(_dbContext);
        var firstPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: searchTerm, PageNumber: 1, PageSize: 100));
        var secondPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: searchTerm, PageNumber: 2, PageSize: 100));
        var repeatedFirstPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: searchTerm, PageNumber: 1, PageSize: 100));
        var lastPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: searchTerm, PageNumber: 100, PageSize: 100));
        var beyondLastPage = await queryService.SearchAsync(
            new ArticleSearchRequest(Query: searchTerm, PageNumber: 101, PageSize: 100));

        Assert.AreEqual(10_000, firstPage.TotalCount);
        Assert.HasCount(100, firstPage.Items);
        Assert.HasCount(100, secondPage.Items);
        Assert.HasCount(100, lastPage.Items);
        Assert.IsEmpty(beyondLastPage.Items);
        Assert.AreEqual(
            100,
            firstPage.Items.Select(item => item.Id).Distinct().Count());
        Assert.IsFalse(firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)).Any());
        CollectionAssert.AreEqual(
            firstPage.Items.Select(item => item.Id).ToArray(),
            repeatedFirstPage.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(
            50,
            await _dbContext.FeedSubscriptions.CountAsync(feed =>
                feed.FeedUrl.StartsWith($"https://scale.example.test/feeds/{suffix}/")));
    }

    [TestMethod]
    public async Task SavedLibraryExportIsVersionedAndPreservesOrganizationStateAndSafeProvenance()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var url = $"https://example.test/export/{suffix}";
        var article = CreateSavedArticle(url, $"Export title {suffix}", $"Description {suffix}", $"Private note {suffix}");
        article.SetRead(true, _now);
        article.SetFavorite(true, _now);
        await _dbContext.Articles.AddAsync(article);
        await _dbContext.SaveChangesAsync();

        var commandService = CreateCommandService(_dbContext);
        await commandService.SetOrganizationAsync(
            article.Id,
            [$"Collection-{suffix}"],
            [$"MixedCase-{suffix}"]);

        var externalOccurrenceKey = $"external-occurrence-{suffix}";
        _dbContext.SourceOccurrences.AddRange(
            SourceOccurrence.Create(
                article.Id,
                SourceOccurrenceType.ManualCapture,
                "manual",
                $"manual-{suffix}",
                _now),
            SourceOccurrence.Create(
                article.Id,
                SourceOccurrenceType.Feed,
                $"feed-{Guid.NewGuid():N}",
                externalOccurrenceKey,
                _now.AddMinutes(1)));

        var trashedUrl = $"https://example.test/export-trashed/{suffix}";
        var trashed = CreateSavedArticle(trashedUrl, "Trashed export item", null, null);
        trashed.MoveToTrash(_now);
        var identity = UrlIdentity.Normalize($"https://example.test/export-inbox/{suffix}");
        var inboxUrl = $"https://example.test/export-inbox/{suffix}";
        var inbox = Article.CreateInbox(
            identity,
            IdentityFingerprint.Create(identity),
            inboxUrl,
            "Inbox export item",
            null,
            _now);
        _articleIds.Add(inbox.Id);
        await _dbContext.Articles.AddRangeAsync(trashed, inbox);
        await _dbContext.SaveChangesAsync();

        var json = await new LibraryExportService(_dbContext, new FixedTimeProvider(_now))
            .ExportJsonAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.AreEqual(LibraryExportService.CurrentVersion, root.GetProperty("version").GetInt32());
        Assert.AreEqual(_now, root.GetProperty("exportedUtc").GetDateTimeOffset());

        var articles = root.GetProperty("articles");
        var exported = articles.EnumerateArray()
            .Single(item => item.GetProperty("originalUrl").GetString() == url);
        Assert.AreEqual(article.Title, exported.GetProperty("title").GetString());
        Assert.AreEqual(article.Description, exported.GetProperty("description").GetString());
        Assert.AreEqual(article.Notes, exported.GetProperty("notes").GetString());
        Assert.IsTrue(exported.GetProperty("isRead").GetBoolean());
        Assert.IsTrue(exported.GetProperty("isFavorite").GetBoolean());
        CollectionAssert.Contains(
            exported.GetProperty("collections").EnumerateArray().Select(value => value.GetString()).ToArray(),
            $"Collection-{suffix}");
        CollectionAssert.Contains(
            exported.GetProperty("tags").EnumerateArray().Select(value => value.GetString()).ToArray(),
            $"MixedCase-{suffix}");

        var sources = exported.GetProperty("sources");
        Assert.IsTrue(sources.EnumerateArray().Any(source =>
            source.GetProperty("type").GetString() == "manual-capture"
            && source.GetProperty("sourceKey").GetString() == "manual"));
        Assert.IsTrue(sources.EnumerateArray().Any(source =>
            source.GetProperty("type").GetString() == "feed"
            && source.GetProperty("sourceKey").GetString()!.StartsWith("feed-", StringComparison.Ordinal)));
        Assert.IsFalse(Encoding.UTF8.GetString(json).Contains(externalOccurrenceKey, StringComparison.Ordinal));
        Assert.IsFalse(articles.EnumerateArray().Any(item =>
            item.GetProperty("originalUrl").GetString() == trashedUrl
            || item.GetProperty("originalUrl").GetString() == inboxUrl));
    }

    [TestMethod]
    public async Task SavedLibraryJsonRoundTripsMetadataOrganizationStateAndProvenance()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var url = $"https://example.test/round-trip/{suffix}";
        var article = CreateSavedArticle(url, $"Round trip {suffix}", "Description", $"Note {suffix}");
        article.SetRead(true, _now);
        article.SetFavorite(true, _now);
        await _dbContext.Articles.AddAsync(article);
        await _dbContext.SaveChangesAsync();
        await CreateCommandService(_dbContext).SetOrganizationAsync(
            article.Id,
            [$"Collection-{suffix}"],
            [$"Tag-{suffix}"]);
        _dbContext.SourceOccurrences.Add(SourceOccurrence.Create(
            article.Id,
            SourceOccurrenceType.ManualCapture,
            "manual",
            $"round-trip-{suffix}",
            _now.AddMinutes(-2)));
        await _dbContext.SaveChangesAsync();

        var export = await new LibraryExportService(_dbContext, new FixedTimeProvider(_now))
            .ExportJsonAsync();
        var exportedDocument = JsonSerializer.Deserialize<LibraryExportDocument>(
            export,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The generated test export did not deserialize.");
        var articleDocument = exportedDocument with
        {
            Articles = exportedDocument.Articles
                .Where(candidate => candidate.OriginalUrl == url)
                .ToArray()
        };
        Assert.HasCount(1, articleDocument.Articles);
        export = JsonSerializer.SerializeToUtf8Bytes(
            articleDocument,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var identity = article.IdentityKey;
        var articleId = article.Id;
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Articles
            .Where(candidate => candidate.Id == articleId)
            .ExecuteDeleteAsync();

        await using var importStream = new MemoryStream(export);
        var result = await new LibraryImportService(_dbContext, new FixedTimeProvider(_now.AddDays(1)))
            .ImportJsonAsync(importStream);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.IsEmpty(result.Conflicts);
        var restored = await _dbContext.Articles.AsNoTracking()
            .SingleAsync(candidate => candidate.IdentityKey == identity);
        _articleIds.Add(restored.Id);
        Assert.AreEqual(url, restored.OriginalUrl);
        Assert.AreEqual("Round trip " + suffix, restored.Title);
        Assert.AreEqual("Description", restored.Description);
        Assert.AreEqual("Note " + suffix, restored.Notes);
        Assert.IsTrue(restored.IsRead);
        Assert.IsTrue(restored.IsFavorite);
        Assert.AreEqual(_now.ToUnixTimeMilliseconds(), restored.CreatedUtc.ToUnixTimeMilliseconds());
        Assert.AreEqual(ArticleState.Saved, restored.State);
        CollectionAssert.Contains(
            await _dbContext.Set<ArticleCollection>()
                .Where(membership => membership.ArticleId == restored.Id)
                .Select(membership => membership.Collection.Name)
                .ToArrayAsync(),
            $"Collection-{suffix}");
        CollectionAssert.Contains(
            await _dbContext.Set<ArticleTag>()
                .Where(membership => membership.ArticleId == restored.Id)
                .Select(membership => membership.Tag.DisplayName)
                .ToArrayAsync(),
            $"Tag-{suffix}");
        Assert.AreEqual(
            1,
            await _dbContext.SourceOccurrences.CountAsync(occurrence => occurrence.ArticleId == restored.Id));
    }

    [TestMethod]
    public async Task ImportSkipsExistingTombstonedAndInFileDuplicatesWithoutOverwriting()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var existingUrl = $"https://example.test/import-existing/{suffix}";
        var existing = CreateSavedArticle(existingUrl, "Owner title", "Owner description", $"Owner note {suffix}");
        existing.SetRead(true, _now);
        existing.SetFavorite(true, _now);
        await _dbContext.Articles.AddAsync(existing);
        await _dbContext.SaveChangesAsync();
        await CreateCommandService(_dbContext).SetOrganizationAsync(
            existing.Id,
            [$"Shared-{suffix}"],
            [$"KeepCase-{suffix}"]);

        var inboxUrl = $"https://example.test/import-inbox/{suffix}";
        var inboxIdentity = UrlIdentity.Normalize(inboxUrl);
        var inbox = Article.CreateInbox(
            inboxIdentity,
            IdentityFingerprint.Create(inboxIdentity),
            inboxUrl,
            "Inbox item",
            null,
            _now);
        var dismissedUrl = $"https://example.test/import-dismissed/{suffix}";
        var dismissedIdentity = UrlIdentity.Normalize(dismissedUrl);
        var dismissed = Article.CreateInbox(
            dismissedIdentity,
            IdentityFingerprint.Create(dismissedIdentity),
            dismissedUrl,
            "Dismissed item",
            null,
            _now);
        dismissed.Dismiss(_now);
        var trashedUrl = $"https://example.test/import-trashed/{suffix}";
        var trashed = CreateSavedArticle(trashedUrl, "Trashed item", null, null);
        trashed.MoveToTrash(_now);
        _articleIds.Add(inbox.Id);
        _articleIds.Add(dismissed.Id);
        var purgedUrl = $"https://example.test/import-purged/{suffix}";
        var purgedIdentity = UrlIdentity.Normalize(purgedUrl);
        var purgedFingerprint = IdentityFingerprint.Create(purgedIdentity);
        _fingerprints.Add(purgedFingerprint);
        _dbContext.IdentityTombstones.Add(IdentityTombstone.Create(purgedFingerprint, _now));
        await _dbContext.Articles.AddRangeAsync(inbox, dismissed, trashed);
        await _dbContext.SaveChangesAsync();

        var newUrl = $"https://example.test/import-new/{suffix}";
        var feedSourceKey = $"feed-{Guid.NewGuid():N}";
        var newEntry = CreateImportEntry(
            newUrl,
            $"Imported title {suffix}",
            $"Imported description {suffix}",
            $"Imported note {suffix}",
            isRead: true,
            isFavorite: true,
            [$"Shared-{suffix}", $"New-{suffix}"],
            [$"keepcase-{suffix}", $"NewTag-{suffix}"],
            [
                new LibraryExportSource("manual-capture", "manual", _now.AddMinutes(-3)),
                new LibraryExportSource("feed", feedSourceKey, _now.AddMinutes(-1))
            ]);
        var document = new LibraryExportDocument(
            LibraryExportService.CurrentVersion,
            _now,
            [
                newEntry,
                CreateImportEntry(existingUrl, "Changed title", null, "Changed note", false, false, [], [], []),
                CreateImportEntry(inboxUrl, "Changed inbox title", null, null, false, false, [], [], []),
                CreateImportEntry(dismissedUrl, "Changed dismissed title", null, null, false, false, [], [], []),
                CreateImportEntry(trashedUrl, "Changed trashed title", null, null, false, false, [], [], []),
                CreateImportEntry(purgedUrl, "Purged title", null, null, false, false, [], [], []),
                CreateImportEntry(newUrl + "#duplicate", "Duplicate in file", null, null, false, false, [], [], [])
            ]);
        var importBytes = JsonSerializer.SerializeToUtf8Bytes(
            document,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var importer = new LibraryImportService(_dbContext, new FixedTimeProvider(_now.AddMinutes(5)));

        await using var importStream = new MemoryStream(importBytes);
        var result = await importer.ImportJsonAsync(importStream);

        Assert.AreEqual(1, result.ImportedCount);
        Assert.HasCount(6, result.Conflicts);
        Assert.IsTrue(result.Conflicts.Any(conflict => conflict.OriginalUrl == existingUrl));
        Assert.IsTrue(result.Conflicts.Any(conflict => conflict.OriginalUrl == inboxUrl));
        Assert.IsTrue(result.Conflicts.Any(conflict => conflict.OriginalUrl == dismissedUrl));
        Assert.IsTrue(result.Conflicts.Any(conflict => conflict.OriginalUrl == trashedUrl));
        Assert.IsTrue(result.Conflicts.Any(conflict => conflict.OriginalUrl == purgedUrl));
        Assert.IsTrue(result.Conflicts.Any(conflict => conflict.OriginalUrl == newUrl + "#duplicate"));

        _dbContext.ChangeTracker.Clear();
        var unchanged = await _dbContext.Articles.AsNoTracking()
            .SingleAsync(candidate => candidate.OriginalUrl == existingUrl);
        Assert.AreEqual("Owner title", unchanged.Title);
        Assert.AreEqual("Owner description", unchanged.Description);
        Assert.AreEqual($"Owner note {suffix}", unchanged.Notes);
        Assert.IsTrue(unchanged.IsRead);
        Assert.IsTrue(unchanged.IsFavorite);
        Assert.AreEqual(ArticleState.Inbox,
            (await _dbContext.Articles.AsNoTracking().SingleAsync(candidate => candidate.Id == inbox.Id)).State);
        Assert.AreEqual(ArticleState.Dismissed,
            (await _dbContext.Articles.AsNoTracking().SingleAsync(candidate => candidate.Id == dismissed.Id)).State);
        Assert.AreEqual(ArticleState.Trashed,
            (await _dbContext.Articles.AsNoTracking().SingleAsync(candidate => candidate.Id == trashed.Id)).State);
        Assert.IsTrue(await _dbContext.IdentityTombstones.AnyAsync(
            tombstone => tombstone.IdentityFingerprint == purgedFingerprint));

        var imported = await _dbContext.Articles.AsNoTracking()
            .SingleAsync(candidate => candidate.OriginalUrl == newUrl);
        _articleIds.Add(imported.Id);
        Assert.AreEqual($"Imported note {suffix}", imported.Notes);
        Assert.IsTrue(imported.IsRead);
        Assert.IsTrue(imported.IsFavorite);
        Assert.AreEqual(2, await _dbContext.SourceOccurrences.CountAsync(
            occurrence => occurrence.ArticleId == imported.Id));
        Assert.AreEqual($"KeepCase-{suffix}", await _dbContext.Set<ArticleTag>()
            .Where(membership => membership.ArticleId == imported.Id)
            .Select(membership => membership.Tag.DisplayName)
            .SingleAsync(name => name == $"KeepCase-{suffix}"));

        await using var retryStream = new MemoryStream(importBytes);
        var retry = await importer.ImportJsonAsync(retryStream);
        Assert.AreEqual(0, retry.ImportedCount);
        Assert.HasCount(7, retry.Conflicts);
        Assert.AreEqual(2, await _dbContext.SourceOccurrences.CountAsync(
            occurrence => occurrence.ArticleId == imported.Id));
    }

    [TestMethod]
    public async Task InvalidImportDocumentIsRejectedBeforeAnyArticleIsWritten()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var validFirstUrl = $"https://example.test/import-valid-first/{suffix}";
        var invalidDocument = new LibraryExportDocument(
            LibraryExportService.CurrentVersion,
            _now,
            [
                CreateImportEntry(validFirstUrl, "Valid title", null, null, false, false, [], [], []),
                CreateImportEntry($"https://example.test/import-invalid/{suffix}", "   ", null, null, false, false, [], [], [])
            ]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            invalidDocument,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var stream = new MemoryStream(bytes);
        var importer = new LibraryImportService(_dbContext, new FixedTimeProvider(_now));

        await Assert.ThrowsExactlyAsync<LibraryImportValidationException>(async () =>
            await importer.ImportJsonAsync(stream));

        Assert.IsFalse(await _dbContext.Articles.AnyAsync(article => article.OriginalUrl == validFirstUrl));
    }

    [TestMethod]
    public async Task ConcurrentManualCaptureCreatesOneArticleWithoutOverwritingSavedEdits()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var url = $"https://example.test/concurrent/{suffix}";
        var identity = UrlIdentity.Normalize(url);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ManualCaptureResult> CaptureAsync(string title)
        {
            await start.Task;
            var options = new DbContextOptionsBuilder<LibraryDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
            await using var context = new LibraryDbContext(options);
            return await CreateCommandService(context).CaptureAsync(url, title, null, Guid.NewGuid());
        }

        var firstCapture = CaptureAsync("First concurrent title");
        var secondCapture = CaptureAsync("Second concurrent title");
        start.SetResult();
        var results = await Task.WhenAll(firstCapture, secondCapture);

        Assert.AreEqual(1, results.Count(result => result.Outcome == ManualCaptureOutcome.Created));
        Assert.AreEqual(1, results.Count(result => result.Outcome == ManualCaptureOutcome.AlreadyPresent));

        var article = await _dbContext.Articles.SingleAsync(candidate => candidate.IdentityKey == identity);
        _articleIds.Add(article.Id);
        article.UpdateMetadata("Owner-edited title", "Owner-edited description", "Owner-edited note", _now);
        await _dbContext.SaveChangesAsync();

        var duplicate = await CreateCommandService(_dbContext).CaptureAsync(
            url,
            "Later source title",
            "Later source description",
            Guid.NewGuid());
        Assert.AreEqual(ManualCaptureOutcome.AlreadyPresent, duplicate.Outcome);

        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Articles.AsNoTracking().SingleAsync(candidate => candidate.Id == article.Id);
        Assert.AreEqual("Owner-edited title", persisted.Title);
        Assert.AreEqual("Owner-edited description", persisted.Description);
        Assert.AreEqual("Owner-edited note", persisted.Notes);
        Assert.AreEqual(
            1,
            await _dbContext.Articles.CountAsync(candidate => candidate.IdentityKey == identity));
    }

    [TestMethod]
    public async Task ManualCaptureUsesSafeMetadataAndReportsFetchFailuresWithoutSaving()
    {
        var url = $"https://example.test/manual-metadata/{Guid.NewGuid():N}";
        var html = Encoding.UTF8.GetBytes(
            "<html><head><title>Fetched title</title><meta name='description' content='Fetched summary'></head></html>");
        var fetcher = new SequenceSafeHttpFetcher(
            () => new SafeHttpFetchResponse(
                HttpStatusCode.OK,
                new Uri(url),
                "text/html",
                html,
                null,
                null));
        var service = new ManualUrlCaptureService(CreateCommandService(_dbContext), fetcher);

        var result = await service.CaptureAsync(url, null, null, Guid.NewGuid());

        Assert.AreEqual(ManualCaptureOutcome.Created, result.Capture.Outcome);
        Assert.IsNull(result.MetadataFailure);
        var saved = await _dbContext.Articles.SingleAsync(article => article.OriginalUrl == url);
        _articleIds.Add(saved.Id);
        Assert.AreEqual(ArticleState.Saved, saved.State);
        Assert.IsFalse(saved.IsRead);
        Assert.AreEqual("Fetched title", saved.Title);
        Assert.AreEqual("Fetched summary", saved.Description);

        var unavailableUrl = $"https://example.test/unavailable/{Guid.NewGuid():N}";
        var failingService = new ManualUrlCaptureService(
            CreateCommandService(_dbContext),
            new SequenceSafeHttpFetcher(
                () => throw new SafeHttpFetchException(SafeHttpFetchFailure.NetworkFailure)));
        var unavailable = await failingService.CaptureAsync(
            unavailableUrl,
            null,
            null,
            Guid.NewGuid());

        Assert.AreEqual(ManualCaptureOutcome.TitleRequired, unavailable.Capture.Outcome);
        Assert.AreEqual(SafeHttpFetchFailure.NetworkFailure, unavailable.MetadataFailure);
        Assert.IsFalse(await _dbContext.Articles.AnyAsync(article => article.OriginalUrl == unavailableUrl));
    }

    [TestMethod]
    public async Task InitialFeedRefreshIngestsLatestTwentyAnd304KeepsConditionalHealth()
    {
        var feedUrl = $"https://feeds.example.test/{Guid.NewGuid():N}.xml";
        var subscriptions = new FeedSubscriptionService(_dbContext, new FixedTimeProvider(_now));
        var feedId = await subscriptions.AddAsync(feedUrl);
        _feedIds.Add(feedId);

        var xml = BuildFeedXml(feedId, 25);
        var firstLastModified = _now.AddHours(-2);
        var secondLastModified = _now.AddHours(-1);
        var updatedXml = BuildFeedXml(feedId, 26);
        var fetcher = new SequenceSafeHttpFetcher(
            () => new SafeHttpFetchResponse(
                HttpStatusCode.OK,
                new Uri(feedUrl),
                "application/rss+xml",
                Encoding.UTF8.GetBytes(xml),
                "\"feed-v1\"",
                firstLastModified),
            () => new SafeHttpFetchResponse(
                HttpStatusCode.OK,
                new Uri(feedUrl),
                "application/rss+xml",
                Encoding.UTF8.GetBytes(updatedXml),
                "\"feed-v2\"",
                secondLastModified),
            () => new SafeHttpFetchResponse(
                HttpStatusCode.NotModified,
                new Uri(feedUrl),
                "application/rss+xml",
                [],
                "\"feed-v2\"",
                secondLastModified));
        var ingestion = new FeedIngestionService(_dbContext, new FixedTimeProvider(_now));
        var polling = new FeedPollingService(
            _dbContext,
            fetcher,
            ingestion,
            new FeedRefreshGate(),
            new FixedTimeProvider(_now));

        var first = await polling.RefreshAsync(feedId);
        Assert.IsTrue(first.Succeeded);
        Assert.AreEqual(20, first.NewInboxArticles);
        Assert.AreEqual(20, first.NewSourceOccurrences);
        var expectedBoundary = RssFeedParser.Parse(Encoding.UTF8.GetBytes(xml), new Uri(feedUrl))
            .Items
            .OrderByDescending(item => item.PublishedUtc ?? DateTimeOffset.MinValue)
            .Take(20)
            .Last()
            .OccurrenceKey;
        var initialSnapshot = await _dbContext.FeedSubscriptions
            .AsNoTracking()
            .SingleAsync(feed => feed.Id == feedId);
        Assert.AreEqual(expectedBoundary, initialSnapshot.InitialBoundaryOccurrenceKey);
        var articles = await _dbContext.Articles
            .AsNoTracking()
            .Where(article => article.OriginalUrl.Contains(feedId.ToString("N")))
            .OrderBy(article => article.OriginalUrl)
            .ToArrayAsync();
        _articleIds.AddRange(articles.Select(article => article.Id));
        Assert.HasCount(20, articles);
        Assert.IsTrue(articles.All(article => article.State == ArticleState.Inbox));
        Assert.IsTrue(articles.All(article => !article.IsRead));
        Assert.IsFalse(articles.Any(article => article.OriginalUrl.EndsWith("entry-0", StringComparison.Ordinal)));
        Assert.IsTrue(articles.Any(article => article.OriginalUrl.EndsWith("entry-24", StringComparison.Ordinal)));

        var reviewedArticle = await _dbContext.Articles.SingleAsync(article =>
            article.OriginalUrl == $"https://example.test/{feedId:N}/entry-24");
        reviewedArticle.Dismiss(_now);
        await _dbContext.SaveChangesAsync();
        var duplicateInAnotherFeed = RssFeedParser.Parse(Encoding.UTF8.GetBytes(xml), new Uri(feedUrl))
            .Items.Single(item => item.Url.EndsWith("entry-24", StringComparison.Ordinal));
        var otherFeedId = Guid.NewGuid();
        var otherSource = await ingestion.IngestAsync(otherFeedId, [duplicateInAnotherFeed]);
        Assert.AreEqual(0, otherSource.NewInboxArticles);
        Assert.AreEqual(1, otherSource.NewSourceOccurrences);
        Assert.AreEqual(
            ArticleState.Dismissed,
            (await _dbContext.Articles.AsNoTracking().SingleAsync(article => article.Id == reviewedArticle.Id)).State);

        var second = await polling.RefreshAsync(feedId);
        Assert.IsTrue(second.Succeeded);
        Assert.AreEqual(1, second.NewInboxArticles);
        Assert.AreEqual(1, second.NewSourceOccurrences);
        Assert.AreEqual(19, second.RepeatedOccurrences);
        Assert.AreEqual(21, await _dbContext.Articles.CountAsync(article =>
            article.OriginalUrl.Contains(feedId.ToString("N"))));
        Assert.IsFalse(await _dbContext.Articles.AnyAsync(article =>
            article.OriginalUrl == $"https://example.test/{feedId:N}/entry-0"));
        Assert.IsTrue(await _dbContext.Articles.AnyAsync(article =>
            article.OriginalUrl == $"https://example.test/{feedId:N}/entry-25"));
        Assert.AreEqual(
            ArticleState.Dismissed,
            (await _dbContext.Articles.AsNoTracking().SingleAsync(article => article.Id == reviewedArticle.Id)).State);

        var third = await polling.RefreshAsync(feedId);
        Assert.IsTrue(third.Succeeded);
        Assert.IsTrue(third.NotModified);
        Assert.AreEqual("\"feed-v1\"", fetcher.EntityTags[1]);
        Assert.AreEqual(firstLastModified, fetcher.LastModifiedValues[1]);
        Assert.AreEqual("\"feed-v2\"", fetcher.EntityTags[2]);
        Assert.AreEqual(secondLastModified, fetcher.LastModifiedValues[2]);
        var allFeedArticles = await _dbContext.Articles
            .Where(article => article.OriginalUrl.Contains(feedId.ToString("N")))
            .Select(article => article.Id)
            .ToArrayAsync();
        _articleIds.AddRange(allFeedArticles);

        await subscriptions.DisableAsync(feedId);
        var disabled = await _dbContext.FeedSubscriptions.AsNoTracking().SingleAsync(feed => feed.Id == feedId);
        Assert.IsFalse(disabled.IsEnabled);
        Assert.IsNull(disabled.NextDueUtc);
        Assert.AreEqual(21, await _dbContext.SourceOccurrences.CountAsync(occurrence =>
            occurrence.SourceKey == $"feed-{feedId:N}"));
        Assert.AreEqual(
            2,
            await _dbContext.SourceOccurrences.CountAsync(occurrence => occurrence.ArticleId == reviewedArticle.Id));
    }

    [TestMethod]
    public async Task MalformedFeedFailureIsVisibleAndSchedulesRetry()
    {
        var feedUrl = $"https://feeds.example.test/{Guid.NewGuid():N}.xml";
        var feedId = await new FeedSubscriptionService(_dbContext, new FixedTimeProvider(_now))
            .AddAsync(feedUrl);
        _feedIds.Add(feedId);
        var fetcher = new SequenceSafeHttpFetcher(
            () => new SafeHttpFetchResponse(
                HttpStatusCode.OK,
                new Uri(feedUrl),
                "application/rss+xml",
                Encoding.UTF8.GetBytes("<rss><channel>"),
                null,
                null));
        var polling = new FeedPollingService(
            _dbContext,
            fetcher,
            new FeedIngestionService(_dbContext, new FixedTimeProvider(_now)),
            new FeedRefreshGate(),
            new FixedTimeProvider(_now));

        var result = await polling.RefreshAsync(feedId);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("The feed response was malformed or used an unsupported format.", result.Error);
        var feed = await _dbContext.FeedSubscriptions.AsNoTracking().SingleAsync(candidate => candidate.Id == feedId);
        Assert.AreEqual(
            _now.AddMinutes(1).ToUnixTimeMilliseconds(),
            feed.NextDueUtc?.ToUnixTimeMilliseconds());
        Assert.AreEqual(1, feed.ConsecutiveFailures);
        Assert.AreEqual(result.Error, feed.LastError);
        Assert.IsFalse(await _dbContext.Articles.AnyAsync(article => article.OriginalUrl.Contains(feedId.ToString("N"))));
    }

    [TestMethod]
    public async Task PurgeRemovesMetadataAndProvenanceThenRequiresConfirmationForRecapture()
    {
        var url = $"https://example.test/purged/{Guid.NewGuid():N}";
        var identity = UrlIdentity.Normalize(url);
        var fingerprint = IdentityFingerprint.Create(identity);
        _fingerprints.Add(fingerprint);
        var expiredAt = _now.AddDays(-31);
        var article = Article.CreateInbox(identity, fingerprint, url, "Old title", "Old description", expiredAt);
        article.Dismiss(expiredAt);
        _articleIds.Add(article.Id);
        await _dbContext.Articles.AddAsync(article);
        _dbContext.SourceOccurrences.Add(SourceOccurrence.Create(
            article.Id,
            SourceOccurrenceType.Feed,
            $"feed-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("N"),
            expiredAt));
        await _dbContext.SaveChangesAsync();

        var retentionService = new ArticleRetentionService(_dbContext, new FixedTimeProvider(_now));
        var purgedCount = await retentionService.PurgeExpiredAsync();

        Assert.AreEqual(1, purgedCount);
        Assert.IsFalse(await _dbContext.Articles.AsNoTracking().AnyAsync(candidate => candidate.Id == article.Id));
        Assert.IsFalse(await _dbContext.SourceOccurrences.AsNoTracking().AnyAsync(occurrence => occurrence.ArticleId == article.Id));
        Assert.IsTrue(await _dbContext.IdentityTombstones.AsNoTracking()
            .AnyAsync(tombstone => tombstone.IdentityFingerprint == fingerprint));

        _dbContext.ChangeTracker.Clear();
        var captureService = CreateCommandService(_dbContext);
        var requestId = Guid.NewGuid();
        var confirmation = await captureService.CaptureAsync(url, "Fresh title", null, requestId);
        Assert.AreEqual(ManualCaptureOutcome.PurgedIdentityConfirmationRequired, confirmation.Outcome);
        Assert.IsNull(confirmation.ExistingArticle);

        var confirmed = await captureService.CaptureAsync(
            url,
            "Fresh title",
            null,
            requestId,
            confirmPurgedIdentity: true);
        Assert.AreEqual(ManualCaptureOutcome.Created, confirmed.Outcome);

        var freshArticle = await _dbContext.Articles.SingleAsync(candidate => candidate.IdentityKey == identity);
        _articleIds.Add(freshArticle.Id);
        Assert.AreEqual(ArticleState.Saved, freshArticle.State);
        Assert.IsFalse(freshArticle.IsRead);
        Assert.IsTrue(await _dbContext.IdentityTombstones.AnyAsync(tombstone => tombstone.IdentityFingerprint == fingerprint));

        var retry = await captureService.CaptureAsync(
            url,
            "Fresh title",
            null,
            requestId,
            confirmPurgedIdentity: true);
        Assert.AreEqual(ManualCaptureOutcome.AlreadyPresent, retry.Outcome);
        Assert.AreEqual(
            1,
            await _dbContext.SourceOccurrences.CountAsync(occurrence => occurrence.ArticleId == freshArticle.Id));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await captureService.CaptureAsync(
                $"https://example.test/other/{Guid.NewGuid():N}",
                "Other title",
                null,
                requestId,
                confirmPurgedIdentity: true));
    }

    [TestMethod]
    public async Task RetainedTrashedMatchShowsPriorDetailsAndExplicitRestorePreservesOrganization()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var url = $"https://example.test/retained/{suffix}";
        var identity = UrlIdentity.Normalize(url);
        var fingerprint = IdentityFingerprint.Create(identity);
        var article = Article.CreateSaved(identity, fingerprint, url, "Prior title", "Prior description", _now);
        article.UpdateMetadata("Prior title", "Prior description", $"private note {suffix}", _now);
        article.SetRead(true, _now);
        article.SetFavorite(true, _now);
        _articleIds.Add(article.Id);
        await _dbContext.Articles.AddAsync(article);
        _dbContext.SourceOccurrences.Add(SourceOccurrence.Create(
            article.Id,
            SourceOccurrenceType.Feed,
            $"feed-{suffix}",
            $"entry-{suffix}",
            _now));
        await _dbContext.SaveChangesAsync();

        var commandService = CreateCommandService(_dbContext);
        await commandService.SetOrganizationAsync(article.Id, [$"Collection-{suffix}"], [$"Tag-{suffix}"]);
        article.MoveToTrash(_now);
        await _dbContext.SaveChangesAsync();

        var requestId = Guid.NewGuid();
        var decision = await commandService.CaptureAsync(url, "New title", null, requestId);

        Assert.AreEqual(ManualCaptureOutcome.RecoveryDecisionRequired, decision.Outcome);
        Assert.IsNotNull(decision.ExistingArticle);
        Assert.AreEqual("Prior title", decision.ExistingArticle.Article.Title);
        Assert.AreEqual($"private note {suffix}", decision.ExistingArticle.Article.Notes);
        CollectionAssert.Contains(decision.ExistingArticle.Article.Collections.ToArray(), $"Collection-{suffix}");
        CollectionAssert.Contains(decision.ExistingArticle.Article.Tags.ToArray(), $"Tag-{suffix}");
        Assert.HasCount(1, decision.ExistingArticle.SourceOccurrences);

        await commandService.RestoreManualCaptureMatchAsync(article.Id, requestId);
        _dbContext.ChangeTracker.Clear();
        var restored = await _dbContext.Articles.AsNoTracking().SingleAsync(candidate => candidate.Id == article.Id);
        Assert.AreEqual(ArticleState.Saved, restored.State);
        Assert.IsFalse(restored.IsRead);
        Assert.IsTrue(restored.IsFavorite);
        Assert.AreEqual($"private note {suffix}", restored.Notes);
        var occurrences = await _dbContext.SourceOccurrences
            .Where(occurrence => occurrence.ArticleId == article.Id)
            .ToArrayAsync();
        Assert.HasCount(2, occurrences);

        var retry = await commandService.RestoreManualCaptureMatchAsync(article.Id, requestId);
        Assert.AreEqual(ManualCaptureOutcome.AlreadyPresent, retry.Outcome);
        Assert.AreEqual(
            2,
            await _dbContext.SourceOccurrences.CountAsync(occurrence => occurrence.ArticleId == article.Id));

        var otherUrl = $"https://example.test/other-retained/{suffix}";
        var otherIdentity = UrlIdentity.Normalize(otherUrl);
        var otherArticle = Article.CreateSaved(
            otherIdentity,
            IdentityFingerprint.Create(otherIdentity),
            otherUrl,
            "Other prior title",
            null,
            _now);
        otherArticle.MoveToTrash(_now);
        _articleIds.Add(otherArticle.Id);
        await _dbContext.Articles.AddAsync(otherArticle);
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await commandService.RestoreManualCaptureMatchAsync(otherArticle.Id, requestId));
    }

    private Article CreateSavedArticle(string url, string title, string? description, string? notes)
    {
        var identity = UrlIdentity.Normalize(url);
        var article = Article.CreateSaved(
            identity,
            IdentityFingerprint.Create(identity),
            url,
            title,
            description,
            _now);
        if (notes is not null)
        {
            article.UpdateMetadata(title, description, notes, _now);
        }

        _articleIds.Add(article.Id);
        return article;
    }

    private LibraryExportArticle CreateImportEntry(
        string originalUrl,
        string? title,
        string? description,
        string? notes,
        bool isRead,
        bool isFavorite,
        IReadOnlyList<string> collections,
        IReadOnlyList<string> tags,
        IReadOnlyList<LibraryExportSource> sources) =>
        new(
            originalUrl,
            title,
            description,
            notes,
            isRead,
            isFavorite,
            _now.AddDays(-365),
            _now.AddDays(-370),
            collections,
            tags,
            sources);

    private LibraryCommandService CreateCommandService(LibraryDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(_now));

    private static string BuildFeedXml(Guid feedId, int itemCount)
    {
        var builder = new StringBuilder(
            "<rss version=\"2.0\"><channel><title>Integration Feed</title>");
        for (var index = 0; index < itemCount; index++)
        {
            var published = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index);
            builder.Append("<item><guid>entry-")
                .Append(index)
                .Append("</guid><title>Feed item ")
                .Append(index)
                .Append("</title><link>https://example.test/")
                .Append(feedId.ToString("N"))
                .Append("/entry-")
                .Append(index)
                .Append("</link><description>Summary ")
                .Append(index)
                .Append("</description><pubDate>")
                .Append(published.ToString("O"))
                .Append("</pubDate></item>");
        }

        return builder.Append("</channel></rss>").ToString();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequenceSafeHttpFetcher(params Func<SafeHttpFetchResponse>[] results) : ISafePublicHttpFetcher
    {
        public List<string?> EntityTags { get; } = [];

        public List<DateTimeOffset?> LastModifiedValues { get; } = [];

        public Task<SafeHttpFetchResponse> FetchAsync(
            Uri target,
            string? entityTag = null,
            DateTimeOffset? lastModified = null,
            CancellationToken cancellationToken = default)
        {
            EntityTags.Add(entityTag);
            LastModifiedValues.Add(lastModified);
            try
            {
                return Task.FromResult(results[EntityTags.Count - 1]());
            }
            catch (SafeHttpFetchException exception)
            {
                return Task.FromException<SafeHttpFetchResponse>(exception);
            }
        }
    }
}

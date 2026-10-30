# Marginalia

Marginalia is a private, single-owner library for saved article links. It stores
article metadata and organization, not article bodies or images.

## Local development

Prerequisites:

- .NET 10 SDK
- Aspire CLI
- Docker Desktop with its Linux container engine running
- The organization's approved NuGet package source configured for `dotnet`

This repository intentionally does not configure a public NuGet source or commit
the organization's internal feed URL. Restore with the approved machine or
organization NuGet configuration. Inspect configured sources with
`dotnet nuget list source`; internal feed URLs and credentials belong in
user- or organization-managed NuGet configuration, not this public repository.
If restore attempts to contact `nuget.org`, stop and correct the approved source
configuration rather than adding a public source.

Restore and start through Aspire:

```powershell
dotnet restore Marginalia.sln
aspire start --non-interactive
aspire wait postgres
aspire wait web
aspire describe
```

The AppHost runs one Blazor Interactive Server web application and one local
PostgreSQL container. PostgreSQL uses a named data volume so database records
survive ordinary application/container restarts. Keep the Aspire endpoints on
their local host addresses; do not expose endpoints to the LAN, add a tunnel,
or enable external endpoints.

The root URL is a public welcome page with a GitHub sign-in link. The Inbox and
other library routes remain owner-protected; after sign-in, the link returns to
`/inbox`. Until OAuth is configured, the welcome page explains that setup is
pending and the sign-in endpoint provides the local callback and configuration
steps.

## GitHub owner authentication

Marginalia uses a server-side GitHub OAuth authorization-code flow and a
secure, local application cookie. The configured positive numeric GitHub user
ID is the sole owner identity and stays in external configuration, not this
repository. Marginalia requests **no OAuth scopes** and maps only the numeric
public user ID from GitHub's authenticated `/user` response. It does not use a
CLI token or personal access token for login, request repository, organization,
or private-email permissions, or retain the provider access token after
identity resolution. OAuth state/correlation and S256 PKCE are enabled; return
URLs are restricted to local paths. Sign-out clears only the local app cookie.
The prior Entra cookie is not accepted and is expired when seen.

The local OAuth app must use these exact URLs:

- Homepage URL: `https://localhost:7144/`
- Authorization callback URL: `https://localhost:7144/signin-github`

Create a **GitHub OAuth App** (not an installed GitHub App) manually after
development is complete. Do not grant or request additional scopes. The local
callback is fixed HTTPS; any deployed callback requires a separate future
decision.

Store the OAuth app values outside the repository. After creating the app,
initialize local user-secrets and supply the values without putting them in
source-controlled files:

```powershell
dotnet user-secrets init --project Marginalia.Web/Marginalia.Web.csproj
dotnet user-secrets set "GitHubOAuth:ClientId" "<oauth-client-id>" --project Marginalia.Web/Marginalia.Web.csproj
dotnet user-secrets set "GitHubOAuth:ClientSecret" "<oauth-client-secret>" --project Marginalia.Web/Marginalia.Web.csproj
dotnet user-secrets set "GitHubOAuth:OwnerId" "<stable-numeric-github-user-id>" --project Marginalia.Web/Marginalia.Web.csproj
```

No OAuth app or credentials have been created for this implementation. Until
all three settings are supplied, authentication fails closed: application
routes remain inaccessible and API requests return `401`; there is no
Development bypass. Partial or invalid settings fail startup. Automated auth
checks use synthetic identities only. Real GitHub sign-in remains unverified
until the owner completes the manual app setup. GitHub login is independent of
any future WorkIQ integration.

## Tests and migrations

Run tests with `dotnet test Marginalia.sln`. EF Core migrations are applied by
the web application at startup after Aspire has made PostgreSQL ready. Add
future schema changes with the repository's local `dotnet-ef` tool manifest.
The first-run singleton library settings record is persisted in PostgreSQL and
is not recreated on ordinary restarts.

## Installable, online-only app

The web manifest and public shell assets support installation where the browser
offers it. In Edge and Chrome, use the browser's install action; on iOS, use
Safari's Add to Home Screen flow. Installation behavior varies by browser and
should not be assumed to be identical.

Marginalia remains online-only. Its service worker caches only public static
shell assets and the connection-required explanation; it never caches
authenticated library responses or queues changes. When the connection is
lost, reconnect before searching, capturing, or changing the library.

## Saved-library export

The Settings page downloads a versioned JSON export of Saved links, titles,
descriptions, notes, read/favorite state, collections, tags, and permitted
source provenance. It excludes credentials, article bodies, and source
occurrence keys. The export still contains original URLs and personal notes;
store it outside the repository and handle it as private data. Settings accepts
version-1 JSON files up to 128 MiB. The entire document is validated before any
write. New identities are imported as Saved; an identity already Saved, in the
Inbox, Dismissed, Trashed, or represented by a purge tombstone is skipped and
reported without changing existing data. Duplicate identities within one file are also
reported; retrying the same file does not create duplicate articles or source
occurrences.

## PostgreSQL backup and isolated restore

Use the local Aspire PostgreSQL container only. Keep backups outside the
repository: they contain private URLs, notes, and organization. `pg_dump` makes
a transactionally consistent database snapshot; do not restore over the active
`marginalia` database. The example restores to a new database and deliberately
does not drop it, so an existing database is never overwritten.

```powershell
$containers = @(docker ps --format '{{.Names}}' | Where-Object { $_ -like 'postgres-*' })
if ($containers.Count -ne 1) { throw 'Start Marginalia with Aspire and ensure exactly one PostgreSQL container is running.' }
$container = $containers[0]
$backupDirectory = Join-Path $env:USERPROFILE 'MarginaliaBackups'
New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
$backupId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$backupFile = Join-Path $backupDirectory "marginalia-$backupId.dump"
$containerBackup = "/tmp/marginalia-backup-$backupId.dump"
$containerRestore = "/tmp/marginalia-restore-$backupId.dump"
docker exec $container sh -c "PGPASSWORD=`"`$POSTGRES_PASSWORD`" pg_dump -U `"`$POSTGRES_USER`" -d marginalia -Fc -f $containerBackup"
if ($LASTEXITCODE -ne 0) { throw 'Database backup failed.' }
docker cp "${container}:$containerBackup" $backupFile
if ($LASTEXITCODE -ne 0) { throw 'Could not copy the backup outside the container.' }
docker exec $container rm -f $containerBackup
if ($LASTEXITCODE -ne 0) { throw 'Could not remove the temporary in-container backup copy.' }
```

For an isolated restore check, choose a new database name. `createdb` fails if
the name already exists, so the restore never overwrites an existing database:

```powershell
$restoreDatabase = 'marginalia_restore_verification'
docker exec $container sh -c "PGPASSWORD=`"`$POSTGRES_PASSWORD`" createdb -U `"`$POSTGRES_USER`" $restoreDatabase"
if ($LASTEXITCODE -ne 0) { throw 'Restore database could not be created; nothing was overwritten.' }
docker cp $backupFile "${container}:$containerRestore"
if ($LASTEXITCODE -ne 0) { throw 'Could not copy the backup into the container.' }
docker exec $container sh -c "PGPASSWORD=`"`$POSTGRES_PASSWORD`" pg_restore -U `"`$POSTGRES_USER`" --no-owner --no-acl -d $restoreDatabase $containerRestore"
if ($LASTEXITCODE -ne 0) { throw 'Restore failed; the working marginalia database was not targeted.' }
$archiveEntries = docker exec $container pg_restore --list $containerRestore
if ($LASTEXITCODE -ne 0 -or $archiveEntries.Count -lt 2) { throw 'Could not read the backup archive.' }
docker exec $container rm -f $containerRestore
if ($LASTEXITCODE -ne 0) { throw 'Could not remove the temporary in-container restore copy.' }
```

Compare table counts in the source and restored databases before treating the
restore as verified. This query compares all application tables and the EF
migration history:

```powershell
$countQuery = @'
SELECT 'ArticleCollections', count(*) FROM "ArticleCollections"
UNION ALL SELECT 'ArticleTags', count(*) FROM "ArticleTags"
UNION ALL SELECT 'Articles', count(*) FROM "Articles"
UNION ALL SELECT 'Collections', count(*) FROM "Collections"
UNION ALL SELECT 'FeedSubscriptions', count(*) FROM "FeedSubscriptions"
UNION ALL SELECT 'IdentityTombstones', count(*) FROM "IdentityTombstones"
UNION ALL SELECT 'LibrarySettings', count(*) FROM "LibrarySettings"
UNION ALL SELECT 'SourceOccurrences', count(*) FROM "SourceOccurrences"
UNION ALL SELECT 'Tags', count(*) FROM "Tags"
UNION ALL SELECT 'Migrations', count(*) FROM "__EFMigrationsHistory"
ORDER BY 1;
'@
$sourceCounts = $countQuery | docker exec -i $container sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d marginalia -At -F "|"'
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the source database.' }
$restoreCommand = 'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d ' + $restoreDatabase + ' -At -F "|"'
$restoredCounts = $countQuery | docker exec -i $container sh -c $restoreCommand
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the restored database.' }
if (($sourceCounts -join "`n") -ne ($restoredCounts -join "`n")) { throw 'Source and restored table counts differ.' }
'Table counts match.'
```

If writes occur while comparing, take another consistent backup and repeat the
isolated check. Keep or remove the verification database only after confirming
it is the one created for that check.

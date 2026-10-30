# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

One owner uses a GitHub account to gather and organize links for later reference across their devices. The app is not a shared or multi-user library. Access is planned to be restricted by a stable numeric GitHub user ID held only in external configuration.

## Product Purpose

Marginalia turns RSS links and manually pasted URLs into a private, searchable reference library. New feed entries arrive in an Inbox for individual review; only entries the owner explicitly saves join the library. Reading happens at the original website.

## Positioning

An owner-only library centered on an explicit Inbox-to-Saved workflow. It stores link metadata and the owner's organization, not article bodies or images.

## Operating Context

The owner primarily uses Marginalia at a desk during the workday and also expects responsive access on mobile while online. Individual RSS subscriptions refresh on a persisted daily schedule while the app is running, and the owner can refresh them manually. Manual capture starts by pasting a URL in the app. The supported browser/device matrix is current stable Edge and Chrome on desktop, Safari on iOS, and Edge and Chrome on Android. Authentication must remain owner-only; the numeric GitHub owner ID belongs in external configuration, not this repository.

## Capabilities and Constraints

- Articles retain their original URL, title, optional description, notes, tags, collections, read state, favorite state, and permitted source history. The original page remains the reading destination.
- Feed items enter the Inbox. Manual URL capture creates an unread Saved item. Opening a link never changes its read state.
- Inbox review is individual. Articles can be saved, dismissed, restored, or moved to recoverable trash according to their lifecycle state. Dismissed and trashed metadata is recoverable for 30 days; after expiry, metadata and provenance are purged while a minimal identity fingerprint remains for duplicate suppression.
- One article identity is retained when duplicates arrive; retained articles preserve their source occurrences, and reviewed items are not automatically resurfaced. Tag identity trims outer whitespace and compares case-insensitively while preserving the first display spelling.
- Collections are optional and many-to-many. Keyword search covers title, description, URL, notes, tags, and collections, with server-side paging.
- Feed subscriptions are individual URLs. Initial ingestion is limited to the latest 20 available entries; subsequent polling is persisted, conditional where supported, manually refreshable, and reports health. Fetching is restricted to validated public HTTP(S) destinations. Feed content is metadata-only.
- A mandatory title is required to save when metadata is unavailable; description is optional. Failures must be visible.
- The target scale is 50 feeds and 10,000 saved articles; these are design targets, not enforced caps.
- The product is online-only. It must not imply offline browsing, queued capture, or background synchronization.
- Versioned JSON export and import cover Saved metadata, organization, explicit state, and permitted provenance. Imports validate the complete document, add only new identities, and report conflicts without overwriting existing records. Documented local PostgreSQL backup and isolated restore are implemented. Email/WorkIQ automation and Azure deployment are not part of the current implementation.
- GitHub account authentication uses a server-side authorization-code flow and secure application cookie. Access is restricted to the configured stable numeric GitHub user ID, stored only in external configuration. The app requests no OAuth scopes, reads only the public numeric user ID, uses state/correlation and S256 PKCE, validates local return URLs, does not retain the provider access token, and provides local sign-out. OAuth app registration and credentials are manual owner setup after development; real sign-in remains unverified until then. No CLI token/PAT login, authentication bypass, or Entra integration is used.

## Brand Commitments

The product name is Marginalia. No approved logo, external brand assets, or brand voice have been selected. The web interface uses the confirmed Source Intake Register visual system documented in `DESIGN.md`.

## Evidence on Hand

There are no approved brand assets or real content examples. Use synthetic fixtures only in tests or demonstrations, and never present them as real library content.

## Product Principles

- Keep the owner in control of what becomes Saved and when an item is marked read.
- Preserve the original source as the place to read; retain only the metadata needed to find and organize it.
- Make duplicate handling, failures, recovery, and destructive lifecycle deadlines explicit.
- Fail closed on identity and network boundaries; do not imply privacy or offline behavior the product does not provide.

## Accessibility & Inclusion

WCAG 2.2 AA is the confirmed accessibility target. Acceptance includes keyboard operation, visible focus, labels, and usable small-screen controls. The selected browser/device matrix is current stable Edge and Chrome on desktop, Safari on iOS, and Edge and Chrome on Android. PWA installation behavior must be verified per browser rather than assumed uniform.

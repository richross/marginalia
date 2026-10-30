# Marginalia Library

The Marginalia Library context describes the saved-link library and the records that explain where each link came from.

## Articles and review

**Article**:
A reference to one original web URL and the descriptive metadata and user organization attached to it.
_Avoid_: Page archive, bookmark

**Inbox Article**:
An article discovered through a feed and awaiting an individual keep-or-dismiss decision.
_Avoid_: Saved article

**Saved Article**:
An article the owner chose to keep in the library.
_Avoid_: Read article

**Dismissed Article**:
An Inbox article the owner chose not to save, retained temporarily for recovery and duplicate recognition.
_Avoid_: Deleted article

**Trashed Article**:
A previously saved article removed from the active library but retained temporarily for recovery.
_Avoid_: Dismissed article

**Source Occurrence**:
A record that a retained article was encountered through a particular source at a particular time.
_Avoid_: Duplicate article

**Identity Fingerprint**:
A minimal identifier retained after temporary article metadata and source history have been purged, used to recognize the same URL again.
_Avoid_: Archived article

## Organization

**Collection**:
An optional named grouping to which an article may belong alongside other collections.
_Avoid_: Folder

**Tag**:
A reusable label attached to an article; outer whitespace is trimmed and matching is case-insensitive, while the first spelling remains the displayed spelling.
_Avoid_: Category

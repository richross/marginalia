---
name: Marginalia
description: A private link library presented as a source intake register.
colors:
  ink: "#172e3c"
  ink-soft: "#38525f"
  cobalt: "#174b70"
  cobalt-dark: "#123952"
  cobalt-light: "#dce8ee"
  signal-gold: "#e7b73e"
  signal-dark: "#745300"
  chalk: "#f4f2e9"
  paper: "#fffef9"
  rule: "#c6d0d1"
  rule-dark: "#869ba2"
  error: "#8b2f24"
  error-paper: "#f8e9e3"
  good: "#287354"
  pending: "#687e88"
  focus: "#e2a820"
typography:
  body:
    fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
    fontWeight: 400
    lineHeight: 1.5
  page-title:
    fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
    fontSize: "clamp(2rem, 3.7vw, 3.1rem)"
    fontWeight: 720
    lineHeight: 1.16
    letterSpacing: "-0.035em"
  section-title:
    fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
    fontSize: "1.28rem"
    fontWeight: 700
    lineHeight: 1.16
    letterSpacing: "-0.02em"
  control:
    fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
    fontWeight: 680
  field-label:
    fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
    fontSize: "0.86rem"
    fontWeight: 700
  source-label:
    fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
    fontSize: "0.75rem"
    fontWeight: 720
rounded:
  control: "0px"
components:
  button-primary:
    backgroundColor: "{colors.cobalt}"
    textColor: "#ffffff"
    typography: "{typography.control}"
    rounded: "{rounded.control}"
    padding: "0.55rem 0.9rem"
    height: "2.7rem"
  button-primary-hover:
    backgroundColor: "{colors.cobalt-dark}"
    textColor: "#ffffff"
  button-outline:
    backgroundColor: "transparent"
    textColor: "{colors.cobalt}"
    typography: "{typography.control}"
    rounded: "{rounded.control}"
    padding: "0.55rem 0.9rem"
    height: "2.7rem"
  button-quiet:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.control}"
    rounded: "{rounded.control}"
    padding: "0.55rem 0.9rem"
    height: "2.7rem"
  input-field:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "0.55rem 0.65rem"
    height: "2.75rem"
  source-label:
    backgroundColor: "{colors.cobalt}"
    textColor: "#ffffff"
    typography: "{typography.source-label}"
    rounded: "{rounded.control}"
    padding: "0.12rem 0.42rem"
    height: "1.45rem"
---

# Design System: Marginalia

## Overview

**Creative North Star: "Source Intake Register"**

Marginalia uses a daylight receiving-register language: a cobalt navigation rail, chalk work surface, ruled docket slips, and signal-gold marks. Source and date stay legible beside each link; Inbox items receive an individual Save or Dismiss decision.

The register stores link metadata and organization, not article pages or imagery. Keep article language plain and use the original site as the reading destination.

**Key Characteristics:**
- Source and publication date remain explicit on each docket.
- Ruled strips and stamped state marks organize the interface.
- Controls and panels use squared corners.

## Colors

The palette pairs cobalt wayfinding with signal-gold state accents against chalk and ink; named status and rule colors provide supporting contrast.

### Primary
- **Cobalt** (`{colors.cobalt}`): Primary actions, links, source labels, and selected states.
- **Signal-gold** (`{colors.signal-gold}`): Docket rules, status accents, and visible keyboard focus.

### Neutral
- **Chalk** (`{colors.chalk}`) and **paper** (`{colors.paper}`): Main canvas and light content surfaces.
- **Ink** (`{colors.ink}`) and **soft ink** (`{colors.ink-soft}`): Headings and supporting copy.
- **Rules** (`{colors.rule}`, `{colors.rule-dark}`): Docket boundaries and section dividers.
- **Status colors** (`{colors.good}`, `{colors.pending}`, `{colors.error}`): Feed health and errors; indicators are paired with text.

## Typography

**Display Font:** `"Segoe UI", "Helvetica Neue", Arial, sans-serif`
**Body Font:** `"Segoe UI", "Helvetica Neue", Arial, sans-serif`

**Character:** A single sans-serif family keeps navigation, metadata, controls, and article titles consistent and scan-friendly.

### Hierarchy
- **Page title** (720, `clamp(2rem, 3.7vw, 3.1rem)`, 1.16): Page heading, tightened tracking; at 620px and below it uses `clamp(1.9rem, 9vw, 2.55rem)`.
- **Section title** (700, `1.28rem`, 1.16): Register and panel headings.
- **Body** (400, inherited size, 1.5): General copy and form content.
- **Field label** (700, `0.86rem`): Form labels; control text uses weight 680.

## Layout

At wide widths, a 15.5rem sticky left rail frames a centered workspace capped at 94rem. The Inbox places the receiving register beside feed health; other pages use ruled registers and forms. At 820px and below, the rail becomes a sticky top band with horizontally scrollable navigation, the Inbox stacks register and health, and the health list uses two columns. At 620px and below, page headings and forms stack, docket actions wrap into wide targets, organization and health lists become single-column, and feed state moves below its heading. The document remains usable from 320px wide. A faint 32px horizontal rule repeats behind the chalk canvas.

## Elevation & Depth

The system is flat: borders, tonal surfaces, and rules establish grouping instead of raised cards. The only structural shadow-like treatment is the inset signal-gold marker on the active navigation item; on narrow navigation it moves to the item's bottom edge.

## Shapes

Buttons, fields, docket slips, labels, and organization marks are square-cornered (`0px` control radius). Thin horizontal rules define slips and sections. Small circular health indicators are the exception and communicate state, not decoration.

## Components

### Buttons
- **Primary:** Cobalt fill with white text; square, at least `2.7rem` high, with `0.55rem 0.9rem` padding. Hover shifts to dark cobalt.
- **Outline:** Transparent with cobalt text and border; hover fills cobalt.
- **Quiet:** Transparent with ink text and a dark rule border; hover uses light cobalt. Disabled buttons dim and do not accept actions.
- **Focus:** All keyboard-focused controls use a visible `3px` signal focus outline with `3px` offset.

### Inputs / Fields
- White-paper fill, dark-rule border, square corners, minimum `2.75rem` height, and `0.55rem 0.65rem` padding. Labels are visible; textareas resize vertically. Disabled fields use a muted surface.

### Navigation
- Desktop uses a sticky cobalt left rail with a gold-accented active item. At 820px and below it becomes a sticky top band with horizontal overflow; active indication changes from a left inset mark to a bottom inset mark.

### Docket Slip
- A paper-toned strip with dark top rule, light bottom rule, and a short signal-gold bar. Source, date, title, original link, optional description, and organization marks are grouped before the state-specific actions.
- Inbox slips offer individual **Save to library** and **Dismiss** actions; a missing title reveals the required title field and disables Save until supplied. Saved slips expose read, favorite, trash, and expandable metadata/organization editing controls. Dismissed and trashed slips expose their corresponding restore action and recovery date where available.
- The title and **Open original** link navigate to the original page. Opening it does not change read state.

### Feed Health
- Health is shown beside the Inbox register at wide widths and below it on narrow screens. Circular outlined indicators distinguish good, warning, and pending states, accompanied by status text.

### Forms and Status
- Feed, capture, search, and organization forms use visible labels and square fields. Online mutations are disabled while offline or busy; non-mutating cancel actions remain available when offline. The offline notice is a sticky signal-gold strip and states that changes are not queued; success, error, and loading messages use textual status roles.

## Do's and Don'ts

### Do:
- **Do** preserve source identity, date, and individual item actions in docket-based lists.
- **Do** retain visible keyboard focus, a skip link, labeled fields, and responsive navigation and controls; WCAG 2.2 AA is the confirmed accessibility target, not a claim of audited conformance.
- **Do** use status text alongside color indicators.

### Don't:
- **Don't** imply that Marginalia archives article bodies or images; reading stays on the original website.
- **Don't** turn Inbox review into bulk disposition; the selected direction is one explicit decision per item.
- **Don't** add unconfirmed logos, imagery, or alternate visual identities.

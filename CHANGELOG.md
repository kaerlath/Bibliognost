# Changelog

## 0.24.4 — Readable light-dismiss showcase

- Restored the solid archive-themed background and illuminated border around the floating mod showcase so its dossier remains readable over image-heavy catalogue pages.
- Kept the showcase non-modal: it has no Close button and dismisses when the user clicks anywhere outside its frame.

## 0.24.3 — Light-dismiss floating showcase

- Changed the mod showcase from a boxed modal into a borderless floating presentation over the catalogue.
- Removed the dedicated Close button and dimmed modal layer; clicking anywhere outside the showcase now dismisses it and returns directly to the unchanged listings.
- Retained the prominent title, large hero image, scrolling dossier, and all library and installation actions.

## 0.24.2 — Mod showcase overlay

- Replaced the width-changing side panel with a centered, dimmed showcase overlay, so opening a mod no longer reflows or moves the catalogue behind it.
- Reworked the showcase hierarchy around a larger title and author header, a generous unclipped hero image, and the complete dossier and actions below.
- Made the overlay scale to both narrow and wide Bibliognost windows while preserving its own scrolling content area.
- Preserved favorites, collections, gallery previews, source matching, descriptions, and provider installation controls inside the new presentation.

## 0.24.1 — Personal collections and stronger matching

- Added a dedicated in-game Library for Favorites, Recently Viewed, and user-created named collections.
- Collections can be created, deleted, populated from the active mod dossier, browsed, reopened, and edited without downloading anything.
- Strengthened cross-source confidence with listing-type agreement, shared tags, and stable preview-file fingerprints.
- Match explanations now expose the additional signals, while supplemental fingerprints remain unable to force an automatic merge on their own.

## 0.24.0 — Reliability, library, and accessibility

- Added live provider-health diagnostics with response timing, result counts, cache state, last-success time, error reporting, a forced refresh, and a credential-safe clipboard report.
- Cancels superseded searches so an older response cannot replace a newer query, while retaining the fast ten-minute result cache.
- Added a standalone XMA parser regression harness and sanitized Similar Mods fixture.
- Added persistent Favorites and a 50-entry Recently Viewed library directly to the catalogue toolbar.
- Added full-size gallery previews with previous/next navigation.
- Added configurable 85–140% interface scaling, reduced-motion behavior, and a High Contrast theme.
- Added configurable pacing and a remembered completion time for manual installed-mod update scans.
- Limits image transfers to four concurrent downloads and prunes previews older than 30 days or beyond a 512 MB cache budget.

## 0.23.14 — Responsive narrow grid

- Added Narrow, Balanced, and Showcase responsive layout presets; each continuously reflows as the window changes size.
- Extended the card-size control down to 260 pixels and made preview heights scale safely with narrow cards.
- Preserved title, author, and listing-type metadata in the responsive small-card layout.

## 0.23.13 — Accurate XMA galleries

- Excludes XMA's Similar Mods recommendation cards from the selected mod's preview gallery.
- Gallery extraction now rejects images nested in links or cards belonging to other mod IDs.

## 0.23.12 — Catalogue typography, themes, and safeguards

- Added independent Windows font selectors and size controls for card titles, authors, and listing types, with safe Dalamud-font fallback.
- Added optional stronger title emphasis and dynamic card spacing for larger typography.
- Added Archive Gold, Moonlit Azure, Amethyst Nocturne, Verdant Aether, and Crimson Manuscript interface themes.
- Applied the selected palette throughout cards, controls, borders, headings, backgrounds, and glow effects.
- Ordinary ZIP downloads are now inspected for a real Penumbra or TexTools manifest before installation; incompatible archives remain safely in Downloads with a clear explanation.
- Reopening Bibliognost now refreshes the active catalogue view instead of displaying a stale in-memory result set.
- Added an All control for selecting or clearing every mod-type filter at once.

## 0.23.10 — Reliable Heliosphere routing

- Uses Heliosphere's raw custom vanity value only when a real vanity is configured; computed UUID fallbacks are no longer treated as public routes.
- Falls back to the variant short ID for standard Heliosphere web links.
- Sends the internal package UUID directly to Heliosphere's in-game install command, fully separating command identity from browser routing.

## 0.23.9 — Heliosphere standard-route fallback

- Added Heliosphere variant short IDs to catalogue and detail queries.
- Uses the default/latest variant's public short ID when a package has no custom vanity URL, preventing UUID-based 404 links and failed in-game handoffs.

## 0.23.8 — In-game Heliosphere handoff

- Added an **Install with Heliosphere** action when the Heliosphere plugin is installed and loaded.
- Sends Heliosphere's supported install command without bypassing its confirmation or variant-selection workflow.
- Falls back to the mod's public Heliosphere page when its plugin or command handler is unavailable.

## 0.23.7 — Correct Heliosphere links

- Changed Heliosphere source links to use each mod's public vanity route rather than its internal API UUID.
- Retained internal UUIDs for GraphQL details and image requests, with a safe link fallback when no vanity route is supplied.

## 0.23.6 — Banner version label

- Added the installed Bibliognost version to the lower-right corner of each title banner.

## 0.23.5 — Complete mixed XMA results

- Corrected the XMA adult-content setting so **Show** includes ordinary and adult entries instead of accidentally requesting adult-only results.
- Kept **Hide adult content** as the only mode that restricts the provider query.

## 0.23.4 — Exact provider ordering

- Preserved XMA's own result order in the XIV Mod Archive-only view instead of re-sorting cards using partially available detail-page dates.
- Kept normalized chronological sorting for the combined All Sources timeline, where cross-provider comparison is required.
- Made a new main-field search in XMA-only mode use XMA's relevance ranking, matching the website and preventing a direct title match from being buried several update-sorted pages deep.

## 0.23.3 — Complete XMA catalogue query

- Made an unfiltered XMA search explicitly request every current website type, including gear, bodies, faces, hair, reshades, other, minions, mounts, furniture, skin, racial scaling, poses, VFX, animation, sound, Dalamud plugins, modding tools, and apps.
- Changed the initial and cleared catalogue to XMA's **Last Version Update · Descending** ordering so it matches the website's complete current-results view.

## 0.23.2 — Packaged icon metadata correction

- Added the 512×512 icon URL to the installed plugin manifest as well as the remote repository feed.
- Added the conventional `images/icon.png` publication asset so Dalamud receives a stable, extension-safe image URL.

## 0.23.1 — Plugin-list icon correction

- Resized the illuminated archive sigil to Dalamud's supported 512×512 plugin-list dimensions.

## 0.23.0 — Private testing release

- Added XMA, Heliosphere, and Final Fantasy XIV-scoped Nexus providers.
- Added merged chronological browsing, structured filters, paging, and latest-release views.
- Added conservative duplicate detection with manual match decisions and multiple source actions.
- Added encrypted Windows user-scope storage for XMA sessions and Nexus API keys.
- Added large responsive cards, animated archive styling, a full details dossier, and the bundled Charito title font.
- Added guarded download and Penumbra import workflows with explicit confirmation and history.
- Added manual installed-mod update scanning, review, ignore, and unlink controls.
- Added in-game XMA cookie help and a native download-folder picker.

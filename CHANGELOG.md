# Changelog

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

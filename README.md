# Bibliognost

![Bibliognost illuminated archive sigil](Assets/Branding/Bibliognost-Icon.png)

Bibliognost is a Dalamud plugin for browsing Final Fantasy XIV mods from inside the game. It currently supports XIV Mod Archive, Heliosphere, and Nexus Mods, with provider-aware search and details, secure optional account connections, duplicate-source grouping, disk-backed preview caching, and guarded package delivery to Penumbra.

> **Private testing release:** Bibliognost is under active development. Provider websites can change without notice, and installing third-party mods always remains a player-authorized action.

## Install from the private testing repository

1. Copy the raw URL for this repository's `repo.json` file.
2. In-game, open `/xlsettings` and add that URL under **Experimental → Custom Plugin Repositories**.
3. Open `/xlplugins`, search for **Bibliognost**, and install it.

Custom repository URL: `https://raw.githubusercontent.com/kaerlath/Bibliognost/main/repo.json`

The GitHub repository must be public for this installation method; Dalamud cannot sign in to retrieve files from a private GitHub repository.

## Local testing

1. Build with `dotnet build -c Release`.
2. In Dalamud, open `/xlsettings`, enable plugin testing/developer options, and add the Release output DLL as a development plugin.
3. Run `/bibliognost`.
4. Open **Settings** and use the guided **Sign in to XMA** flow. XMA currently authenticates through Discord and does not provide a plugin login API, so the final connection step requires copying the `connect.sid` value created by XMA in the browser.
   Bibliognost includes a native in-game **Cookie Help** window with click-by-click Edge, Chrome, and Firefox instructions for beginners. It never redirects to another plugin or an external help page.
5. To test Nexus Mods, use **Get API key** in Settings, paste a personal Nexus API key, and choose **Save & Verify**. Every Nexus endpoint is hard-scoped to the `finalfantasy14` game domain.

Plaintext credentials are never written to configuration or logs. Windows DPAPI encrypts the XMA session and Nexus API key separately for the current Windows user. Clearing either connection removes its encrypted value.
Settings intentionally never repopulates plaintext credentials into editable fields. It shows a green **Saved securely** state and a replacement placeholder whenever an encrypted credential is present.

## Notes

- XMA is scraped because no supported public catalog API is available. Selectors are centralized and defensive, but an XMA HTML redesign can require a provider update.
- XMA no longer exposes a supported identity/session-verification endpoint. Bibliognost confirms reachability, stores and attaches the cookie as Atomos does, and lets XMA remain the authority for account-restricted results.
- Adult-content settings only filter or blur results already returned by XMA; they do not bypass account permissions.
- Search supports free text plus mod name, author, gender, race, tags, affected item/clothing slot, mod types, sorting, compatibility, and adult-content policy.
- Clearing the main query immediately restores the newest catalogue. **Search** runs the current query; **Latest Releases** clears filters and shows entries originally published today across all connected providers.
- The default catalogue is provider-neutral and sorted by latest version update first: XMA Last Version Update, Heliosphere version `updatedAt`, and Nexus `updated_timestamp` are normalized into one chronological timeline. **Latest Releases** remains the explicit original-publication-date view.
- An unfiltered XMA request sends every current XMA content type explicitly. This includes gear, body, face, hair, reshade, other, minion, mount, furniture, skin, racial scaling, pose, VFX, animation, sound, Dalamud plugins, modding tools, and apps; XMA does not treat an omitted type list as equivalent to selecting everything.
- Pagination keeps numbered page history, Previous/Next navigation, and a direct page-number field for long searches.
- In All Sources mode, Bibliognost gathers the required page depth from every provider, merges and sorts the combined timeline, and only then slices the requested page. This prevents provider-local page numbers from masquerading as global chronology.
- Card size is adjustable from 480–900 pixels and the catalog automatically reflows with the window. Existing installations migrate to the larger 640-pixel default.
- The details showcase uses an animated glow response, prominent title treatment, large hero artwork, selectable preview thumbnails, compact dossier metadata, and an optional expanding description panel.
- Detail hero artwork reserves a small internal frame-safe inset so its luminous border and glow remain visible instead of being clipped by the scrollable drawer.
- The main archive now has a crisp 48-pixel display-font wordmark, animated multicolor indexing spectrum, luminous frame, and compact subtitle inspired by Encore's presentation principles while retaining Bibliognost's own identity.
- Filter types are intentionally split across two rows; dossier tags wrap to additional rows; and duplicate entries list every known provider inline under **Sources**.
- Hiding the description collapses only the description panel. The full-height dossier and its scrollable source, delivery, progress, and status controls remain available.
- Settings shares the main archive's animated spectrum masthead, dark grid background, luminous section bands, and display hierarchy for a consistent application-wide visual language.
- The large Bibliognost wordmark can use any installed Windows TrueType/OpenType font selected in Settings, with an immediate live preview and a persistent safe fallback chain.
- Charito is bundled unchanged as Bibliognost's default title font under the SIL Open Font License 1.1. Its copyright and complete license are included beside the font in `Assets/Fonts/Charito-OFL.txt`.
- Recognized `.ttmp`, `.ttmp2`, `.pmp`, and `.zip` artifacts from XMA or Nexus are downloaded to the user's Downloads folder with progress, cancellation, collision-safe names, empty-file rejection, and a 4 GB safety ceiling, then passed to Penumbra through `Penumbra.InstallMod.V5`. The button itself is the explicit install action; Bibliognost never installs from passive browsing.
- Install actions show a source/version/filename confirmation first. Bibliognost checks Penumbra's mod list and labels likely existing entries as updates, while preserving Penumbra as the final authority.
- Settings offers a custom download folder, keep/remove-after-import behavior, and a persistent 30-entry success/failure history. Failed transfers retain their selected source so the action can be retried from the dossier.
- Download location selection uses Dalamud's native folder-picker interface; players never need to type or edit a filesystem path. A reset button restores the standard Windows Downloads folder.
- Files that are not recognized Penumbra packages are downloaded only. Entries without an authorized direct artifact remain provider/source links.
- Heliosphere packages are deliberately handed to Heliosphere's official page and plugin: its current public delivery format is a version manifest plus content-addressed chunks and installer choices, not a simple archive URL that Bibliognost can safely pass to Penumbra.
- The catalogue masks clipped card-row fragments above its pager, and direct page entry uses a clean number field without spinner controls.
- Heliosphere browsing uses its public GraphQL API and does not require authentication.
- Choose **All sources**, **XIV Mod Archive**, **Heliosphere**, or **Nexus Mods** beside the search field. In combined mode, provider requests run independently; one provider failing does not discard successful results.
- Cross-site duplicate grouping is deliberately conservative: normalized titles must match and normalized authors must match or be an unambiguous contained form. All source links and delivery choices remain available. A future provider can supply stronger fingerprints without changing the UI model.
- Selecting an entry now performs a targeted cross-provider identity lookup, so copies that landed on different provider result pages can still merge. Confirmed matches update the card badge and dossier source list, and each source receives its own install, download, or provider handoff control.
- Cross-provider identity normalizes common title variations such as `M`/`Male`, `F`/`Female`, `Miqo`/`Miqo'te`, punctuation, and race plurals. Targeted lookups use distinctive title words rather than requiring one provider's complete title to appear verbatim on another.
- Alternate-source discovery independently searches by distinctive title and by creator across up to three provider result pages, then uses a weighted title/author confidence score. Low-confidence candidates stay as separate entries rather than being silently combined.
- Match confidence and shared signals are visible in the dossier. Borderline candidates can be marked **Same Mod** or **Not the Same**, and Bibliognost remembers both decisions locally.
- The catalogue includes direct Newest, Recently Updated, Popular, and Today's Releases views plus an optional compact card presentation.
- The manual Updates workspace records provider receipts after successful Bibliognost installs. Quick Scan compares linked Penumbra mods directly against current provider metadata without downloading anything; semantic versions are ordered when possible and ambiguous version/date changes are flagged for review.
- Legacy Discovery is an explicit, cancellable slow scan for older Penumbra entries without source receipts. Suggested provider matches require player confirmation before they become linked.
- Update results support Review Update, Ignore This Version, and Unlink. A later version reappears after an ignored release, and every actual update still uses the normal download validation and Penumbra confirmation flow.
- Nexus personal keys are appropriate for private development. A public distribution should register Bibliognost with Nexus Mods and replace this step with their SSO application flow.
- Glamour Dresser is not yet a provider. It should only be added after confirming an authorized, stable catalogue interface and acceptable-use rules; Bibliognost will not silently scrape it merely because pages are public.


# DocTwin

A Windows WPF application for **read-only Confluence discovery, comparison and human review**.
Developed by
Dawood Ali.

## Run

Open `DocTwin.sln` in Visual Studio and run the `DocTwin` project, or:

```powershell
dotnet run --project DocTwin\DocTwin.csproj
```

Requires Windows and .NET 10 Desktop runtime. Building requires the .NET 10 SDK:
`global.json` selects 10.0.404 or a later stable .NET 10 feature band. The
Windows GitHub Actions workflow restores and builds Release with warnings as
errors; it does not yet run a persistent test suite or enforce coverage.

## Connect and compare

1. Select **Connect to Confluence**.
2. Enter your Confluence Cloud site (`https://example.atlassian.net`) and
   Atlassian account email. Only standard `*.atlassian.net` site addresses are
   supported; do not enter a page URL in this field.
3. For a **scoped API token**, leave the scoped-token option selected and enter
   the site's Cloud ID (UUID), available from your Atlassian administrator.
   Scoped tokens use the Atlassian API gateway. Tokens created without scopes
   use the site directly; uncheck the option only for that token type.
4. Enter your token in the masked field. Confirm organisational approval and
   your authorisation to access the selected pages, then **Test connection**.
   Scoped tokens need `read:page:confluence` and `read:user:confluence`.
   The test checks the authenticated current user (REST v1, no v2 equivalent
   used here) and page read access (REST v2). No write scopes are needed.
5. Optionally select the storage-consent checkbox and **Save validated token**.
   Windows Credential Manager protects the token for this Windows account. The
   next time you open the window, the token is loaded without displaying it,
   together with your non-secret scan setup (root URL, space keys, limits, scan
   mode and both consent ticks, stored in
   `%LOCALAPPDATA%\DocTwin\scan-settings.json`), so you can go straight to
   **Discover duplicates**; the scan re-validates the connection itself.
   Saving is never automatic. Use **Remove saved connection** to
   remove it locally; revocation must be done separately in Atlassian.
6. On **Review page pair**, enter the reference/source and candidate
   page URLs, then **Fetch & compare live pages**. Both must belong to your
   configured site. Full `/wiki/spaces/.../pages/12345/...` and legacy
   `/wiki/pages/viewpage.action?pageId=12345` links work; short links do not.
7. Review the side-by-side extracted text, version metadata and change labels.
   Browser buttons open the original pages. Cancel is available during requests.

Only use tokens and content your organisation permits. Follow
[Atlassian token guidance](https://support.atlassian.com/atlassian-account/docs/manage-api-tokens-for-your-atlassian-account/).
Atlassian recommends OAuth for distributable integrations; this local,
user-configured prototype does not establish approval for distribution.

## Discover, review and optionally analyse

1. On **Discover duplicates**, expand **Scan settings**. Enter the source root
   URL, approve all accessible spaces or enter explicit external space keys,
   and select **Quick** (default) or **Deep**. Scope defaults to unapproved.
   Scoped tokens additionally need `read:hierarchical-content:confluence` for
   descendants, `read:content-details:confluence` for title search, and
   `read:space:confluence` for selected-space lookup. No write scope is needed.
2. Both modes traverse the complete source hierarchy within the source-node
   limit (default 500, maximum 1,000). If that boundary is unknown, external
   matching is blocked. Known unextractable pages are excluded from matching
   without hiding their descendants.
3. Source titles drive cross-space Confluence CQL searches: integration-style
   identifiers first, then distinctive title-word combinations. Search terms
   are generated from retrieved pages, not a hard-coded list of known matches.
   Each query retrieves at most 100 results. Quick uses at most 80 queries,
   Deep at most 200; targeted retrieval has a 2,000-unique-candidate budget.
   Search failures and caps are reported explicitly, never treated as proof
   that no related pages exist.
4. **Quick** prioritises these candidates, optionally filling from a bounded
   inventory-title window, and attempts at most 100 external body reads (or the
   lower configured external limit). It inspects at most 2,000 unique external
   metadata entries in total. **Deep** searches titles first, then traverses
   remaining inventory in API order within the configured external body-read
   budget (default 1,000; maximum 5,000). Failed/empty reads consume that budget.
   Switching modes starts a fresh scan; this build does not resume a prior run.
5. Findings appear as they are found. Settings collapse during scanning to
   preserve a usable candidate list at smaller window sizes. After completion
   or cancellation, select a row and choose **Review selected finding** for
   captured side-by-side evidence; the first result is selected automatically.
   Fetch live pages if you need to refresh their versions.
6. **Exact/lexical matches** use extracted-text identity and five-word shingles.
   **Potential functional overlap** also flags rewritten material when specific
   titles/identifiers and distinct content words corroborate a shared topic.
   This is a conservative review heuristic, not semantic equivalence or proof
   of redundancy. A shared identifier alone is insufficient. Combined source
   documents can produce separate findings for multiple older references.
   The report preserves the match basis, title overlap, true lexical score,
   limitations and source hierarchy; no similarity score designates authority.
   **Content engine:** when two pages share a title (ignoring case, word order,
   a leading "Copy of" and trailing "(2)"-style counters), a second engine
   compares the inner content block by block and reports a verdict (Identical,
   Duplicate, Near-duplicate, Contained, Partial overlap or Different content),
   content match %, word similarity %, differing numbers/IDs and a suggested next
   step. Same-title pages with different content are still listed (capped at 500
   per scan). Every column in the findings grid can be sorted by clicking its
   header; the arrow shows direction.
   **Content health:** the *Content health* tab (and the exported report) lists
   every page read from the source tree with automated suggestions: *Empty*
   (no readable text; a page with children is flagged as a possible index page),
   *Thin* (under 50 words), *Stale* (not updated for 18+ months) and *Duplicate
   group* (pages linked by exact or near-duplicate findings, with an estimate of
   redundant words if one page per group were kept). The rules are simple and
   transparent; they are prompts for an owner to review, not quality verdicts.
   **AI quality advice (whole tree):** the *AI quality advice* tab reviews the
   scanned pages for clarity, completeness, structure, actionability, possible
   staleness and redundancy. After a scan it shows a plan (page count, characters,
   provider, model and any pages that will not be sent). Tick the page types to
   review (Thin, Stale, In a duplicate group, Healthy; all ticked by default and
   remembered), then tick the single consent
   box and choose **Run AI quality review**; one request is made per page (at most
   50 pages and 600,000 characters, flagged pages first, nothing truncated) and the
   run stops after 3 failed requests in a row. Each rating comes with exact quotes
   checked against the page, a suggestion and a suggested next step (never delete).
   Only page text, title, hierarchy path, update date and version are sent - no
   tokens or owner IDs. Consent is withdrawn if the scan or AI settings change.
   Results are advisory and appear in the exported report.
7. **Prepare AI** loads a finding and opens the AI tab without sending anything.
   Alternatively fetch two live pages first. The AI tab identifies the loaded
   pair and explains missing prerequisites. Choose a provider/model and approve
   sharing explicitly using the checkbox above **Analyse selected pair with AI**.
   The consent control stays visible beside the action while provider settings
   scroll. Opening a tab alone does not load or transmit a pair. Loading another
   pair resets consent. OpenAI-compatible (`OPENAI_API_KEY`) and Claude
   (`ANTHROPIC_API_KEY`) adapters use the named environment variable; GitHub Copilot
   uses `COPILOT_GITHUB_TOKEN` when set, otherwise SDK/CLI user sign-in. Keys are
   never stored by DocTwin; see [AI provider setup](docs/ai-setup.md).
   Provider responses require structured output and checked evidence quotes.
8. Expand **Record a local human-review decision** for notes about maintained
   sources, legacy projects, owners, approvals or reasons to keep both. Save
   decisions locally and export HTML/CSV reports. These actions never change
   Confluence. A reviewer must establish which guidance should be maintained.

Quick is faster because it searches less, not because it labels titles as
proven duplicates. Neither mode guarantees discovery of every redundant page.
Invisible content, search indexing, rewritten titles, budgets and extraction
limitations all affect recall. Read coverage and warnings before using a report.

### Scan, fix, re-scan: the page cache

DocTwin keeps a local page cache so repeated scans of the same area are cheaper.
Before reusing a cached page, it checks that page's current Confluence version
(batched, up to 100 pages per request, using your token). An unchanged page is
reused; a changed page is downloaded again; a page your account can no longer
read is never served from the cache. Matching, quality findings and coverage are
always recomputed from current content, so after you fix or remove a page and
scan again, its finding disappears. The coverage note reports how many pages
were reused, refreshed or newly read.

To begin the whole analysis from scratch, press **Clear page cache** (disabled
while a scan runs). The next scan then reads every page from Confluence. The
cache holds page text, so it is encrypted for your Windows account (DPAPI) and
compressed in `%LOCALAPPDATA%\DocTwin\page-cache.bin`, capped at 6,000 pages and
250 MB; a damaged or unreadable file is ignored and rebuilt. Clearing it does
not affect settings, saved tokens or review decisions. The live **Review page
pair** comparison never uses the cache. AI quality advice is not cached and is
re-requested (with consent) on each run.

## Data protection and limitations

- **Secrets guardrail.** Pages that appear to contain passwords, keys, tokens or
  private keys are flagged **Sensitive content** (highest severity) by a local,
  offline scanner. Those pages are never sent to an AI provider: they are removed
  from the AI quality plan, refused when building any AI request, and checked again
  on the final payload before any key is read. Their values are masked in previews
  and diffs and never shown, exported or logged. The scanner is heuristic, so a
  clean result is not proof there are no secrets. See
  [docs/reading-the-results.md](docs/reading-the-results.md#sensitive-content-guardrail).
- Credentials and connection metadata are saved only in Windows Credential
  Manager, never plain-text settings or SQLite. Its 2,560-byte credential-blob
  limit applies; oversized credentials can be used without saving. Removing a
  local credential does not revoke the Atlassian token.
- Page bodies and comparison results stay in application memory unless you
  explicitly approve sending a loaded pair to an AI provider. The one exception
  is the scan page cache: page text read by Discover is also kept in an
  encrypted (Windows DPAPI, current user) local file until you press **Clear
  page cache**. Delete `page-cache.bin` or clear it if the machine is shared. AI requests
  include both texts, titles, IDs, available owner/parent metadata, hierarchy
  context and extraction limitations, never the Confluence token. No application
  content logging is enabled. Exports contain internal titles, links, evidence
  metrics and review notes; protect them accordingly. Reviews and non-secret
  AI profiles are stored in the local DocTwin application-data folder.
  Tokens necessarily exist in process memory while used. Protect your Windows
  account and machine; Credential Manager is not protection against malware
  running as you.
- Confluence requests are GET-only; redirects and cross-site page URLs are
  blocked. Authentication and authorisation failures are explicit. Polly permits
  at most three attempts for HTTP 429/502/503/504 and transport failures;
  cancellation interrupts backoff. Retry delays over 30 seconds are surfaced,
  never shortened. A per-client circuit opens for 30 seconds when at least four
  logical reads in a 30-second sample have at least 75% transient failures.
  A concurrency limiter allows two in-flight sends through response headers;
  rejected work fails explicitly. There is no fabricated-success fallback.
  Billed AI requests are never automatically retried.
- Discovery covers only accessible current pages in the approved scope and
  configured budgets, not a point-in-time site snapshot. Rovo MCP sign-in in an
  IDE is separate and is not used by this build. AI is optional and is never
  invoked automatically by discovery or exports.
- HTML is parsed, not executed. Headings, paragraphs, lists and table rows are
  extracted. Images, attachments, dynamic/include macros and comments are not
  compared; extraction limitations are shown. Formatting and table-layout
  changes can affect evidence. Review original links before deciding.
  Empty or unextractable pages are skipped with explicit scan warnings, not
  treated as duplicates or analysed non-matches. Their descendants are still
  traversed, and source-content coverage remains incomplete. Manual comparison
  and AI analysis require extractable text on both pages.
- Exact extracted-text identity uses SHA-256. DiffPlex provides an ordered
  line-level comparison; case matters and formatting whitespace is normalised.
  Changed entries count non-unchanged, non-placeholder line entries across both
  sides; total entries count all non-placeholder lines across both sides. A
  changed line can count once on each side. This is not a similarity probability,
  semantic analysis or an authority decision.
- Processing bounds: 10 MB per API response, 200,000 extracted characters,
  10,000 non-empty text blocks, and 8,000 characters in a single block per page.
  Extraction limits produce an explicit skipped-page result, not an exception
  or a truncated successful comparison. Warnings include observed sizes and
  bounds. Source descendants are still traversed; skipped pages are excluded
  from matching and cannot be sent to AI or manually compared.
  Progress and final coverage separate empty/unsupported pages, size-limit
  skips, HTTP 403 access denials, HTTP 404 missing/not-visible pages, and other
  read errors. A size-limit skip does not indicate missing Confluence access.
  Coverage remains incomplete whenever content could not be analysed.
- Confluence HTTP timeout is 45 seconds and AI HTTP timeout is two minutes;
  these limits also apply to response-body transfer after headers. Ordinary
  operations have a three-minute deadline and discovery has a 60-minute deadline.
  CPU extraction, matching and diff run off the UI thread. Cancellation retains explicitly partial scan
  results; already running CPU work may finish before cancellation. In-memory
  scans are bounded to 15 million retained characters and 10,000 findings.
- Optional AI accepts at most 160,000 combined page-text/hierarchy characters;
  larger inputs are rejected without truncation or transmission. Provider token
  limits, model access and charges still apply. Checked quotes do not establish
  that AI explanations or recommendations are correct.

## Emerald & Slate dark theme

- `DocTwin\Themes\EmeraldPalette.xaml`: semantic colors, brushes and Segoe UI font.
- `DocTwin\Themes\ModernControls.xaml`: rounded buttons, text/password inputs,
  editable and non-editable combo boxes, tabs, checkboxes, lists, DataGrids and
  narrow scrollbars. Mint hover/focus indicators and short opacity transitions
  provide feedback; selected table rows use translucent emerald.
- `DocTwin\App.xaml` merges the palette **before** the controls and retains the
  shared DocTwin vector brand mark. WPF includes these dictionaries as Page
  resources automatically; no additional NuGet package is required.

Use `Style="{StaticResource WindowStyle}"` on new windows. Common controls use
implicit styles; use `PrimaryButtonStyle`, `QuietButtonStyle` or
`SidebarButtonStyle` for explicit button roles. Extend implicit styles with
`BasedOn="{StaticResource {x:Type TextBox}}"` rather than shadowing them locally.
Use `PanelBrush`, `CanvasBrush`, `InkBrush`, `MutedBrush` and `LineBrush` instead
of hard-coded light colors. **Switch theme** replaces the semantic palette at
runtime with `LightTheme.xaml` or the dark `EmeraldPalette.xaml`; the preference
is saved locally. Primary-action text and its normal, hover and pressed colors
are checked against a 4.5:1 contrast threshold in both palettes. This check is
not a full accessibility certification. Native Windows title bars and message
boxes remain system-managed.

## Branding

`DocTwin\Assets\DocTwin.ico` contains 16-256px variants generated from the same
WPF vector logo used inside the application. It is embedded in the executable
and assigned to the windows. Regenerate after changing the vector mark with:

```powershell
powershell -STA -NoProfile -File tools\New-AppIcon.ps1
```

If a previously pinned shortcut still shows an old icon, close old instances,
run the rebuilt executable and re-pin that executable.

## Help, diagnostics and local storage

Use **Help** in the Confluence window for searchable offline guidance. Search
covers topic titles and text; Alt+S focuses Help search and Escape closes it.
Topics cover token scopes, Quick/Deep, functional overlap, coverage, AI setup,
local decisions, recovery and keyboard use. Both themes retain scrollable panes.

Serilog writes content-free JSON events under `%LOCALAPPDATA%\DocTwin\logs`,
with up to seven rolling files of approximately 5 MB each (an individual event
can exceed the roll boundary). Async operation events share a correlation ID;
failed operations display the corresponding support reference. Logs exclude
page bodies, titles, URLs, email, credentials and exception messages. Log write
failures are surfaced. Unexpected dispatcher errors stop the app explicitly
instead of continuing with potentially inconsistent state. Logs are local,
editable support evidence, not an immutable enterprise audit trail.

Non-secret profiles, appearance and reviews use bounded JSON and atomic
same-directory replacement. Exclusive write leases prevent lost acknowledged
updates; contention fails explicitly rather than blocking the UI indefinitely.
Malformed existing history is not overwritten. Reviews require positive known
page versions and at most 4,000 note characters; refresh pages if needed.

## Engineering status and documentation

This is a hardened prototype, **not production-certified**. Permanent xUnit
project registration is blocked by an unavailable native Visual Studio project
inventory capability. Session-only regression and synthetic stress checks have
passed, but do not establish repository test discovery or 90% coverage.
Signing, deployment-policy approval, a longer soak and assistive-technology
validation remain release gates. Do not treat fast synthetic timings as live
Confluence throughput guarantees.

- [AI provider setup (environment variables)](docs/ai-setup.md)
- [Reading the results: every tab, column and status](docs/reading-the-results.md)
- [Architecture and decisions](docs/architecture.md)
- [Client contracts, error catalogue and operations](docs/operations-and-contracts.md)
- [Validation evidence and release gates](docs/enterprise-readiness.md)
- [Demo script and walkthrough](docs/demo-walkthrough.md)

These are a local Confluence-ready package; nothing has been published.

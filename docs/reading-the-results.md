# Reading the DocTwin results

This guide explains every tab, column and status in the **Discover duplicates**
screen. Everything DocTwin shows is a **suggestion for a person to review**, not
a fact or an instruction. DocTwin is read-only and never edits, merges, moves,
archives or deletes Confluence pages.

The values below come directly from the analysis code, so they can be quoted
as-is in other documentation (see [Reusing this in Confluence](#reusing-this-in-confluence)).

## The five tabs at a glance

| Tab | Question it answers | Powered by |
|---|---|---|
| **Content health** | Which pages in my tree are empty, thin, stale or duplicated? | Automated rules (no AI) |
| **Candidate pairs** | Which pages look like duplicates of each other, and how strongly? | Lexical matching plus the inner-content engine (no AI) |
| **AI quality advice** | How good is the writing on each page and how could it improve? | Optional AI review, only after explicit consent |
| **Source hierarchy** | What did the scan actually traverse under my root page? | Confluence page tree |
| **Coverage warnings** | What did the scan not cover, and why should I be cautious? | Scan bookkeeping |

Every grid can be sorted by clicking a column header. Page titles are links that
open the original page in the browser.

---

## 1. Content health

One row per page in the **source tree** (the root page you supplied and all its
nested pages). The summary above the grid reads, for example:

> 90 pages assessed in the source tree. 71 look healthy; 19 have at least one suggestion.
> Empty: 2 | Thin (under 50 words): 17 | Not updated for 18+ months: 0 | In duplicate groups: 4.
> Duplicate groups: 1. If one page were kept per group, 3 pages and about 21 words (roughly 1 minute of reading per pass) would be redundant.

### Columns

| Column | Meaning |
|---|---|
| **Page** | Page title, linked to the live Confluence page. |
| **Status** | The finding label. See the status table below. Sorting by Status sorts by severity. |
| **Words** | Number of words in the extracted readable text (a word is a run of letters or digits). Images, attachments and some macros contribute no words. |
| **Last updated** | Confluence's last-updated timestamp for the current version (UTC, ISO format). |
| **Duplicate group** | `Group N (X pages)` when the page belongs to a group of exact or near-duplicate pages; blank otherwise. Pages with the same group number are copies of each other. |
| **Advice (suggestion)** | Plain-language next step for a human reviewer. |
| **Source path** | Where the page sits under your root, so you can find the owner team or area. |

Default order is most severe first, then alphabetical by title.

### Status values

A page has exactly one of these status labels. **Thin**, **Stale** and
**Duplicate group** can combine with ` + ` (for example `Thin + Stale`,
`Stale + Duplicate group`, `Thin + Stale + Duplicate group`). **Sensitive content**
can also combine with any of them, for example `Sensitive content + Thin`.

| Status | Severity | Rule | What it usually means | Suggested human action |
|---|---|---|---|---|
| **Sensitive content** | 4 (highest) | The local secret scanner found a possible password, key, token or private key in the page text. | Credentials were pasted into the page by mistake. The advice names the kind and the line, never the value. | Ask the owner to remove the secret from the page and its version history and **rotate the credential**. The page is never sent to AI. See [Sensitive content guardrail](#sensitive-content-guardrail). |
| **Empty** | 3 | The page has no readable text at all (0 words). | A placeholder, a draft nobody finished, or a page whose content is only in attachments, images or macros. If the page has child pages the advice says it may be an **index / container page**. | Ask the owner whether it is intended. Container pages: confirm and consider a short overview. Otherwise complete it or review it for removal. |
| **Thin** | 2 | Fewer than **50 words** (not empty). | Too little to be useful guidance: a stub, a heading with a sentence, a template that was never filled in. | Add purpose, owner and steps, or merge it into a related page. |
| **Duplicate group** | 2 | The page is linked to other pages by exact or near-duplicate findings. | Several copies of the same guidance exist, so readers may not know which to trust. | Confirm the canonical page, ask an owner to reconcile, or review the copies for archiving. |
| **Stale** | 1 | Last updated **18 or more months** ago (a month is 30.44 days). | The content may be out of date. Age alone does not mean it is wrong. | Ask the owner to confirm it is still accurate, or review it for archiving. |
| **Healthy** | 0 | None of the checks above fired. | No issues detected by the automated checks. | None. This is **not** a quality guarantee; the AI quality tab can look deeper. |

Notes:

- **Empty** is mutually exclusive with the other statuses because an empty page
  has nothing to measure. Empty pages also appear on the Coverage warnings tab
  because they are skipped for duplicate matching.
- Pages that could not be read or whose content could not be extracted
  completely are **not** judged healthy or unhealthy. They are reported on the
  **Coverage warnings** tab instead, so absence from this grid is not a clean
  bill of health.
- Stale is only evaluated when Confluence supplied a parseable date.
- The thresholds (50 words, 18 months, 200 words per minute for the reading-time
  estimate) are fixed in this prototype.

### Summary line explained

| Phrase | Meaning |
|---|---|
| **pages assessed** | Source-tree pages that produced a row. |
| **look healthy** | Rows with status `Healthy`. |
| **at least one suggestion** | Assessed pages minus healthy pages. |
| **Empty / Thin / Not updated for 18+ months / In duplicate groups** | Number of pages carrying that label. A page can count in several. |
| **WARNING: N pages may contain passwords or keys** | A line above the summary, shown only when the secret scanner flagged pages. |
| **Duplicate groups** | Number of distinct groups. Pages are grouped transitively: if A matches B and B matches C, all three form one group. |
| **pages ... would be redundant** | If one page were kept per group, the rest (group size minus one). |
| **about N words (roughly M minutes of reading per pass)** | Word count of the copies that would be redundant, treating the longest page in each group as the keeper; minutes assume 200 words per minute. A rough effort indicator, not a precise saving. |

### Worked reading of a few rows

- `INT010 Visa Credit Cards Inbound | Empty | 0 words`: no readable text.
  Ask the owner if it is a placeholder or only holds attachments.
- `Appendicies | Thin | 6 words`: a stub. Add purpose, owner and steps, or merge.
- `Error Scenario | Thin | 12 words`: same, with a few more words.

---

## 2. Candidate pairs

One row per pair of pages that look related. Pages come from the source tree
(left column) and from elsewhere in the approved spaces (matching page).
Select a row and press **Review selected finding** to compare the pages side by
side.

DocTwin uses two independent engines:

1. **Candidate finder** (lexical): fingerprints the text and compares overlapping
   five-word phrases (shingles), plus title and identifier overlap. It finds *possible* pairs quickly.
2. **Inner-content engine**: when the two **titles match**, the title is ignored
   and the actual content is compared block by block, by word-frequency
   similarity, and by the numbers and identifiers in the text. This decides
   whether a same-title pair is really a duplicate.

### Columns

| Column | Meaning |
|---|---|
| **Source page / Matching page** | The two pages, linked. |
| **Titles match** | `Yes` when the normalised titles are the same, otherwise `No`. |
| **Content verdict (inner content)** | The inner-content engine's conclusion. See the verdict table. `Not assessed (titles differ)` means the engine only runs for same-title pairs. |
| **Content match %** | How much of the text matches block by block (both pages combined). |
| **Word similarity %** | Word-frequency (cosine) similarity of the two pages. |
| **Differing numbers / IDs** | Numbers and identifiers found on only one page (for example a different account number or limit). `None` when they all agree. A match with differing facts is a prompt to check which page is correct. |
| **Space ID** | Confluence space of the matching page. |
| **Relationship** | Overall classification of the pair. See the relationship table. |
| **Match basis** | How the pair was found. See the match basis table. |
| **Title overlap %** | Share of the smaller title's meaningful words that appear in the other title. |
| **Lexical %** | Similarity of the five-word phrase sets. `100` for exact text. |
| **Source covered %** | Share of the source page's phrases that also appear in the candidate. |
| **Candidate-only %** | Share of the candidate's phrases that do not appear in the source. High means the candidate has extra material. |
| **Suggestion (advisory)** | Recommended next step for a person. |
| **Reason / trust limits** | The evidence behind the suggestion and its limits. |
| **Source path** | Location of the source page in your tree. |
| **Evidence / limits** | Detailed measurements and caveats. |

### Content verdict values

| Verdict | Rule (all measured on extracted text) | Suggested next step |
|---|---|---|
| **Duplicate - identical content** | Every word is the same in the same order; only case, punctuation or layout can differ. | Confirm which page is canonical, then ask an owner to review the other for archiving. |
| **Duplicate - same content** | Block match at least 95%, word similarity at least 97%, and no differing numbers or IDs. | Same as above. |
| **Near duplicate - differences to reconcile** | Block match at least 75% and word similarity at least 85%. | Ask an owner to reconcile the content and confirm the canonical page. |
| **One page is contained in the other** | At least 90% of one page's text is found in the other, which holds extra material. | Ask an owner whether the smaller page is superseded or deliberately separate. |
| **Partial overlap only** | Block match at least 40% or word similarity at least 60%. | Review whether the shared sections should live on one page; keep both if purposes differ. |
| **Not a duplicate - same title, different content** | Titles match but content does not (or one page has no comparable text). | Likely not a duplicate; keep both unless an owner says otherwise. Consider a clearer title. |
| **Not assessed (titles differ)** | The titles differ, so only the lexical finder judged the pair. | Use Relationship and the lexical columns. |

### Relationship values

| Relationship | Meaning |
|---|---|
| **Exact extracted-text duplicate** | The case-sensitive, whitespace-normalised text of both pages is identical (SHA-256 match). |
| **Possible near duplicate** | Lexical similarity is at or above your threshold. |
| **Possible partial overlap** | A large part of the smaller page appears in the other, but overall similarity is below the threshold. |
| **Potential functional overlap** | The wording differs but titles share meaningful terms or identifiers and enough content words, suggesting a shared business purpose. Not proof of duplication. |
| **Same title, content overlap** | Titles match and the inner-content engine found shared content, although lexical matching did not flag it. |
| **Same title, different content** | Titles match but the content verified as different. Listed for transparency, not as a duplicate. Same-title pairs found only through the title are capped at 500 per scan. |

### Match basis values

| Match basis | Meaning |
|---|---|
| **Identical extracted text** | Fingerprints of the full text match. |
| **5-word lexical overlap** | Found through shared five-word phrases. |
| **Title/identifier + content-word overlap; human review required** | Found through title terms/identifiers plus shared content words. |
| **Matching title + inner-content verification** | Found because titles match; the inner-content engine then judged the content. |

### Suggestion values (when the inner-content engine did not run)

| Suggestion | When |
|---|---|
| **Review as a potential duplicate** | Exact text match. |
| **Review candidate for substantial overlap** | At least 80% of the source is in the candidate and 20% or less of the candidate is extra. |
| **Compare complementary portions** | One of those two conditions holds. |
| **Review as related material** | Weaker overlap. |
| **Review shared purpose with the owners** | Potential functional overlap. |

### How a pair gets into the grid, and how it is labelled

The finder keeps a pair only if at least one of these tests passes. They are
checked in this order, and the first that applies decides the **Relationship**.

| Order | Test | Exact rule | Resulting Relationship | Resulting Match basis |
|---|---|---|---|---|
| 1 | **Exact text** | Both pages have the same SHA-256 fingerprint of their text (whitespace normalised, **case-sensitive**). | Exact extracted-text duplicate | Identical extracted text |
| 2 | **Lexical match** | At least 5 shared five-word phrases **and** either Lexical % is at or above the threshold, or at least `max(80%, threshold)` of the smaller page's phrases are shared. | Possible near duplicate (Lexical % at or above the threshold) or Possible partial overlap (matched by the 80% rule only) | 5-word lexical overlap |
| 3 | **Functional overlap** | Only when test 2 failed. At least 10 shared distinct content words (3+ letters) covering 15% or more of the smaller page's word set, **and** either (a) a shared title identifier, 2+ shared meaningful title terms and 50%+ title overlap, or (b) 3+ shared meaningful title terms and 75%+ title overlap. | Potential functional overlap | Title/identifier + content-word overlap; human review required |
| 4 | **Same title** | Only when tests 1-3 failed. The two titles normalise to the same key (see below). The inner-content engine then decides. | Same title, content overlap, or Same title, different content | Matching title + inner-content verification |

If none of the four passes, the pair is not listed at all.

Every pair whose titles match is **also** run through the inner-content engine,
whichever test found it. In that case the **Content verdict** and the
**Suggestion** come from the inner-content engine (it overrides the lexical
suggestion), while Relationship and Match basis still describe how the pair was found.

**Meaningful title terms** are words of 3+ letters or digits, lower-cased, excluding
generic words such as *the, and, for, with, page, copy, overview, guide, documentation,
reference, integration(s), interface, data, sync, adapter, inbound, outbound,
specification, design, technical, functional, draft, version*.
A **title identifier** is a code made of 2-12 letters followed by 2-8 digits,
for example `INT023`.

**Titles match = Yes** means the titles are the same after normalising: lower-case,
punctuation and word order ignored, a leading `Copy of` removed, and a trailing
`copy` or counter such as `(2)` removed. So `Copy of Onboarding Guide (2)` and
`Onboarding guide` match.

### Relationship, match basis and suggestion together

| Relationship | Typical Match basis | Typical Titles match | Content verdict shown | Suggestion comes from |
|---|---|---|---|---|
| Exact extracted-text duplicate | Identical extracted text | Yes or No | `Duplicate - identical content` if titles match, otherwise `Not assessed (titles differ)` | Content engine if titles match, otherwise "Review as a potential duplicate" |
| Possible near duplicate | 5-word lexical overlap | Yes or No | Any verdict if titles match, otherwise not assessed | Content engine if titles match, otherwise the lexical suggestion table |
| Possible partial overlap | 5-word lexical overlap | Yes or No | As above | As above |
| Potential functional overlap | Title/identifier + content-word overlap | Usually No | Any verdict if titles match, otherwise not assessed | "Review shared purpose with the owners" unless titles match |
| Same title, content overlap | Matching title + inner-content verification | Yes | Duplicate-same, Near duplicate, Contained or Partial overlap | Content engine |
| Same title, different content | Matching title + inner-content verification | Yes | `Not a duplicate - same title, different content` | Content engine: keep both, consider a clearer title |

### Heading reference (how each number is calculated)

All percentages are rounded to one decimal place and are estimates from
extracted text only (images, attachments and many macros are not compared).

| Heading | Calculation | How to read it |
|---|---|---|
| **Lexical %** | Distinct five-word phrases shared by both pages divided by distinct phrases across both pages (Jaccard similarity). `100` for exact text. | Overall similarity of the two pages. Near 100 means almost the same; 50 means about half the phrases are shared. |
| **Source covered %** | Shared phrases divided by the source page's phrases. | How much of the source page also appears in the candidate. 100 means the source is fully inside the candidate. |
| **Candidate-only %** | Candidate phrases that are not in the source divided by the candidate's phrases. | How much extra material the candidate has. Near 0 means the candidate adds nothing new. |
| **Title overlap %** | Shared meaningful title terms divided by the term count of the shorter title. | Closeness of the titles even when they are not identical. |
| **Titles match** | Normalised title keys are equal. | Yes means the inner-content engine ran for this pair. |
| **Content match %** | Block-by-block comparison of the extracted text, one line (paragraph, heading, list item or table cell) at a time: exact blocks first, then reworded blocks, weighted by length across both pages. | How much of the actual content is the same, ignoring titles. |
| **Word similarity %** | Cosine similarity of word-frequency vectors. | Whether the pages use the same vocabulary in similar proportions, even if reordered. |
| **Differing numbers / IDs** | Numbers and identifiers present on only one page. | `None` means they agree. Otherwise one page may hold a different limit, account, code or date, so check which is right. |
| **Content verdict** | Thresholds in the verdict table above. | The plain-language conclusion on the content. |
| **Space ID** | Confluence space of the matching page. | Shows when a copy lives in another space. |
| **Match basis / Relationship** | Tables above. | How the pair was found and what kind of match it is. |
| **Suggestion (advisory)** | Content engine next step when titles match, otherwise the lexical suggestion table. | A starting point for the reviewer, never an instruction. |
| **Reason / trust limits** | The measurements behind the suggestion, with the reminder that lexical similarity does not prove authority or completeness. | Read before acting. |
| **Evidence / limits** | Shared phrase counts, fingerprint note and extraction caveats. | Technical detail for auditing a finding. |

### Default ordering

Pairs are listed with exact duplicates first, then by content verdict strength
(Identical, Same, Near duplicate, Contained, Partial, Different, then not
assessed), then by higher Lexical %, then by larger shared share of the smaller
page. Click any header to re-sort.

### Reading common combinations

| What you see | Interpretation | Sensible reviewer action |
|---|---|---|
| Exact duplicate, Titles match Yes, `Duplicate - identical content` | Textbook copy. | Confirm the canonical page; review the other for archiving. |
| Possible near duplicate, `Near duplicate`, Differing numbers/IDs not `None` | Two copies that disagree on specific facts. Highest risk of conflicting instructions. | Ask an owner to reconcile; decide which values are correct. |
| `One page is contained in the other`, Candidate-only % high | The candidate is a superset of the source (or vice versa). | Ask whether the smaller page is superseded or deliberately separate. |
| Potential functional overlap, Titles match No | Different wording, likely same purpose (for example two documents for the same integration code). | Review shared purpose with both owners. |
| Same title, different content | Generic title reused for unrelated pages. | Keep both; consider clearer titles. |
| Titles match No, Possible partial overlap | Pages share sections but not enough to call a duplicate. | Consider whether the shared sections should live in one place. |

### The scan settings that influence this grid

The **Lexical threshold** (0.1 to 1.0, default 0.5) controls test 2. Lower finds
more, looser pairs; higher finds fewer, closer pairs.

The line above the grid (scan coverage) reports the scan mode, title searches and
candidates, search errors, external page body reads, source and external pages
analysed, candidate pair count, a breakdown of skipped pages (empty or
unsupported, size limits, access denied, missing or not visible, other read
errors), whether the source hierarchy, source content and external inventory were
fully covered, and page cache activity. If any "complete" flag is `False`, results are partial.

### Recording a human review decision

Select a pair, open **Record a local human-review decision**, choose a decision,
add notes (up to 4,000 characters: owner, approval evidence, audience, open
questions) and press **Save local review**. This is stored only on your machine
(page IDs, page versions, notes, timestamp) and never changes Confluence.

| Decision | Intended meaning |
|---|---|
| **NotReviewed** | Default. No human has looked at this pair yet. |
| **Investigate** | Needs a closer look before deciding. |
| **AwaitOwner** | Waiting for the page owner to respond. |
| **PreferSource** | The source page should be treated as the one to keep. |
| **PreferCandidate** | The matching page should be treated as the one to keep. |
| **KeepBoth** | The pages serve different purposes; no action. |
| **Ignore** | A false positive or not worth acting on. |

Decisions are the reviewer's record of intent; DocTwin does not act on them.
Use **Review selected finding** to compare the two pages side by side,
**Prepare AI** to load the pair for optional AI comparison, and
**Export review report** to produce an HTML or CSV report.

---

## 3. AI quality advice

Optional. It runs only after you confirm consent to send page text to your chosen AI
provider. One row per assessed page. Output must pass strict validation: every
issue needs a verbatim quote from the page, so unsupported answers are rejected
rather than shown as advice.

### Which pages are sent for review

Pages are selected from the source tree after a scan, in this order:

1. **You choose the page types.** Tick any of **Thin**, **Stale**, **In a duplicate
   group** and **Healthy** (each shows its page count). A page is included if *any*
   of its Content health statuses is ticked, so `Thin + Stale` is included if either
   box is ticked. All four are ticked by default, and your choice is remembered.
2. **Most serious first.** Within your selection, pages are ordered by Content health
   severity, then by title.
3. **Hard limits.** At most 50 pages, 600,000 characters in total and 40,000
   characters per page. Nothing is truncated: a page that does not fit is listed in
   the plan as *not sent* with the reason.
4. **No text, no send.** Empty pages, and pages with no extractable text, are never
   sent.
5. **No secrets, no send.** Pages flagged **Sensitive content** are always excluded,
   whatever you tick. See [Sensitive content guardrail](#sensitive-content-guardrail).

The plan text shows the provider, model, page count and every exclusion before you
approve. Changing the selection, model or provider withdraws your consent, so you
always approve exactly what will be sent. Each page is one request, and the review
stops early after three consecutive failures.

### Columns

| Column | Meaning |
|---|---|
| **AI rating** | `Good`: clear and usable. `NeedsRefinement`: useful but has gaps. `Poor`: hard to use as written. `Uncertain`: not enough text to judge. |
| **Suggested human review** | `NoAction`, `ImproveContent`, `OwnerReview`, `ReviewForArchiving` (a person should *consider* it; never a decision) or `Uncertain`. |
| **Summary** | One or two sentences about the page. |
| **Issues and suggestions** | Each issue has a category, a quoted passage, an explanation and a concrete suggestion. Categories: `Clarity`, `Completeness`, `Structure`, `Actionability`, `PossiblyOutdated`, `Redundancy`, `Other`. |
| **Uncertainties** | What the AI could not judge (for example missing images or macros). |
| **Not accepted because** | Why the answer was discarded (for example a quote that does not exist on the page, or a rating without any supporting issue). A row here has no usable advice. |
| **Source path** | Location in your tree. |

AI advice is a second opinion on writing quality. It is independent of the
rule-based Content health statuses, so a `Healthy` page can still be rated
`NeedsRefinement`.

### What "Not accepted" means

The AI rating column says **Not accepted** when DocTwin discarded the AI's answer
for that page. It is a safety outcome, not a verdict on the page: the page simply
has no AI advice. Nothing is lost and no content was changed. Read the **Not
accepted because** column (scroll right) for the reason.

| Reason shown | What it means | What to do |
|---|---|---|
| AI evidence did not match the actual page text | Every issue the AI gave quoted text that is not on the page (invented or paraphrased). Spacing and curly-versus-straight quote differences are ignored, and an issue with a bad quote is dropped on its own, so this only appears when none survive. | Run the review again, or try a stronger model. Very short or macro-only pages fail most often. |
| AI rated the page as needing work without any supporting issue | `NeedsRefinement` or `Poor` with no issue to back it. | Run again. |
| AI returned an unsupported or incomplete assessment / did not match the expected structure | The answer was not in the required format (unknown rating, missing fields, oversized text). | Run again or switch model. |
| Provider errors (HTTP code, "not signed in", "set the ... variable", network or TLS failure) | The provider could not answer. Nothing about the page is implied. | Fix the key, sign-in, model or network, then run again. |
| The page has no usable extracted text / exceeds the size limit | Nothing was sent for this page. | Open the page in Confluence; the content may be macro-only or very large. |

If **three pages in a row** are not accepted, the run stops early and says so,
because the provider, key or model is probably wrong. When some AI issues are left
out because their quote was not on the page, the page is still accepted and the
**Uncertainties** column says how many were left out.

---

## 4. Source hierarchy

A tree of the root page and every nested page the scan traversed. Select a node
to see its context and key data on the right. Use it to confirm the scan
covered the area you intended, and to find which branch a finding belongs to.

## 5. Coverage warnings

Plain-language list of limits for this scan: pages that could not be read
(permissions, rate limits, errors), pages skipped because text could not be
extracted completely, search or page limits reached, and Quick versus Deep
mode caveats. The coverage note also reports page cache activity
(pages reused, refreshed or newly read).

**Always read this tab before trusting a report.** "No findings" never proves
that no redundant content exists, only that none was found within the scanned
and visible content.

---

## AI analysis of one pair (the "AI analysis (optional)" tab)

Explains one pair of pages from the Candidate pairs grid. Press **Load selected
discovery finding**, check the provider and model, tick the consent box and press
**Analyse selected pair with AI**.

**What the result shows**

| Part | Meaning |
|---|---|
| Header | Provider, model and time. |
| PAGES COMPARED | Source (page A) and Candidate (page B): title, page ID, space, version, last updated and link, so you always know which pages the answer is about. |
| Relationship | `ExactDuplicate`, `NearDuplicate`, `Related`, `PotentialConflict`, `Unrelated` or `Uncertain`. |
| Suggested human review | The AI's suggested next step. It is advice, never an action. |
| Evidence | Each item has a kind (`Similarity`, `Difference`, `SourceOnly`, `CandidateOnly`, `PotentialConflict`), an explanation, and quotes from page A and page B. |
| Uncertainties | What the AI could not judge, such as macros that were not extracted. |

**How quotes are checked.** Every quote must be found in the captured page text.
Spacing, line breaks and curly-versus-straight quote marks are ignored when
matching. An evidence item whose quote cannot be found is left out and the result
says how many were dropped. If a relationship is claimed and none of its evidence
survives, the whole answer is rejected with "AI evidence did not match the actual
page text". Try again, or use a different model.

**Limits.** One pair at a time. Pages made mostly of macros, tables or code have little extractable
text, so `Uncertain` is a common and honest answer. Pairs where either page is
flagged **Sensitive content** are never sent.

---

## Sensitive content guardrail

Confluence pages sometimes hold passwords, keys or tokens that were pasted by
mistake. DocTwin highlights them and guarantees, in code, that they are never
sent to an AI provider.

**What it detects (locally, offline).** Private keys; AWS, GitHub, Atlassian, Slack,
Google and Stripe key formats; AI provider API keys; JSON web tokens; passwords
embedded in URLs or connection strings; `password:`, `secret=`, `api key:` and
`token:` followed by a value that looks real (it must contain a digit or a symbol);
and long random-looking strings next to words such as key, secret or token.
Placeholders (`<password>`, `${TOKEN}`, `********`) and plain prose ("the password
policy requires...") are ignored.

**What you see.** A **Sensitive content** status (severity 4, sorted first), advice
naming the kind and the line, and a WARNING line in the summary. The value itself is
never shown, exported or logged.

**What is enforced.**

| Layer | Behaviour |
|---|---|
| AI quality plan | Flagged pages are removed before your page-type ticks apply. They cannot be re-added. The plan lists them as "never sent to AI". |
| AI payload builders | Building a request for a page, or a pair, throws if the title, text, path or limitations contain a possible secret. Pair explanation therefore refuses if either page is flagged. |
| Provider call | The final payload is scanned again before any key is read or any request is made. The check applies to every provider, including GitHub Copilot. If the scan cannot finish, it is treated as a possible secret. |
| Local displays | The Source hierarchy preview, the side-by-side page diff and the "differing numbers/identifiers" text show `[REDACTED: kind]` instead of the value. |

**Limits - please read.**

- It is heuristic. It can miss a secret (for example a password written as plain
  prose, or one that exists only in an image, attachment or macro) and it can flag
  harmless text. A clean result is **not** proof that a page has no secrets.
- Because masked text is compared, two pages that differ only in a secret value can
  look identical in the content engine.
- The encrypted local page cache still stores the page text on this machine. It never
  leaves the machine.
- Flagging a page does not fix it. A person must remove the secret from the page
  (and its version history) and rotate the credential.

---

## Repeat scans and the page cache

- DocTwin keeps an encrypted local page cache. On each scan it checks every
  cached page's current version, reuses unchanged pages and re-reads changed
  ones. Findings are recomputed every time from current content.
- Fix a page in Confluence and scan again: the finding for that page disappears
  or changes status.
- Press **Clear page cache** to start from scratch.

## Quick versus Deep

- **Quick**: searches by source titles first and reads a limited number of
  external pages. Faster, but may miss rewritten or differently titled copies.
- **Deep**: also walks the full inventory of approved spaces up to your page limits. Slower, broader.

### Scan limits: the numbers to use

The Scan setup panel shows these ranges under the three boxes, and warns live if a value is out of range.

| Setting | Allowed | Default | Notes |
|---|---|---|---|
| Source node limit | 1-1,000 | 500 | Pages read under your root. |
| External page limit | 1-5,000 | 1,000 | Budget for reading pages outside the root. Quick mode reads at most 100 whatever you enter; Deep can use up to 5,000. |
| Lexical threshold | 0.1-1.0 | 0.5 | Lower finds more, looser matches; higher finds fewer, closer ones. |

A remembered value above the maximum is brought down to it when the app starts.

## Responsible use

- Results are suggestions, labelled as such, with the evidence shown.
- A person reviews every finding and decides what to do.
- DocTwin only reads content the account is authorised to see and only for the approved scope.
- Nothing is ever changed, merged, redirected, archived or deleted by the tool.
- Pages with possible passwords or keys are highlighted and are never sent to AI (see the guardrail section).

## Reusing this in Confluence

This document is the source for the planned product documentation in
Confluence. Reusable pointers:

1. The status vocabulary (`Sensitive content`, `Empty`, `Thin`, `Stale`, `Duplicate group`, `Healthy`) and thresholds (50 words, 18 months).
2. Severity order: Sensitive content, then Empty, then Thin and Duplicate group, then Stale, then Healthy.
3. Two-engine duplicate detection: lexical candidate finder, then inner-content verification for same-title pairs, with the verdict thresholds above.
4. AI is optional, consent-gated, and validated; its ratings are separate from the rule-based statuses.
5. Scan, fix, re-scan workflow with the page cache and the **Clear page cache** reset.
6. Read-only and human-in-the-loop guarantees under Responsible use.

When the product behaviour changes (thresholds, labels, columns), update this
file and the Confluence copy together.

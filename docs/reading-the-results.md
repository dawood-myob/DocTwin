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
`Stale + Duplicate group`, `Thin + Stale + Duplicate group`).

| Status | Severity | Rule | What it usually means | Suggested human action |
|---|---|---|---|---|
| **Empty** | 3 (highest) | The page has no readable text at all (0 words). | A placeholder, a draft nobody finished, or a page whose content is only in attachments, images or macros. If the page has child pages the advice says it may be an **index / container page**. | Ask the owner whether it is intended. Container pages: confirm and consider a short overview. Otherwise complete it or review it for removal. |
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

The **Lexical threshold** setting (0.1 to 1.0, default 0.5) controls how similar
the five-word phrase sets must be for the finder to flag a pair. A pair is also
flagged when at least 80% (or the threshold, if higher) of the smaller page is
shared, with at least five common phrases.

The **Review decision** options (Investigate, Await owner, Prefer source,
Prefer candidate, Keep both, Ignore) and notes are saved locally only. They never change Confluence.

---

## 3. AI quality advice

Optional. It runs only after you confirm consent to send page text to your chosen AI
provider. One row per assessed page. Output must pass strict validation: every
issue needs a verbatim quote from the page, so unsupported answers are rejected
rather than shown as advice.

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

## Responsible use

- Results are suggestions, labelled as such, with the evidence shown.
- A person reviews every finding and decides what to do.
- DocTwin only reads content the account is authorised to see and only for the approved scope.
- Nothing is ever changed, merged, redirected, archived or deleted by the tool.

## Reusing this in Confluence

This document is the source for the planned product documentation in
Confluence. Reusable pointers:

1. The status vocabulary (`Empty`, `Thin`, `Stale`, `Duplicate group`, `Healthy`) and thresholds (50 words, 18 months).
2. Severity order: Empty, then Thin and Duplicate group, then Stale, then Healthy.
3. Two-engine duplicate detection: lexical candidate finder, then inner-content verification for same-title pairs, with the verdict thresholds above.
4. AI is optional, consent-gated, and validated; its ratings are separate from the rule-based statuses.
5. Scan, fix, re-scan workflow with the page cache and the **Clear page cache** reset.
6. Read-only and human-in-the-loop guarantees under Responsible use.

When the product behaviour changes (thresholds, labels, columns), update this
file and the Confluence copy together.

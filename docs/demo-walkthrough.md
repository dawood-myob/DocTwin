# DocTwin demo script and walkthrough outline

Audience: knowledge owners, integration architects and support leads.
Target: 8-10 minutes. This is a script/outline, **not a recorded video**.
Use an approved synthetic Confluence dataset for recording. Blur account and
internal page information if an approved live pilot is demonstrated. Never
show API keys, token fields, Credential Manager contents or environment dumps.

## 0:00-0:45 | Executive summary and business value

Screen: branded home window, then Confluence discovery.

Narration: "When integration guidance lives in multiple spaces, teams can follow
outdated instructions. DocTwin helps owners discover related or redundant
references, compare the evidence, and record a human decision. It does not
change Confluence or decide authority for you."

Show Light/Dark switch. State that this is a hardened prototype under validation,
not a claim of production certification or guaranteed exhaustive discovery.

## 0:45-1:45 | Connection and architecture

Screen: approved test connection, never reveal the token.

Narration: "The Windows WPF client retrieves accessible content with read-only
REST calls. Credentials are optional local Windows Credential Manager entries.
Extraction, title/identifier retrieval, lexical evidence and human reviews are
separate services. Page bodies stay in memory unless a reviewer explicitly
approves an AI request. The only local copy is the encrypted scan cache, which
speeds up re-scans after pages are fixed and can be cleared with one button."

Use the architecture sequence diagram. Explain that Workday is the documentation
domain, not an implemented Workday transaction/API connector. Do not display
fabricated SQL, middleware or web-controller components.

## 1:45-3:15 | Quick discovery and rewritten references

Screen: synthetic source root and explicit approved external space, Quick mode.

Narration: "Quick traverses the source hierarchy, searches its identifiers and
distinctive titles, then prioritises a bounded external sample. Deep starts a
fresh, broader scan; it is not a resume button."

Select a synthetic integration with rewritten source and legacy titles. Show
live-arriving findings, the first-row selection, coverage and warnings.

Narration: "This Potential functional overlap finding can have low five-word
lexical similarity. Specific titles and shared content support a review signal;
a shared integration ID alone is not enough. This is not proof of redundancy."

Avoid hard-coded target injection, fake production timing or claiming every
external page was checked. Explain search indexing, permissions and limits.

## 3:15-4:30 | Review, combined guidance and local decisions

Screen: select finding -> Review selected finding -> original-page links.

Narration: "Captured versions and side-by-side extracted evidence let an owner
check audience, purpose, accuracy and authority. A newer combined guide can
cover several older references, each requiring its own decision."

Return to the local review panel. Record a synthetic owner-confirmation note
and an AwaitOwner or KeepBoth decision. Export a report to an approved location.
Show match basis and version/coverage caveats. Explain that later revisions need
re-review and no page was archived, edited or deleted.

## 4:30-6:00 | Optional AI and failure transparency

Screen: Prepare AI -> visibly loaded pair -> provider settings -> consent.

Narration: "Preparation does not send anything. AI remains disabled until a
pair is loaded, the app is idle and sharing is approved. The consent checkbox
stays beside Analyse while configuration scrolls."

Send only synthetic text to an approved provider. Show advisory relationship,
verbatim evidence and uncertainty. Switch provider or load a different pair to
show consent being revoked.

Narration: "Responses must pass schema and quotation checks. An invented quote,
refusal, timeout or provider failure produces no accepted result. API keys come
from named environment variables; Copilot uses separate CLI sign-in. Requests
are not automatically retried because they may incur charges."

If a provider is unavailable, show the real error and deterministic comparison;
do not fabricate a successful response or silently substitute another provider.

## 6:00-7:15 | Resilience, edge cases and synthetic stress

Screen: recorded terminal output from approved local synthetic checks, labelled
'SYNTHETIC - NO LIVE CONFLUENCE LOAD'. Use actual output, not staged success text.

Show the verified 503 sequence: four failed logical reads (three attempts each),
then an open-circuit rejection without another HTTP call, followed by a
successful probe after the real 30-second break. Explain Retry-After, explicit
concurrency rejection and cancellation. This is a service-level fixture, not
an unimplemented in-app outage simulator.

Show the bounded workload result described in enterprise-readiness.md:
1,600 extracts, 400 changed-line comparisons, 400 concurrent-batch synthetic
scans with exact expected findings, heap samples and scheduling heartbeat.
Explain that this is not a long-duration leak test or live capacity guarantee.

Demonstrate an empty/oversized page being explicitly skipped with descendants
retained, using synthetic data. Contrast this with a 403 permission failure.
No partial page is silently analysed as complete.

## 7:15-8:15 | Help and support journey

Screen: Help from the Confluence window. Search 'enable analysis button',
'75%' and 'token'. Show topic details, keyboard navigation and both themes.

Narration: "Offline help explains setup, scan scope, AI prerequisites, errors,
local recovery and accessibility. A failed operation supplies a support
reference linked to content-free local structured logs. Neither page text nor
credentials belong in those logs or a support ticket."

Show only a sanitised synthetic log. Do not describe logs as immutable audit
records. Explain that an unexpected dispatcher fault stops the application
rather than continuing with unknown state.

## 8:15-9:00 | Close, limitations and next release gates

Narration: "The outcome is an evidence-led review queue, not automated deletion.
Known title-led retrieval cases and all three AI adapters have been verified,
but deployment still requires organisational approval and release gates.
Permanent xUnit discovery and measured 90% coverage remain blocked by the
Visual Studio project-inventory capability. Longer soak, accessibility,
signing, auth-policy and representative owner acceptance are still required."

Call to action: approved pilot owners validate maintained versus legacy
references, agree authority and record decisions. Any Confluence publication
or retirement is a separate authorised human workflow.

## Recording checklist

- Capture the exact build/version, theme and synthetic dataset version.
- Close personal notifications and unrelated windows; check recordings for
  secrets and internal metadata before distribution.
- Verify provider availability beforehand with synthetic content; retain real
  error handling rather than promising a flawless live run.
- Use readable scaling/captions, clear keyboard focus and audible narration.
- Keep the full 30-second circuit-recovery wait in supporting evidence; the
  presentation may clearly label an edited time jump.
- Keep the readiness caveats visible in the final slide and accompanying notes.
- Obtain owner approval before publishing documentation or recording to the
  approved Confluence parent. DocTwin itself never publishes it.

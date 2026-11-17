# DocTwin validation evidence and release gates

Assessment date: 17 November 2026. **Hardened prototype, not production-certified.**
This document records measured evidence and known limits, not a guarantee of
zero regressions, complete discovery or enterprise policy compliance.

## Delivered in this hardening pass

- Source-title/identifier-led Quick/Deep discovery with separate functional
  overlap evidence, streamed findings and explicit partial-coverage reporting.
- Visible, selectable review/evidence areas at minimum supported window size.
- AI preparation without transmission; visible per-pair consent beside Analyse;
  unsaved-profile preflight; actionable provider errors; no automatic billed
  retry; strict response/schema/quotation validation.
- Bounded atomic JSON writes, cross-instance write leases, null/schema/identity
  validation, known-version review requirements, and latest-snapshot profile
  merge. Invalid history is preserved, not silently replaced.
- Polly Confluence retries, Retry-After handling, circuit breaker and outbound
  concurrency rejection. No successful-data fallback is fabricated.
- Serilog content-free local JSON logs with operation correlation, process ID,
  duration and explicit failure references; bounded retention and write-failure
  notices; unexpected dispatcher fault notification/shutdown.
- Searchable offline in-app Help, available inside the Confluence workflow,
  plus local architecture ADRs, client contracts, error/support catalogue,
  deployment/rollback runbooks, sequence diagrams and a video demo script.

## Verification evidence

| Check | Observed result | Interpretation |
| --- | --- | --- |
| Native Visual Studio build of DocTwin.csproj | Successful after changes | Loaded production project compiles; not test discovery |
| Release build, warnings as errors | 0 warnings, 0 errors | `dotnet build .\DocTwin\DocTwin.csproj -c Release --no-restore --nologo -warnaserror -v minimal` |
| Framework-dependent Release publish | Successful | Output under `DocTwin\bin\Release\net10.0-windows\publish`; not signed or deployed |
| Session-only regression verification | **262 checks passed** | Synthetic service, contracts, MVVM, actual WPF layout, persistence and help; not 262 registered xUnit tests |
| Actual rebuilt executable | Responsive main window, normal zero-code shutdown | Startup/lifecycle smoke; content-free process-correlated JSON start/exit events persisted |
| Live AI: OpenAICompatible | Valid structured NearDuplicate result, 2 evidence entries | Synthetic content only, existing local profile; no copied/printed secrets |
| Live AI: Claude | Valid structured NearDuplicate result, 2 evidence entries | Synthetic content only; provider recommendations remain advisory |
| Live AI: GitHubCopilot | Valid structured NearDuplicate result, 3 evidence entries | CLI/SDK authentication and response validation worked in this environment |
| NuGet direct/transitive vulnerability query | No vulnerable packages reported by configured sources | Point-in-time feed result, not a security certification or licence review |
| Native Test Explorer query, FullyQualifiedName=DocTwin | No matching tests | No claim of permanent test registration, execution or coverage |

### Known redundant-reference verification (read-only)

Maintained-source example: MSS page `11004641501`, plus its three descendants.
Legacy candidate example: PB page `10199434119`. Their full extracted texts
were read and compared locally, never saved in this repository or transmitted
to an AI provider.

- Source/candidate: 29,382 / 42,367 extracted characters; both complete.
- Source subtree: four readable pages, complete known boundary, zero skips.
- Smaller one-word vocabulary overlap: 37.4%; five-word lexical overlap: 0.1%.
  This is why literal overlap alone is insufficient for the primary use case.
- Automatic **Quick** scan from the Workday root: pair found without seeding the
  candidate URL; 88 source pages, 100 body attempts, 80 title queries,
  926 retrieved search candidates, zero search errors, 63 findings, 39.5 seconds.
- Automatic **Deep** scan from the maintained INT002 root with approved PB scope:
  pair found without seeding; four source pages, 100 body attempts, four title
  queries, six search candidates, zero search errors, four findings, 9.9 seconds.
- Full pair passed the local AI input preflight (82,386-character JSON envelope)
  without transmission. Live AI behavior on the company pair is **not** claimed.

These scans have different roots/scopes and are not a controlled Quick-versus-
Deep speed comparison. API indexing, visibility, content changes and budgets
can alter future outcomes. Finding the example does not validate global recall
or authorise retirement of the legacy page.

### Resilience and failure checks

Synthetic HttpMessageHandler fixtures execute the real Confluence/AI clients.
Verified: 401/403/404/400 do not retry; 429/502/503/504 attempt exactly three
reads; all retry/final response bodies are disposed; delta/date Retry-After over
30 seconds is not shortened; cancellation interrupts backoff without another
send; a third concurrent send is explicitly rejected; permits are released.

Four transient logical failures produce 12 total sends, followed by open-circuit
rejection with no send. After the actual 30-second break, a successful half-open
probe closes the circuit and subsequent reads succeed. Async diagnostic events
share one correlation ID; exception-message/token/site/email sentinels do not
appear in those events. AI HTTP errors are actionable, make one request, and
do not expose response bodies. Unsaved query-string endpoints are rejected
before transmission.

Persistence checks include null/invalid records, duplicate identities/providers,
unknown versions, held leases, malformed-history preservation, import shape and
size bounds, profile merge and 100 parallel independent-store writes. The final
record count equals acknowledged successful writes; competing writers fail
explicitly. Windows can report delete-pending lock contention as either IO or
access-denied failure; both are surfaced, not silently accepted.

WPF checks exercise real bindings, rendered rows, selection, pair preparation,
AI consent and output, Quick/Deep selection, expanded settings scrolling, local
notes reachability, both themes, searchable topic rendering and empty search.
Required AI consent and Analyse remain visible without settings scrolling at
940 x 720. Help panes remain usable at 760 x 560. This is not a full keyboard,
screen-reader, scaling or WCAG AA certification.

## Bounded synthetic stress measurements

Local synthetic data and fake services only. No live Confluence load, billed
AI traffic, credentials or user documents were used. This is a repeatable
session workload, **not** an enterprise load-test platform, a long soak or a
proof of no memory leaks/thread-pool starvation.

| Workload | Acceptance threshold | Observed sample |
| --- | --- | --- |
| 1,600 HTML extractions, 30 paragraphs/page, concurrency 8 | Every extraction complete; <= 90 s total; p95 <= 2,000 ms | 0.24 s; 6,530.7 pages/s; p95 1.48 ms |
| 400 diffs, 150 entirely changed lines per side, concurrency 8 | Exactly 300 changed/total entries per comparison; <= 90 s | 0.30 s, all expected evidence retained |
| 400 Deep scans in batches of 4; each 20 source + 200 external pages | Exactly 4,190 findings/scan; complete synthetic coverage; no skipped pages; <= 120 s | 5.23 s; 1,676,000 findings checked in total |
| Retained managed heap across ten forced-GC samples during scans | Growth <= 64 MiB from warm baseline | Maximum growth 1.20 MiB; samples approximately 2.30-2.31 MiB |
| 50 ms thread-pool heartbeat during workload | Maximum observed interval <= 2,000 ms | 249.74 ms |

Total sample duration: 5.83 seconds. These small in-memory pages deliberately
exercise repeatability/concurrency, not worst-case page sizes, real latency,
UI event backlogs over hours or server quotas. Prior regression checks also
compare two completely different 10,000-line pages without dropping evidence.
Timings depend on shared-machine load and are not release SLAs.

### Reproduction available in the current session

The temporary harness is under ignored `session-state\files\discovery-verification`.
It is not a registered/persistent test project or a clean-clone deliverable.
Do not treat it as CI infrastructure. The following commands were used locally:

```powershell
dotnet run --project .\session-state\files\discovery-verification\Verification.csproj --nologo
dotnet run --project .\session-state\files\discovery-verification\Verification.csproj --no-build -- --stress
dotnet run --project .\session-state\files\discovery-verification\Verification.csproj --no-build -- --live-ai
dotnet run --project .\session-state\files\discovery-verification\Verification.csproj --no-build -- --known-pair
dotnet list .\DocTwin\DocTwin.csproj package --vulnerable --include-transitive --no-restore
```

Live switches require approved local credentials/profile access. `--live-ai`
uses synthetic content only; `--known-pair` reads company pages and keeps their
text local. Do not run these switches unattended in CI or load-test live APIs.

## Production release gates: still open

| Gate | State / required evidence |
| --- | --- |
| Permanent xUnit + mocking/assertion-library suite | **Blocked**: native Visual Studio loaded-project inventory capability is unavailable. The required preflight cannot establish canonical loaded solution/project identity. No CLI/DTE/manual solution-registration workaround was used. Resolve tooling, register one suitable sibling test project natively, and port/expand verified behaviors into it. |
| 90%+ coverage | **Unmeasured**, not achieved/claimed. Run the registered suite with coverage and publish line/branch evidence for actual business/analysis/client services. A passing build or check count is not a coverage percentage. |
| Durable automated integration/stress deliverables and CI | **Blocked by persistent test wiring**. Session fixtures/measurements are evidence, not delivered xUnit or a clean-clone stress pipeline. Register canonical tests, retain deterministic fake-server fixtures and a repeatable longer-running load runner, then wire CI discovery. |
| Representative semantic recall/precision | Validate a labelled owner-approved dataset, including rewritten/combined references, same IDs with distinct purposes, conflicts and unrelated titles. One known pair is not a recall guarantee. |
| Longer soak / peak enterprise workload | Run approved sustained and burst workloads with real-size synthetic pages, cancellation, UI responsiveness, retention and resource evidence over an agreed duration. No live API stress without explicit provider/admin approval. |
| Accessibility | Complete keyboard-only, assistive-technology, scaling/high-contrast and contrast review; obtain WCAG AA assessment where applicable. |
| Authentication/distribution policy | Approve token-based local pilot or design approved OAuth before broad distribution. Current API-token support is not proof of Atlassian/MYOB app-policy compliance. |
| Deployment assurance | Pin/review SDK selection, signing and packaging; stage install/upgrade/rollback and backup compatibility tests; approve ownership/support and dependency licences. |
| AI governance | Approve destinations, permitted content, data policies, costs, CLI/runtime behavior and model access. Synthetic adapter success does not approve sending internal pages. |
| Owner acceptance | Confirm maintained guidance/legacy retirement decisions and report usefulness. DocTwin does not execute content changes. |

Workday API malformations, SQL deadlocks/EF integration, web exception
middleware, sagas and outboxes are **not applicable to the implemented product**.
Do not claim they were tested or add unrelated infrastructure solely to tick a
checklist. There is no production sign-off until the applicable gates above
have real evidence and owner approval.

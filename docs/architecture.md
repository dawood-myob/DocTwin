# DocTwin architecture and initial delivery

## Current state

The solution contains a .NET 10 Windows WPF application with secure Confluence
connection, recursive source discovery, Quick/Deep cross-space scans, pair
comparison, optional three-provider AI, local human-review history, HTML/CSV
exports and runtime Light/Dark themes. Credentials are tested and explicitly
saved in Windows Credential Manager. Views bind to a CommunityToolkit.Mvvm
view model; services are constructor-injected at the desktop composition root.

`ConfluenceDiscovery` implements hierarchy/inventory retrieval and the optional
`IConfluenceCandidateSearch` capability using GET-only CQL title search. Both
scan modes prioritise source identifiers and distinctive title terms before
inventory sampling. Scope restrictions are included in each CQL query; cursor
pagination retains the original filters and never forwards arbitrary links.
Query/body budgets and search failures remain explicit coverage evidence.

`DuplicateDiscovery` indexes five-word shingles and meaningful title terms.
Exact/lexical findings are distinct from potential functional-overlap findings:
the latter require corroborating title/identifier and content-word evidence,
not title similarity alone. They flag rewritten/combined references for human
review without claiming semantic equivalence or authority. Findings stream to
the UI, and bounded scrollable settings protect the evidence/results viewport.

Page bodies stay in memory unless separate AI consent is granted. Loading a
new pair resets consent. AI input and outputs are bounded, structured evidence
quotes are validated, and Confluence credentials are never included. No service
writes to Confluence. See the README for workflow, scope requirements, retrieval
heuristics, safety limits, persistence and privacy details.

## Product boundary

DocTwin is an evidence-first, read-only assistant. A user selects an approved
source tree and an external search scope; the application compares accessible
pages and presents links, evidence, limitations and advisory recommendations.
A person decides which reference is authoritative and records the outcome.
No Confluence write, archival, deletion, or permission-changing capability is
in scope. AI is optional and requires separate organisational approval.

## Implemented hardening and decisions

- **ADR-001: read-only desktop boundary (accepted).** Keep one WPF deployment
  and constructor-injected service contracts. Analysis, Confluence, AI,
  persistence, diagnostics and presentation are separated by namespaces, not
  empty tier projects. A full multi-assembly Clean Architecture migration is
  deferred until justified. No Workday execution API, SQL/EF database, web
  controller, saga or transactional outbox exists in this product.
- **ADR-002: title-led retrieval plus evidence (accepted).** Search source
  identifiers/distinctive titles before inventory fallback. Keep functional
  overlap separate from literal duplication and require content corroboration.
  Known redundant examples are verification cases, never seeded candidates.
- **ADR-003: bounded Confluence resilience (accepted).** Polly applies a
  two-send concurrency limit, a 30-second circuit break after at least four
  logical reads with a 75% transient failure ratio in 30 seconds, and at most
  two retries for 429/502/503/504 and transport failures. The circuit wraps
  retries so it measures logical reads. Retry-After over 30 seconds stops the
  operation. Limiting covers sends through response headers, not subsequent
  JSON parsing. Response-body transfer has its own configured HTTP timeout;
  caller cancellation is preserved. No fallback fabricates data or marks partial
  coverage complete.
- **ADR-004: consent-isolated AI (accepted).** Provider settings and the loaded
  pair define the sharing boundary. Changing either revokes consent. The
  consent checkbox remains beside the action. Validate profiles before sending
  even if unsaved; validate schema and exact quotations after receiving. Do
  not automatically retry potentially billed AI POSTs or weaken validation.
- **ADR-005: local atomic JSON (accepted).** Bound reads and writes, flush a
  uniquely named temporary file, replace on the same volume and hold an
  exclusive lease across read-modify-write. Fail contention explicitly. Keep
  invalid history intact and merge single-provider changes with the latest
  snapshot. This is not a distributed database or guaranteed power-loss audit.
- **ADR-006: privacy-minimising diagnostics (accepted).** Serilog JSON logs
  contain operation names, outcome, duration, retry status and async correlation
  IDs, never request bodies, titles, URLs, email, tokens or exception messages.
  Surface logging failures. Fatal dispatcher errors notify and shut down;
  domain-unhandled errors are logged without pretending recovery succeeded.
- **ADR-007: offline help and honest readiness (accepted).** Searchable topics
  require no network or new AI processing. Keep release gates explicit: session
  checks do not replace persistent tests, measured coverage or user acceptance.

Installed packages include Polly 8.9.1, Polly.RateLimiting 8.9.1, Serilog 4.4.0
and Serilog.Sinks.File 7.0.0, in addition to the original four dependencies.
No EF, hosting, mocking or persistent test packages were installed.

See [client contracts and operations](operations-and-contracts.md),
[validation evidence](enterprise-readiness.md) and
[demo walkthrough](demo-walkthrough.md). The sections below preserve the initial
proposal and are not an installed-component inventory.

## Historical proposed structure (not the current delivery inventory)

- **Desktop:** WPF views, MVVM navigation, presentation and accessibility.
- **Core:** domain models and service contracts, independent of WPF and HTTP.
- **Infrastructure:** Confluence read client, credential storage, local
  persistence and connection configuration.
- **Analysis:** extraction, normalisation, candidate retrieval, matching and
  evidence, independent of the desktop and provider implementations.
- **Tests:** initially one xUnit project, outside the desktop project's default
  file globs; separate projects can be introduced when justified.
- **AI and reporting:** introduce separate modules only when their features are
  implemented, rather than adding empty projects in advance.

Use dependency injection at the desktop composition root. Keep credentials and
Confluence response bodies out of logs. Scan progress and errors should make
partial coverage explicit; every long-running operation must support
cancellation. Scope should default to a limited, approved selection, and page
visibility is limited to the authenticated identity.

## Initial dependency assessment

As of 17 November 2026, NuGet package metadata identifies these stable versions
and declared licences. Versions are candidates, not installed or locked; check
package vulnerability advisories, transitive licences and compatibility again
before adding them. Framework compatibility is not a security or maintenance
endorsement.

| Package | Candidate | Licence | Decision |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM; NuGet lists compatibility through .NET 8 and computed compatibility with .NET 10. Verify by building. |
| Microsoft.Extensions.Hosting | 10.0.15 | MIT | Host and dependency injection; align with the .NET 10 line. |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.15 | MIT | Local metadata and review history; .NET 10 target. Review native SQLite dependencies too. |
| xunit | 2.9.3 | Apache-2.0 | Candidate test framework; verify adapter, test runner and framework compatibility together before scaffolding. |
| AngleSharp | 1.10.4 | MIT | Candidate HTML parser when extraction is implemented. |
| DiffPlex | 1.9.0 | Apache-2.0 | Candidate text comparison library for a later phase. |
| FuzzySharp | 2.0.2 | MIT | Candidate only; evaluate maintenance and suitability against the labelled dataset. |
| Serilog | 4.4.0 | Apache-2.0 | Candidate structured logger; select compatible hosting integration and sink only when needed. |
| Microsoft.Extensions.AI | 10.11.0 | MIT | Defer until an approved AI provider and actual abstraction requirements are known. |

Do not use the stable Lucene.Net 3.0.3 package as the default choice: its NuGet
page describes it as targeting .NET Framework. Its newer 4.8 line is described
as under development. Evaluate maturity, licensing and .NET 10 compatibility
against SQLite full-text search or another maintained alternative before
choosing an index. Evaluate any embedding runtime and model separately,
including model licence, distribution and privacy implications.

Sources: [NuGet](https://www.nuget.org/),
[Lucene.Net package](https://www.nuget.org/packages/Lucene.Net/), and the
packages' NuGet metadata. CommunityToolkit.Mvvm 8.4.2, AngleSharp 1.10.4 and
DiffPlex 1.9.0 are explicitly referenced by the desktop project. Serilog 4.4.0
was subsequently installed for local diagnostics; see the installed-package
summary above. EF, hosting, fuzzy search and the other proposed packages
remain uninstalled candidates.

## Authentication and distribution

Atlassian documents email and API token Basic authentication for direct API
calls, but recommends more secure methods for apps. Its current guidance also
states that apps collecting API tokens do not comply with its cloud-app security
requirements. The requested local prototype now supports an explicitly
user-configured token; the UI requires confirmation that the organisation
permits its use. This is not evidence of policy compliance or approval for
distribution. Confirm the deployment model with MYOB and Atlassian before
rolling it out. Atlassian recommends a single distributable OAuth 2.0 (3LO) app
for integrations. OAuth registration, approved scopes, desktop flow and secure
storage need their own design review; do not embed a client secret in a desktop
application.

Scoped API tokens use `api.atlassian.com/ex/confluence/{cloudId}` according to
Atlassian's account documentation; do not assume that every token type works
against the site URL directly. If an approved token flow is adopted, keep the
secret out of source, configuration, SQLite and logs; save it only after a
successful test and explicit consent using an appropriate Windows credential
facility. Never ask a user to send a token in chat.

References: [Atlassian Basic authentication](https://developer.atlassian.com/cloud/confluence/basic-auth-for-rest-apis/),
[API token management](https://support.atlassian.com/atlassian-account/docs/manage-api-tokens-for-your-atlassian-account/),
[OAuth 2.0 3LO](https://developer.atlassian.com/cloud/confluence/oauth-2-3lo-apps/),
[Confluence REST v2](https://developer.atlassian.com/cloud/confluence/rest/v2/intro/).

## Rovo MCP option

Investigate Atlassian Rovo MCP as a possible read-only content provider before
committing to direct REST authentication. The hosted MCP server supports
OAuth 2.1, Confluence content retrieval and search subject to user permissions
and administrator controls. It also exposes write and destructive capabilities;
DocTwin must never invoke those capabilities or expose arbitrary tool execution.
Request only appropriate read/search permission groups, enforce an explicit
client-side allowlist, and test that write and destructive tools cannot be
called. This does not replace server-side permissions or organisational policy.

A proof of concept must establish whether the authorised tools provide complete
source-tree traversal, pagination, content fidelity, metadata and incremental
indexing at acceptable cost and limits. If not, consider a documented hybrid
with direct REST for the missing operations. Some search/context tools consume
Rovo credits. Keep comparisons local unless an AI provider and data use have
been explicitly approved. The official MCP C# SDK is a candidate, pending
compatibility, licence, OAuth-flow and security review. No MCP connection has
been configured or tested.

References: [Rovo MCP overview](https://developer.atlassian.com/cloud/rovo-mcp/),
[supported tools](https://developer.atlassian.com/cloud/rovo-mcp/guides/supported-tools/),
[organisation controls](https://support.atlassian.com/security-and-access-policies/docs/understand-atlassian-mcp-server/).

## Phase 1 implementation plan

1. Confirm Visual Studio's loaded project inventory and register one xUnit test
   project in the open solution; confirm both loaded and saved membership.
2. Add the smallest needed production projects and references. Preserve the
   existing branded WPF screens and developed-by attribution.
3. Add verified .NET 10-compatible MVVM and hosting packages, compose services
   with dependency injection, and migrate navigation to view models.
4. Add local persistence for non-secret metadata and the first tested SQLite
   migration. Do not persist page bodies or tokens by default.
5. Add clearly labelled synthetic/demo data, isolated from live data, and
   initial tests. No fabricated scan metrics should appear as real results.
6. Configure structured logging without sensitive values; document setup and
   build, and run the solution build and tests.

The Visual Studio project-inventory tool was unavailable during initial
inspection, so test-project creation and loaded-solution verification have not
been performed. Resolve that tooling limitation before changing project wiring.
The project targets `net10.0-windows`; root `global.json` now selects SDK
10.0.404 or a later stable .NET 10 feature band instead of .NET 11.

## Subsequent phases and risks

Proceed from secure connection and permission-aware source-tree extraction to
external discovery, deterministic matching, evidence and comparison, optional
approved AI, human review and reporting. Use cursor pagination, bounded
concurrency, rate-limit handling and explicit incomplete-scan reporting. Test
against synthetic labelled data and mock HTTP first. Similarity does not prove
a duplicate or determine authority; section-aware evidence and human decisions
are essential, especially for pages that are related but serve different
purposes. Protect cached content and provide retention controls before storing
sensitive material locally.

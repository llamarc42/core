# CORE-02 implementation coverage

This is Phase 2 implementation evidence for [CORE-02](https://github.com/llamarc42/core/issues/2).
It does not establish maintainer acceptance or complete Phase 3 VAL-01.

Authority: accepted Documentation Contract v0.1, Deterministic Validation Contract
v0.1 (particularly sections 4–12), Standalone Initialization Contract v0.1 section
7.1, ADR-005, and ADR-006, reviewed at docs/main
`4d4591f300aa0fe57492bc0d11c1a853c4bad944`.

## Per-code matrix

Each row is an individual theory invocation of
[EveryCodeHasEmittingAndNearbyValidEvidence](../tests/Llamarc42.Core.Tests/Validation/ContractCoverageTests.cs).
The fixture first validates a complete valid project (including specifications,
reciprocal supersession, reciprocal amendment, navigation, and iteration records),
then changes the relevant condition and asserts the exact code and aggregate outcome.
It repeats validation and compares serialized structured values, then checks CRLF
equivalence. V101–V104 use the same production path-rule method with synthetic
locators so prohibited/colliding names remain testable on every host; their valid
control uses a portable, unique locator.

| Code / fixture argument | Changed condition | Severity | Phase |
| --- | --- | --- | --- |
| LC42V001 | No valid root | Error | 1 |
| LC42V002 | Invalid marker type | Error | 1 |
| LC42V003 | Missing declaration | Error | 1 |
| LC42V004 | Duplicate declarations | Error | 1 |
| LC42V005 | Malformed declaration | Error | 1 |
| LC42V006 | Unsupported declaration | Error | 1 |
| LC42V007 | Missing ATX H1 | Error | 1 |
| LC42V008 | Missing authored body | Error | 1 |
| LC42V009 | Initialization placeholder | Error | 1 |
| LC42V100 | Invalid UTF-8 | Error | 2 |
| LC42V101 | Prohibited path characters | Error | 2 |
| LC42V102 | Trailing period/space | Error | 2 |
| LC42V103 | Reserved filename stem | Error | 2 |
| LC42V104 | NFC/default-fold collision | Error | 2 |
| LC42V105 | Invalid local locator | Error | 4 |
| LC42V106 | Missing/non-unique exact target | Error | 4 |
| LC42V107 | Excluded link/nested boundary | Error | 4 |
| LC42V109 | Iteration component grammar | Error | 2 |
| LC42V150 | Duplicate specification status | Error | 3 |
| LC42V151 | Invalid specification status | Error | 3 |
| LC42V200 | ADR filename grammar | Error | 2 |
| LC42V201 | Duplicate decision identifier | Error | 2 |
| LC42V202 | ADR title identifier | Error | 3 |
| LC42V203 | Missing/empty ADR status | Error | 3 |
| LC42V204 | Ambiguous ADR status | Error | 3 |
| LC42V205 | Invalid ADR status | Error | 3 |
| LC42V206 | Context structure | Error | 3 |
| LC42V207 | Decision structure | Error | 3 |
| LC42V208 | Consequences structure | Error | 3 |
| LC42V210 | Duplicate relationship heading | Error | 5 |
| LC42V211 | Non-decision/external relationship | Error | 5 |
| LC42V212 | Self relationship | Error | 5 |
| LC42V213 | Duplicate target | Error | 5 |
| LC42V214 | Successor status/reciprocity | Error | 5 |
| LC42V215 | Predecessor status/reciprocity | Error | 5 |
| LC42V216 | Supersession chain | Error | 5 |
| LC42V217 | Amendment status/reciprocity | Error | 5 |
| LC42V218 | Amendment scope | Error | 5 |
| LC42V300 | Iteration placement | Warning | 6 |
| LC42V301 | Gregorian date | Error | 2 |
| LC42V302 | Directory/filename year mismatch | Warning | 6 |
| LC42V400 | AGENTS navigation | Warning | 6 |

All four tool codes have individual theory invocations of
`EveryToolErrorHasControlledFailureAndSuccessfulControl` in the same file:

| Code | Controlled failure | Successful control |
| --- | --- | --- |
| LC42T001 | Missing supplied entry | Existing valid project |
| LC42T002 | Inspector throws IOException at root marker | Normal discovery |
| LC42T003 | Read opens file, reads a byte, then throws IOException | Complete read |
| LC42T999 | Discovery delegate throws InvalidOperationException | Normal pipeline |

## Cross-cutting evidence

- [ValidationBoundaryTests](../tests/Llamarc42.Core.Tests/Validation/ValidationBoundaryTests.cs):
  declaration/header exclusions, authored-body exclusions, strict percent decoding,
  whole-document reference definitions, supporting assets, NFC references, no remote
  fetching, before/after filesystem snapshots, directory links/junctions, linked
  starts/descendants, non-authoritative intended relationships, invalid encoding,
  access denial, partial inventory, and full Unicode case-fold examples.
- [ValidationEvidenceTests](../tests/Llamarc42.Core.Tests/Validation/ValidationEvidenceTests.cs):
  exact one-phase-per-code table; mixed phases on the same source; distinct synthetic
  NFC/case-fold colliding sources; independent historical/navigation/guidance/spec/ADR
  authority; duplicate IDs; source tie-breaking; suppression; path-independent meaning.
- [ValidationResultTests](../tests/Llamarc42.Core.Tests/Validation/ValidationResultTests.cs):
  aggregate outcomes, warning severity, all diagnostic ordering keys, source evidence,
  and inventory snapshots/grouping.
- [DocumentationValidatorTests](../tests/Llamarc42.Core.Tests/Validation/DocumentationValidatorTests.cs):
  minimum project, optional artifacts, BOM/line endings, Unicode folds, links,
  ADR metadata, iteration/navigation rules, and controlled read/internal failures.
- Existing [ProjectDiscoveryTests](../tests/Llamarc42.Core.Tests/Discovery/ProjectDiscoveryTests.cs)
  remain the shared discovery evidence for FIFO/socket classification, links, exact
  markers, nested roots, Unicode code-point ordering, and native inspection exceptions.

## Execution and limits

Automated-review regressions in `ValidationBoundaryTests` cover fences inside actual
HTML comments, literal comment delimiters inside inline/fenced code, known year
mismatches despite malformed iteration topics, invalid ADR identifiers suppressing
dependent authority edges without changing source authority, and historical
amendments on Superseded ADRs. Relationship resolution and reverse adjacency are
cached once per validation to avoid re-parsing relationships at each chain hop.

Run the commands in [README](../README.md). CI runs formatting verification, Release
build with warnings as errors and recommended analysis, and the entire test suite
on Ubuntu, macOS, and Windows. PR evidence records the exact run/HEAD and totals.

Synthetic enumeration is used where host filesystems cannot retain distinct
NFC/case-fold locators or represent prohibited names. Native FIFO/socket tests run
only on Unix; their early return on Windows does not prove Windows special-file
execution. Junction tests execute on Windows and directory-symlink tests on Unix.
Controlled error delegates avoid permission assumptions and timing races.
Fatal runtime termination (for example exhausted process memory) is not a practical
controlled T999 fixture. Ordinary unexpected exceptions are covered.

No accepted contract or ADR is changed. The result is a Core object model; CLI
presentation, JSON wire format, exit codes, initialization, and semantic review
remain outside this issue.

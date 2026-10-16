# llamarc42 Core

Reusable C# behavior for the llamarc42 documentation framework.

Core provides deterministic Documentation Contract v0.1 discovery, inventory,
and structural validation. Validation reads local files, evaluates the finite
accepted rules, and returns ordered diagnostics, per-source authority, and
`Valid`, `Invalid`, or `Error` without modifying the project or fetching URLs.

```csharp
using Llamarc42.Core.Validation;

var result = DocumentationValidator.Validate("path/to/project/or/document.md");
// Consumers branch on Outcome, diagnostic Code/Severity, and ToolError.Code.
```

`ProjectDiscovery.Discover` remains available for callers that need only the
structural inventory. `AGENTS.md`, ADRs, and iteration logs are optional.

## Build and test

```text
dotnet restore Llamarc42.Core.slnx
dotnet format Llamarc42.Core.slnx --verify-no-changes --no-restore --severity info
dotnet build Llamarc42.Core.slnx --configuration Release --no-restore
dotnet test Llamarc42.Core.slnx --configuration Release --no-build
```

The test fixtures are Phase 2 implementation evidence for CORE-01 and CORE-02.
See the [CORE-02 coverage matrix](docs/core-02-coverage.md) for per-code evidence,
suppression, ordering, failure, Unicode, and platform checks. These fixtures do
not complete the independent Phase 3 VAL-01 work.

Markdig 0.45.0 supplies CommonMark 0.31.2 parsing with precise source positions.
Unicode 17 full default case-fold mappings are embedded as licensed data;
runtime validation needs no downloads. See [dependency notes](docs/dependencies.md).

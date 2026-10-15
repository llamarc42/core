# llamarc42 Core

Reusable C# behavior for the llamarc42 documentation framework.

The initial implementation provides deterministic Documentation Contract v0.1
project discovery and structural document inventory. It is local and read-only:
it does not validate document content, mutate files, access the network, or
depend on `AGENTS.md`, repository metadata, models, or agents.

## Build and test

```text
dotnet restore Llamarc42.Core.slnx
dotnet build Llamarc42.Core.slnx --configuration Release --no-restore
dotnet test Llamarc42.Core.slnx --configuration Release --no-build
```

The test fixtures are Phase 2 implementation evidence for CORE-01. They are not
the independent conformance fixtures owned by Phase 3 VAL-01.

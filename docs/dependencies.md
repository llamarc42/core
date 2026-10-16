# CORE-02 dependency notes

## Markdown

Core pins [Markdig 0.45.0](https://www.nuget.org/packages/Markdig/0.45.0),
BSD-2-Clause, for its CommonMark 0.31.2 parser and source-positioned syntax tree.
The [upstream project](https://github.com/xoofx/markdig) documents its CommonMark
test coverage. Core uses the default CommonMark grammar with precise source
locations, without optional syntax extensions or rendering. Whole-document
reference definitions remain available when extracting relationship sections.

Core's tests exercise the contract's use of headings, links, reference definitions,
code blocks, lists, blockquotes, HTML comments, and excluded image/HTML links.
The framework's declaration scanner and header fields are narrower than general
Markdown and are evaluated on source lines with parsed context checks.

## Unicode

[CaseFolding-17.0.0.txt](../src/Llamarc42.Core/Validation/Unicode/CaseFolding-17.0.0.txt)
is the official Unicode 17.0.0 table, dated 2025-07-30, obtained from
[Unicode](https://www.unicode.org/Public/17.0.0/ucd/CaseFolding.txt).
The original table and [Unicode license](../src/Llamarc42.Core/Validation/Unicode/LICENSE.txt)
are retained. The table's UTF-8/LF SHA-256 is
`ff8d8fefbf123574205085d6714c36149eb946d717a0c585c27f0f4ef58c4183`.

Core loads C and F mappings (full default case folding), ignores S and T mappings,
and leaves unlisted code points unchanged. NFC is applied to document identities
before folding. Folding is used for collision detection and mismatch explanation,
never target selection. The version is an implementation data choice; updates
require review because newly assigned Unicode characters can acquire mappings.
NFC normalization uses the .NET runtime. The CI matrix exercises representable
Unicode fixtures on Windows, macOS, and Linux.

Both the parser and case-fold mappings operate offline. No validation path
fetches a URL, invokes a model, or accesses repository metadata. No ICU runtime
dependency is included.

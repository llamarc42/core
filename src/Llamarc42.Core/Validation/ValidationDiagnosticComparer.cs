using Llamarc42.Core.Discovery;

namespace Llamarc42.Core.Validation;

internal sealed class ValidationDiagnosticComparer : IComparer<ValidationDiagnostic>
{
    internal static ValidationDiagnosticComparer Instance { get; } = new();

    public int Compare(ValidationDiagnostic? x, ValidationDiagnostic? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var comparison = ValidationDiagnosticCatalog.GetPhase(x.Code)
            .CompareTo(ValidationDiagnosticCatalog.GetPhase(y.Code));
        comparison = comparison != 0 ? comparison : CompareNullableText(x.Path, y.Path);
        comparison = comparison != 0 ? comparison : CompareNullableText(x.SourcePath, y.SourcePath);
        comparison = comparison != 0 ? comparison : CompareLines(x.Line, y.Line);
        comparison = comparison != 0 ? comparison : ((int)x.Code).CompareTo((int)y.Code);
        comparison = comparison != 0
            ? comparison
            : CompareSequences(x.RelatedSourcePaths, y.RelatedSourcePaths);
        comparison = comparison != 0
            ? comparison
            : CompareNullableText(x.NormalizedDestination, y.NormalizedDestination);
        return comparison != 0
            ? comparison
            : CompareNullableText(x.OriginalDestination, y.OriginalDestination);
    }

    private static int CompareNullableText(string? x, string? y)
    {
        if (x is null)
        {
            return y is null ? 0 : -1;
        }

        return y is null ? 1 : UnicodeCodePointComparer.Instance.Compare(x, y);
    }

    private static int CompareLines(int? x, int? y)
    {
        if (x.HasValue)
        {
            return y.HasValue ? x.Value.CompareTo(y.Value) : -1;
        }

        return y.HasValue ? 1 : 0;
    }

    private static int CompareSequences(IReadOnlyList<string> x, IReadOnlyList<string> y)
    {
        var commonLength = Math.Min(x.Count, y.Count);
        for (var index = 0; index < commonLength; index++)
        {
            var comparison = UnicodeCodePointComparer.Instance.Compare(x[index], y[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return x.Count.CompareTo(y.Count);
    }
}

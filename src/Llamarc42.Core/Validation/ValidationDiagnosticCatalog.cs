namespace Llamarc42.Core.Validation;

internal static class ValidationDiagnosticCatalog
{
    internal static int GetPhase(ValidationDiagnosticCode code) => code switch
    {
        >= ValidationDiagnosticCode.LC42V001 and <= ValidationDiagnosticCode.LC42V009 => 1,
        ValidationDiagnosticCode.LC42V100 or
        ValidationDiagnosticCode.LC42V101 or
        ValidationDiagnosticCode.LC42V102 or
        ValidationDiagnosticCode.LC42V103 or
        ValidationDiagnosticCode.LC42V104 or
        ValidationDiagnosticCode.LC42V109 or
        ValidationDiagnosticCode.LC42V200 or
        ValidationDiagnosticCode.LC42V201 or
        ValidationDiagnosticCode.LC42V301 => 2,
        ValidationDiagnosticCode.LC42V150 or
        ValidationDiagnosticCode.LC42V151 or
        (>= ValidationDiagnosticCode.LC42V202 and <= ValidationDiagnosticCode.LC42V208) => 3,
        >= ValidationDiagnosticCode.LC42V105 and <= ValidationDiagnosticCode.LC42V107 => 4,
        >= ValidationDiagnosticCode.LC42V210 and <= ValidationDiagnosticCode.LC42V218 => 5,
        ValidationDiagnosticCode.LC42V300 or
        ValidationDiagnosticCode.LC42V302 or
        ValidationDiagnosticCode.LC42V400 => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown validation diagnostic code."),
    };

    internal static ValidationSeverity GetSeverity(ValidationDiagnosticCode code)
    {
        _ = GetPhase(code);
        return code is ValidationDiagnosticCode.LC42V300 or
            ValidationDiagnosticCode.LC42V302 or
            ValidationDiagnosticCode.LC42V400
            ? ValidationSeverity.Warning
            : ValidationSeverity.Error;
    }
}

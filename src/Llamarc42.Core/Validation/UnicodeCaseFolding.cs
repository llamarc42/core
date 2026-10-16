using System.Globalization;
using System.Text;

namespace Llamarc42.Core.Validation;

internal static class UnicodeCaseFolding
{
    private static readonly Dictionary<int, string> Mappings = LoadMappings();

    internal static string Fold(string value)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            result.Append(Mappings.TryGetValue(rune.Value, out var mapped) ? mapped : rune.ToString());
        }

        return result.ToString();
    }

    private static Dictionary<int, string> LoadMappings()
    {
        using var stream = typeof(UnicodeCaseFolding).Assembly.GetManifestResourceStream(
            "Llamarc42.Core.Validation.Unicode.CaseFolding-17.0.0.txt")!;
        using var reader = new StreamReader(stream);
        var mappings = new Dictionary<int, string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var fields = line.Split(';');
            if (fields[1].Trim() is not ("C" or "F")) continue;
            var code = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            mappings.Add(code, string.Concat(fields[2].Trim().Split(' ')
                .Select(value => char.ConvertFromUtf32(int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)))));
        }

        return mappings;
    }
}

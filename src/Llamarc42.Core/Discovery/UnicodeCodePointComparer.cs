using System.Buffers;
using System.Text;

namespace Llamarc42.Core.Discovery;

internal sealed class UnicodeCodePointComparer : IComparer<string>
{
    public static UnicodeCodePointComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
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

        var xIndex = 0;
        var yIndex = 0;

        while (xIndex < x.Length && yIndex < y.Length)
        {
            var xValue = ReadCodePoint(x, ref xIndex);
            var yValue = ReadCodePoint(y, ref yIndex);
            var comparison = xValue.CompareTo(yValue);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        var lengthComparison = (x.Length - xIndex).CompareTo(y.Length - yIndex);
        return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(x, y);
    }

    private static int ReadCodePoint(string value, ref int index)
    {
        var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
        if (status == OperationStatus.Done)
        {
            index += consumed;
            return rune.Value;
        }

        return value[index++];
    }
}

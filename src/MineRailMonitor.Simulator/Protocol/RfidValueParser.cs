using System.Linq;

namespace MineRailMonitor.Simulator.Protocol;

public static class RfidValueParser
{
    public static bool TryParse(string? value, out ushort parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value!.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hexText = trimmed.Substring(2);
            return ushort.TryParse(hexText, System.Globalization.NumberStyles.HexNumber, null, out parsed);
        }

        if (trimmed.Length == 4 && trimmed[0] == '0' ||
            trimmed.Any(character => character is >= 'A' and <= 'F' or >= 'a' and <= 'f'))
        {
            return ushort.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out parsed);
        }

        if (trimmed.All(char.IsDigit))
        {
            return ushort.TryParse(trimmed, out parsed);
        }

        return false;
    }
}

namespace OsuMate.Utils;

internal static class MathUtils
{
    internal static double IsNaNWithNum(double? number)
    {
        if (number is null) return 0.0;
        return double.IsNaN(number.Value) ? 0.0 : number.Value;
    }

    internal static string FormatUnder4Chars(double value)
    {
        if (double.IsNaN(value) || value == 0) return "0.00";

        string abs = Math.Abs(value).ToString("G4");
        string result = value < 0 ? "-" + abs : abs;

        if (Math.Abs(value) >= 1000) return ((int)value).ToString();
        if (Math.Abs(value) >= 100) return value.ToString("F1");
        return value.ToString("F2");
    }

    internal static string FormatUnder4CharsSign(double value)
    {
        if (double.IsNaN(value) || value == 0) return "0.00";
        string sign = value > 0 ? "+" : "-";
        double abs = Math.Abs(value);
        if (abs >= 1000) return sign + ((int)abs).ToString();
        if (abs >= 100) return sign + abs.ToString("F1");
        return sign + abs.ToString("F2");
    }

    internal static string FormatNaturalSign(double value)
    {
        if (double.IsNaN(value) || value == 0) return "0";
        string sign = value > 0 ? "+" : "-";
        return sign + ((int)Math.Abs(value)).ToString();
    }
}

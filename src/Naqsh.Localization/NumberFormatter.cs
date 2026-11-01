namespace Naqsh.Localization;

/// <summary>Digit conversion between Latin (0–9) and Persian (۰–۹).</summary>
public static class NumberFormatter
{
    private const char PersianZero = '\u06F0';
    private const char ArabicIndicZero = '\u0660';

    /// <summary>Replaces Latin digits with Persian digits and '.' between digits with the Persian decimal separator '٫'.</summary>
    public static string ToPersianDigits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (c is >= '0' and <= '9')
                {
                    span[i] = (char)(PersianZero + (c - '0'));
                }
                else if (c == '.' && i > 0 && i < src.Length - 1 && char.IsAsciiDigit(src[i - 1]) && char.IsAsciiDigit(src[i + 1]))
                {
                    span[i] = '\u066B';
                }
                else
                {
                    span[i] = c;
                }
            }
        });
    }

    /// <summary>Replaces Persian and Arabic-Indic digits with Latin digits (for parsing user input).</summary>
    public static string ToLatinDigits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                span[i] = c switch
                {
                    >= PersianZero and <= '\u06F9' => (char)('0' + (c - PersianZero)),
                    >= ArabicIndicZero and <= '\u0669' => (char)('0' + (c - ArabicIndicZero)),
                    '\u066B' => '.',
                    _ => c,
                };
            }
        });
    }
}

using AltomateHR.Api.Modules.Employees.Entities;   // Gender, MaritalStatus

namespace AltomateHR.Api.Modules.Payroll;

// Field formatting for the fixed-width statutory submission files.
//
// KWSP, PERKESO and LHDN each parse by BYTE POSITION, so a field one
// character short shifts every column after it and the whole row is rejected —
// or worse, silently misread. Everything here is therefore total: it always
// returns exactly `width` characters, truncating rather than overflowing.
//
// Pure and static like the rest of this module root.
public static class StatutoryFileFields
{
    // Text field: left-aligned, space-padded, truncated from the RIGHT (the
    // start of a name is what identifies someone).
    public static string PadRight(string? value, int width)
    {
        var s = value ?? string.Empty;
        return s.Length >= width ? s[..width] : s.PadRight(width, ' ');
    }

    // Numeric text field: right-aligned, space-padded. Truncates from the LEFT
    // so the low-order digits survive — dropping the leading digit of an amount
    // is wrong, but dropping the sen is worse.
    public static string PadLeft(string? value, int width)
    {
        var s = value ?? string.Empty;
        return s.Length >= width ? s[^width..] : s.PadLeft(width, ' ');
    }

    // Numeric field: right-aligned, ZERO-padded. Same left truncation.
    public static string PadZero(string? value, int width)
    {
        var s = value ?? string.Empty;
        return s.Length >= width ? s[^width..] : s.PadLeft(width, '0');
    }

    public static string PadZero(long value, int width) =>
        PadZero(value.ToString(System.Globalization.CultureInfo.InvariantCulture), width);

    // Ringgit to sen. These files carry no decimal point — the position of the
    // last two digits IS the decimal point, so the conversion has to be exact.
    // Round half away from zero, as everywhere else in this module.
    public static long ToSen(decimal ringgit) =>
        (long)Math.Round(ringgit * 100m, 0, MidpointRounding.AwayFromZero);

    // Digits only. An IC typed as 900101-14-5567 and one typed as
    // 900101145567 are the same person, and only one of them is submittable.
    public static string DigitsOnly(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());

    // Letters and digits, no separators — passports and EPF numbers.
    public static string AlphanumericOnly(string? value) =>
        value is null
            ? string.Empty
            : new string(value.Where(char.IsAsciiLetterOrDigit).ToArray());

    // An LHDN tax reference with its SG / OG / C prefix and separators removed,
    // leaving the digits ready to pad.
    public static string NormaliseTaxRef(string? taxRef) => DigitsOnly(taxRef);

    // LHDN splits the tax reference into a 10-digit number plus a trailing
    // "wife code". A reference of 11 digits or more already carries it.
    public static string TaxRefWithoutWifeCode(string? taxRef)
    {
        var reference = NormaliseTaxRef(taxRef);
        return reference.Length >= 11 ? reference[..^1] : reference;
    }

    // 0 for a man or an unmarried woman; 1–9 for a married woman assessed
    // jointly with her husband. Taken from the tax reference when it carries
    // one, and otherwise inferred — an inferred 1 is a guess, but a wrong wife
    // code is a rejected row while a missing one is a rejected FILE.
    public static string PcbWifeCode(string? taxRef, Gender? gender, MaritalStatus? maritalStatus)
    {
        var reference = NormaliseTaxRef(taxRef);
        if (reference.Length == 0) return "0";
        if (reference.Length >= 11) return reference[^1..];

        return gender == Gender.FEMALE && maritalStatus == MaritalStatus.MARRIED ? "1" : "0";
    }

    // RFC 4180: quote only when the value contains a comma, quote or newline,
    // so the file stays close to KWSP's published sample.
    public static string CsvField(string? value)
    {
        var s = value ?? string.Empty;
        if (s.Length == 0) return string.Empty;

        return s.AsSpan().IndexOfAny(",\"\n\r") >= 0
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
    }

    // Every one of these formats is CRLF, regardless of the host OS.
    public const string LineEnding = "\r\n";
}

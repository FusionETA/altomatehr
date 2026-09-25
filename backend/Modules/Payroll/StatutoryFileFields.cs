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

    // A PERKESO money field: sen with the two cents digits ALWAYS present, so
    // at least three digits — RM0.00 is "000", RM0.50 is "050". The ASSIST
    // parser rejects a bare "0", which is what a foreign worker's zero EIS
    // share used to come out as. Space-padding to the column is separate.
    public static string SenDigits(decimal ringgit) =>
        ToSen(ringgit).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3, '0');

    // The nationality spellings the previous system treated as Malaysian.
    // PERKESO routes on this (IC vs SOCSO number), so the set must match the
    // one the files were filed under: "Malaysia", "MY", "MYS" and the Malay
    // forms appear in imported profiles.
    public static bool IsMalaysianNationality(string? nationality)
    {
        var v = (nationality ?? string.Empty).Trim().ToLowerInvariant();
        if (v.Length == 0) return false;
        return v is "malaysian" or "malaysia" or "my" or "mys"
            || v.Contains("warganegara malaysia")
            || v.Contains("rakyat malaysia");
    }

    // Digits only. An IC typed as 900101-14-5567 and one typed as
    // 900101145567 are the same person, and only one of them is submittable.
    public static string DigitsOnly(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());

    // Letters and digits, no separators — passports and EPF numbers.
    public static string AlphanumericOnly(string? value) =>
        value is null
            ? string.Empty
            : new string(value.Where(char.IsAsciiLetterOrDigit).ToArray());

    // Normalise an employer identifier before it goes into a fixed-width
    // statutory column: drop everything that isn't a letter or digit, and
    // uppercase what's left.
    //
    // Admins type these with the separators printed on the certificate
    // ("A 3702 1815 43P"), and the renderers pad the value verbatim into a
    // column — so the spaces travel to PERKESO and come back as "Invalid
    // employer code format".
    public static string NormaliseEmployerCode(string? value) =>
        new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // True for an identifier that is plainly a stand-in rather than a real one:
    // a single character repeated ("0000", "1111"), or a run of the counting
    // sequence ("1234", "123456789", "0987654321").
    //
    // The renderers already refuse a MISSING number, but a seeded demo value
    // passes that check and is caught only by the agency — LHDN rejected a
    // whole PCB submission on "No E (HQ) 1234567890 not exist", and PERKESO
    // rejected a SOCSO file on employer code "1234", in the same week.
    //
    // This cannot confirm a real identifier, only reject an obviously fake
    // one, so it stays deliberately narrow: no length or format rules. Those
    // differ per agency and would block legitimate older codes — PERKESO codes
    // in the wild are both 12-char alphanumeric (A3702181543P) and 6-digit
    // numeric (907968).
    public static bool LooksLikePlaceholderId(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (v.Length < 3) return false;
        if (v.All(c => c == v[0])) return true;
        if (!v.All(char.IsDigit)) return false;
        return "01234567890".Contains(v) || "09876543210".Contains(v);
    }

    // The 2-character country code LHDN wants beside a foreign employee's
    // passport, derived from the nationality already on the profile.
    public static string CountryCodeForNationality(string? nationality) =>
        NationalityCountryCodes.ForNationality(nationality);

    // An LHDN tax reference with its SG / OG / C prefix and separators removed.
    //
    // Exactly the previous system's rule: drop ASCII letters, whitespace, and
    // - _ ( ). Anything else ("/", ".") is KEPT, so it counts toward the
    // 11-character check and reaches the file as the previous system sent it.
    public static string NormaliseTaxRef(string? taxRef) =>
        taxRef is null
            ? string.Empty
            : new string(taxRef
                .Where(c => !char.IsAsciiLetter(c) && !char.IsWhiteSpace(c)
                            && c is not ('-' or '_' or '(' or ')'))
                .ToArray());

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

    // Period stamp the previous system put in statutory file names: MMYYYY.
    public static string PeriodMmYyyy(int year, int month) =>
        $"{month:D2}{year:D4}";
}

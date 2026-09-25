namespace AltomateHR.Api.Modules.Payroll;

// Turns whatever a spreadsheet or partner system calls someone's nationality
// into the demonym the profile dropdown offers — "Malaysia", "MYS" and
// "Warganegara Malaysia" all become "Malaysian".
//
// It matters beyond tidiness: the CP39 file keys locals by an EXACT
// "Malaysian", and the LHDN country code is looked up by demonym, so "China"
// filed a Chinese passport holder with no country code at all.
//
// The canonical list is the 179 demonyms in NationalityCountryCodes (the
// previous system's dropdown). Aliases are each country's English name plus a
// few common forms. Two-letter codes are NOT accepted in general — a cell
// reading "NA" (not applicable) would otherwise become Namibian — only MY/MYS.
//
// Something unrecognised is kept exactly as typed, never guessed at; the
// importer reports it so an admin can pick the right one.
public static class Nationalities
{
    // Canonical spelling by normalised key, e.g. "malaysian" → "Malaysian".
    private static readonly Dictionary<string, string> Canonical =
        NationalityCountryCodes.Demonyms.ToDictionary(Key, d => d, StringComparer.Ordinal);

    // Generated from the ISO codes in NationalityCountryCodes via the English
    // country names, then hand-corrected where the official name is not the
    // one people type ("Türkiye", "Myanmar (Burma)", "Côte d’Ivoire").
    private static readonly Dictionary<string, string> Aliases = new[]
    {
        ("Malaysia", "Malaysian"),
        ("MY", "Malaysian"),
        ("MYS", "Malaysian"),
        ("Warganegara Malaysia", "Malaysian"),
        ("Rakyat Malaysia", "Malaysian"),
        ("Malaysian Citizen", "Malaysian"),
        ("Malaysia Citizen", "Malaysian"),
        ("Warganegara", "Malaysian"),
        ("Afghanistan", "Afghan"),
        ("Albania", "Albanian"),
        ("Algeria", "Algerian"),
        ("United States", "American"),
        ("USA", "American"),
        ("U.S.A.", "American"),
        ("United States of America", "American"),
        ("America", "American"),
        ("Andorra", "Andorran"),
        ("Angola", "Angolan"),
        ("Argentina", "Argentine"),
        ("Armenia", "Armenian"),
        ("Australia", "Australian"),
        ("Austria", "Austrian"),
        ("Azerbaijan", "Azerbaijani"),
        ("Bahamas", "Bahamian"),
        ("Bahrain", "Bahraini"),
        ("Bangladesh", "Bangladeshi"),
        ("Barbados", "Barbadian"),
        ("Belarus", "Belarusian"),
        ("Belgium", "Belgian"),
        ("Belize", "Belizean"),
        ("Benin", "Beninese"),
        ("Bhutan", "Bhutanese"),
        ("Bolivia", "Bolivian"),
        ("Bosnia and Herzegovina", "Bosnian"),
        ("Bosnia", "Bosnian"),
        ("Botswana", "Botswanan"),
        ("Brazil", "Brazilian"),
        ("United Kingdom", "British"),
        ("UK", "British"),
        ("U.K.", "British"),
        ("Great Britain", "British"),
        ("England", "British"),
        ("United Kingdom of Great Britain and Northern Ireland", "British"),
        ("Brunei", "Bruneian"),
        ("Bulgaria", "Bulgarian"),
        ("Burkina Faso", "Burkinabe"),
        ("Myanmar", "Burmese"),
        ("Burma", "Burmese"),
        ("Burundi", "Burundian"),
        ("Cambodia", "Cambodian"),
        ("Cameroon", "Cameroonian"),
        ("Canada", "Canadian"),
        ("Cape Verde", "Cape Verdean"),
        ("Central African Republic", "Central African"),
        ("Chad", "Chadian"),
        ("Chile", "Chilean"),
        ("China", "Chinese"),
        ("PRC", "Chinese"),
        ("People's Republic of China", "Chinese"),
        ("Mainland China", "Chinese"),
        ("Colombia", "Colombian"),
        ("Comoros", "Comoran"),
        ("Congo", "Congolese"),
        ("Republic of the Congo", "Congolese"),
        ("Congo-Brazzaville", "Congolese"),
        ("Costa Rica", "Costa Rican"),
        ("Croatia", "Croatian"),
        ("Cuba", "Cuban"),
        ("Cyprus", "Cypriot"),
        ("Czechia", "Czech"),
        ("Denmark", "Danish"),
        ("Djibouti", "Djiboutian"),
        ("Dominican Republic", "Dominican"),
        ("Netherlands", "Dutch"),
        ("Holland", "Dutch"),
        ("The Netherlands", "Dutch"),
        ("Timor-Leste", "East Timorese"),
        ("Ecuador", "Ecuadorean"),
        ("Egypt", "Egyptian"),
        ("United Arab Emirates", "Emirati"),
        ("UAE", "Emirati"),
        ("U.A.E.", "Emirati"),
        ("Equatorial Guinea", "Equatorial Guinean"),
        ("Eritrea", "Eritrean"),
        ("Estonia", "Estonian"),
        ("Ethiopia", "Ethiopian"),
        ("Fiji", "Fijian"),
        ("Philippines", "Filipino"),
        ("Philippine", "Filipino"),
        ("Finland", "Finnish"),
        ("France", "French"),
        ("Gabon", "Gabonese"),
        ("Gambia", "Gambian"),
        ("Georgia", "Georgian"),
        ("Germany", "German"),
        ("Ghana", "Ghanaian"),
        ("Greece", "Greek"),
        ("Grenada", "Grenadian"),
        ("Guatemala", "Guatemalan"),
        ("Guinea", "Guinean"),
        ("Guyana", "Guyanese"),
        ("Haiti", "Haitian"),
        ("Honduras", "Honduran"),
        ("Hong Kong SAR China", "Hong Konger"),
        ("Hungary", "Hungarian"),
        ("Iceland", "Icelandic"),
        ("India", "Indian"),
        ("Republic of India", "Indian"),
        ("Indonesia", "Indonesian"),
        ("Republic of Indonesia", "Indonesian"),
        ("Iran", "Iranian"),
        ("Iraq", "Iraqi"),
        ("Ireland", "Irish"),
        ("Israel", "Israeli"),
        ("Italy", "Italian"),
        ("Ivory Coast", "Ivorian"),
        ("Cote d'Ivoire", "Ivorian"),
        ("Côte d'Ivoire", "Ivorian"),
        ("Jamaica", "Jamaican"),
        ("Japan", "Japanese"),
        ("Jordan", "Jordanian"),
        ("Kazakhstan", "Kazakh"),
        ("Kenya", "Kenyan"),
        ("Kuwait", "Kuwaiti"),
        ("Kyrgyzstan", "Kyrgyz"),
        ("Laos", "Lao"),
        ("Latvia", "Latvian"),
        ("Lebanon", "Lebanese"),
        ("Liberia", "Liberian"),
        ("Libya", "Libyan"),
        ("Liechtenstein", "Liechtensteiner"),
        ("Lithuania", "Lithuanian"),
        ("Luxembourg", "Luxembourger"),
        ("Macao SAR China", "Macanese"),
        ("North Macedonia", "Macedonian"),
        ("Madagascar", "Malagasy"),
        ("Malawi", "Malawian"),
        ("Maldives", "Maldivian"),
        ("Mali", "Malian"),
        ("Malta", "Maltese"),
        ("Mauritania", "Mauritanian"),
        ("Mauritius", "Mauritian"),
        ("Mexico", "Mexican"),
        ("Moldova", "Moldovan"),
        ("Monaco", "Monegasque"),
        ("Mongolia", "Mongolian"),
        ("Montenegro", "Montenegrin"),
        ("Morocco", "Moroccan"),
        ("Mozambique", "Mozambican"),
        ("Namibia", "Namibian"),
        ("Nepal", "Nepali"),
        ("New Zealand", "New Zealander"),
        ("Nicaragua", "Nicaraguan"),
        ("Nigeria", "Nigerian"),
        ("Niger", "Nigerien"),
        ("North Korea", "North Korean"),
        ("Norway", "Norwegian"),
        ("Oman", "Omani"),
        ("Pakistan", "Pakistani"),
        ("Palestinian Territories", "Palestinian"),
        ("Panama", "Panamanian"),
        ("Papua New Guinea", "Papua New Guinean"),
        ("Paraguay", "Paraguayan"),
        ("Peru", "Peruvian"),
        ("Poland", "Polish"),
        ("Portugal", "Portuguese"),
        ("Qatar", "Qatari"),
        ("Romania", "Romanian"),
        ("Russia", "Russian"),
        ("Rwanda", "Rwandan"),
        ("El Salvador", "Salvadoran"),
        ("Samoa", "Samoan"),
        ("Saudi Arabia", "Saudi"),
        ("Senegal", "Senegalese"),
        ("Serbia", "Serbian"),
        ("Seychelles", "Seychellois"),
        ("Sierra Leone", "Sierra Leonean"),
        ("Singapore", "Singaporean"),
        ("Slovakia", "Slovak"),
        ("Slovenia", "Slovenian"),
        ("Somalia", "Somali"),
        ("South Africa", "South African"),
        ("South Korea", "South Korean"),
        ("Korea", "South Korean"),
        ("Republic of Korea", "South Korean"),
        ("South Sudan", "South Sudanese"),
        ("Spain", "Spanish"),
        ("Sri Lanka", "Sri Lankan"),
        ("Sudan", "Sudanese"),
        ("Suriname", "Surinamese"),
        ("Eswatini", "Swazi"),
        ("Sweden", "Swedish"),
        ("Switzerland", "Swiss"),
        ("Syria", "Syrian"),
        ("Taiwan", "Taiwanese"),
        ("Tajikistan", "Tajik"),
        ("Tanzania", "Tanzanian"),
        ("Thailand", "Thai"),
        ("Togo", "Togolese"),
        ("Tonga", "Tongan"),
        ("Trinidad and Tobago", "Trinidadian"),
        ("Trinidad", "Trinidadian"),
        ("Tunisia", "Tunisian"),
        ("Turkey", "Turkish"),
        ("Türkiye", "Turkish"),
        ("Turkmenistan", "Turkmen"),
        ("Uganda", "Ugandan"),
        ("Ukraine", "Ukrainian"),
        ("Uruguay", "Uruguayan"),
        ("Uzbekistan", "Uzbek"),
        ("Venezuela", "Venezuelan"),
        ("Vietnam", "Vietnamese"),
        ("Yemen", "Yemeni"),
        ("Zambia", "Zambian"),
        ("Zimbabwe", "Zimbabwean"),
    }.ToDictionary(p => Key(p.Item1), p => p.Item2, StringComparer.Ordinal);

    // The demonym for a recognised value; the value itself (trimmed) when it
    // is not recognised; null when blank.
    public static string? Normalise(string? value) => Resolve(value).Value;

    // Recognised = on the dropdown list, or an alias of something on it.
    public static (string? Value, bool Recognised) Resolve(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return (null, true);

        var key = Key(trimmed);
        if (Canonical.TryGetValue(key, out var demonym)) return (demonym, true);
        if (Aliases.TryGetValue(key, out var aliased)) return (aliased, true);

        return (trimmed, false);
    }

    // Case, repeated spaces, curly apostrophes and a trailing full stop are
    // not differences anyone means.
    private static string Key(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Replace('\u2019', '\'').TrimEnd('.').ToLowerInvariant();
    }
}

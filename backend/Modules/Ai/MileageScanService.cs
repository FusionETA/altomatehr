using System.Text;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Ai.Dtos;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Ai;

// Mileage pre-fill from a route screenshot: the employee plans the trip in
// Google Maps or Waze, screenshots the route, and the scan reads the distance
// and the two ends off it. The screenshot then stays on the claim as its main
// document, so the approver sees what the distance came from.
//
// The read is the planned route, not the trip driven — it's the same evidence
// an approver would ask for by hand, made quicker, not a tracker.
public class MileageScanService : IMileageScanService
{
    private const decimal KmPerMile = 1.609344m;

    // Past this the read is almost certainly wrong (a misplaced decimal point,
    // a flight), and a wrong pre-fill is worse than an empty field.
    private const decimal MaxKm = 3000m;

    private readonly IGeminiClient _gemini;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public MileageScanService(
        IGeminiClient gemini,
        IOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _gemini = gemini;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    public async Task<MileageExtractionDto> AnalyzeAsync(
        byte[] fileBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Length == 0)
            throw new AiProviderException("Cannot analyze an empty file.");

        var raw = await _gemini.GenerateFromImageAsync(Prompt, fileBytes, mimeType, cancellationToken);
        var root = GeminiJson.ParseObject(raw);

        var orgId = _currentUser.OrganizationId;
        var unit = orgId is null
            ? MileageUnit.KM
            : (await _organizations.GetByIdAsync(orgId))?.MileageUnit ?? MileageUnit.KM;

        var isRoute = GeminiJson.BoolOrNull(root, "isRoute") ?? false;
        var detected = GeminiJson.DecimalOrNull(root, "distance");
        var detectedUnit = NormalizeUnit(GeminiJson.StringOrNull(root, "distanceUnit"));

        var km = (detected, detectedUnit) switch
        {
            (null, _) or (_, null) => (decimal?)null,
            (var d, "M") => d / 1000m,
            (var d, "MILE") => d * KmPerMile,
            (var d, _) => d,
        };
        if (!isRoute || km is not > 0m || km > MaxKm) km = null;

        var distance = km is null
            ? (decimal?)null
            : Math.Round(unit == MileageUnit.MILE ? km.Value / KmPerMile : km.Value, 2, MidpointRounding.AwayFromZero);

        var mode = GeminiJson.StringOrNull(root, "travelMode")?.ToUpperInvariant();

        return new MileageExtractionDto
        {
            IsRoute = isRoute,
            Distance = distance,
            Unit = unit,
            // What the screenshot showed, for the "converted from" note — only
            // alongside a distance that was used. Metres are given as km: the
            // form only knows km and miles.
            DetectedDistance = km is null ? null : detectedUnit == "M" ? detected / 1000m : detected,
            DetectedUnit = km is null ? null : detectedUnit == "M" ? "KM" : detectedUnit,
            Origin = isRoute ? Place(GeminiJson.StringOrNull(root, "origin")) : null,
            Destination = isRoute ? Place(GeminiJson.StringOrNull(root, "destination")) : null,
            Via = isRoute ? GeminiJson.StringOrNull(root, "via") : null,
            Date = isRoute ? GeminiJson.StringOrNull(root, "date") : null,
            TravelMode = isRoute && mode is not null && mode != "DRIVE" ? mode : null,
            OtherRoutesShown = isRoute && (GeminiJson.BoolOrNull(root, "otherRoutesShown") ?? false),
            Description = isRoute ? GeminiJson.StringOrNull(root, "description") : null,
            Provider = "gemini",
        };
    }

    // "km", "KM", "kilometres" → KM; "mi", "miles" → MILE; "m", "metres" → M.
    private static string? NormalizeUnit(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "km" or "kms" or "kilometer" or "kilometers" or "kilometre" or "kilometres" => "KM",
            "mi" or "mile" or "miles" => "MILE",
            "m" or "meter" or "meters" or "metre" or "metres" => "M",
            _ => null,
        };

    // "Your location" is where the phone was, not an address an approver can check.
    private static string? Place(string? value) =>
        value is null || value.Contains("your location", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static readonly string Prompt = BuildPrompt();

    private static string BuildPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You read screenshots of a planned route from a map or navigation app (Google Maps, Waze, Apple Maps or similar) for a mileage expense claim. Extract the trip.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a single JSON object matching this exact shape (no prose, no markdown fences):");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("  \"isRoute\": <true if the image shows a route between two places with a distance, otherwise false>,");
        sb.AppendLine("  \"distance\": <the selected route's distance as a number, or null>,");
        sb.AppendLine("  \"distanceUnit\": \"km, mi or m exactly as shown next to the distance, or null\",");
        sb.AppendLine("  \"origin\": \"the starting point as shown, or null\",");
        sb.AppendLine("  \"destination\": \"the end point as shown, or null\",");
        sb.AppendLine("  \"via\": \"the road the selected route goes via, e.g. 'via Federal Hwy/Route 2', or null\",");
        sb.AppendLine("  \"travelMode\": \"DRIVE, MOTORCYCLE, WALK, TRANSIT or CYCLE for the selected mode, or null\",");
        sb.AppendLine("  \"otherRoutesShown\": <true if alternative routes are also listed or drawn>,");
        sb.AppendLine("  \"date\": \"yyyy-mm-dd or null\",");
        sb.AppendLine("  \"description\": \"One short sentence, e.g. 'Drive from Bangsar to Shah Alam', or null\"");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("FIELD-BY-FIELD GUIDANCE:");
        sb.AppendLine();
        sb.AppendLine("distance — the SELECTED route only: the highlighted one (usually solid blue in Google Maps), or the first in the list if none is highlighted. Never a grey alternative, never the travel time, never a distance for walking to a car park. Read the digits exactly; keep decimals (e.g. 23.4).");
        sb.AppendLine();
        sb.AppendLine("origin / destination — copy the names or addresses as the app shows them in the search boxes or route header. If the start says 'Your location', return 'Your location'. Never invent an address.");
        sb.AppendLine();
        sb.AppendLine("date — only a trip date the screenshot clearly states (e.g. a timeline entry or a 'Depart at' date). NEVER the phone's status-bar clock. Otherwise null.");
        sb.AppendLine();
        sb.AppendLine("GENERAL RULES:");
        sb.AppendLine("- If the image is not a route (a receipt, a photo, a map with no route), set isRoute to false and every other field to null.");
        sb.AppendLine("- Return null for any field you can't read confidently. Do NOT guess.");
        sb.AppendLine("- Numbers must be plain JSON numbers (no quotes, no units).");
        return sb.ToString();
    }
}

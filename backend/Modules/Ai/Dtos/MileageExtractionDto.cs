using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Ai.Dtos;

// What the scan read off a map or navigation screenshot. Like a receipt read,
// it is form pre-fill only: everything is nullable and the employee checks it.
public class MileageExtractionDto
{
    // False when the image isn't a route at all (a receipt, a photo of a car).
    public bool IsRoute { get; set; }

    // In the org's mileage unit, ready for the distance field.
    public decimal? Distance { get; set; }
    public MileageUnit Unit { get; set; } = MileageUnit.KM;

    // As the screenshot showed it, so a converted figure can say what it came from.
    public decimal? DetectedDistance { get; set; }
    public string? DetectedUnit { get; set; }            // "KM" or "MILE"

    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public string? Via { get; set; }                     // the route's "via" road, if shown
    public string? Date { get; set; }                    // yyyy-MM-dd, only when the screenshot states one

    // The app's travel mode when it isn't driving (WALK, TRANSIT, ...), so the
    // form can say a mileage claim normally needs a driving route.
    public string? TravelMode { get; set; }

    // The screenshot listed other routes too; the read took the selected one.
    public bool OtherRoutesShown { get; set; }

    public string? Description { get; set; }
    public string Provider { get; set; } = "gemini";
}

// Returned by POST /claims/mileage/analyze — the extraction plus the stored
// screenshot's URL, which the claim keeps as its main document.
public class AnalyzeMileageResponseDto
{
    public string ReceiptUrl { get; set; } = string.Empty;
    public MileageExtractionDto Extraction { get; set; } = new();
}

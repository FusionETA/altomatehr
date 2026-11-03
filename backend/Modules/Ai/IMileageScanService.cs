using AltomateHR.Api.Modules.Ai.Dtos;

namespace AltomateHR.Api.Modules.Ai;

public interface IMileageScanService
{
    // Reads the distance, start and end off a route screenshot (Google Maps,
    // Waze, ...). Takes bytes for the same reason as IReceiptOcrService: the
    // caller has them from the upload.
    //
    // Writes nothing to the database — the result is form pre-fill only.
    Task<MileageExtractionDto> AnalyzeAsync(
        byte[] fileBytes,
        string mimeType,
        CancellationToken cancellationToken = default);
}

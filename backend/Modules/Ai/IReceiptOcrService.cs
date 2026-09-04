using AltomateHR.Api.Modules.Ai.Dtos;

namespace AltomateHR.Api.Modules.Ai;

public interface IReceiptOcrService
{
    // Sends receipt bytes to the model and returns the gated extraction. Takes
    // bytes rather than a path deliberately: the caller already has them in hand
    // from the upload, and re-reading the stored copy would mean going through
    // the claim-based authorization check — which fails for a receipt that has
    // no claim yet, i.e. every OCR call.
    //
    // Writes nothing to the database — the result is form pre-fill only.
    Task<ReceiptExtractionDto> AnalyzeAsync(
        byte[] fileBytes,
        string mimeType,
        CancellationToken cancellationToken = default);
}

namespace AltomateHR.Api.Modules.Claims.Dtos;

public class UploadReceiptResponseDto
{
    public string ReceiptUrl { get; set; } = string.Empty;

    // Set when the receipt went to Xero Files. The client sends it back with
    // the claim so the id is persisted alongside the url.
    public string? ReceiptXeroFileId { get; set; }
}

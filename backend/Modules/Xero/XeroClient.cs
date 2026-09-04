using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Xero;

public class XeroClient : IXeroClient
{
    private const string AuthorizeUrl = "https://login.xero.com/identity/connect/authorize";
    private const string TokenUrl = "https://identity.xero.com/connect/token";
    private const string ConnectionsUrl = "https://api.xero.com/connections";
    private const string AccountsUrl = "https://api.xero.com/api.xro/2.0/Accounts";
    private const string ProjectsUrl = "https://api.xero.com/projects.xro/2.0/Projects";
    private const string FilesUrl = "https://api.xero.com/files.xro/1.0/Files";
    private const string InvoicesUrl = "https://api.xero.com/api.xro/2.0/Invoices";
    private const string BankTransactionsUrl = "https://api.xero.com/api.xro/2.0/BankTransactions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly XeroOptions _options;

    public XeroClient(HttpClient http, IOptions<XeroOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string BuildAuthorizationUrl(string state)
    {
        EnsureConfigured();

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = _options.Scopes,
            ["state"] = state,
        };

        return $"{AuthorizeUrl}?{string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value ?? string.Empty)}"))}";
    }

    public Task<XeroTokenResponse> ExchangeCodeAsync(string code) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
        });

    public Task<XeroTokenResponse> RefreshTokenAsync(string refreshToken) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

    public async Task<List<XeroTenantResponse>> GetTenantsAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response, "Xero tenant lookup failed.");

        var payload = await response.Content.ReadFromJsonAsync<List<XeroTenantPayload>>(JsonOptions);
        return payload?
            .Where(t => !string.IsNullOrWhiteSpace(t.TenantId))
            .Select(t => new XeroTenantResponse(
                t.Id ?? string.Empty,
                t.TenantId ?? string.Empty,
                t.TenantName ?? string.Empty,
                t.TenantType))
            .ToList() ?? [];
    }

    public async Task<XeroFileContent?> GetFileContentAsync(
        string accessToken, string tenantId, string fileId)
    {
        // Metadata first — it carries the real name and MIME type, which the
        // content endpoint doesn't reliably return.
        using var metaRequest = new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}/{fileId}");
        metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        metaRequest.Headers.Add("xero-tenant-id", tenantId);
        metaRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var metaResponse = await _http.SendAsync(metaRequest);
        if (metaResponse.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(metaResponse, "Xero file lookup failed.");
        var meta = await metaResponse.Content.ReadFromJsonAsync<XeroFilePayload>(JsonOptions);

        using var contentRequest = new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}/{fileId}/Content");
        contentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        contentRequest.Headers.Add("xero-tenant-id", tenantId);

        using var contentResponse = await _http.SendAsync(contentRequest);
        if (contentResponse.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(contentResponse, "Xero file download failed.");

        return new XeroFileContent(
            await contentResponse.Content.ReadAsByteArrayAsync(),
            meta?.MimeType ?? "application/octet-stream",
            meta?.Name ?? fileId);
    }

    private sealed class XeroFilePayload
    {
        public string? Name { get; set; }
        public string? MimeType { get; set; }
    }

    // A bill is an Invoice of Type ACCPAY — Xero has one endpoint for both
    // directions and the type is what separates money owed from money due.
    //
    // The caller chooses DRAFT or AUTHORISED. Xero shows AUTHORISED as
    // "Awaiting payment": a live liability in aged payables. DRAFT is the
    // reviewable version, which is what a cautious finance team wants.
    public async Task<XeroBillResponse> CreateBillAsync(
        string accessToken, string tenantId, XeroBillRequest bill)
    {
        var payload = new
        {
            Invoices = new[]
            {
                new
                {
                    Type = "ACCPAY",
                    Contact = new { Name = bill.ContactName },
                    // Xero wants dates as yyyy-MM-dd; sending an ISO instant
                    // makes it guess, and it guesses in its own timezone.
                    Date = bill.Date.ToString("yyyy-MM-dd"),
                    DueDate = bill.DueDate.ToString("yyyy-MM-dd"),
                    Reference = bill.Reference,
                    CurrencyCode = bill.CurrencyCode,
                    Status = bill.Status == XeroBillStatus.Draft ? "DRAFT" : "AUTHORISED",
                    LineItems = bill.Lines.Select(line => new
                    {
                        line.Description,
                        Quantity = 1,
                        UnitAmount = line.Amount,
                        AccountCode = line.AccountCode,
                    }).ToArray(),
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, InvoicesUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("xero-tenant-id", tenantId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response, "Xero bill creation failed.");

        var result = await response.Content.ReadFromJsonAsync<XeroInvoicesPayload>(JsonOptions);
        var created = result?.Invoices?.FirstOrDefault();

        // A 200 with no invoice back means Xero accepted the call but created
        // nothing — treated as a failure so the claim is never marked SYNCED
        // against a bill that does not exist.
        if (created is null || string.IsNullOrWhiteSpace(created.InvoiceID))
            throw new XeroConnectionException("Xero accepted the bill but returned no invoice.");

        return new XeroBillResponse(created.InvoiceID, created.InvoiceNumber ?? bill.Reference);
    }

    // Company-paid spend. Always AUTHORISED: unlike a bill, this records money
    // that has ALREADY moved, so there is no meaningful draft state — the
    // bank statement will show it either way.
    public async Task<XeroSpendResponse> CreateSpendAsync(
        string accessToken, string tenantId, XeroSpendRequest spend)
    {
        var payload = new
        {
            BankTransactions = new[]
            {
                new
                {
                    Type = "SPEND",
                    Contact = new { Name = spend.ContactName },
                    BankAccount = new { Code = spend.BankAccountCode },
                    Date = spend.Date.ToString("yyyy-MM-dd"),
                    Reference = spend.Reference,
                    CurrencyCode = spend.CurrencyCode,
                    Status = "AUTHORISED",
                    LineItems = spend.Lines.Select(line => new
                    {
                        line.Description,
                        Quantity = 1,
                        UnitAmount = line.Amount,
                        AccountCode = line.AccountCode,
                    }).ToArray(),
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BankTransactionsUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("xero-tenant-id", tenantId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response, "Xero spend-money failed.");

        var result = await response.Content.ReadFromJsonAsync<XeroBankTransactionsPayload>(JsonOptions);
        var created = result?.BankTransactions?.FirstOrDefault();

        if (created is null || string.IsNullOrWhiteSpace(created.BankTransactionID))
            throw new XeroConnectionException("Xero accepted the spend but returned no transaction.");

        return new XeroSpendResponse(created.BankTransactionID);
    }

    public async Task<List<XeroAccountResponse>> GetAccountsAsync(string accessToken, string tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AccountsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("xero-tenant-id", tenantId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response, "Xero account sync failed.");

        var payload = await response.Content.ReadFromJsonAsync<XeroAccountsPayload>(JsonOptions);
        return payload?.Accounts?
            .Where(a =>
                !string.IsNullOrWhiteSpace(a.AccountId) &&
                !string.IsNullOrWhiteSpace(a.Code) &&
                !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new XeroAccountResponse(
                a.AccountId ?? string.Empty,
                a.Code ?? string.Empty,
                a.Name ?? string.Empty,
                a.Type ?? string.Empty,
                a.Status ?? string.Empty,
                a.EnablePaymentsToAccount))
            .ToList() ?? [];
    }

    public async Task<List<XeroProjectResponse>> GetProjectsAsync(string accessToken, string tenantId)
    {
        var projects = new List<XeroProjectResponse>();
        var page = 1;

        while (true)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{ProjectsUrl}?page={page}&pageSize=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("xero-tenant-id", tenantId);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request);
            await EnsureSuccessAsync(response, "Xero project sync failed.");

            var payload = await response.Content.ReadFromJsonAsync<XeroProjectsPayload>(JsonOptions);
            var pageProjects = payload?.Items ?? payload?.Projects ?? [];
            projects.AddRange(pageProjects
                .Where(p =>
                    !string.IsNullOrWhiteSpace(p.ProjectId) &&
                    !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new XeroProjectResponse(
                    p.ProjectId ?? string.Empty,
                    p.Name ?? string.Empty,
                    p.Status ?? p.State ?? string.Empty)));

            if (pageProjects.Count < 100)
                break;

            page++;
        }

        return projects;
    }

    private async Task<XeroTokenResponse> RequestTokenAsync(Dictionary<string, string> form)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
        request.Content = new FormUrlEncodedContent(form);

        using var response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response, "Xero token exchange failed.");

        var payload = await response.Content.ReadFromJsonAsync<XeroTokenPayload>(JsonOptions)
            ?? throw new XeroConnectionException("Xero token response was empty.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken) || string.IsNullOrWhiteSpace(payload.RefreshToken))
            throw new XeroConnectionException("Xero token response did not include the required tokens.");

        return new XeroTokenResponse(
            payload.AccessToken,
            payload.RefreshToken,
            payload.ExpiresIn,
            payload.TokenType ?? "Bearer",
            payload.Scope ?? string.Empty);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new XeroConfigurationException(
                "Xero is not configured. Set Xero:ClientId, Xero:ClientSecret and Xero:RedirectUri.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string message)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        throw new XeroConnectionException($"{message} Xero returned {(int)response.StatusCode}: {body}");
    }

    private sealed class XeroTokenPayload
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class XeroTenantPayload
    {
        public string? Id { get; set; }
        public string? TenantId { get; set; }
        public string? TenantName { get; set; }
        public string? TenantType { get; set; }
    }

    private sealed class XeroBankTransactionsPayload
    {
        [JsonPropertyName("BankTransactions")]
        public List<XeroBankTransactionPayload>? BankTransactions { get; set; }
    }

    private sealed class XeroBankTransactionPayload
    {
        public string? BankTransactionID { get; set; }
    }

    private sealed class XeroInvoicesPayload
    {
        [JsonPropertyName("Invoices")]
        public List<XeroInvoicePayload>? Invoices { get; set; }
    }

    private sealed class XeroInvoicePayload
    {
        public string? InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
    }

    private sealed class XeroAccountsPayload
    {
        public List<XeroAccountPayload>? Accounts { get; set; }
    }

    private sealed class XeroAccountPayload
    {
        public string? AccountId { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public bool? EnablePaymentsToAccount { get; set; }
    }

    private sealed class XeroProjectsPayload
    {
        public List<XeroProjectPayload>? Items { get; set; }
        public List<XeroProjectPayload>? Projects { get; set; }
    }

    private sealed class XeroProjectPayload
    {
        public string? ProjectId { get; set; }
        public string? Name { get; set; }
        public string? Status { get; set; }
        public string? State { get; set; }
    }
}

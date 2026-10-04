using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiMonitoring;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;

namespace AltomateHR.Api.Tests.ApiMonitoring;

// The middleware is on every request, so these pin the two things it owes:
// a row that groups per endpoint without leaking ids or bodies, and never
// costing the request anything when something goes wrong.
public class ApiRequestLogMiddlewareTests
{
    private readonly CapturingQueue _queue = new();

    [Fact]
    public async Task RecordsTheRouteTemplateNotTheRawPath()
    {
        var context = Request("POST", "/payroll/runs/run-123/generate", "payroll/runs/{id}/generate");

        await Middleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; })
            .InvokeAsync(context);

        var row = Assert.Single(_queue.Rows);
        Assert.Equal("POST", row.Method);
        Assert.Equal("payroll/runs/{id}/generate", row.Route);
        Assert.Equal(200, row.StatusCode);
        Assert.Null(row.ErrorMessage);
        Assert.DoesNotContain("run-123", row.Route);
    }

    // [Route("[controller]")] fills in the class name; the URL is lowercase.
    [Fact]
    public async Task LowercasesTheTemplatesLiteralSegmentsOnly()
    {
        var context = Request("GET", "/organizations/org-1/admins", "Organizations/{organizationId}/admins");

        await Middleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal("organizations/{organizationId}/admins", Assert.Single(_queue.Rows).Route);
    }

    [Fact]
    public async Task RecordsTheCompanyAndCallerFromTheClaims()
    {
        var context = Request("GET", "/employees", "employees");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("org", "org-1"),
            new Claim(ApiKeyAuthenticationDefaults.ApiKeyIdClaim, "key-9"),
        ], "test"));

        await Middleware(_ => Task.CompletedTask).InvokeAsync(context);

        var row = Assert.Single(_queue.Rows);
        Assert.Equal("org-1", row.OrganizationId);
        Assert.Equal(ApiCallerTypes.ApiKey, row.CallerType);
        Assert.Equal("key-9", row.CallerId);
    }

    [Fact]
    public async Task AnUnmatchedRequestIsGroupedRatherThanKeptByPath()
    {
        var context = Request("GET", "/no/such/thing/abc", template: null);

        await Middleware(c => { c.Response.StatusCode = 404; return Task.CompletedTask; })
            .InvokeAsync(context);

        Assert.Equal("(unmatched)", Assert.Single(_queue.Rows).Route);
    }

    [Theory]
    [InlineData("OPTIONS", "/payroll/runs")]
    [InlineData("GET", "/realtime/stream")]
    [InlineData("GET", "/health")]
    public async Task SkipsPreflightAndTheLiveStream(string method, string path)
    {
        var context = Request(method, path, "x");
        var called = false;

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.True(called);
        Assert.Empty(_queue.Rows);
    }

    [Fact]
    public async Task KeepsTheMessageAFailedCallWasShown()
    {
        var context = Request("POST", "/payroll/runs/run-1/submit", "payroll/runs/{id}/submit");

        await Middleware(async c =>
        {
            c.Response.StatusCode = 409;
            c.Response.ContentType = "application/json";
            await c.Response.WriteAsync("{\"error\":\"This run is no longer a draft.\"}");
        }).InvokeAsync(context);

        var row = Assert.Single(_queue.Rows);
        Assert.Equal(409, row.StatusCode);
        Assert.Equal("This run is no longer a draft.", row.ErrorMessage);
        Assert.Null(row.ExceptionType);
    }

    [Fact]
    public async Task ReadsAValidationProblemsFirstError()
    {
        var context = Request("POST", "/projects", "projects");

        await Middleware(async c =>
        {
            c.Response.StatusCode = 400;
            c.Response.ContentType = "application/problem+json";
            await c.Response.WriteAsync(
                "{\"title\":\"One or more validation errors occurred.\","
                + "\"errors\":{\"allowedIpEntries\":[\"'10.0.0' is not a valid IP.\"]}}");
        }).InvokeAsync(context);

        Assert.Equal("'10.0.0' is not a valid IP.", Assert.Single(_queue.Rows).ErrorMessage);
    }

    [Fact]
    public async Task TruncatesALongMessage()
    {
        var context = Request("GET", "/x", "x");

        await Middleware(async c =>
        {
            c.Response.StatusCode = 400;
            c.Response.ContentType = "text/plain";
            await c.Response.WriteAsync(new string('a', 3000));
        }).InvokeAsync(context);

        Assert.Equal(1000, Assert.Single(_queue.Rows).ErrorMessage!.Length);
    }

    // What the real exception handler does: it answers, clears the endpoint,
    // and leaves the exception on a feature. The row must still name the
    // endpoint that threw and carry the exception.
    [Fact]
    public async Task AHandledExceptionKeepsItsEndpointAndType()
    {
        var context = Request("POST", "/xero/sync/abc", "xero/sync/{id}");
        var endpoint = context.GetEndpoint();

        await Middleware(async c =>
        {
            c.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
            {
                Error = new InvalidOperationException("Xero said no."),
                Endpoint = endpoint,
                Path = "/xero/sync/abc",
            });
            c.SetEndpoint(null);
            c.Response.StatusCode = 500;
            c.Response.ContentType = "application/problem+json";
            await c.Response.WriteAsync("{\"title\":\"An unexpected error occurred.\"}");
        }).InvokeAsync(context);

        var row = Assert.Single(_queue.Rows);
        Assert.Equal("xero/sync/{id}", row.Route);
        Assert.Equal(500, row.StatusCode);
        Assert.Equal(typeof(InvalidOperationException).FullName, row.ExceptionType);
        // A crash's own message, not the handler's generic sentence.
        Assert.Equal("Xero said no.", row.ErrorMessage);
    }

    [Fact]
    public async Task AnUnhandledExceptionIsRecordedAndStillPropagates()
    {
        var context = Request("GET", "/claims", "claims");

        await Assert.ThrowsAsync<NullReferenceException>(() =>
            Middleware(_ => throw new NullReferenceException("boom")).InvokeAsync(context));

        var row = Assert.Single(_queue.Rows);
        Assert.Equal(500, row.StatusCode);
        Assert.Equal(typeof(NullReferenceException).FullName, row.ExceptionType);
        Assert.NotNull(row.ExceptionSource);
    }

    [Fact]
    public async Task AFullQueueNeverBreaksTheRequest()
    {
        _queue.Full = true;
        var context = Request("GET", "/employees", "employees");

        await Middleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; })
            .InvokeAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Empty(_queue.Rows);
    }

    [Fact]
    public async Task TheResponseBodyStillReachesTheCaller()
    {
        var context = Request("GET", "/employees", "employees");
        var sink = new MemoryStream();
        context.Response.Body = sink;

        await Middleware(c => c.Response.WriteAsync("hello")).InvokeAsync(context);

        Assert.Equal("hello", Encoding.UTF8.GetString(sink.ToArray()));
        Assert.Same(sink, context.Response.Body);
    }

    // ---- wiring ----

    private ApiRequestLogMiddleware Middleware(RequestDelegate next) =>
        new(next, _queue, NullLogger<ApiRequestLogMiddleware>.Instance);

    private static DefaultHttpContext Request(string method, string path, string? template)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (template is not null)
        {
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(template),
                order: 0,
                EndpointMetadataCollection.Empty,
                displayName: template));
        }
        return context;
    }

    private sealed class CapturingQueue : IApiRequestLogQueue
    {
        public List<ApiRequestLog> Rows { get; } = [];
        public bool Full { get; set; }

        public bool TryEnqueue(ApiRequestLog row)
        {
            if (Full) return false;
            Rows.Add(row);
            return true;
        }

        public ChannelReader<ApiRequestLog> Reader => throw new NotSupportedException();
    }
}

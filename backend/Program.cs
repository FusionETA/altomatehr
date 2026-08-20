using AltomateHR.Api.Modules.Employees;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Xero;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Services: the DI container ----
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddDataProtection();
builder.Services.Configure<XeroOptions>(builder.Configuration.GetSection("Xero"));

// CORS — let the Vite frontend (:5173) read our responses from the browser.
builder.Services.AddCors(options =>
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins(
                  "http://localhost:5173",
                  "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));   // allow the browser to send/receive cookies cross-origin

// Database (EF Core + Pomelo/MySQL). Connection string from user-secrets — never committed.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not set. Run:\n" +
        "  dotnet user-secrets set \"ConnectionStrings:Default\" \"Server=...;Port=...;Database=...;User=...;Password=...;SslMode=Required\"");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

// ---- Authentication (JWT) ----
var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not set. Run: dotnet user-secrets set \"Jwt:Key\" \"$(openssl rand -base64 48)\"");

// Two credential types share ONE pipeline. A "Smart" policy scheme peeks at the
// Authorization header per request and forwards to the right handler by token shape:
//   • "wp_live_..."  → ApiKey handler (machine / external apps)
//   • anything else  → JWT handler    (human logins from the frontend)
// Both produce the same claim shape (sub/org/role), so every controller works unchanged.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Smart";
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,          // reject expired tokens
            ValidateIssuerSigningKey = true,  // verify the signature with our key
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme, _ => { })
    .AddPolicyScheme("Smart", "JWT or wp_live_ API key", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..]
                : header;
            return token.TrimStart().StartsWith(ApiTokenGenerator.Prefix, StringComparison.Ordinal)
                ? ApiKeyAuthenticationDefaults.Scheme
                : JwtBearerDefaults.AuthenticationScheme;
        };
    });
builder.Services.AddAuthorization();

// Rate limiting protects auth endpoints from brute-force guessing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        await Results.Problem(
            title: "Too many requests.",
            detail: "Too many attempts. Please wait before trying again.",
            statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(context.HttpContext);
    };

    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientPartitionKey(httpContext, "auth-login"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientPartitionKey(httpContext, "auth-refresh"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

// Multi-tenancy + request context (ICurrentUser reads the JWT via IHttpContextAccessor)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IChartOfAccountRepository, ChartOfAccountRepository>();
builder.Services.AddScoped<IChartOfAccountService, ChartOfAccountService>();
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IAttendancePhotoStorage, AttendancePhotoStorage>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<ILeaveTypeRepository, LeaveTypeRepository>();
builder.Services.AddScoped<ILeaveApplicationRepository, LeaveApplicationRepository>();
builder.Services.AddScoped<ILeaveTypeService, LeaveTypeService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IOvertimeRepository, OvertimeRepository>();
builder.Services.AddScoped<IOvertimePhotoStorage, OvertimePhotoStorage>();
builder.Services.AddScoped<IOvertimeService, OvertimeService>();
builder.Services.AddScoped<IEmployeePolicyRepository, EmployeePolicyRepository>();
builder.Services.AddScoped<IPolicyLeaveEntitlementRepository, PolicyLeaveEntitlementRepository>();
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<ITeamMembershipRepository, TeamMembershipRepository>();
builder.Services.AddScoped<IApprovalChainService, ApprovalChainService>();
builder.Services.AddScoped<IApprovalRouter, ApprovalRouter>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IXeroRepository, XeroRepository>();
builder.Services.AddScoped<IXeroService, XeroService>();
builder.Services.AddHttpClient<IXeroClient, XeroClient>();

// Modules
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOrganizationMembershipRepository, OrganizationMembershipRepository>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<ISupervisionService, SupervisionService>();
builder.Services.AddScoped<IClaimsRepository, ClaimsRepository>();
builder.Services.AddScoped<IClaimReceiptStorage, ClaimReceiptStorage>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();

var app = builder.Build();

// ---- Middleware pipeline (order matters) ----
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();   // Swagger-style UI at /scalar
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        if (exception is not null)
        {
            logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Something went wrong.",
            Detail = app.Environment.IsDevelopment()
                ? exception?.Message
                : "The server hit an unexpected error. Please try again later.",
        });
    });
});

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();   // WHO are you?  (JWT or wp_live_ key) — MUST be before UseAuthorization
app.UseAuthorization();    // WHAT may you do?  ([Authorize] is enforced here)
app.UseMiddleware<ApiKeyAuditMiddleware>();  // audit + LastUsedAt for wp_live_ traffic (after the endpoint)
app.MapControllers();

// Seed the demo org + users (hashed) and backfill any pre-tenancy rows.
using (var scope = app.Services.CreateScope())
{
    var organizations = scope.ServiceProvider.GetRequiredService<IOrganizationRepository>();
    var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var memberships = scope.ServiceProvider.GetRequiredService<IOrganizationMembershipRepository>();
    var claims = scope.ServiceProvider.GetRequiredService<IClaimsRepository>();
    var leaveTypes = scope.ServiceProvider.GetRequiredService<ILeaveTypeRepository>();
    var policies = scope.ServiceProvider.GetRequiredService<IEmployeePolicyRepository>();
    var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
    var attendance = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
    await DbSeeder.SeedAsync(organizations, users, memberships, claims, leaveTypes, policies, projects, attendance);
}

app.Run();

static string GetClientPartitionKey(HttpContext context, string policyName)
{
    var ip = context.Connection.RemoteIpAddress?.ToString();
    return $"{policyName}:{ip ?? "unknown"}";
}

public partial class Program { }

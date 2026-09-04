using AltomateHR.Api.Modules.Employees;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Cron;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Dashboard;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Cron;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Holidays;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Partners;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Shifts;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Xero;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

// QuestPDF requires the licence to be declared before the first render.
// Community is free for organisations under USD 1M annual revenue.
//
// Confirmed 2026-09-02: Community is fine for now. Revisit if revenue
// approaches that threshold, or before selling this as a hosted product —
// the licence is judged on the organisation's revenue, not the app's.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ---- Services: the DI container ----
builder.Services.AddControllers(o => o.Filters.Add<PartnerAccessFilter>())   // deny-by-default for partner tokens
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
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(maxRetryCount: 5)));

// ---- Distributed cache (Redis) ----
// Backs the partner-integration ticket/token store (and general caching). If a Redis
// connection string is configured → real Redis; otherwise an in-memory IDistributedCache
// so dev/tests run with no Redis installed. Both expose the same IDistributedCache API.
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
else
    builder.Services.AddDistributedMemoryCache();

// ---- Authentication ----
var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not set. Run: dotnet user-secrets set \"Jwt:Key\" \"$(openssl rand -base64 48)\"");

// THREE credential types share ONE pipeline. A "Smart" policy scheme peeks at the
// Authorization header per request and forwards to the right handler by token shape:
//   • "wp_live_..."  → ApiKey handler        (machine keys / external apps)
//   • "apx_live_..." → PartnerToken handler  (partner apps, e.g. Appraisify)
//   • anything else  → JWT handler           (human logins from the frontend)
// All produce the same claim shape (sub/org/role + scopes), so every controller works unchanged.
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
    .AddScheme<AuthenticationSchemeOptions, PartnerTokenAuthenticationHandler>(
        PartnerAuthenticationDefaults.Scheme, _ => { })
    .AddPolicyScheme("Smart", "JWT, wp_live_ key, or apx_live_ partner token", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            var token = (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..]
                : header).TrimStart();
            if (token.StartsWith(ApiTokenGenerator.Prefix, StringComparison.Ordinal))
                return ApiKeyAuthenticationDefaults.Scheme;                         // wp_live_ → machine key
            if (token.StartsWith(PartnerTokenGenerator.AccessTokenPrefix, StringComparison.Ordinal))
                return PartnerAuthenticationDefaults.Scheme;                        // apx_live_ → partner app
            return JwtBearerDefaults.AuthenticationScheme;                          // anything else → human JWT
        };
    });
builder.Services.AddAuthorization(options =>
{
    // Platform gate: only SUPERADMIN_EMAILS staff (never org Owners) may provision plans.
    options.AddPolicy(AuthPolicies.Superadmin, policy =>
        policy.Requirements.Add(new SuperadminRequirement()));
});
builder.Services.AddSingleton<ISuperadminRegistry, SuperadminRegistry>();
builder.Services.AddSingleton<IAuthorizationHandler, SuperadminAuthorizationHandler>();

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

// Realtime (SSE). The HUB is a singleton because it holds open connections,
// which outlive the request that opened them; the service around it is scoped
// like every other service so it can read ICurrentUser.
builder.Services.AddSingleton<IRealtimeHub, RealtimeHub>();
builder.Services.AddScoped<IRealtimeService, RealtimeService>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IModuleAccessService, ModuleAccessService>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IChartOfAccountRepository, ChartOfAccountRepository>();
builder.Services.AddScoped<IChartOfAccountService, ChartOfAccountService>();
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IAttendanceSessionRepository, AttendanceSessionRepository>();
builder.Services.AddScoped<IAttendanceBreakRepository, AttendanceBreakRepository>();
builder.Services.AddScoped<IAttendanceApprovalRequestRepository, AttendanceApprovalRequestRepository>();
builder.Services.AddScoped<IAttendancePhotoStorage, AttendancePhotoStorage>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IHoursSummaryService, HoursSummaryService>();
builder.Services.AddHostedService<LeaveRolloverBackgroundService>();
builder.Services.AddHostedService<LeaveAccrualBackgroundService>();
builder.Services.AddHostedService<AutoClockOutBackgroundService>();
builder.Services.AddHostedService<OtWarningBackgroundService>();
builder.Services.AddHostedService<ApprovalDigestBackgroundService>();
builder.Services.AddScoped<ILeaveTypeRepository, LeaveTypeRepository>();
builder.Services.AddScoped<ILeaveApplicationRepository, LeaveApplicationRepository>();
builder.Services.AddScoped<ILeaveTypeService, LeaveTypeService>();
builder.Services.AddScoped<ILeaveEntitlementRepository, LeaveEntitlementRepository>();
builder.Services.AddScoped<ILeaveCronService, LeaveCronService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IOvertimeRepository, OvertimeRepository>();
builder.Services.AddScoped<IOvertimePhotoStorage, OvertimePhotoStorage>();
builder.Services.AddScoped<IOvertimeService, OvertimeService>();
builder.Services.AddScoped<IOtRateService, OtRateService>();
builder.Services.AddScoped<IHolidayRepository, HolidayRepository>();
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddScoped<IEmployeePolicyRepository, EmployeePolicyRepository>();
builder.Services.AddScoped<IPolicyLeaveEntitlementRepository, PolicyLeaveEntitlementRepository>();
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<IShiftRepository, ShiftRepository>();
builder.Services.AddScoped<IShiftService, ShiftService>();
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
// The shared identity/membership read surface every module depends on — see
// IDirectoryService. Keeps the Employees/Auth repositories out of other modules.
builder.Services.AddScoped<IDirectoryService, DirectoryService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmployeeProfileRepository, EmployeeProfileRepository>();
builder.Services.AddScoped<IEmployeeProfileService, EmployeeProfileService>();
builder.Services.AddScoped<ISupervisionService, SupervisionService>();
builder.Services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
builder.Services.AddScoped<IClaimsRepository, ClaimsRepository>();
builder.Services.AddScoped<IClaimReceiptStorage, ClaimReceiptStorage>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();
builder.Services.AddScoped<IAdminOverviewService, AdminOverviewService>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IApiClientRepository, ApiClientRepository>();
builder.Services.AddScoped<IPartnerAuthStore, PartnerAuthStore>();
builder.Services.AddScoped<IPartnerAuthService, PartnerAuthService>();

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

// On boot: ensure the schema exists, then (dev only) seed demo data.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // Apply any pending EF migrations so the schema is ready — a Droplet deploy needs no
    // separate `dotnet ef database update`. Guarded to relational providers so the in-memory
    // test host (which can't migrate) is skipped. Real data comes from the legacy migration.
    var db = services.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();

    // Demo org + users (admin@altomate.com / password123, …) are DEVELOPMENT-ONLY — never
    // create these accounts against a real database; they'd be a public backdoor.
    //
    // Seed:DemoData exists because user-secrets only load in Development, so pointing a
    // dev run at a REAL database is the one case where "Development" and "safe to seed"
    // come apart. Set it to false and the demo rows are never written.
    if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Seed:DemoData", true))
    {
        var organizations = services.GetRequiredService<IOrganizationRepository>();
        var users = services.GetRequiredService<IUserRepository>();
        var memberships = services.GetRequiredService<IOrganizationMembershipRepository>();
        var claims = services.GetRequiredService<IClaimsRepository>();
        var leaveTypes = services.GetRequiredService<ILeaveTypeRepository>();
        var policies = services.GetRequiredService<IEmployeePolicyRepository>();
        var projects = services.GetRequiredService<IProjectRepository>();
        var attendance = services.GetRequiredService<IAttendanceRepository>();
        var attendanceApprovalRequests = services.GetRequiredService<IAttendanceApprovalRequestRepository>();
        var apiClients = services.GetRequiredService<IApiClientRepository>();
        await DbSeeder.SeedAsync(organizations, users, memberships, claims, leaveTypes, policies, projects, attendance, attendanceApprovalRequests, apiClients);
    }
}

app.Run();

static string GetClientPartitionKey(HttpContext context, string policyName)
{
    var ip = context.Connection.RemoteIpAddress?.ToString();
    return $"{policyName}:{ip ?? "unknown"}";
}

public partial class Program { }

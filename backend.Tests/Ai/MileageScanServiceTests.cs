using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Ai;

// A route screenshot read is pre-fill, so what's pinned here is what the
// service refuses to pass through: a non-route, an absurd distance, "Your
// location" as an address — and that the distance lands in the org's unit.
public class MileageScanServiceTests
{
    private static string Json(
        bool isRoute = true, string distance = "23.4", string unit = "\"km\"",
        string origin = "\"Bangsar South\"", string destination = "\"Shah Alam\"",
        string mode = "\"DRIVE\"", string others = "false") =>
        $$"""
        {"isRoute": {{isRoute.ToString().ToLowerInvariant()}}, "distance": {{distance}}, "distanceUnit": {{unit}},
         "origin": {{origin}}, "destination": {{destination}}, "via": "Federal Hwy", "travelMode": {{mode}},
         "otherRoutesShown": {{others}}, "date": null, "description": "Drive from Bangsar South to Shah Alam"}
        """;

    private static MileageScanService Service(string response, MileageUnit unit = MileageUnit.KM) =>
        new(new FakeGeminiClient(response), new FakeOrganizationService(unit),
            new StubCurrentUser { OrganizationId = "org-1" });

    [Fact]
    public async Task ARoute_FillsTheTrip()
    {
        var read = await Service(Json()).AnalyzeAsync([1], "image/png");

        Assert.True(read.IsRoute);
        Assert.Equal(23.4m, read.Distance);
        Assert.Equal(MileageUnit.KM, read.Unit);
        Assert.Equal(("Bangsar South", "Shah Alam"), (read.Origin, read.Destination));
        Assert.Null(read.TravelMode);   // driving is the normal case, so nothing to flag
    }

    [Fact]
    public async Task AnEmptyFile_IsRejectedBeforeCallingTheModel()
    {
        await Assert.ThrowsAsync<AiProviderException>(() => Service(Json()).AnalyzeAsync([], "image/png"));
    }

    [Fact]
    public async Task NotARoute_FillsNothing()
    {
        var read = await Service(Json(isRoute: false)).AnalyzeAsync([1], "image/png");

        Assert.False(read.IsRoute);
        Assert.Null(read.Distance);
        Assert.Null(read.Origin);
    }

    [Theory]
    [InlineData(MileageUnit.KM, "16.09", "\"mi\"", 25.89)]     // miles on the screen, km in the org
    [InlineData(MileageUnit.MILE, "40", "\"km\"", 24.85)]      // km on the screen, miles in the org
    [InlineData(MileageUnit.KM, "850", "\"m\"", 0.85)]         // a short hop shown in metres
    public async Task TheDistanceLandsInTheOrgsUnit(MileageUnit orgUnit, string distance, string unit, double expected)
    {
        var read = await Service(Json(distance: distance, unit: unit), orgUnit).AnalyzeAsync([1], "image/png");

        Assert.Equal((decimal)expected, read.Distance);
        Assert.Equal(orgUnit, read.Unit);
    }

    // A wrong pre-fill is worse than an empty field.
    [Theory]
    [InlineData("0", "\"km\"")]
    [InlineData("-5", "\"km\"")]
    [InlineData("12000", "\"km\"")]
    [InlineData("23.4", "null")]   // a number with no unit can't be trusted either
    public async Task AnImplausibleDistance_IsLeftBlank(string distance, string unit)
    {
        var read = await Service(Json(distance: distance, unit: unit)).AnalyzeAsync([1], "image/png");

        Assert.Null(read.Distance);
        Assert.Equal("Shah Alam", read.Destination);
    }

    [Fact]
    public async Task YourLocation_IsNotAnAddress()
    {
        var read = await Service(Json(origin: "\"Your location\"")).AnalyzeAsync([1], "image/png");

        Assert.Null(read.Origin);
    }

    [Fact]
    public async Task ANonDrivingRoute_IsFlagged()
    {
        var read = await Service(Json(mode: "\"walk\"", others: "true")).AnalyzeAsync([1], "image/png");

        Assert.Equal("WALK", read.TravelMode);
        Assert.True(read.OtherRoutesShown);
    }

    private sealed class FakeGeminiClient(string response) : IGeminiClient
    {
        public Task<string> GenerateTextAsync(
            string systemInstruction, IReadOnlyList<GeminiTurn> turns, GeminiTextOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The mileage scan only sends images.");

        public Task<string> GenerateFromImageAsync(
            string prompt, byte[] fileBytes, string mimeType, CancellationToken cancellationToken = default) =>
            Task.FromResult(response);
    }

    private sealed class FakeOrganizationService(MileageUnit unit) : IOrganizationService
    {
        public Task<OrganizationDto?> GetByIdAsync(string organizationId) =>
            Task.FromResult<OrganizationDto?>(new OrganizationDto { Id = organizationId, MileageUnit = unit });

        public Task<IReadOnlyList<AdminAccessDto>> ListAdminsAsync() => throw new NotSupportedException();
        public Task<AdminAccessDto?> SetAdminModulesAsync(string userId, List<string>? modules) =>
            throw new NotSupportedException();
        public Task<OrganizationDto?> SetClaimSettingsAsync(
            string organizationId, int cutoffDay, ClaimSettlement settlementRoute, XeroBillStatus xeroBillStage) =>
            throw new NotSupportedException();
        public Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId, bool viaSso = false, bool viaSupport = false) =>
            throw new NotSupportedException();
        public Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto) =>
            throw new NotSupportedException();
        public Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto) =>
            throw new NotSupportedException();
    }
}

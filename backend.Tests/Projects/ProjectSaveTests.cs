using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Projects;

// Create used to ignore geofence sites and the IP allowlist (only update wrote
// them), and archive/restore answered with both lists empty.
public class ProjectSaveTests
{
    private static ProjectService Create(List<Project> projects) =>
        new(new FakeProjectRepository(projects), new FakeAuditService(),
            new FakeProjectTeamService([]), new FixedTrackingScope(null));

    private static SaveProjectDto WithSitesAndAllowlist(string cidr = "203.0.113.0/24") => new()
    {
        Name = "HQ",
        GeofencePoints = [new() { Label = "Main gate", Latitude = 3.1, Longitude = 101.6 }],
        AllowedIpEntries = [new() { Label = "Office", Cidr = cidr }],
    };

    [Fact]
    public async Task CreateAsync_SavesTheSitesAndAllowlistItIsGiven()
    {
        var service = Create([]);

        var created = await service.CreateAsync(WithSitesAndAllowlist());

        Assert.Equal(["Main gate"], created.GeofencePoints.Select(g => g.Label));
        Assert.Equal(["203.0.113.0/24"], created.AllowedIpEntries.Select(a => a.Cidr));
    }

    [Fact]
    public async Task CreateAsync_RejectsABadAllowlistEntryBeforeSavingAnything()
    {
        var projects = new List<Project>();
        var service = Create(projects);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(WithSitesAndAllowlist(cidr: "not-an-ip")));
        Assert.Empty(projects);
    }

    [Fact]
    public async Task SetArchivedAsync_AnswersWithTheProjectsSitesAndAllowlist()
    {
        var projects = new List<Project>();
        var service = Create(projects);
        var created = await service.CreateAsync(WithSitesAndAllowlist());

        var archived = await service.SetArchivedAsync(created.Id, true);

        Assert.NotNull(archived);
        Assert.True(archived!.IsArchived);
        Assert.Single(archived.GeofencePoints);
        Assert.Single(archived.AllowedIpEntries);
    }
}

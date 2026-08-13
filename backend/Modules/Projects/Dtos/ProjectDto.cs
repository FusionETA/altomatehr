namespace AltomateHR.Api.Modules.Projects.Dtos;

public class ProjectDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
}

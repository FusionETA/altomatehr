using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Organizations.Dtos;

public class OrgHolidayDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
}

// What an admin PUTs to set a year's calendar. The whole year is replaced, so
// omitting a date removes it.
public class SaveHolidaysDto
{
    [Required]
    public List<SaveHolidayDto> Holidays { get; set; } = [];
}

public class SaveHolidayDto
{
    [Required]
    public DateTime? Date { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;
}

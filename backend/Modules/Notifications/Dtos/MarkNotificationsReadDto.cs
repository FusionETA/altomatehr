namespace AltomateHR.Api.Modules.Notifications.Dtos;

// POST /notifications/read body:
//   { "id": "<notificationId>" } → mark that one read
//   { "all": true }              → mark every unread one read
public class MarkNotificationsReadDto
{
    public string? Id { get; set; }
    public bool All { get; set; }
}

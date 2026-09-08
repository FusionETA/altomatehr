using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Partners;
using AltomateHR.Api.Modules.Partners.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using System.Text.Json;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Data;

// Seeds the demo tenant + users (hashed passwords) on startup, and backfills any
// pre-tenancy rows so they belong to the demo org. Runs from Program.cs after build,
// where there is no request context (so the global query filter is a no-op).
public static class DbSeeder
{
    private const string DemoOrgId = "org-altomate";

    // DEV-ONLY partner client secret for Appraisify. In production a real secret is
    // generated once and delivered out-of-band; only its hash is ever stored. This
    // fixed dev value lets you exercise POST /partner/token locally.
    public const string DevAppraisifyClientSecret = "altomate_sk_dev_appraisify_secret_change_me";

    // Placeholder overtime evidence: 160x120 solid PNGs, two tints so the before
    // and after slots are told apart at a glance. Inline because the seeder has
    // to work on a clean checkout, with no asset to copy from.
    private const string DemoBeforePhotoPng =
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAAB4CAIAAAD6wG44AAAAzklEQVR42u3RAQ0AAAjDsMtGADIQi4+nyRSsmT0VFwsAC7AAC7AAC7AAAxZgARZgARZgAQYswAIswAIswAIswIAFWIAFWIAFWIABC7AAC7AAC7AAAxZgARZgARZgARZgwAIswAIswAIswIAFWIAFWIAFWIABuwBYgAVYgAVYgAUYsAALsAALsAALMGABFmABFmABFmABBizAAizAAizAAgxYgAVYgAVYgAUYsAALsAALsAALsAADFmABFmABFmABBizAAizAAizAAgzYheYe9FWO+ldjGAUAAAAASUVORK5CYII=";

    private const string DemoAfterPhotoPng =
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAAB4CAIAAAD6wG44AAAAzklEQVR42u3RAQ0AAAjDsMtGADIQi4+nyRSsuR0VFwsAC7AAC7AAC7AAAxZgARZgARZgAQYswAIswAIswAIswIAFWIAFWIAFWIABC7AAC7AAC7AAAxZgARZgARZgARZgwAIswAIswAIswIAFWIAFWIAFWIABuwBYgAVYgAVYgAUYsAALsAALsAALMGABFmABFmABFmABBizAAizAAizAAgxYgAVYgAVYgAUYsAALsAALsAALsAADFmABFmABFmABBizAAizAAizAAgzYheYeDZ+O+lNchHwAAAAASUVORK5CYII=";

    public static async Task SeedAsync(
        IOrganizationRepository organizations,
        IUserRepository users,
        IOrganizationMembershipRepository memberships,
        IClaimsRepository claims,
        ILeaveTypeRepository leaveTypes,
        IEmployeePolicyRepository policies,
        IProjectRepository projects,
        IAttendanceRepository attendance,
        IAttendanceApprovalRequestRepository approvalRequests,
        IApiClientRepository apiClients,
        IOvertimeRepository overtime,
        IOvertimePhotoStorage overtimePhotos,
        ILeaveApplicationRepository leaveApplications)
    {
        await SeedOrganizationAsync(organizations);
        await SeedApiClientsAsync(apiClients);
        await EnsureUserAsync(users, memberships, "usr-admin", "admin@altomate.com", "Owner", "Demo Admin", "Founder");
        await EnsureUserAsync(users, memberships, "usr-super", "supervisor@altomate.com", "Supervisor", "Sara Supervisor", "Team Lead");
        await EnsureUserAsync(users, memberships, "usr-emp", "employee@altomate.com", "Employee", "Evan Employee", "Associate");
        await BackfillClaimsAsync(claims);
        await SeedLeaveTypesAsync(leaveTypes);
        await SeedPolicyAsync(policies);
        var demoProject = await SeedAttendanceProjectAsync(projects);
        await SeedAttendanceAsync(attendance, approvalRequests, demoProject.Id);
        await BackfillLatenessAsync(attendance, organizations);
        await SeedOvertimeAsync(overtime, overtimePhotos, demoProject.Id);
        await SeedLeaveAsync(leaveApplications, leaveTypes);
    }

    // Register the Appraisify partner app (idempotent). Reads the roster
    // (employees:read) and sends notifications (notifications:write) — no
    // other scope granted.
    // Fills LateByMin on rows written before clock-in started computing it.
    //
    // Only ever fills nulls, so it's safe on every boot and never overwrites a
    // decided value — including the demo rows above, which set their own.
    //
    // Measures against the ORG's working hours rather than each employee's
    // effective shift: this runs with no request context, so the tenant filter
    // and the shift-resolution chain have no current org to work from. That
    // matches the live fallback for anyone without an assigned shift, which is
    // everyone in this data — but it would understate lateness for a shifted
    // employee, so it is a backfill for existing rows, not a general repair.
    private static async Task BackfillLatenessAsync(
        IAttendanceRepository attendance,
        IOrganizationRepository organizations)
    {
        var org = await organizations.GetByIdAsync(DemoOrgId);
        if (org is null) return;

        var pending = (await attendance.GetAllAsync())
            .Where(r => r.OrganizationId == DemoOrgId && r.TimeIn is not null && r.LateByMin is null)
            .ToList();

        foreach (var record in pending)
        {
            var late = AttendanceLateness.Minutes(record.TimeIn!.Value, org.WorkingHoursStart);
            if (late is null) continue;   // on time, or no schedule — leave it null

            record.LateByMin = late;
            await attendance.UpdateAsync(record);
        }
    }

    private static async Task SeedApiClientsAsync(IApiClientRepository apiClients)
    {
        if (await apiClients.GetByNameAsync("appraisify") is not null) return;

        await apiClients.AddAsync(new ApiClient
        {
            Id = "client-appraisify",
            Name = "appraisify",                                   // also the /sso/launch/{app} slug
            SecretHash = PartnerTokenGenerator.Hash(DevAppraisifyClientSecret),
            Scopes = "employees:read,notifications:write",         // read the roster, and send bell/email notifications
            RedirectUrl = "https://appraisify.app/auth/altomate-callback",
            Audience = "appraisify",
            Active = true,
        });
    }

    private static async Task<Project> SeedAttendanceProjectAsync(IProjectRepository projects)
    {
        var existing = (await projects.GetAllAsync())
            .FirstOrDefault(p => p.OrganizationId == DemoOrgId && !p.IsArchived);
        if (existing is not null) return existing;

        return await projects.AddAsync(new Project
        {
            Id = "proj-demo-site",
            OrganizationId = DemoOrgId,
            Name = "HQ Office",
            Latitude = 3.1478,
            Longitude = 101.6953,
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static async Task SeedAttendanceAsync(
        IAttendanceRepository attendance,
        IAttendanceApprovalRequestRepository approvalRequests,
        string projectId)
    {
        var now = DateTime.UtcNow;
        var employeeRows = new[]
        {
            Row("usr-emp", 0, 9, 26, null, null, AttendanceStatus.LATE, 26, 238),
            Row("usr-emp", 1, 8, 57, 18, 5, AttendanceStatus.ON_TIME, null, 8),
            Row("usr-emp", 2, 9, 18, 18, 22, AttendanceStatus.LATE, 18, 316),
            Row("usr-emp", 3, 8, 51, 17, 45, AttendanceStatus.ON_TIME, null, 5),
            Row("usr-emp", 4, 9, 7, 18, 16, AttendanceStatus.LATE, 7, 9),
            Row("usr-emp", 5, null, null, null, null, AttendanceStatus.MISSING, null, null),
            Row("usr-emp", 6, 8, 54, 18, 1, AttendanceStatus.ON_TIME, null, 4),
            Row("usr-emp", 8, 8, 59, 18, 7, AttendanceStatus.ON_TIME, null, 7),
            Row("usr-emp", 9, 9, 24, 18, 30, AttendanceStatus.LATE, 24, 452),
            Row("usr-emp", 10, 8, 48, 17, 55, AttendanceStatus.ON_TIME, null, 6),
            Row("usr-emp", 11, 9, 2, 18, 10, AttendanceStatus.LATE, 2, 10),
            Row("usr-emp", 14, 8, 56, 18, 4, AttendanceStatus.ON_TIME, null, 3),
            Row("usr-emp", 15, 8, 52, 17, 50, AttendanceStatus.ON_TIME, null, 4),
            Row("usr-emp", 16, 9, 31, 18, 12, AttendanceStatus.LATE, 31, 287),
            Row("usr-emp", 17, 8, 58, 18, 3, AttendanceStatus.ON_TIME, null, 6),
            Row("usr-emp", 18, null, null, null, null, AttendanceStatus.ON_LEAVE, null, null),
            Row("usr-emp", 21, 8, 50, 18, 2, AttendanceStatus.ON_TIME, null, 5),
        };

        var supervisorRows = new[]
        {
            Row("usr-super", 0, 8, 45, 17, 55, AttendanceStatus.CLOCKED_OUT, null, 2),
            Row("usr-super", 1, 8, 42, 17, 50, AttendanceStatus.ON_TIME, null, 3),
            Row("usr-super", 2, 9, 12, 18, 20, AttendanceStatus.LATE, 12, 341),
            Row("usr-super", 3, 8, 48, 17, 58, AttendanceStatus.ON_TIME, null, 4),
            Row("usr-super", 4, 8, 55, 18, 5, AttendanceStatus.ON_TIME, null, 5),
        };

        var todayKey = AttendanceTime.StartOfLocalDay(now);

        foreach (var row in employeeRows.Concat(supervisorRows))
        {
            var date = AttendanceTime.StartOfLocalDay(now.AddDays(-row.DaysAgo));

            if (date == todayKey) continue;
            DateTime? timeIn = row.InHour is null ? null : LocalToUtc(date, row.InHour.Value, row.InMinute!.Value);
            DateTime? timeOut = row.OutHour is null ? null : LocalToUtc(date, row.OutHour.Value, row.OutMinute!.Value);
            var duration = timeIn is not null && timeOut is not null
                ? (int)Math.Round((timeOut.Value - timeIn.Value).TotalMinutes)
                : (int?)null;
            // Seed a day only if it's empty. This used to overwrite, and the
            // seeder runs on EVERY startup — so restarting the API to pick up a
            // code change quietly replaced real clocks with demo times. First it
            // ate a real off-site clock-in (fixed by skipping today); then it ate
            // a shift that had been left open overnight, which is precisely the
            // case the "close your old shift first" rule exists to handle.
            //
            // Demo history is a starting point, not something to keep enforcing.
            // The cost is that editing the rows above no longer changes an
            // already-seeded database — worth it against silently destroying
            // whatever someone was actually testing.
            var existing = await attendance.GetForEmployeeOnDateAsync(row.EmployeeId, date);
            if (existing is not null) continue;

            var record = new AttendanceRecord { OrganizationId = DemoOrgId, EmployeeId = row.EmployeeId };
            var approvalStatus = ApplyDemoAttendance(record, row, projectId, date, timeIn, timeOut, duration);
            record = await attendance.AddAsync(record);

            // The record was just created, so it has no approval history to
            // stomp — a day that already existed was skipped above.
            if (timeIn is null) continue;

            var eventAt = timeOut ?? timeIn.Value;
            await approvalRequests.AddAsync(new AttendanceApprovalRequest
            {
                OrganizationId = DemoOrgId,
                EmployeeId = row.EmployeeId,
                Kind = timeOut is null ? AttendanceApprovalKind.CLOCK_IN : AttendanceApprovalKind.CLOCK_OUT,
                AttendanceRecordId = record.Id,
                EventAt = eventAt,
                ApprovalStatus = approvalStatus,
                CurrentStep = 0,
                SubmittedAt = timeIn.Value,
                DecidedAt = approvalStatus == AttendanceApprovalStatus.APPROVED ? eventAt : null,
                CreatedAt = eventAt,
                UpdatedAt = eventAt,
            });
        }
    }

    private static AttendanceApprovalStatus ApplyDemoAttendance(
        AttendanceRecord record,
        DemoAttendanceRow row,
        string projectId,
        DateTime date,
        DateTime? timeIn,
        DateTime? timeOut,
        int? duration)
    {
        var offSite = row.DistanceMeters > 200;

        record.Date = date;
        record.TimeIn = timeIn;
        record.TimeOut = timeOut;
        record.DurationMin = duration;
        record.LateByMin = row.LateByMin;
        record.ProjectId = timeIn is null ? null : projectId;
        record.Status = row.Status;
        var approvalStatus =
            timeIn is null || row.Status == AttendanceStatus.ON_LEAVE
                ? AttendanceApprovalStatus.APPROVED
                : row.Status == AttendanceStatus.LATE || offSite
                    ? AttendanceApprovalStatus.PENDING
                    : AttendanceApprovalStatus.APPROVED;
        record.ClockInLat = timeIn is null ? null : offSite ? 3.1509 : 3.1478;
        record.ClockInLng = timeIn is null ? null : offSite ? 101.6984 : 101.6953;
        record.ClockInDistanceMeters = row.DistanceMeters;
        record.ClockOutLat = timeOut is null ? null : offSite ? 3.1510 : 3.1479;
        record.ClockOutLng = timeOut is null ? null : offSite ? 101.6982 : 101.6952;
        record.ClockOutDistanceMeters = timeOut is null || row.DistanceMeters is null ? null : row.DistanceMeters + 2;
        record.Location = timeIn is null ? null : offSite ? "Client site" : "HQ Office";
        record.Notes = row.Status == AttendanceStatus.ON_LEAVE
            ? "Annual leave"
            : offSite
                ? "Off-site clock recorded for demo data"
                : null;
        record.Remark = row.Status == AttendanceStatus.MISSING
            ? "Demo missing day"
            : offSite
                ? "Client visit / field work"
                : null;
        record.CreatedAt = timeIn ?? date;
        record.UpdatedAt = timeOut ?? timeIn ?? date;
        return approvalStatus;
    }

    private static DemoAttendanceRow Row(
        string employeeId,
        int daysAgo,
        int? inHour,
        int? inMinute,
        int? outHour,
        int? outMinute,
        AttendanceStatus status,
        int? lateByMin,
        double? distanceMeters) =>
        new(employeeId, daysAgo, inHour, inMinute, outHour, outMinute, status, lateByMin, distanceMeters);

    private static DateTime LocalToUtc(DateTime dateKey, int hour, int minute)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(AttendanceTime.DefaultTimeZone);
        var local = new DateTime(dateKey.Year, dateKey.Month, dateKey.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private sealed record DemoAttendanceRow(
        string EmployeeId,
        int DaysAgo,
        int? InHour,
        int? InMinute,
        int? OutHour,
        int? OutMinute,
        AttendanceStatus Status,
        int? LateByMin,
        double? DistanceMeters);

    private static async Task SeedPolicyAsync(IEmployeePolicyRepository policies)
    {
        if ((await policies.GetAllAsync()).Count > 0) return;

        var now = DateTime.UtcNow;
        await policies.AddAsync(new EmployeePolicy
        {
            OrganizationId = DemoOrgId,   // set explicitly — no request context during seeding
            Name = "Full-time",
            Description = "Default policy: geofenced attendance, standard leave.",
            IsDefault = true,
            RequireGeofence = true,
            SalaryType = SalaryType.MONTHLY,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static async Task SeedLeaveTypesAsync(ILeaveTypeRepository leaveTypes)
    {
        if ((await leaveTypes.GetAllAsync()).Count > 0) return;

        var now = DateTime.UtcNow;
        LeaveType make(string code, string name, bool paid, double days) => new()
        {
            OrganizationId = DemoOrgId,   // set explicitly — no request context during seeding
            Code = code,
            Name = name,
            Paid = paid,
            DefaultDays = days,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Code must be ANNUAL, not an abbreviation — LeaveTypeService.IsAnnualCode
        // (and LeaveDefaults.AnnualCode) key the pro-rated/carry-forward
        // restriction off this exact code, so a seeded "AL" could never use them.
        await leaveTypes.AddAsync(make("ANNUAL", "Annual Leave", true, 14));
        await leaveTypes.AddAsync(make("MC", "Medical Leave", true, 14));
        await leaveTypes.AddAsync(make("UL", "Unpaid Leave", false, 0));
    }

    // Demo overtime for Evan, whose approver is Sara (via their shared Team).
    //
    // Idempotent per ROW, on a fixed id — not "skip if the table has anything",
    // which the attendance rows use. That rule would have made this a no-op on
    // any database that already had a single overtime request in it, which is
    // every database anyone has actually clicked around in. Keying on the id
    // seeds what's missing and never touches a row you created or decided.
    //
    // One PENDING row deliberately has no after-work photo. ApproveAsync refuses
    // without one, so a queue of rows that all had it would never exercise the
    // gate; this way Sara's queue shows one she can sign off and one she can't yet.
    private static async Task SeedOvertimeAsync(
        IOvertimeRepository requests,
        IOvertimePhotoStorage photos,
        string projectId)
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            //                 id             daysAgo  start   end     status                   after  project
            new DemoOvertimeRow("ot-demo-1",  3,      19, 0,  21, 30, OvertimeStatus.PENDING,  true,  true,
                "Client demo build — stayed back to finish the deployment.", null),
            new DemoOvertimeRow("ot-demo-2",  2,      18, 30, 20, 0,  OvertimeStatus.PENDING,  false, false,
                "Covered the evening support shift.", null),
            new DemoOvertimeRow("ot-demo-3",  9,      19, 0,  22, 0,  OvertimeStatus.APPROVED, true,  true,
                "Month-end stock count with the warehouse team.", null),
            new DemoOvertimeRow("ot-demo-4",  12,     20, 0,  23, 0,  OvertimeStatus.REJECTED, true,  false,
                "Reworked the report layout.", "Not urgent — do this in normal hours next week."),
        };

        var missing = new List<DemoOvertimeRow>();
        foreach (var row in rows)
            if (await requests.GetByIdAsync(row.Id) is null) missing.Add(row);

        // Nothing to add → return BEFORE storing photos. StoreAsync names each
        // file by timestamp, so calling it unconditionally would litter the
        // upload directory with two more PNGs on every startup.
        if (missing.Count == 0) return;

        // Real files on disk, not made-up URLs: the photo route 404s on a missing
        // file, and a demo row whose evidence won't load is a bug report waiting
        // to happen.
        var beforePhotoUrl = await StoreDemoPhotoAsync(photos, "demo-overtime-before.png", DemoBeforePhotoPng);
        var afterPhotoUrl = await StoreDemoPhotoAsync(photos, "demo-overtime-after.png", DemoAfterPhotoPng);

        foreach (var row in missing)
        {
            var workDate = AttendanceTime.StartOfLocalDay(now.AddDays(-row.DaysAgo));
            var startAt = LocalToUtc(workDate, row.StartHour, row.StartMinute);
            var endAt = LocalToUtc(workDate, row.EndHour, row.EndMinute);
            var decided = row.Status is OvertimeStatus.APPROVED or OvertimeStatus.REJECTED;

            await requests.AddAsync(new OvertimeRequest
            {
                Id = row.Id,
                OrganizationId = DemoOrgId,   // set explicitly — no request context during seeding
                EmployeeId = "usr-emp",
                ProjectId = row.OnProject ? projectId : null,
                WorkDate = workDate,
                StartAt = startAt,
                EndAt = endAt,
                RequestedMinutes = (int)Math.Round((endAt - startAt).TotalMinutes),
                Reason = row.Reason,
                BeforePhotoUrl = beforePhotoUrl,
                AfterPhotoUrl = row.HasAfterPhoto ? afterPhotoUrl : null,
                Status = row.Status,
                CurrentStep = 0,
                ReviewNotes = row.ReviewNotes,
                SubmittedAt = endAt,
                DecidedAt = decided ? endAt.AddHours(14) : null,
                CreatedAt = endAt,
                UpdatedAt = decided ? endAt.AddHours(14) : endAt,
            });
        }
    }

    private static async Task<string> StoreDemoPhotoAsync(
        IOvertimePhotoStorage photos, string fileName, string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        await using var content = new MemoryStream(bytes);
        var stored = await photos.StoreAsync(
            new OvertimePhotoUpload(fileName, "image/png", bytes.Length, content));
        return stored.PhotoUrl;
    }

    // Demo leave for Evan: two PENDING rows for Sara's queue, plus a decided pair
    // so the history and audit-trail screens aren't empty. Per-row idempotency on
    // a fixed id, for the same reason as the overtime rows above.
    //
    // The decided rows carry a real Approvals trail, not just ReviewNotes —
    // ReviewNotes holds the LAST decision only, so a row without a trail shows an
    // empty audit log, which reads as a bug rather than as demo data.
    private static async Task SeedLeaveAsync(
        ILeaveApplicationRepository applications,
        ILeaveTypeRepository leaveTypes)
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            //               id            type      start  end   duration                 days  status
            new DemoLeaveRow("lv-demo-1",  Annual,    5,    7,  LeaveDuration.FULL_DAY,  3,    LeaveStatus.PENDING,
                "Family trip — flights already booked.", null),
            new DemoLeaveRow("lv-demo-2",  Medical,  -1,   -1,  LeaveDuration.MORNING,   0.5,  LeaveStatus.PENDING,
                "Clinic appointment, back after lunch.", null),
            new DemoLeaveRow("lv-demo-3",  Annual,  -24,  -23,  LeaveDuration.FULL_DAY,  2,    LeaveStatus.APPROVED,
                "Long weekend.", "Approved — enjoy the break."),
            new DemoLeaveRow("lv-demo-4",  Unpaid,  -11,  -10,  LeaveDuration.FULL_DAY,  2,    LeaveStatus.REJECTED,
                "Personal errand.", "Month-end close that week — please refile for the following one."),
        };

        foreach (var row in rows)
        {
            if (await applications.GetByIdAsync(row.Id) is not null) continue;

            // Skip rather than invent a type: an application pointing at a
            // LeaveTypeId that doesn't exist is worse than a missing demo row.
            var type = await ResolveLeaveTypeAsync(leaveTypes, row.TypeCodes);
            if (type is null) continue;

            var startDate = AttendanceTime.StartOfLocalDay(now.AddDays(row.StartDaysFromToday));
            var endDate = AttendanceTime.StartOfLocalDay(now.AddDays(row.EndDaysFromToday));
            var submittedAt = now.AddDays(row.StartDaysFromToday - 4);
            var decided = row.Status is LeaveStatus.APPROVED or LeaveStatus.REJECTED;
            var decidedAt = decided ? submittedAt.AddDays(1) : (DateTime?)null;

            await applications.AddAsync(new LeaveApplication
            {
                Id = row.Id,
                OrganizationId = DemoOrgId,   // set explicitly — no request context during seeding
                EmployeeId = "usr-emp",
                LeaveTypeId = type.Id,
                StartDate = startDate,
                EndDate = endDate,
                Duration = row.Duration,
                TotalDays = row.TotalDays,
                Reason = row.Reason,
                Status = row.Status,
                CurrentStep = 0,
                ReviewNotes = row.ReviewNotes,
                DecidedAt = decidedAt,
                Approvals = decidedAt is null
                    ? null
                    : DemoApprovalTrail(row.Status.ToString(), row.ReviewNotes, decidedAt.Value),
                CreatedAt = submittedAt,
                UpdatedAt = decidedAt ?? submittedAt,
            });
        }
    }

    // Two vocabularies for the same three types are in the wild: SeedLeaveTypesAsync
    // below writes AL/MC/UL, LeaveDefaults writes ANNUAL/MEDICAL/UNPAID, and a
    // database that has been through both has a MIX of them. Hardcoding one set
    // silently dropped the annual-leave rows on exactly such a database, so each
    // demo row names every code it would accept and takes the first that resolves.
    private static readonly string[] Annual = ["AL", "ANNUAL"];
    private static readonly string[] Medical = ["MC", "MEDICAL"];
    private static readonly string[] Unpaid = ["UL", "UNPAID"];

    private static async Task<LeaveType?> ResolveLeaveTypeAsync(
        ILeaveTypeRepository leaveTypes, string[] codes)
    {
        foreach (var code in codes)
        {
            var type = await leaveTypes.GetByCodeAsync(code);
            if (type is not null) return type;
        }

        return null;
    }

    // Mirrors LeaveService.AppendTrail: camelCase JSON, one entry per decision.
    private static string DemoApprovalTrail(string decision, string? notes, DateTime decidedAt) =>
        JsonSerializer.Serialize(
            new List<LeaveApprovalEntryDto>
            {
                new()
                {
                    Step = 0,
                    ApproverId = "usr-super",
                    Decision = decision,
                    DecidedAt = decidedAt,
                    Notes = notes,
                },
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private sealed record DemoOvertimeRow(
        string Id,
        int DaysAgo,
        int StartHour,
        int StartMinute,
        int EndHour,
        int EndMinute,
        OvertimeStatus Status,
        bool HasAfterPhoto,
        bool OnProject,
        string Reason,
        string? ReviewNotes);

    private sealed record DemoLeaveRow(
        string Id,
        string[] TypeCodes,
        int StartDaysFromToday,
        int EndDaysFromToday,
        LeaveDuration Duration,
        double TotalDays,
        LeaveStatus Status,
        string Reason,
        string? ReviewNotes);

    private static async Task SeedOrganizationAsync(IOrganizationRepository organizations)
    {
        var existing = await organizations.GetByIdAsync(DemoOrgId);
        if (existing is not null)
        {
            // Backfill the package on the pre-plan demo org so it keeps full access
            // (Claims + Attendance) after the plan columns shipped.
            if (existing.Plan == OrgPlan.DIY && existing.Tier is null)
            {
                existing.Tier = OrgPlanTier.PAID;
                existing.Addons = "expense_claim,clock";
                await organizations.UpdateAsync(existing);
            }
            return;
        }

        await organizations.AddAsync(new Organization
        {
            Id = DemoOrgId,
            Name = "AltomateHR Demo Co",
            DefaultCurrency = "MYR",
            DefaultMileageRate = 0.60m,
            GeofenceRadiusMeters = 200,
            Plan = OrgPlan.DIY,
            Tier = OrgPlanTier.PAID,
            Addons = "expense_claim,clock",
            CreatedAt = DateTime.UtcNow,
        });
    }

    // Create the login account if missing, then ensure it has a membership (with
    // its role) in the demo org. Role/supervisor/policy are per-org, so they live
    // on the membership — not the global User.
    private static async Task EnsureUserAsync(
        IUserRepository users, IOrganizationMembershipRepository memberships,
        string id, string email, string role, string name, string jobTitle)
    {
        var user = await users.GetByEmailAsync(email);
        if (user is null)
        {
            await users.AddAsync(new User
            {
                Id = id,
                Email = email,
                Name = name,
                PasswordHash = BC.HashPassword("password123"),   // hashed at seed time, never stored plain
                CreatedAt = DateTime.UtcNow,
            });
        }
        else if (string.IsNullOrEmpty(user.Name))
        {
            user.Name = name;   // backfill name on demo rows seeded before profiles existed
            await users.UpdateAsync(user);
        }

        var membership = await memberships.GetAsync(DemoOrgId, id);
        if (membership is null)
        {
            await memberships.AddAsync(new OrganizationMembership
            {
                OrganizationId = DemoOrgId,
                UserId = id,
                Role = role,
                JobTitle = jobTitle,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            // Keep the demo seed authoritative for roles (e.g. bumping the founder to
            // Owner), and backfill a job title if the row predates the profile fields.
            var changed = false;
            if (membership.Role != role) { membership.Role = role; changed = true; }
            if (string.IsNullOrEmpty(membership.JobTitle)) { membership.JobTitle = jobTitle; changed = true; }
            if (changed) await memberships.UpdateAsync(membership);
        }
    }

    // Point old demo claims at the real user ids + the demo org.
    private static async Task BackfillClaimsAsync(IClaimsRepository claims)
    {
        var allClaims = await claims.GetAllAsync();
        foreach (var claim in allClaims)
        {
            var changed = false;

            var updatedEmployeeId = claim.EmployeeId switch
            {
                "employee@altomate.com" => "usr-emp",
                "admin@altomate.com" => "usr-admin",
                _ => claim.EmployeeId,
            };
            if (updatedEmployeeId != claim.EmployeeId)
            {
                claim.EmployeeId = updatedEmployeeId;
                changed = true;
            }

            if (string.IsNullOrEmpty(claim.OrganizationId))
            {
                claim.OrganizationId = DemoOrgId;
                changed = true;
            }

            if (changed) await claims.UpdateAsync(claim);
        }
    }
}

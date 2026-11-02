using System.Text;
using System.Text.Json;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Fah;
using AltomateHR.Api.Modules.Fah.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Tests.Employees;

namespace AltomateHR.Api.Tests.Fah;

// FAH's employee import: the model names which column is which, code copies
// every cell, and the real employee import decides what's valid — so these
// run against the real import, with in-memory people.
public class FahImportServiceTests
{
    private const string StaffCsv = """
        Staff Name,Email Address,NRIC / Passport,D.O.B,Gender,Date Joined,Basic Pay (RM)
        Lim Wei Jie,WeiJie.Lim@Example.test,920314-14-5523,14/03/1992,M,01/02/2024,"RM 4,200.00"
        Nguyen Van An,van.an@example.test,C1234567 (Passport),1990-06-30,Male,10/08/2024,1800
        Siti Hajar,existing@example.test,,03/12/1997,F,01/03/2025,2400
        No Birthday,nobday@example.test,950101-10-1234,,F,01/03/2025,2400
        Lim Wei Jie,weijie.lim@example.test,920314-14-5523,14/03/1992,M,01/02/2024,"RM 4,200.00"
        """;

    private static readonly string Mapping = """
        {"file":"staff.csv","columns":{"name":"Staff Name","email":"Email Address","idNumber":"NRIC / Passport",
         "dateOfBirth":"D.O.B","gender":"Gender","joinDate":"Date Joined","monthlySalary":"Basic Pay (RM)"},
         "numberNewPeople":true}
        """;

    private readonly EmployeeImportTests.Harness _h = EmployeeImportTests.Make(
        [EmployeeImportTests.Member("u1", "existing@example.test")]);

    private FahImportService Service(bool payroll = true, AdminAccess? access = null) =>
        new(_h.Service, _h.Employees, new Access(payroll, access ?? AdminAccess.Full));

    private static FahAttachment Sheet(string name = "staff.csv", string csv = StaffCsv)
    {
        var file = FahFiles.Prepare(name, Encoding.UTF8.GetBytes(csv)).File!;
        return new FahAttachment { FileName = file.FileName, TableJson = JsonSerializer.Serialize(file.Table) };
    }

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task AnAdminsOwnSpreadsheet_IsMappedAndChecked_RowByRow()
    {
        var (plan, error) = await Service().PrepareAsync([Sheet()], Input(Mapping));

        Assert.Null(error);
        var rows = plan!.Rows.ToDictionary(r => r.SourceRow);
        Assert.Equal(FahImportRowStatus.Add, rows[2].Status);
        Assert.Equal(FahImportRowStatus.Add, rows[3].Status);
        Assert.Equal(FahImportRowStatus.Update, rows[4].Status);
        // The import's own rule: a new person needs a date of birth.
        Assert.Equal(FahImportRowStatus.Problem, rows[5].Status);
        Assert.Contains("Date of Birth is required", rows[5].Problem);
        // A repeat of row 2 is left out, not applied on top of it.
        Assert.Equal(FahImportRowStatus.LeftOut, rows[6].Status);
        Assert.Contains("row 2", rows[6].Problem);
        Assert.Equal(2, plan.Adds);
        Assert.Equal(1, plan.Updates);
        Assert.Empty(_h.Employees.Created);   // checked, nothing saved
    }

    [Fact]
    public async Task CellsAreCopied_WithOnlyTheFewThingsTheImportWouldRefuse_Tidied()
    {
        var (plan, _) = await Service().PrepareAsync([Sheet()], Input(Mapping));

        var lim = plan!.Rows.Single(r => r.SourceRow == 2).Values;
        Assert.Equal("weijie.lim@example.test", lim["email"]);
        Assert.Equal("MALE", lim["gender"]);
        Assert.Equal("NRIC", lim["idType"]);
        Assert.Equal("RM 4,200.00", lim["monthlySalary"]);   // as typed: the import reads it
        Assert.Equal("14/03/1992", lim["dateOfBirth"]);

        var an = plan.Rows.Single(r => r.SourceRow == 3).Values;
        Assert.Equal("C1234567", an["idNumber"]);
        Assert.Equal("PASSPORT", an["idType"]);
    }

    // Only a new person's ID type is guessed. Someone already here keeps the
    // type on their record: an army number isn't a passport.
    [Fact]
    public async Task SomeoneAlreadyHere_KeepsTheirIdType()
    {
        const string csv = """
            Name,Email,IC
            Siti Hajar,existing@example.test,T1234567
            """;
        var (plan, _) = await Service().PrepareAsync([Sheet(csv: csv)],
            Input("""{"file":"staff.csv","columns":{"name":"Name","email":"Email","idNumber":"IC"}}"""));

        var siti = Assert.Single(plan!.Rows);
        Assert.Equal(FahImportRowStatus.Update, siti.Status);
        Assert.Equal("", siti.Values.GetValueOrDefault("idType", ""));
    }

    // Rows go by their number in Excel, blank rows and all, since that's the
    // number the admin reads off the sheet and says.
    [Fact]
    public async Task RowNumbers_AreExcelsOwn()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet("Staff");
        sheet.Cell(1, 1).Value = "Staff list";
        string[][] lines =
        [
            ["Staff Name", "Email Address", "D.O.B"],
            ["Ali", "ali@example.test", "01/01/1990"],
            [],
            ["Siti", "siti@example.test", "02/02/1991"],
        ];
        for (var r = 0; r < lines.Length; r++)
            for (var c = 0; c < lines[r].Length; c++)
                sheet.Cell(r + 3, c + 1).Value = lines[r][c];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var file = FahFiles.Prepare("staff.xlsx", stream.ToArray()).File!;
        var attachment = new FahAttachment { FileName = file.FileName, TableJson = JsonSerializer.Serialize(file.Table) };

        var (plan, error) = await Service().PrepareAsync([attachment], Input("""
            {"file":"staff.xlsx","headerRow":3,"columns":{"name":"Staff Name","email":"Email Address","dateOfBirth":"D.O.B"},
             "numberNewPeople":true,"skipRows":[4]}
            """));

        Assert.Null(error);
        Assert.Contains("4: Ali,", file.Text);
        Assert.Contains("6: Siti,", file.Text);
        Assert.Equal([4, 6], plan!.Rows.Select(r => r.SourceRow));
        Assert.Equal(FahImportRowStatus.LeftOut, plan.Rows[0].Status);
        Assert.Equal("siti@example.test", plan.Rows[1].Values["email"]);
    }

    // A row with something only in columns that weren't mapped is no one —
    // and must not shift the next rows' problems onto their neighbours.
    [Fact]
    public async Task ARowWithNothingMapped_IsNoOne()
    {
        const string csv = """
            Dept,Name,Email,DOB
            Kitchen,,,
            ,Ali,ali@example.test,01/01/1990
            ,Siti,siti@example.test,
            """;
        var (plan, error) = await Service().PrepareAsync([Sheet(csv: csv)], Input("""
            {"file":"staff.csv","columns":{"name":"Name","email":"Email","dateOfBirth":"DOB"},"numberNewPeople":true}
            """));

        Assert.Null(error);
        Assert.Equal([3, 4], plan!.Rows.Select(r => r.SourceRow));
        Assert.Equal(FahImportRowStatus.Add, plan.Rows[0].Status);
        Assert.Equal(FahImportRowStatus.Problem, plan.Rows[1].Status);
    }

    [Fact]
    public async Task NewPeople_AreNumbered_AfterTheCompanysOwnNumbers()
    {
        var (plan, _) = await Service().PrepareAsync([Sheet()], Input(Mapping));

        Assert.Equal("E-002", plan!.Rows.Single(r => r.SourceRow == 2).Values["employeeNumber"]);
        Assert.Equal("E-003", plan.Rows.Single(r => r.SourceRow == 3).Values["employeeNumber"]);
        // Someone already in the company keeps theirs (blank = unchanged).
        Assert.Equal("", plan.Rows.Single(r => r.SourceRow == 4).Values.GetValueOrDefault("employeeNumber", ""));
    }

    [Fact]
    public async Task Confirm_ImportsOnlyTheRowsThatPassed()
    {
        var service = Service();
        var (plan, _) = await service.PrepareAsync([Sheet()], Input(Mapping));

        var (text, saved) = await service.RunAsync(plan!);

        Assert.True(saved);
        Assert.Equal(["weijie.lim@example.test", "van.an@example.test"], _h.Employees.Created.Select(c => c.Email));
        Assert.Equal("u1", Assert.Single(_h.Employees.Updated).Id);
        Assert.Contains("2 added, 1 updated", text);
        // Never a first password (email + birthday), and no welcome emails.
        Assert.DoesNotContain("weijie.lim@example.test0314", text);
        Assert.DoesNotContain(_h.Employees.Created, c => c.SendWelcomeEmail);
    }

    [Fact]
    public async Task TheDownload_IsAnAltomateHrImportFile_WithTheProblemRowsToFix()
    {
        var service = Service();
        var (plan, _) = await service.PrepareAsync([Sheet()], Input(Mapping));

        var file = await service.BuildFileAsync(plan!);

        Assert.Equal("employees-import-staff.xlsx", file.FileName);
        var employees = TabularReader.ReadAllSheets(file.Content, TabularFormat.Xlsx).Single(s => s.Name == "Employees");
        Assert.Equal(5, employees.Rows.Count);   // header + 4: the repeat is left out, the problem row kept
        Assert.Contains(employees.Rows, r => r.Contains("nobday@example.test"));
    }

    // What the admin tells FAH about particular people goes into the same
    // import, by row — Aisyah's email, a birthday the file lacked.
    [Fact]
    public async Task DetailsGivenInTheChat_GoIntoTheirRows()
    {
        var input = Mapping.Replace("\"numberNewPeople\":true",
            "\"numberNewPeople\":true,\"edits\":{\"5\":{\"dateOfBirth\":\"1995-01-01\"},\"2\":{\"epfNumber\":\"12345678\"}}");

        var (plan, error) = await Service().PrepareAsync([Sheet()], Input(input));

        Assert.Null(error);
        var row5 = plan!.Rows.Single(r => r.SourceRow == 5);
        Assert.Equal(FahImportRowStatus.Add, row5.Status);   // no longer missing its birthday
        Assert.Equal("12345678", plan.Rows.Single(r => r.SourceRow == 2).Values["epfNumber"]);
        Assert.Contains("epfNumber", plan.Keys);
    }

    [Fact]
    public async Task AnEditForARowThatIsntThere_IsRefused()
    {
        var input = Mapping.Replace("\"numberNewPeople\":true", "\"edits\":{\"99\":{\"name\":\"X\"}}");

        var (_, error) = await Service().PrepareAsync([Sheet()], Input(input));

        Assert.Contains("isn't a row of people", error);
    }

    [Theory]
    [InlineData("""{"file":"other.csv","columns":{"email":"Email Address"}}""", "Attached: staff.csv")]
    [InlineData("""{"file":"staff.csv","columns":{"email":"E-mail"}}""", "no column headed \"E-mail\"")]
    [InlineData("""{"file":"staff.csv","columns":{"name":"Staff Name"}}""", "Map the email column")]
    [InlineData("""{"file":"staff.csv","columns":{"email":"Email Address","salary":"Basic Pay (RM)"}}""", "isn't an AltomateHR field")]
    [InlineData("""{"file":"staff.csv","columns":{"email":"Email Address"},"values":{"monthlySalary":"3000"}}""", "differs from person to person")]
    public async Task AMappingThatDoesntFit_TellsTheModelWhy(string input, string expected)
    {
        var (plan, error) = await Service().PrepareAsync([Sheet()], Input(input));

        Assert.Null(plan);
        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task WithoutPayrollAccess_PayrollFieldsAreRefused()
    {
        var (_, error) = await Service(payroll: false).PrepareAsync([Sheet()], Input(Mapping));

        Assert.Contains("payroll field", error);
    }

    [Fact]
    public async Task AnAdminLimitedToSomePolicies_IsNotOfferedTheImport()
    {
        var limited = new AdminAccess(true, null, false, ["pol-1"]);

        Assert.Null(await Service(access: limited).ToolAsync());
    }

    [Fact]
    public async Task AnAdminWhoCanOnlyViewEmployees_IsNotOfferedTheImport()
    {
        var viewOnly = new AdminAccess(true, new Dictionary<string, ModuleLevel> { ["employees"] = ModuleLevel.View }, false, null);

        Assert.Null(await Service(access: viewOnly).ToolAsync());
    }

    private sealed class Access(bool payroll, AdminAccess access) : IModuleAccessService
    {
        private static readonly string[] Base = ["employees", "leave"];

        public Task<IReadOnlyCollection<string>> GetEnabledModulesAsync() =>
            Task.FromResult<IReadOnlyCollection<string>>(payroll ? [.. Base, "payroll"] : Base);

        public Task<IReadOnlyCollection<string>> GetOrgModulesAsync() =>
            Task.FromResult<IReadOnlyCollection<string>>([.. Base, "payroll"]);

        public Task<AdminAccess> GetAccessAsync() => Task.FromResult(access);

        public Task<ModuleLevel> GetCallerLevelAsync(string module) => Task.FromResult(access.LevelFor(module));
    }
}

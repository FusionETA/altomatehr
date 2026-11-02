using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Fah.Entities;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Fah;

public partial class FahImportService : IFahImportService
{
    // Fields that differ person to person: never one value for every row.
    private static readonly HashSet<string> PerPerson = new(StringComparer.Ordinal)
    {
        EmployeeImportSheet.EmailKey, EmployeeImportSheet.NameKey, EmployeeImportSheet.EmployeeNumberKey,
        EmployeeImportSheet.DateOfBirthKey, EmployeeImportSheet.NicknameKey, "idNumber", "phone",
        "alternateEmail", "addressLine1", "addressLine2", "postcode", "emergencyContactName",
        "emergencyContactPhone", "spouseIdNumber", "spousePcbNumber", "epfNumber", "socsoNumber",
        "incomeTaxNumber", "bankAccountNumber", "bankAccountHolderName", "workPermitNumber",
        "monthlySalary", "hourlyRate",
    };

    private readonly IEmployeeImportService _import;
    private readonly IEmployeeService _employees;
    private readonly IModuleAccessService _access;

    public FahImportService(IEmployeeImportService import, IEmployeeService employees, IModuleAccessService access)
    {
        _import = import;
        _employees = employees;
        _access = access;
    }

    public async Task<FahTool?> ToolAsync()
    {
        // The Import screen's own gates: Manage on Employees (a View grant
        // can't write), and every employee in scope.
        if (await _access.GetCallerLevelAsync(OrgModules.Employees) != ModuleLevel.Manage) return null;
        if (!(await _access.GetAccessAsync()).HasFullEmployeeScope) return null;
        var modules = await _access.GetEnabledModulesAsync();

        var payroll = modules.Contains(OrgModules.Payroll, StringComparer.OrdinalIgnoreCase);
        var fields = string.Join("; ", EmployeeImportSheet.ColumnsFor(payroll).Select(c => $"{c.Key} ({c.Label})"));
        var description =
            "Add or update many employees at once from a spreadsheet the admin attached (.xlsx or .csv), "
            + "through AltomateHR's employee import. Say which of the file's columns is which AltomateHR field; "
            + "AltomateHR copies every cell itself and checks every row the way its Import screen does. "
            + "The admin then sees one card for the whole file — who is added, who is updated, which rows have "
            + "problems — with Confirm, Cancel and a download of the import file. Nothing is saved until they "
            + "press Confirm, and the import sends no emails.\n\n"
            + "An email already in the company updates that person (blank cells change nothing); a new email "
            + "adds a person, who needs a name, an employee number and a date of birth (their first password "
            + "is their email followed by their birthday as MMDD). Details the admin gives you in the chat for "
            + "particular people go in edits — call this again with them, rather than adding anyone one by one.\n\n"
            + $"AltomateHR fields: {fields}.";

        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["file"] = new { type = "string", description = "The attached spreadsheet's file name." },
                ["sheet"] = new { type = "string", description = "For a workbook: the sheet to read. Default: the first sheet with data." },
                ["headerRow"] = new { type = "integer", description = "The row holding the column headings, 1 = the first row. Default 1." },
                ["columns"] = new
                {
                    type = "object",
                    description = "AltomateHR field → the file's column heading, exactly as written in the file. Must include email.",
                    additionalProperties = new { type = "string" },
                },
                ["values"] = new
                {
                    type = "object",
                    description = "AltomateHR field → one value for every row, for a field the file has no column for "
                        + "(e.g. {\"role\":\"Employee\"}). Not for per-person fields like name, IC or bank account.",
                    additionalProperties = new { type = "string" },
                },
                ["edits"] = new
                {
                    type = "object",
                    description = "Details the admin gave you for particular rows, by the row's number in the file: "
                        + "{\"7\":{\"epfNumber\":\"12345678\"},\"10\":{\"email\":\"aisyah@example.com\"}}. "
                        + "These replace what the file says for that row. Only what the admin actually told you.",
                    additionalProperties = new { type = "object", additionalProperties = new { type = "string" } },
                },
                ["numberNewPeople"] = new
                {
                    type = "boolean",
                    description = "Give new people who have no employee number the company's next numbers, in row order.",
                },
                ["skipRows"] = new
                {
                    type = "array",
                    items = new { type = "integer" },
                    description = "Row numbers (as in the file, 1 = first row) to leave out.",
                },
            },
            required = new[] { "file", "columns" },
        });

        return new FahTool(IFahImportService.ToolName, description, schema);
    }

    public async Task<(FahImportPlan? Plan, string? Error)> PrepareAsync(IReadOnlyList<FahAttachment> sheets, JsonElement input)
    {
        if (await ToolAsync() is null)
            return (null, "This admin can't import employees: it needs Manage access to Employees, covering every employee.");
        var payroll = (await _access.GetEnabledModulesAsync()).Contains(OrgModules.Payroll, StringComparer.OrdinalIgnoreCase);
        var allowed = EmployeeImportSheet.ColumnsFor(payroll).ToDictionary(c => c.Key, StringComparer.Ordinal);

        // ---- The file and sheet ----
        var fileName = Str(input, "file");
        var spreadsheets = sheets.Where(s => s.TableJson is not null).ToList();
        var attachment = spreadsheets.LastOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (attachment is null)
            return (null, spreadsheets.Count == 0
                ? "No spreadsheet (.xlsx or .csv) is attached in this chat."
                : $"No attached spreadsheet is called \"{fileName}\". Attached: {string.Join(", ", spreadsheets.Select(s => s.FileName))}.");

        var tables = JsonSerializer.Deserialize<List<FahFiles.FahSheet>>(attachment.TableJson!) ?? [];
        var sheetName = Str(input, "sheet");
        var table = string.IsNullOrWhiteSpace(sheetName)
            ? tables.FirstOrDefault()
            : tables.FirstOrDefault(t => string.Equals(t.Name, sheetName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (table is null)
            return (null, $"\"{attachment.FileName}\" has no sheet called \"{sheetName}\". Sheets: {string.Join(", ", tables.Select(t => t.Name))}.");

        var headerRow = input.TryGetProperty("headerRow", out var h) && h.TryGetInt32(out var hr) ? hr : 1;
        if (headerRow < 1 || headerRow >= table.Rows.Count)
            return (null, $"Row {headerRow} can't be the heading row: the sheet has {table.Rows.Count} rows.");
        var headings = table.Rows[headerRow - 1];

        // ---- Which column is which field ----
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        if (input.TryGetProperty("columns", out var mapping) && mapping.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in mapping.EnumerateObject())
            {
                if (KeyProblem(pair.Name, allowed, payroll) is { } keyProblem) return (null, keyProblem);
                var heading = pair.Value.GetString()?.Trim() ?? string.Empty;
                var index = headings.ToList().FindIndex(c => string.Equals(c.Trim(), heading, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    return (null, $"The sheet has no column headed \"{heading}\". Its headings (row {headerRow}): "
                        + string.Join(", ", headings.Where(c => c.Trim().Length > 0).Select(c => $"\"{c.Trim()}\"")) + ".");
                columns[pair.Name] = index;
            }
        }
        if (!columns.ContainsKey(EmployeeImportSheet.EmailKey))
            return (null, "Map the email column: the import finds each person by email. If the file has none, tell the admin it's needed.");

        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        if (input.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in values.EnumerateObject())
            {
                if (KeyProblem(pair.Name, allowed, payroll) is { } keyProblem) return (null, keyProblem);
                if (PerPerson.Contains(pair.Name))
                    return (null, $"{allowed[pair.Name].Label} differs from person to person, so it can't be one value for everyone.");
                constants[pair.Name] = pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString()!.Trim() : pair.Value.GetRawText();
            }
        }

        // Details the admin gave in the chat, by source row number.
        var edits = new Dictionary<int, Dictionary<string, string>>();
        if (input.TryGetProperty("edits", out var editsJson) && editsJson.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in editsJson.EnumerateObject())
            {
                if (!int.TryParse(pair.Name, out var rowNumber) || rowNumber <= headerRow || rowNumber > table.Rows.Count)
                    return (null, $"\"{pair.Name}\" in edits isn't a row of people in the sheet (rows {headerRow + 1} to {table.Rows.Count}).");
                if (pair.Value.ValueKind != JsonValueKind.Object)
                    return (null, $"Edits for row {rowNumber} must be an object of field → value.");
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in pair.Value.EnumerateObject())
                {
                    if (KeyProblem(field.Name, allowed, payroll) is { } keyProblem) return (null, keyProblem);
                    fields[field.Name] = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString()!.Trim() : field.Value.GetRawText();
                }
                edits[rowNumber] = fields;
            }
        }

        var skip = input.TryGetProperty("skipRows", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Where(e => e.TryGetInt32(out _)).Select(e => e.GetInt32()).ToHashSet()
            : [];
        var numberNewPeople = input.TryGetProperty("numberNewPeople", out var n) && n.ValueKind == JsonValueKind.True;

        // ---- The rows, in the import's layout ----
        var existing = (await _employees.GetAllAsync()).ToList();
        var existingEmails = existing.Select(e => e.Email.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenNumbers = existing.Select(e => e.EmployeeNumber).ToList();

        var keys = columns.Keys.Concat(constants.Keys).Concat(edits.Values.SelectMany(e => e.Keys)).ToHashSet(StringComparer.Ordinal);
        if (keys.Contains("idNumber")) keys.Add("idType");
        if (numberNewPeople) keys.Add(EmployeeImportSheet.EmployeeNumberKey);
        var orderedKeys = EmployeeImportSheet.Columns.Select(c => c.Key).Where(keys.Contains).ToList();

        var rows = new List<FahImportRow>();
        var firstRowOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = headerRow; i < table.Rows.Count; i++)
        {
            var cells = table.Rows[i];
            var sourceRow = i + 1;
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            var row = new Dictionary<string, string>(constants, StringComparer.Ordinal);
            foreach (var (key, index) in columns)
            {
                var cell = index < cells.Count ? cells[index].Trim() : string.Empty;
                if (cell.Length > 0 || !row.ContainsKey(key)) row[key] = cell;
            }
            if (edits.TryGetValue(sourceRow, out var edit))
                foreach (var (key, value) in edit) row[key] = value;
            Normalise(row);

            var email = row.GetValueOrDefault(EmployeeImportSheet.EmailKey) ?? string.Empty;
            if (skip.Contains(sourceRow))
            {
                rows.Add(new FahImportRow(sourceRow, row, FahImportRowStatus.LeftOut, "Left out, as asked."));
                continue;
            }
            if (email.Length > 0 && firstRowOf.TryGetValue(email, out var first))
            {
                rows.Add(new FahImportRow(sourceRow, row, FahImportRowStatus.LeftOut,
                    $"Same email as row {first}, so it's left out (the import would apply it on top of row {first})."));
                continue;
            }
            if (email.Length > 0) firstRowOf[email] = sourceRow;

            if (numberNewPeople && email.Length > 0 && !existingEmails.Contains(email)
                && string.IsNullOrWhiteSpace(row.GetValueOrDefault(EmployeeImportSheet.EmployeeNumberKey)))
            {
                var next = EmployeeNumbers.Next(takenNumbers);
                takenNumbers.Add(next);
                row[EmployeeImportSheet.EmployeeNumberKey] = next;
            }

            rows.Add(new FahImportRow(sourceRow, row,
                existingEmails.Contains(email) ? FahImportRowStatus.Update : FahImportRowStatus.Add));
        }
        if (rows.Count == 0) return (null, "The sheet has headings but no rows of people under them.");

        // ---- Checked by the import itself, saving nothing ----
        var candidates = rows.Where(r => r.Status is FahImportRowStatus.Add or FahImportRowStatus.Update).ToList();
        if (candidates.Count == 0)
            return (null, "Every row is left out (repeats or skipped rows), so there's nothing to import.");
        var check = await _import.ImportAsync(Csv(orderedKeys, candidates), TabularFormat.Csv, checkOnly: true);
        if (check.Message is { } fileProblem) return (null, $"The import refused the file: {fileProblem}");

        // Row 2 of the generated file is candidates[0].
        var problems = check.Errors.GroupBy(e => e.Row).ToDictionary(g => g.Key, g => string.Join(" ", g.Select(e => e.Message)));
        var notes = check.Warnings.GroupBy(e => e.Row).ToDictionary(g => g.Key, g => string.Join(" ", g.Select(e => e.Message)));
        var fileRowOf = candidates.Select((r, at) => (r.SourceRow, FileRow: at + 2)).ToDictionary(x => x.SourceRow, x => x.FileRow);
        var checkedRows = rows.Select(r =>
        {
            if (!fileRowOf.TryGetValue(r.SourceRow, out var fileRow)) return r;
            return problems.TryGetValue(fileRow, out var problem)
                ? r with { Status = FahImportRowStatus.Problem, Problem = problem }
                : r with { Note = notes.GetValueOrDefault(fileRow) };
        }).ToList();

        return (new FahImportPlan(attachment.FileName, orderedKeys, checkedRows, check.Created, check.Updated), null);
    }

    public async Task<(string Text, bool Saved)> RunAsync(FahImportPlan plan)
    {
        // Access may have changed since the card was made.
        if (await ToolAsync() is null)
            return ("Nothing was imported: this admin no longer has Manage access to every employee.", false);
        var importing = plan.Rows.Where(r => r.Status is FahImportRowStatus.Add or FahImportRowStatus.Update).ToList();
        if (importing.Count == 0) return ("Nothing was imported: no row passed the checks.", false);

        var result = await _import.ImportAsync(Csv(plan.Keys, importing), TabularFormat.Csv);
        if (result.Message is { } fileProblem) return ($"The import refused the file: {fileProblem}", false);

        // Never the first passwords: the model doesn't need them, and the
        // admin knows the rule (email + birthday as MMDD).
        var text = new StringBuilder($"Imported from {plan.SourceFile}: {result.Created} added, {result.Updated} updated.");
        foreach (var error in result.Errors)
        {
            var source = error.Row - 2 >= 0 && error.Row - 2 < importing.Count ? importing[error.Row - 2].SourceRow : error.Row;
            text.Append($"\nRow {source} not imported: {error.Message}");
        }
        var skipped = plan.Rows.Count - importing.Count;
        if (skipped > 0) text.Append($"\n{skipped} row(s) with problems or repeats were left out, as shown on the card.");
        text.Append("\nNo emails were sent. A new person's first password is their email followed by their birthday as MMDD; "
            + "a welcome email can be sent from their page in Manage Employee.");
        return (text.ToString(), result.Created + result.Updated > 0);
    }

    public async Task<TabularExportResult> BuildFileAsync(FahImportPlan plan)
    {
        var payroll = (await _access.GetEnabledModulesAsync()).Contains(OrgModules.Payroll, StringComparer.OrdinalIgnoreCase);
        var rows = plan.Rows.Where(r => r.Status != FahImportRowStatus.LeftOut).ToList();
        var name = Path.GetFileNameWithoutExtension(plan.SourceFile);
        var safe = NotFileSafe().Replace(name, "-").Trim('-');
        return TabularExportResult.From(
            EmployeeImportSheet.Workbook(Sheet(plan.Keys, rows), payroll),
            TabularFormat.Xlsx,
            $"employees-import-{(safe.Length == 0 ? "from-fah" : safe)}");
    }

    // ---- Helpers ----

    private static TabularSheet Sheet(IReadOnlyList<string> keys, IEnumerable<FahImportRow> rows)
    {
        var labels = keys.Select(k => EmployeeImportSheet.Columns.First(c => c.Key == k).Label).ToList();
        var sheet = new TabularSheet(EmployeeImportSheet.SheetName, labels);
        foreach (var row in rows) sheet.AddRow(keys.Select(k => row.Values.GetValueOrDefault(k)));
        return sheet;
    }

    private static byte[] Csv(IReadOnlyList<string> keys, IEnumerable<FahImportRow> rows) =>
        TabularExportResult.From(Sheet(keys, rows), TabularFormat.Csv, "import").Content;

    private static string? KeyProblem(string key, IReadOnlyDictionary<string, TabularColumn> allowed, bool payroll)
    {
        if (allowed.ContainsKey(key)) return null;
        if (!payroll && EmployeeImportSheet.PayrollKeys.Contains(key))
            return $"\"{key}\" is a payroll field, and this admin's access doesn't include Payroll. Leave it out.";
        return $"\"{key}\" isn't an AltomateHR field. Use one of: {string.Join(", ", allowed.Keys)}.";
    }

    // The few things a spreadsheet writes in ways the import doesn't take.
    // Everything else goes across exactly as typed, for the import to check.
    private static void Normalise(Dictionary<string, string> row)
    {
        if (row.TryGetValue(EmployeeImportSheet.EmailKey, out var email)) row[EmployeeImportSheet.EmailKey] = email.Trim().ToLowerInvariant();

        if (row.TryGetValue("gender", out var gender))
            row["gender"] = gender.Trim().ToUpperInvariant() switch
            {
                "M" or "MALE" or "L" or "LELAKI" => "MALE",
                "F" or "FEMALE" or "P" or "PEREMPUAN" => "FEMALE",
                _ => gender,
            };

        if (row.TryGetValue("idNumber", out var id) && id.Length > 0)
        {
            // "C1234567 (Passport)" → "C1234567".
            var cleaned = Parenthetical().Replace(id, string.Empty).Trim();
            row["idNumber"] = cleaned;
            if (string.IsNullOrWhiteSpace(row.GetValueOrDefault("idType")))
                row["idType"] = cleaned.Count(char.IsDigit) == 12 && cleaned.All(c => char.IsDigit(c) || c is '-' or ' ')
                    ? "NRIC"
                    : "PASSPORT";
        }
    }

    private static string Str(JsonElement input, string name) =>
        input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;

    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"[^A-Za-z0-9_-]+")]
    private static partial Regex NotFileSafe();
}

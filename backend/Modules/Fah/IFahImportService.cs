using System.Text.Json;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Fah.Entities;

namespace AltomateHR.Api.Modules.Fah;

// Adding or updating many employees from a spreadsheet the admin attached in
// FAH, through the same employee import as Manage Employee → Import — one
// card and one Confirm for the whole file, not one per person.
//
// The model only says which of the file's columns is which AltomateHR field;
// every cell is copied across by code, never retyped by the model, and the
// import's own checks decide what's valid.
public interface IFahImportService
{
    public const string ToolName = "prepare_employee_import";

    // The tool's definition for the model, or null when this admin can't
    // import employees (no Employees access, or access limited to some
    // policies — an import matches rows against everyone).
    Task<FahTool?> ToolAsync();

    // Builds and checks a plan from the model's tool call against the chat's
    // spreadsheets. Error: what to tell the model so it can correct the call.
    Task<(FahImportPlan? Plan, string? Error)> PrepareAsync(IReadOnlyList<FahAttachment> sheets, JsonElement input);

    // Imports the plan's Add and Update rows. Returns what to tell the model,
    // and whether anything was saved.
    Task<(string Text, bool Saved)> RunAsync(FahImportPlan plan);

    // The plan as an AltomateHR import workbook (READ ME and column guide
    // included), every row but the left-out ones — for the admin to check or
    // fix in Excel and import from Manage Employee instead.
    Task<TabularExportResult> BuildFileAsync(FahImportPlan plan);
}

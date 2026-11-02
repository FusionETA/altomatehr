namespace AltomateHR.Api.Modules.Fah;

// An employee import FAH has prepared from the admin's own spreadsheet, held
// with the change card until they press Confirm. The rows are already in
// AltomateHR's import layout (template column key → cell), so Confirm and
// the download both build the file from exactly what the card showed.
public sealed record FahImportPlan(
    string SourceFile,
    // The import's column keys the rows fill, in the import sheet's order.
    IReadOnlyList<string> Keys,
    IReadOnlyList<FahImportRow> Rows,
    int Adds,
    int Updates);

// One row of the admin's file. SourceRow is its row number there (1 = the
// first row, as Excel shows it), so problems are named the way they see them.
public sealed record FahImportRow(
    int SourceRow,
    Dictionary<string, string> Values,
    FahImportRowStatus Status,
    string? Problem = null,
    // Imported, but worth a look (a nationality kept as typed).
    string? Note = null);

public enum FahImportRowStatus
{
    Add,
    Update,
    // The import would refuse it: left out of Confirm, kept in the download to fix.
    Problem,
    // A repeat of an earlier row, or one the admin asked to skip: in neither.
    LeftOut,
}

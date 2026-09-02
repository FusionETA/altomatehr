using System.Text;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Dtos;

namespace AltomateHR.Api.Tests.Leave;

public class LeaveCsvExporterTests
{
    private static string Render(params LeaveBalanceDto[] rows) =>
        Encoding.UTF8.GetString(LeaveCsvExporter.BalancesToCsv(rows))
            .TrimStart('﻿');   // drop the Excel BOM for assertions

    private static LeaveBalanceDto Row(string code = "AL", string name = "Annual Leave") => new()
    {
        Code = code, Name = name, Paid = true, Year = 2026, AccrualMethod = "LUMP_SUM",
        IsOpened = true, EntitlementDays = 12, AccruedDays = 12, CarriedDays = 3,
        CarriedExpiresAt = new DateTime(2026, 4, 1), TakenDays = 5, PendingDays = 1,
        RemainingDays = 10,
    };

    [Fact]
    public void WritesHeaderAndOneRowPerBalance()
    {
        var csv = Render(Row(), Row("MC", "Medical Leave"));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Code,Leave Type,Paid,Year", lines[0]);
        Assert.Equal(3, lines.Length);                 // header + 2 rows
        Assert.Contains("AL,Annual Leave,Yes,2026", lines[1]);
        Assert.Contains("MC,Medical Leave", lines[2]);
    }

    [Fact]
    public void QuotesValuesContainingCommas()
    {
        var csv = Render(Row("AL", "Annual, Unpaid"));
        Assert.Contains("\"Annual, Unpaid\"", csv);
    }

    [Fact]
    public void EscapesEmbeddedQuotesByDoublingThem()
    {
        var csv = Render(Row("AL", "The \"Big\" Leave"));
        Assert.Contains("\"The \"\"Big\"\" Leave\"", csv);
    }

    [Fact]
    public void FormatsDatesAndBlanksWhenThereIsNoExpiry()
    {
        Assert.Contains("2026-04-01", Render(Row()));

        var noExpiry = Row();
        noExpiry.CarriedExpiresAt = null;
        Assert.DoesNotContain("2026-04-01", Render(noExpiry));
    }

    [Fact]
    public void StartsWithAUtf8Bom_SoExcelReadsNonAsciiNames()
    {
        var bytes = LeaveCsvExporter.BalancesToCsv([Row()]);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
    }

    [Fact]
    public void PutsTheEmployeeLabelInItsOwnRow_WhenSupplied()
    {
        var csv = Encoding.UTF8.GetString(
            LeaveCsvExporter.BalancesToCsv([Row()], "siti@altomate.com")).TrimStart('﻿');

        Assert.StartsWith("Employee,siti@altomate.com", csv);
    }
}

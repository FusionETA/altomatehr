using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltomateHR.Api.Modules.Payroll;

// How a payslip's snapshot columns are serialised.
//
// ONE definition, shared by the writer (`PayrollRunService`, generating a run)
// and every reader (the PCB details PDF, and anything later that needs the
// working). Two copies of these options is a silent-failure waiting to happen:
// a snapshot written yesterday would deserialise into a record of zeroes
// today, and a page of zeroes reads as "no tax was due".
public static class PayrollSnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}

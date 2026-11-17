using System.Text.Json;
using AltomateHR.Api.Modules.PayrollCalculator.Dtos;

namespace AltomateHR.Api.Modules.PayrollCalculator;

// The public "what does this salary cost and take home" calculator for the
// altomate.io payroll page. Prices one hypothetical employee with the real
// payroll engine; stores nothing.
public interface IPayrollCalculatorService
{
    PayrollCalculatorOutcome Calculate(JsonElement body);
}

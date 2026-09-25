using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AltomateHR.Api.Modules.Audit.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.ApiKeys;

// An API-key caller's UserId is "apikey:<guid>" (43 chars). These columns hold
// the current UserId; at varchar(40) MySQL refused the insert, so a salary
// change or run approval made through the API was a 500 and every API-key
// action went missing from the audit log (whose write failures are logged,
// not thrown). The in-memory test database doesn't enforce lengths, hence a
// check on the declared size.
public class ApiKeyActorIdFitsTests
{
    private static readonly int ApiKeyActorLength = $"apikey:{Guid.NewGuid()}".Length;

    [Theory]
    [InlineData(typeof(AuditLog), nameof(AuditLog.ActorUserId))]
    [InlineData(typeof(SalaryChange), nameof(SalaryChange.ChangedByUserId))]
    [InlineData(typeof(PayrollRun), nameof(PayrollRun.SubmittedById))]
    [InlineData(typeof(PayrollRun), nameof(PayrollRun.SubmittedForApprovalById))]
    public void ActorColumn_HoldsAnApiKeyCaller(Type entity, string property)
    {
        var max = entity.GetProperty(property)!.GetCustomAttribute<MaxLengthAttribute>()!.Length;
        Assert.True(max >= ApiKeyActorLength, $"{entity.Name}.{property} is {max}, needs {ApiKeyActorLength}");
    }
}

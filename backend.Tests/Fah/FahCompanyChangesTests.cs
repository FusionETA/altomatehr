using System.Security.Claims;
using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Fah;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AltomateHR.Api.Tests.Fah;

// FAH's changes across companies, through the real app: its services, its
// database rules and its tenant filter, each company's part done in that
// company as the Owner there.
//
// The owner (usr-owner) owns Alpha, Beta and Delta, and is an Admin in Gamma.
// Fay is an admin in Alpha: Employees to manage, Payroll to view, only the
// people on Sales, no settings.
public class FahCompanyChangesTests : IClassFixture<FahCompanyChangesTests.App>
{
    private const string Owner = "usr-owner";
    private readonly App _app;

    public FahCompanyChangesTests(App app) => _app = app;

    [Fact]
    public async Task ACopiedPolicy_IsMadeInEachCompanyTheyOwn_WithThatCompanysLeaveTypes()
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.CopyPolicyTool, Input("""{"policy":"full-time","to":["all"]}"""), o.Companies));

        Assert.Null(error);
        var steps = plan!.Steps.ToDictionary(s => s.Company);
        Assert.Equal(["Beta", "Delta"], steps.Keys.Order());   // "all" = the ones they own; Gamma isn't
        Assert.False(steps["Beta"].Skip);
        Assert.Contains("No leave type named \"Hospitalisation\"", steps["Beta"].Detail);
        Assert.True(steps["Delta"].Skip);
        Assert.Contains("Already has a policy named \"Full-time\"", steps["Delta"].Detail);
        Assert.DoesNotContain(await _app.PoliciesAsync(o.Beta), p => p.Name == "Full-time");   // nothing made yet

        var (text, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan));

        Assert.True(made);
        Assert.Contains("- Beta: done.", text);
        var copied = (await _app.PoliciesAsync(o.Beta)).Single(p => p.Name == "Full-time");
        Assert.Equal(("Full-time", 2.50m, 600, true), (copied.Name, copied.OtRateNormalDay, copied.OtDailyThresholdMinutes, copied.IsDefault));
        var leave = Assert.Single(await _app.EntitlementsAsync(copied.Id));
        Assert.Equal((o.BetaAnnual, 18.0, o.Beta), (leave.LeaveTypeId, leave.DefaultDays, leave.OrganizationId));
        // Delta's own policy is left as it was.
        Assert.Equal(1.50m, Assert.Single(await _app.PoliciesAsync(o.Delta)).OtRateNormalDay);
    }

    [Fact]
    public async Task ACompanyWhereTheyreOnlyAnAdmin_IsSkipped_AndSaysWhy()
    {
        var o = await _app.SeedAsync();

        var (plan, _) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.CopyPolicyTool, Input("""{"policy":"Full-time","to":["Gamma","Beta"]}"""), o.Companies));
        var (_, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan!));

        var gamma = plan!.Steps.Single(s => s.Company == "Gamma");
        Assert.True(gamma.Skip);
        Assert.Equal("You're Admin here, not the owner.", gamma.Detail);
        Assert.True(made);
        Assert.Empty(await _app.PoliciesAsync(o.Gamma));
    }

    [Fact]
    public async Task AnAddedAdmin_GetsTheSameAccess_WithTheirPolicyLimitMatchedByName()
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.AddAdminTool, Input("""{"email":"FAY@x.test","to":["Beta","Delta"]}"""), o.Companies));

        Assert.Null(error);
        Assert.Contains("Employees, Payroll (view); only people on Sales; can't change settings.", plan!.Steps[0].Detail);
        Assert.Contains("they'd see no employees", plan.Steps[1].Detail);   // Delta has no Sales

        var (text, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan));

        Assert.True(made, text);
        var beta = await _app.MembershipAsync(o.Beta, o.Fay);
        Assert.Equal("Admin", beta!.Role);
        Assert.Equal(["employees", "payroll:view"], beta.Modules!.Split(',').Order());
        Assert.Equal(o.BetaSales, beta.PolicyScope);
        Assert.False(beta.CanChangeSettings);
        // Narrowed, never widened: no matching policy is nobody, not everybody.
        Assert.Equal("", (await _app.MembershipAsync(o.Delta, o.Fay))!.PolicyScope);
    }

    [Fact]
    public async Task SomeoneAlreadyInACompany_IsLeftAsTheyAre()
    {
        var o = await _app.SeedAsync();
        await _app.AddMemberAsync(o.Beta, o.Fay, "Employee");

        var (plan, _) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.AddAdminTool, Input("""{"email":"fay@x.test","to":["Beta"]}"""), o.Companies));
        var (_, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan!));

        Assert.True(plan!.Steps[0].Skip);
        Assert.Contains("Already in this company as Employee", plan.Steps[0].Detail);
        Assert.False(made);
        Assert.Equal("Employee", (await _app.MembershipAsync(o.Beta, o.Fay))!.Role);
    }

    // Confirm checks again: an owner who lost a company since the card was
    // made changes nothing there.
    [Fact]
    public async Task Confirm_ChecksOwnershipAgain()
    {
        var o = await _app.SeedAsync();
        var (plan, _) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.CopyPolicyTool, Input("""{"policy":"Full-time","to":["Beta"]}"""), o.Companies));
        await _app.SetRoleAsync(o.Beta, Owner, "Admin");

        var (text, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan!));

        Assert.False(made);
        Assert.Contains("no longer owns", text);
        Assert.DoesNotContain(await _app.PoliciesAsync(o.Beta), p => p.Name == "Full-time");
    }

    [Theory]
    [InlineData("""{"policy":"Full-time","from":"Gamma","to":["Beta"]}""", "isn't the owner of Gamma")]
    [InlineData("""{"policy":"Nope","to":["Beta"]}""", "Alpha has no policy named \"Nope\"")]
    [InlineData("""{"policy":"Full-time","to":["Omega"]}""", "\"Omega\" isn't one of the admin's companies")]
    [InlineData("""{"policy":"Full-time","to":[]}""", "Say which companies")]
    public async Task ARequestThatDoesntFit_IsExplained(string input, string expected)
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.CopyPolicyTool, Input(input), o.Companies));

        Assert.Null(plan);
        Assert.Contains(expected, error);
    }

    // The request's own company is untouched by acting in another.
    [Fact]
    public async Task ActingInAnotherCompany_LeavesTheRequestInItsOwn()
    {
        var o = await _app.SeedAsync();

        var orgs = await _app.AsOwnerAsync(o.Alpha, async (_, sp) =>
        {
            var inside = await sp.GetRequiredService<ICompanyScopes>()
                .RunAsync(o.Beta, "Owner", inner => Task.FromResult(inner.GetRequiredService<ICurrentUser>().OrganizationId));
            return (inside, sp.GetRequiredService<ICurrentUser>().OrganizationId);
        });

        Assert.Equal((o.Beta, o.Alpha), orgs);
    }

    [Fact]
    public async Task TheTools_AreForSomeoneWhoOwnsTwoOrMore()
    {
        var o = await _app.SeedAsync();

        var owner = await _app.AsOwnerAsync(o.Alpha, c => Task.FromResult(c.Tools(o.Companies)));
        var notEnough = await _app.AsOwnerAsync(o.Alpha, c => Task.FromResult(
            c.Tools([.. o.Companies.Where(x => x.Name is "Alpha" or "Gamma")])));

        Assert.Equal(
            [IFahCompanyChanges.CreatePolicyTool, IFahCompanyChanges.CreateAdminTool, IFahCompanyChanges.CopyPolicyTool, IFahCompanyChanges.AddAdminTool],
            owner.Select(t => t.Name));
        Assert.Equal(   // creating needs no second company
            [IFahCompanyChanges.CreatePolicyTool, IFahCompanyChanges.CreateAdminTool], notEnough.Select(t => t.Name));
        var ownsNone = await _app.AsAsync(o.Gamma, "Admin", c => Task.FromResult(
            c.Tools([.. o.Companies.Where(x => x.Name is "Gamma")])));
        Assert.Empty(ownsNone);   // an Admin can't create a policy or an admin anywhere
    }

    // ─── Create a policy ─────────────────────────────────────────────────

    [Fact]
    public async Task ANewPolicy_FromTheScreensDefaults_IsMadeHereAndInTheCompaniesTheyOwn()
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(IFahCompanyChanges.CreatePolicyTool, Input(
            """{"name":"Probation","salaryType":"MONTHLY","settings":{"requireSelfie":true,"otRateNormalDay":2},"leaveDays":[{"leaveType":"Annual Leave","days":8}],"in":["all"]}"""),
            o.Companies));

        Assert.Null(error);
        Assert.Equal(["Alpha", "Beta", "Delta"], plan!.Steps.Select(s => s.Company).Order());   // "all": here + owned, not Gamma
        Assert.Contains("Salary: monthly.", plan.Settings);
        Assert.Contains("Selfie at clock-in on", plan.Settings);
        Assert.Contains("2× normal day, the company's rate for rest day, the company's rate for public holiday", plan.Settings);
        Assert.Contains("Leave days: Annual Leave 8; other types their own default.", plan.Settings);
        Assert.Contains("No leave type named \"Annual Leave\"", plan.Steps.Single(s => s.Company == "Delta").Detail);
        Assert.DoesNotContain(await _app.PoliciesAsync(o.Alpha), p => p.Name == "Probation");   // nothing made yet

        var (text, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan));

        Assert.True(made, text);
        Assert.StartsWith("Create policy \"Probation\":", text);
        foreach (var org in new[] { o.Alpha, o.Beta, o.Delta })
        {
            var made1 = (await _app.PoliciesAsync(org)).Single(p => p.Name == "Probation");
            Assert.Equal((SalaryType.MONTHLY, true, 2m, 480, true),
                (made1.SalaryType, made1.RequireSelfie, made1.OtRateNormalDay, made1.OtDailyThresholdMinutes, made1.RequireGeofence));
        }
        var betaPolicy = (await _app.PoliciesAsync(o.Beta)).Single(p => p.Name == "Probation");
        var leave = Assert.Single(await _app.EntitlementsAsync(betaPolicy.Id));
        Assert.Equal((o.BetaAnnual, 8.0), (leave.LeaveTypeId, leave.DefaultDays));
        Assert.Empty(await _app.PoliciesAsync(o.Gamma));
    }

    [Fact]
    public async Task ANewPolicy_BasedOnAnother_KeepsItsSettings_ExceptWhatsChanged()
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(IFahCompanyChanges.CreatePolicyTool, Input(
            """{"name":"Full-time (probation)","basedOn":"Full-time","leaveDays":[{"leaveType":"annual leave","days":8}]}"""), o.Companies));
        var (_, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan!));

        Assert.Null(error);
        Assert.Equal("Alpha", Assert.Single(plan!.Steps).Company);   // default: the signed-in company only
        Assert.Contains("Based on \"Full-time\" in Alpha.", plan.Settings);
        Assert.True(made);
        var created = (await _app.PoliciesAsync(o.Alpha)).Single(p => p.Name == "Full-time (probation)");
        Assert.Equal((2.50m, 600), (created.OtRateNormalDay, created.OtDailyThresholdMinutes));
        Assert.Equal([8.0, 60.0], (await _app.EntitlementsAsync(created.Id)).Select(e => e.DefaultDays).Order());
    }

    // In the signed-in company FAH creates as the admin themselves, so the
    // screen's rule holds: an Admin there can't create a policy.
    [Fact]
    public async Task HereAnAdmin_CantCreateAPolicy_AsOnTheScreen()
    {
        var o = await _app.SeedAsync();

        var (plan, _) = await _app.AsAsync(o.Gamma, "Admin", c => c.PrepareAsync(IFahCompanyChanges.CreatePolicyTool,
            Input("""{"name":"Probation","salaryType":"HOURLY","in":["Gamma","Beta"]}"""), o.Companies));
        var (text, made) = await _app.AsAsync(o.Gamma, "Admin", c => c.RunAsync(plan!));

        var steps = plan!.Steps.ToDictionary(s => s.Company);
        Assert.True(steps["Gamma"].Skip);
        Assert.Equal("Only the owner can create a policy here; you're an Admin.", steps["Gamma"].Detail);
        Assert.False(steps["Beta"].Skip);   // one they own, from wherever they're signed in
        Assert.True(made, text);
        Assert.Empty(await _app.PoliciesAsync(o.Gamma));
        Assert.Equal(SalaryType.HOURLY, (await _app.PoliciesAsync(o.Beta)).Single(p => p.Name == "Probation").SalaryType);
    }

    [Theory]
    [InlineData("""{"name":"Probation"}""", "MONTHLY or HOURLY; don't choose it for them")]
    [InlineData("""{"name":"Probation","salaryType":"MONTHLY","settings":{"lunch":true}}""", "\"lunch\" isn't a policy setting")]
    [InlineData("""{"name":"Probation","salaryType":"MONTHLY","settings":{"otRateNormalDay":"lots"}}""", "\"otRateNormalDay\" doesn't take")]
    [InlineData("""{"name":"Probation","salaryType":"MONTHLY","settings":{"otRateNormalDay":120}}""", "OtRateNormalDay")]
    [InlineData("""{"name":"Probation","salaryType":"MONTHLY","settings":{"autoClockOutEnabled":true}}""", "autoClockOutAfterMinutes")]
    [InlineData("""{"name":"Probation","basedOn":"Nope"}""", "Alpha has no policy named \"Nope\"")]
    [InlineData("""{"name":"Probation","basedOn":"Sales","basedOnCompany":"Gamma"}""", "isn't the owner of Gamma")]
    [InlineData("""{"name":"Probation","salaryType":"MONTHLY","leaveDays":[{"leaveType":"Annual Leave","days":400}]}""", "isn't possible")]
    public async Task ANewPolicyThatDoesntFit_IsExplained(string input, string expected)
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.CreatePolicyTool, Input(input), o.Companies));

        Assert.Null(plan);
        Assert.Contains(expected, error);
    }

    // ─── Create an admin ─────────────────────────────────────────────────

    [Fact]
    public async Task ANewAdmin_GetsANewAccount_AndTheAccessTheOwnerChose()
    {
        var o = await _app.SeedAsync();
        var email = $"nia-{Guid.NewGuid():N}@x.test";

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(IFahCompanyChanges.CreateAdminTool, Input(
            $$"""{"email":"{{email.ToUpperInvariant()}}","name":"Nia","dateOfBirth":"1990-03-07","access":{"modules":{"employees":"Manage","payroll":"View"},"employees":["Sales"],"canChangeSettings":false},"in":["all"]}"""),
            o.Companies));

        Assert.Null(error);
        Assert.Equal(["Alpha", "Beta", "Delta"], plan!.Steps.Select(s => s.Company).Order());
        Assert.Contains("If they're new to AltomateHR, their first password is their email followed by their birthday as MMDD", plan.Settings);
        Assert.Contains("Access: Employees, Payroll (view); only people on Sales; can't change settings.", plan.Settings);
        Assert.Contains("No welcome email is sent", plan.Settings);
        Assert.Equal("New admin.", plan.Steps.Single(s => s.Company == "Beta").Detail);
        Assert.Contains("they'd see no employees", plan.Steps.Single(s => s.Company == "Delta").Detail);   // no Sales there

        var (text, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan));

        Assert.True(made, text);
        Assert.StartsWith("Add Nia as an admin:", text);
        var user = await _app.UserAsync(email);
        Assert.True(Modules.Auth.PasswordHasher.Verify($"{email}0307", user!.PasswordHash));
        var beta = await _app.MembershipAsync(o.Beta, user.Id);
        Assert.Equal(("Admin", o.BetaSales, false), (beta!.Role, beta.PolicyScope, beta.CanChangeSettings));
        Assert.Equal(["employees", "payroll:view"], beta.Modules!.Split(',').Order());
        Assert.Equal("", (await _app.MembershipAsync(o.Delta, user.Id))!.PolicyScope);
    }

    // The card never says whether the email has an account, or whose: that
    // account may belong to another company entirely.
    [Fact]
    public async Task SomeoneWithAnAccount_KeepsIt_AndTheCardDoesntSaySo()
    {
        var o = await _app.SeedAsync();
        var before = await _app.UserAsync("fay@x.test");

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(IFahCompanyChanges.CreateAdminTool,
            Input("""{"email":"fay@x.test","name":"Someone","dateOfBirth":"1990-03-07","access":{"full":true},"in":["Beta","Alpha"]}"""), o.Companies));
        var (_, made) = await _app.AsOwnerAsync(o.Alpha, c => c.RunAsync(plan!));

        Assert.Null(error);
        Assert.DoesNotContain("Fay", plan!.Settings);
        Assert.Equal("Add Someone as an admin", plan.Title);
        var after = await _app.UserAsync("fay@x.test");
        Assert.Equal((before!.Name, before.PasswordHash), (after!.Name, after.PasswordHash));
        Assert.Contains("Access: Every module; every employee; can change settings.", plan.Settings);
        Assert.Contains("Already an admin here", plan.Steps.Single(s => s.Company == "Alpha").Detail);
        Assert.True(made);
        var beta = await _app.MembershipAsync(o.Beta, o.Fay);
        Assert.Equal(("Admin", null, null, true), (beta!.Role, beta.Modules, beta.PolicyScope, beta.CanChangeSettings));
    }

    // Adding an admin is the Owner's, in the signed-in company too.
    [Fact]
    public async Task HereAnAdmin_CantAddAnAdmin()
    {
        var o = await _app.SeedAsync();

        var (plan, _) = await _app.AsAsync(o.Gamma, "Admin", c => c.PrepareAsync(IFahCompanyChanges.CreateAdminTool,
            Input("""{"email":"fay@x.test","name":"Fay","dateOfBirth":"1990-03-07","access":{"full":true}}"""), o.Companies));

        Assert.Equal("You're Admin here, not the owner.", Assert.Single(plan!.Steps).Detail);
        Assert.Equal(0, plan.Doing);
    }

    [Theory]
    [InlineData("""{"email":"fay@x.test"}""", "Ask the owner what access they get")]
    [InlineData("""{"email":"fay@x.test","access":{"modules":{"payroll":"View"}}}""", "Ask the owner what access they get")]
    [InlineData("""{"email":"fay@x.test","access":{"modules":{"lunch":"View"},"employees":"all","canChangeSettings":true}}""", "\"lunch\" isn't a module")]
    [InlineData("""{"email":"fay@x.test","access":{"modules":{"payroll":"Admin"},"employees":"all","canChangeSettings":true}}""", "payroll is \"View\" or \"Manage\"")]
    [InlineData("""{"email":"fay@x.test","access":{"modules":{},"employees":[],"canChangeSettings":true}}""", "employees is \"all\" or")]
    [InlineData("""{"email":"brand-new@nowhere.test","access":{"full":true}}""", "Ask for their full name")]
    [InlineData("""{"email":"brand-new@nowhere.test","name":"Bo","access":{"full":true}}""", "Ask for their date of birth")]
    [InlineData("""{"email":"brand-new@nowhere.test","name":"Bo","dateOfBirth":"07/03/1990","access":{"full":true}}""", "YYYY-MM-DD")]
    [InlineData("""{"email":"not an email","access":{"full":true}}""", "Say their login email")]
    public async Task ANewAdminThatDoesntFit_IsExplained(string input, string expected)
    {
        var o = await _app.SeedAsync();

        var (plan, error) = await _app.AsOwnerAsync(o.Alpha, c => c.PrepareAsync(
            IFahCompanyChanges.CreateAdminTool, Input(input), o.Companies));

        Assert.Null(plan);
        Assert.Contains(expected, error);
    }

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public sealed record Seeded(
        string Alpha, string Beta, string Gamma, string Delta, string Fay, string BetaSales, string BetaAnnual,
        IReadOnlyList<Modules.Auth.UserOrgDto> Companies);

    public sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"altomatehr-fah-companies-{Guid.NewGuid()}";

        // A fresh set of companies per test, so tests share the app but not data.
        public async Task<Seeded> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var tag = Guid.NewGuid().ToString("N")[..6];

            if (!await db.Users.AnyAsync(u => u.Id == Owner))
                db.Users.Add(new User { Id = Owner, Email = "owner@x.test", Name = "Olive", PasswordHash = "x", CreatedAt = now });
            var fay = new User { Email = $"fay-{tag}@x.test", Name = "Fay", PasswordHash = "x", CreatedAt = now };
            // The tests ask for fay@x.test: one Fay per seed, found by that address.
            foreach (var old in await db.Users.Where(u => u.Email == "fay@x.test").ToListAsync()) old.Email = $"old-{Guid.NewGuid():N}@x.test";
            fay.Email = "fay@x.test";
            db.Users.Add(fay);

            Organization Org(string name) => new() { Name = name };
            var (alpha, beta, gamma, delta) = (Org("Alpha"), Org("Beta"), Org("Gamma"), Org("Delta"));
            db.Organizations.AddRange(alpha, beta, gamma, delta);

            // Other tests' companies stay, but this owner leaves them.
            foreach (var m in await db.OrganizationMemberships.IgnoreQueryFilters().Where(m => m.UserId == Owner).ToListAsync())
                db.OrganizationMemberships.Remove(m);
            OrganizationMembership Seat(Organization org, string userId, string role) =>
                new() { OrganizationId = org.Id, UserId = userId, Role = role, CreatedAt = now, UpdatedAt = now };
            db.OrganizationMemberships.AddRange(
                Seat(alpha, Owner, "Owner"), Seat(beta, Owner, "Owner"), Seat(gamma, Owner, "Admin"), Seat(delta, Owner, "Owner"));

            EmployeePolicy Policy(Organization org, string name, decimal ot = 1.50m, bool isDefault = false) => new()
            {
                OrganizationId = org.Id, Name = name, OtRateNormalDay = ot, IsDefault = isDefault, CreatedAt = now, UpdatedAt = now,
            };
            var fullTime = Policy(alpha, "Full-time", ot: 2.50m, isDefault: true);
            fullTime.OtDailyThresholdMinutes = 600;
            var alphaSales = Policy(alpha, "Sales");
            var betaSales = Policy(beta, "Sales");
            db.EmployeePolicies.AddRange(fullTime, alphaSales, betaSales, Policy(delta, "Full-time", isDefault: true));

            var fayAdmin = Seat(alpha, fay.Id, "Admin");
            fayAdmin.Modules = "employees,payroll:view";
            fayAdmin.PolicyScope = alphaSales.Id;
            fayAdmin.CanChangeSettings = false;
            db.OrganizationMemberships.Add(fayAdmin);

            LeaveType Leave(Organization org, string code, string name) =>
                new() { OrganizationId = org.Id, Code = code, Name = name, DefaultDays = 14 };
            var (alphaAnnual, alphaHospital, betaAnnual) =
                (Leave(alpha, "AL", "Annual Leave"), Leave(alpha, "HL", "Hospitalisation"), Leave(beta, "AL", "annual leave"));
            db.LeaveTypes.AddRange(alphaAnnual, alphaHospital, betaAnnual);
            db.PolicyLeaveEntitlements.AddRange(
                new PolicyLeaveEntitlement { OrganizationId = alpha.Id, PolicyId = fullTime.Id, LeaveTypeId = alphaAnnual.Id, DefaultDays = 18, CreatedAt = now, UpdatedAt = now },
                new PolicyLeaveEntitlement { OrganizationId = alpha.Id, PolicyId = fullTime.Id, LeaveTypeId = alphaHospital.Id, DefaultDays = 60, CreatedAt = now, UpdatedAt = now });

            await db.SaveChangesAsync();

            var companies = await AsOwnerAsync(alpha.Id, async (_, sp) =>
                (await sp.GetRequiredService<Modules.Auth.IAuthService>().GetOrgsAsync(Owner))
                    .Where(c => !c.IsFormer && OrgRoles.IsAdministrative(c.Role)).ToList());
            return new Seeded(alpha.Id, beta.Id, gamma.Id, delta.Id, fay.Id, betaSales.Id, betaAnnual.Id, companies);
        }

        public Task<T> AsOwnerAsync<T>(string organizationId, Func<IFahCompanyChanges, Task<T>> work) =>
            AsAsync(organizationId, "Owner", (changes, _) => work(changes));

        public Task<T> AsOwnerAsync<T>(string organizationId, Func<IFahCompanyChanges, IServiceProvider, Task<T>> work) =>
            AsAsync(organizationId, "Owner", work);

        public Task<T> AsAsync<T>(string organizationId, string role, Func<IFahCompanyChanges, Task<T>> work) =>
            AsAsync(organizationId, role, (changes, _) => work(changes));

        // A request from the owner, signed into this company with this role there.
        public async Task<T> AsAsync<T>(string organizationId, string role, Func<IFahCompanyChanges, IServiceProvider, Task<T>> work)
        {
            using var scope = Services.CreateScope();
            var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            http.HttpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Owner),
                    new Claim(ClaimTypes.Email, "owner@x.test"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("org", organizationId),
                ], "Test")),
            };
            try
            {
                return await work(scope.ServiceProvider.GetRequiredService<IFahCompanyChanges>(), scope.ServiceProvider);
            }
            finally
            {
                http.HttpContext = null;
            }
        }

        public async Task<List<EmployeePolicy>> PoliciesAsync(string organizationId)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmployeePolicies
                .IgnoreQueryFilters().Where(p => p.OrganizationId == organizationId).ToListAsync();
        }

        public async Task<List<PolicyLeaveEntitlement>> EntitlementsAsync(string policyId)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PolicyLeaveEntitlements
                .IgnoreQueryFilters().Where(e => e.PolicyId == policyId).ToListAsync();
        }

        public async Task<User?> UserAsync(string email)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleOrDefaultAsync(u => u.Email == email);
        }

        public async Task<OrganizationMembership?> MembershipAsync(string organizationId, string userId)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OrganizationMemberships
                .IgnoreQueryFilters().SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId);
        }

        public async Task AddMemberAsync(string organizationId, string userId, string role)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = organizationId, UserId = userId, Role = role, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task SetRoleAsync(string organizationId, string userId, string role)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seat = await db.OrganizationMemberships.IgnoreQueryFilters()
                .SingleAsync(m => m.OrganizationId == organizationId && m.UserId == userId);
            seat.Role = role;
            await db.SaveChangesAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=localhost;Database=tests;User=test;Password=test",
                    ["Jwt:Key"] = "test-jwt-key-that-is-long-enough-for-hmac-sha256-signing",
                    ["Jwt:Issuer"] = "altomatehr-api",
                    ["Jwt:Audience"] = "altomatehr-client",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["Seed:DemoData"] = "false",
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }
    }
}

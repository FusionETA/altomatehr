using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Tests.Employees;

// Admin access and employment, read apart (MembershipRoles).
public class MembershipRolesTests
{
    [Theory]
    [InlineData("Employee", null, "Employee")]
    [InlineData("Supervisor", null, "Supervisor")]
    [InlineData("Admin", null, null)]               // admin only
    [InlineData("Owner", null, null)]
    [InlineData("Admin", "Employee", "Employee")]   // Employee & admin
    [InlineData("Owner", "Supervisor", "Supervisor")]
    [InlineData("Employee", "Supervisor", "Employee")] // a stray value on staff is ignored
    public void Employment_is_the_role_for_staff_and_the_column_for_admins(
        string role, string? employment, string? expected)
    {
        var m = new OrganizationMembership { Role = role, EmploymentRole = employment };

        Assert.Equal(expected, m.EmploymentRole());
        Assert.Equal(expected is not null, m.IsEmployed());
    }

    [Fact]
    public void Promoting_staff_keeps_them_on_the_staff()
    {
        var m = new OrganizationMembership { Role = "Supervisor" };

        m.SetRole("Admin");

        Assert.Equal("Admin", m.Role);
        Assert.Equal("Supervisor", m.EmploymentRole());
        Assert.True(m.IsEmployeeAndAdmin());
    }

    [Fact]
    public void An_admin_moving_to_owner_keeps_their_employment()
    {
        var m = new OrganizationMembership { Role = "Admin", EmploymentRole = "Employee" };

        m.SetRole("Owner");

        Assert.Equal("Employee", m.EmploymentRole());
    }

    [Fact]
    public void Removing_admin_access_leaves_the_staff_role_only()
    {
        var m = new OrganizationMembership { Role = "Admin", EmploymentRole = "Employee" };

        m.SetRole("Supervisor");

        Assert.Equal("Supervisor", m.Role);
        Assert.Null(m.EmploymentRole);
        Assert.False(m.IsEmployeeAndAdmin());
    }
}

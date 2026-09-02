using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepairSnapshotAfterOscarMainMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — a snapshot repair, not a schema change.
            // Same failure as RepairModelSnapshot (20260828023932), recurred in
            // the other direction.
            //
            // Merging origin/main into oscar (6e18a42) text-merged
            // AppDbContextModelSnapshot.cs cleanly, but main's snapshot came
            // from migrations timestamped AFTER oscar's last one
            // (AddEmployeeProfileFields, AddEmployeeProfile, HolidayUniqueIndex)
            // and knew nothing about LeaveEntitlement. The merged snapshot no
            // longer described the real model, so `dotnet ef migrations add`
            // wanted to re-create the LeaveEntitlements table and re-add all 13
            // leave columns — against databases that already have them.
            //
            // This rewrites the snapshot from the merged model. Every item EF
            // wanted to emit here was already applied by AddLeaveEntitlements
            // (20260827023118) and AddWorkingDaysHolidaysAndLeaveDetail
            // (20260828024131), so Up/Down do nothing.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo — see Up.
        }
    }
}

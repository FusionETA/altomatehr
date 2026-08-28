using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltomateHR.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepairModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — a snapshot repair, not a schema change.
            //
            // Merging origin/main left AppDbContextModelSnapshot inconsistent
            // with the migrations actually applied: git merged the file cleanly
            // as text, but the result no longer described the real model. EF
            // diffs new migrations against that snapshot, so the next one would
            // have tried to re-apply Rachel's attendance work — dropping
            // AttendanceRecords columns that are already gone.
            //
            // This rewrites the snapshot from the merged model. Every database
            // already has this schema, so Up/Down do nothing.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo — see Up.
        }
    }
}

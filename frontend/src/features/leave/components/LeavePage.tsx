import { LeaveView } from "./LeaveView";
import { LeaveApprovals } from "./LeaveApprovals";
import { EmptyModule } from "@/features/employee-portal/components/EmptyModule";

// Leave tab: the sidebar sub-tab decides which view to show — "My leave"
// (LeaveView), the supervisor "Approvals" queue (LeaveApprovals), or the
// not-yet-built "Team Balances" stub.
export function LeavePage({ sub }: { sub: string }) {
  if (sub === "leave-approvals") return <LeaveApprovals />;
  if (sub === "leave-team") {
    return (
      <EmptyModule
        title="Team Balances"
        body="An org-wide view of every team member's leave balances will live here."
      />
    );
  }
  return <LeaveView />;
}

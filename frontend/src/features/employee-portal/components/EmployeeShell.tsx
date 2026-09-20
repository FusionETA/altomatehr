import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useUrlNav } from "@/shared/lib/use-url-nav";
import { Building2, ExternalLink, KeyRound, LogOut, MoreVertical } from "lucide-react";
import { AttendanceView } from "@/features/attendance/components/AttendanceView";
import { launchAppraisify } from "@/features/appraisify/api";
import { ClaimsPage } from "@/features/claims/components/ClaimsPage";
import { LeavePage } from "@/features/leave/components/LeavePage";
import { getTeamClaims } from "@/features/claims/api";
import { getTeamLeave } from "@/features/leave/api";
import {
  getTeamAttendanceApprovals,
  getTeamBreakApprovals,
  pendingApprovalIds,
} from "@/features/attendance/api";
import { getTeamOvertime } from "@/features/overtime/api";
import { getAccounts, getMyProjects, getOrganization } from "@/features/settings/api";
import { getLeaveTypes } from "@/features/leave/api";
import { NotificationBell } from "@/features/notifications/components/NotificationBell";
import { OrgSwitcher } from "@/features/admin/components/OrgSwitcher";
import { PushToggleMenuItem } from "@/features/notifications/components/PushToggleMenuItem";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import type { SignedInUser } from "@/shared/types/session";
import { buildInitials, buildName } from "../lib/employee-formatters";
import {
  defaultSubOf,
  employeeNav,
  findNavItem,
  NAV_FALLBACK,
  normaliseEmployeeNav,
} from "../lib/nav";
import type { EmployeeView } from "../lib/types";
import { DashboardView } from "./DashboardView";
import { PayslipsView } from "@/features/payslips/components/PayslipsView";
import { ChangePasswordModal } from "@/features/auth/components/ChangePasswordModal";
import { useCachedQuery } from "@/shared/lib/use-cached-query";

function CountBadge({ count, className = "" }: { count: number; className?: string }) {
  if (count <= 0) return null;
  return (
    <span
      className={`flex h-5 min-w-[1.25rem] shrink-0 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-bold text-destructive-foreground ${className}`}
    >
      {count > 99 ? "99+" : count}
    </span>
  );
}

export function EmployeeShell({
  user,
  onLogout,
}: {
  user: SignedInUser;
  onLogout: () => void;
}) {
  const isSupervisor = user.role === "Supervisor";
  // Mirrors the admin shell: the view lives in the URL so Back steps through
  // the portal rather than out of it. See shared/lib/use-url-nav.
  const [nav, go] = useUrlNav(NAV_FALLBACK, normaliseEmployeeNav);
  const activeView = nav.parent as EmployeeView;
  const sub = nav.child;
  const [organizationName, setOrganizationName] = useState<string | null>(null);
  const [claimBadge, setClaimBadge] = useState(0);
  const [leaveBadge, setLeaveBadge] = useState(0);
  const [accountMenuOpen, setAccountMenuOpen] = useState(false);
  const [changePasswordOpen, setChangePasswordOpen] = useState(false);
  const accountMenuRef = useRef<HTMLDivElement | null>(null);
  const [attendanceBadge, setAttendanceBadge] = useState(0);

  const activeItem = findNavItem(activeView);
  const initials = useMemo(() => buildInitials(user.email), [user.email]);
  const displayName = useMemo(() => buildName(user.email), [user.email]);

  // The org name never changes during a session, so once is enough — it was
  // being refetched on every mount of the shell.
  const orgQuery = useCachedQuery("/organizations/current", getOrganization);

  // Warm the reference data the forms need, here rather than when a form
  // opens.
  //
  // The claim and overtime modals read these through useCachedQuery too, so on
  // a cold cache they were fetching at the moment they rendered — the account
  // and project rows arrived a round trip after the form did, and the fields
  // grew into a layout that had already settled. Nothing about that is a
  // skeleton problem; the data simply wasn't asked for early enough.
  //
  // Requested once per session, shared by key with every reader, and small:
  // one org, one account list, one project list. The employee's own projects,
  // not the org's, because that is the key the forms use — /projects would
  // warm a different entry and leave them cold.
  useCachedQuery("/accounts", getAccounts);
  useCachedQuery("/projects/mine", getMyProjects);
  useCachedQuery("/leave-types", getLeaveTypes);
  useEffect(() => {
    setOrganizationName(orgQuery.data?.name ?? null);
  }, [orgQuery.data]);

  // Named so an approval can call it again. Fetched once on mount, the badge
  // kept claiming work was waiting after the approver had already cleared it.
  const refreshBadges = useCallback(() => {
    if (!isSupervisor) return;
    Promise.all([
      getTeamClaims().catch(() => []),
      getTeamLeave().catch(() => []),
      // The Attendance tab holds three queues, so its badge is their sum —
      // a "2" that turns out to be one clock-out and one overtime request is
      // still the honest count of what is waiting behind that tab.
      getTeamAttendanceApprovals().catch(() => []),
      getTeamBreakApprovals().catch(() => []),
      getTeamOvertime().catch(() => []),
    ]).then(([claims, leave, days, breaks, overtime]) => {
      // canAct, not status. The team view now includes the whole team's
      // claims, so counting every pending one would advertise work that
      // belongs to a different step's approver.
      setClaimBadge(claims.filter((c) => c.canAct).length);
      setLeaveBadge(leave.filter((l) => l.status === "PENDING").length);
      setAttendanceBadge(
        // Decisions, not days: one shift can have a clock-in AND a clock-out
        // waiting, which is two things to review.
        days.reduce((n, day) => n + pendingApprovalIds(day).length, 0) +
          breaks.filter((b) => b.approvalStatus === "PENDING").length +
          // /overtime/team carries decided rows too — the queue shows history.
          overtime.filter((o) => o.status === "PENDING").length,
      );
    });
  }, [isSupervisor]);

  useEffect(() => {
    refreshBadges();
  }, [refreshBadges]);

  useEffect(() => {
    if (!accountMenuOpen) return;

    function handlePointerDown(event: PointerEvent) {
      if (!accountMenuRef.current?.contains(event.target as Node)) {
        setAccountMenuOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setAccountMenuOpen(false);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [accountMenuOpen]);

  function badgeFor(view: EmployeeView) {
    return view === "claims" ? claimBadge : view === "leave" ? leaveBadge : view === "attendance" ? attendanceBadge : 0;
  }

  function childBadge(childId: string) {
    return childId === "claims-queue"
      ? claimBadge
      : childId === "leave-approvals"
        ? leaveBadge
        : childId === "att-approvals"
          ? attendanceBadge
          : 0;
  }

  function selectParent(id: EmployeeView) {
    go({ parent: id, child: defaultSubOf(findNavItem(id)) });
  }

  function selectChild(parentId: EmployeeView, childId: string) {
    go({ parent: parentId, child: childId });
  }

  // A notification's url is a bare frontend path (e.g. "/leave") — this app
  // has no router, so map the paths the backend actually sends to nav ids.
  // Anything unmapped just closes the bell without navigating.
  function navigateFromNotification(url: string) {
    const path = url.split("?")[0];
    if (path === "/claims") selectChild("claims", isSupervisor ? "claims-queue" : "claims-mine");
    else if (path === "/leave") selectChild("leave", isSupervisor ? "leave-approvals" : "leave-mine");
    else if (path === "/attendance") selectChild("attendance", isSupervisor ? "att-approvals" : "att-dashboard");
    else if (path === "/overtime") selectChild("attendance", "att-overtime");
  }

  const visibleChildren = (item = activeItem) =>
    (item.children ?? []).filter((c) => !c.supervisorOnly || isSupervisor);
  const activeChildren = visibleChildren();

  return (
    <div className="min-h-screen bg-background lg:grid lg:grid-cols-[280px_1fr]">
      <aside className="hidden min-h-screen flex-col border-r border-border/60 bg-card/72 p-6 backdrop-blur-xl lg:flex">
        <div className="self-center text-center">
          <img src="/brand-logo.png" alt="AltomateHR logo" className="h-auto w-[148px] object-contain" />
          <p className="mt-2 text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
            Employee Portal
          </p>
        </div>

        <nav className="mt-10 space-y-2">
          {employeeNav.map((item) => {
            const Icon = item.icon;
            const active = item.id === activeView;
            const kids = visibleChildren(item);

            return (
              <div key={item.id}>
                <button
                  type="button"
                  onClick={() => selectParent(item.id)}
                  className={`flex w-full items-center gap-3 rounded-[22px] border px-4 py-3 text-left text-sm font-semibold transition-all ${
                    active
                      ? "border-primary/40 bg-card text-primary shadow-ambient"
                      : "border-transparent text-muted-foreground hover:bg-surface-low hover:text-foreground"
                  }`}
                >
                  <Icon className="h-4 w-4" />
                  <span>{item.label}</span>
                  <CountBadge count={badgeFor(item.id)} className="ml-auto" />
                </button>

                {active && kids.length > 1 ? (
                  <div className="ml-5 mt-1 space-y-0.5 border-l border-border/60 pl-4">
                    {kids.map((child) => {
                      const childActive = child.id === sub;
                      return (
                        <button
                          key={child.id}
                          type="button"
                          onClick={() => selectChild(item.id, child.id)}
                          className={`flex w-full items-center rounded-lg px-3 py-1.5 text-left text-xs font-semibold transition-colors ${
                            childActive
                              ? "bg-primary/10 text-primary"
                              : "text-muted-foreground hover:bg-surface-low hover:text-foreground"
                          }`}
                        >
                          <span>{child.label}</span>
                          <CountBadge count={childBadge(child.id)} className="ml-auto" />
                        </button>
                      );
                    })}
                  </div>
                ) : null}
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="flex min-h-screen min-w-0 flex-col">
        <header className="sticky top-0 z-30 border-b border-border/55 bg-background/82 backdrop-blur-xl">
          <div className="mx-auto flex w-full max-w-6xl items-center justify-between gap-4 px-6 py-4 sm:px-7 lg:px-8">
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Employee
              </p>
              <h1 className="truncate text-2xl font-black tracking-tight text-foreground">
                {activeItem.label}
              </h1>
              {organizationName ? (
                <p className="mt-1 truncate text-sm text-muted-foreground">{organizationName}</p>
              ) : null}
            </div>

            <div className="flex shrink-0 items-center gap-3">
              {/* Renders only for someone who belongs to more than one company,
                  and without "New company" — that is an admin action. Without
                  it a multi-company employee is stuck in whichever membership
                  login happened to pick first, with no way to reach the rest. */}
              <OrgSwitcher allowCreate={false} hideWhenSingle />

              <NotificationBell onNavigate={navigateFromNotification} />

              <div
                ref={accountMenuRef}
                className="relative flex items-center gap-3 rounded-full border border-border/60 bg-card/90 px-3 py-2 shadow-ambient"
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary text-sm font-bold text-primary-foreground">
                  {initials}
                </div>
                <div className="hidden text-right lg:block">
                  <p className="text-sm font-bold text-foreground">{displayName}</p>
                  <p className="text-xs text-muted-foreground">{user.role}</p>
                </div>
                <button
                  type="button"
                  aria-label="Account menu"
                  aria-expanded={accountMenuOpen}
                  onClick={() => setAccountMenuOpen((open) => !open)}
                  className="flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
                >
                  <MoreVertical className="h-4 w-4" />
                </button>

                {accountMenuOpen ? (
                  <div className="absolute right-0 top-[calc(100%+0.6rem)] z-50 w-64 overflow-hidden rounded-2xl border border-border/70 bg-card/98 p-2 text-left shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl">
                    <div className="border-b border-border/60 px-3 py-2.5">
                      <p className="truncate text-sm font-bold text-foreground">{displayName}</p>
                      <p className="truncate text-xs text-muted-foreground">{user.email}</p>
                      {organizationName ? (
                        <p className="mt-1 truncate text-xs font-medium text-primary">{organizationName}</p>
                      ) : null}
                    </div>

                    <button
                      type="button"
                      onClick={() => {
                        setAccountMenuOpen(false);
                        setChangePasswordOpen(true);
                      }}
                      className="mt-2 flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm text-muted-foreground transition hover:bg-muted"
                    >
                      <KeyRound className="mt-0.5 h-4 w-4 shrink-0" />
                      <span>
                        <span className="block font-semibold text-foreground">Change password</span>
                        <span className="block text-xs">Signs out every device</span>
                      </span>
                    </button>

                    <PushToggleMenuItem
                      onClose={() => setAccountMenuOpen(false)}
                      className="flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-foreground transition hover:bg-muted"
                    />

                    <button
                      type="button"
                      disabled
                      className="flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm text-muted-foreground opacity-60"
                    >
                      <Building2 className="mt-0.5 h-4 w-4 shrink-0" />
                      <span>
                        <span className="block font-semibold text-foreground">Switch company</span>
                        <span className="block text-xs">Shown when multiple companies are available</span>
                      </span>
                    </button>

                    <button
                      type="button"
                      onClick={() => {
                        setAccountMenuOpen(false);
                        launchAppraisify().catch(() => {
                          window.alert("Couldn't open Appraisify — please try again.");
                        });
                      }}
                      className="flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-foreground transition hover:bg-muted"
                    >
                      <ExternalLink className="mt-0.5 h-4 w-4 shrink-0" />
                      Launch Appraisify
                    </button>

                    <button
                      type="button"
                      onClick={() => {
                        setAccountMenuOpen(false);
                        onLogout();
                      }}
                      className="mt-1 flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-destructive transition hover:bg-destructive/10"
                    >
                      <LogOut className="h-4 w-4 shrink-0" />
                      Log out
                    </button>
                  </div>
                ) : null}
              </div>
            </div>
          </div>
        </header>

        {/* A successful change revokes every session, this one included, so the
            only coherent next step is the login screen. */}
        {changePasswordOpen ? (
          <ChangePasswordModal
            onClose={() => setChangePasswordOpen(false)}
            onChanged={() => {
              setChangePasswordOpen(false);
              onLogout();
            }}
          />
        ) : null}

        {/* Mobile sub-nav strip for the active tab's sub-pages. */}
        {activeChildren.length > 1 ? (
          <OverflowTabList
            items={activeChildren.map((child) => ({
              id: child.id,
              label: child.label,
              badge: childBadge(child.id),
            }))}
            value={sub ?? activeChildren[0]?.id ?? ""}
            onChange={(childId) => selectChild(activeView, childId)}
            className="border-b border-border/50 px-6 sm:px-7 lg:hidden"
            menuClassName="right-6 sm:right-7"
            ariaLabel={`${activeItem.label} sections`}
          />
        ) : null}

        <main className="flex-1 pb-28 lg:pb-10">
          <div className="mx-auto w-full max-w-6xl px-6 py-6 sm:px-7 lg:px-8 lg:py-8">
            {/* Keyed on the active view so it remounts per tab, easing the
                content in instead of popping; min-h reserves the viewport so a
                shorter tab doesn't collapse the page and jump the scroll.

                Fade only — the slide is gone on purpose. slide-in-from-bottom
                animates translate3d, and a transformed element becomes the
                containing block for every position:fixed DESCENDANT. For the
                200ms it ran, the floating "+" resolved bottom-32 right-5
                against this div instead of the viewport, so it appeared over
                the content and snapped to the corner when the animation
                finished. Opacity creates no containing block. */}
            <div
              key={activeView}
              className="min-h-[60vh] animate-in fade-in-0 duration-200 ease-out"
            >
              {activeView === "dashboard" ? <DashboardView user={user} onNavigate={selectParent} /> : null}
              {activeView === "claims" ? (
                <ClaimsPage sub={sub ?? "claims-mine"} onDecided={refreshBadges} />
              ) : null}
              {activeView === "attendance" ? (
                <AttendanceView
                  sub={sub ?? "att-dashboard"}
                  onViewHistory={() => selectChild("attendance", "att-history")}
                />
              ) : null}
              {activeView === "leave" ? <LeavePage sub={sub ?? "leave-mine"} /> : null}
              {activeView === "payslips" ? <PayslipsView /> : null}
            </div>
          </div>
        </main>

        {/* Mobile bottom nav — all five tabs, with pending badges. */}
        <nav className="fixed inset-x-4 bottom-4 z-40 rounded-[32px] border border-border/60 glass-panel px-2 py-2 shadow-panel lg:hidden">
          <div className="grid grid-cols-5 gap-1">
            {employeeNav.map((item) => {
              const Icon = item.icon;
              const active = item.id === activeView;
              const count = badgeFor(item.id);
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => selectParent(item.id)}
                  className={`relative flex min-h-[62px] flex-col items-center justify-center gap-1 rounded-[24px] px-1 text-center text-[10px] font-semibold leading-tight transition ${
                    active ? "bg-primary text-primary-foreground" : "text-muted-foreground"
                  }`}
                >
                  <Icon className="h-4 w-4 shrink-0" />
                  <span className="line-clamp-2">{item.label}</span>
                  {count > 0 ? (
                    <span className="absolute right-1 top-1 flex h-4 min-w-[1rem] items-center justify-center rounded-full bg-destructive px-1 text-[9px] font-bold text-destructive-foreground">
                      {count > 99 ? "99+" : count}
                    </span>
                  ) : null}
                </button>
              );
            })}
          </div>
        </nav>
      </div>
    </div>
  );
}

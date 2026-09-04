import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Bell, Building2, KeyRound, LogOut, MoreVertical } from "lucide-react";
import { AttendanceView } from "@/features/attendance/components/AttendanceView";
import { ClaimsPage } from "@/features/claims/components/ClaimsPage";
import { LeaveView } from "@/features/leave/components/LeaveView";
import { getTeamClaims } from "@/features/claims/api";
import { getTeamLeave } from "@/features/leave/api";
import { getOrganization } from "@/features/settings/api";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import type { SignedInUser } from "@/shared/types/session";
import { buildInitials, buildName } from "../lib/employee-formatters";
import { defaultSubOf, employeeNav, findNavItem } from "../lib/nav";
import type { EmployeeView } from "../lib/types";
import { DashboardView } from "./DashboardView";
import { EmptyModule } from "./EmptyModule";

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
  const [activeView, setActiveView] = useState<EmployeeView>("dashboard");
  const [sub, setSub] = useState<string | null>(null);
  const [organizationName, setOrganizationName] = useState<string | null>(null);
  const [claimBadge, setClaimBadge] = useState(0);
  const [leaveBadge, setLeaveBadge] = useState(0);
  const [accountMenuOpen, setAccountMenuOpen] = useState(false);
  const accountMenuRef = useRef<HTMLDivElement | null>(null);
  // No org-wide attendance-approval endpoint yet, so this stays 0 (hidden).
  const attendanceBadge = 0;

  const activeItem = findNavItem(activeView);
  const initials = useMemo(() => buildInitials(user.email), [user.email]);
  const displayName = useMemo(() => buildName(user.email), [user.email]);

  useEffect(() => {
    getOrganization()
      .then((org) => setOrganizationName(org.name))
      .catch(() => setOrganizationName(null));
  }, []);

  // Named so an approval can call it again. Fetched once on mount, the badge
  // kept claiming work was waiting after the approver had already cleared it.
  const refreshBadges = useCallback(() => {
    if (!isSupervisor) return;
    Promise.all([getTeamClaims().catch(() => []), getTeamLeave().catch(() => [])]).then(
      ([claims, leave]) => {
        setClaimBadge(claims.filter((c) => c.status === "PENDING").length);
        setLeaveBadge(leave.filter((l) => l.status === "PENDING").length);
      },
    );
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
    setActiveView(id);
    setSub(defaultSubOf(findNavItem(id)));
  }

  function selectChild(parentId: EmployeeView, childId: string) {
    setActiveView(parentId);
    setSub(childId);
  }

  const visibleChildren = (item = activeItem) =>
    (item.children ?? []).filter((c) => !c.supervisorOnly || isSupervisor);
  const activeChildren = visibleChildren();
  const notificationCount = claimBadge + leaveBadge + attendanceBadge;

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
              <button
                type="button"
                aria-label="Notifications"
                className="relative flex h-10 w-10 items-center justify-center rounded-full border border-border/60 bg-card/90 text-muted-foreground shadow-ambient transition hover:text-foreground"
              >
                <Bell className="h-5 w-5" />
                {notificationCount > 0 ? (
                  <span className="absolute -right-0.5 -top-0.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-bold leading-none text-destructive-foreground">
                    {notificationCount > 99 ? "99+" : notificationCount}
                  </span>
                ) : null}
              </button>

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
                      disabled
                      className="mt-2 flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm text-muted-foreground opacity-60"
                    >
                      <KeyRound className="mt-0.5 h-4 w-4 shrink-0" />
                      <span>
                        <span className="block font-semibold text-foreground">Change password</span>
                        <span className="block text-xs">Coming later</span>
                      </span>
                    </button>

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
            {activeView === "leave" ? <LeaveView sub={sub ?? "leave-mine"} role={user.role} /> : null}
            {activeView === "payslips" ? (
              <EmptyModule
                title="Payslips"
                body="This area will hold employee payslip history and payroll document downloads."
              />
            ) : null}
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

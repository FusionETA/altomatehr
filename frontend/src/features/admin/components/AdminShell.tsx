import { useMemo, useState } from "react";
import { Bell, LogOut } from "lucide-react";
import { AccountsSettings } from "@/features/settings/components/AccountsSettings";
import { EmployeesSettings } from "@/features/settings/components/EmployeesSettings";
import { LeaveTypesSettings } from "@/features/settings/components/LeaveTypesSettings";
import { OrganizationSettings } from "@/features/settings/components/OrganizationSettings";
import { PoliciesSettings } from "@/features/settings/components/PoliciesSettings";
import { ProjectsSettings } from "@/features/settings/components/ProjectsSettings";
import { TeamsSettings } from "@/features/settings/components/TeamsSettings";
import { EmptyModule } from "@/features/employee-portal/components/EmptyModule";
import { buildInitials, buildName } from "@/features/employee-portal/lib/employee-formatters";
import { HorizontalScrollArea } from "@/shared/components/HorizontalScrollArea";
import type { SignedInUser } from "@/shared/types/session";
import { adminNav, defaultChildOf, findNavItem } from "../lib/nav";
import { AdminAttendance } from "./AdminAttendance";
import { AdminOverview } from "./AdminOverview";

export function AdminShell({
  user,
  onLogout,
}: {
  user: SignedInUser;
  onLogout: () => void;
}) {
  const [activeParent, setActiveParent] = useState("overview");
  const [activeChild, setActiveChild] = useState("overview");

  const activeItem = findNavItem(activeParent);
  const initials = useMemo(() => buildInitials(user.email), [user.email]);
  const displayName = useMemo(() => buildName(user.email), [user.email]);

  function selectParent(id: string) {
    const item = findNavItem(id);
    setActiveParent(id);
    setActiveChild(defaultChildOf(item));
  }

  function open(parentId: string, childId: string) {
    setActiveParent(parentId);
    setActiveChild(childId);
  }

  return (
    <div className="min-h-screen bg-background lg:grid lg:grid-cols-[300px_1fr]">
      <aside className="hidden min-h-screen flex-col border-r border-border/60 bg-card/72 p-6 backdrop-blur-xl lg:flex">
        <div className="self-center text-center">
          <img src="/brand-logo.png" alt="AltomateHR logo" className="h-auto w-[148px] object-contain" />
          <p className="mt-2 text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            Enterprise Admin
          </p>
        </div>

        <nav className="mt-10 space-y-1.5">
          {adminNav.map((item) => {
            const Icon = item.icon;
            const active = item.id === activeParent;

            return (
              <div key={item.id}>
                <button
                  type="button"
                  onClick={() => selectParent(item.id)}
                  className={`flex w-full items-center gap-3 rounded-[22px] border px-4 py-3 text-left text-sm font-semibold transition-all ${
                    active
                      ? "border-primary/40 bg-card text-primary shadow-ambient"
                      : "border-transparent text-muted-foreground hover:bg-muted hover:text-foreground"
                  }`}
                >
                  <Icon className="h-4 w-4" />
                  <span>{item.label}</span>
                </button>

                {item.children && active ? (
                  <div className="ml-5 mt-1 space-y-0.5 border-l border-border/60 pl-4">
                    {item.children.map((child) => {
                      const childActive = child.id === activeChild;
                      return (
                        <button
                          key={child.id}
                          type="button"
                          onClick={() => setActiveChild(child.id)}
                          className={`block w-full rounded-lg px-3 py-1.5 text-left text-xs font-semibold transition-colors ${
                            childActive
                              ? "bg-primary/10 text-primary"
                              : "text-muted-foreground hover:bg-muted hover:text-foreground"
                          }`}
                        >
                          {child.label}
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
          <div className="mx-auto flex w-full max-w-6xl items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Admin Portal
              </p>
              <h1 className="truncate text-2xl font-black tracking-tight text-foreground">
                {activeItem.label}
              </h1>
              <p className="mt-1 hidden text-sm text-muted-foreground sm:block">AltomateHR</p>
            </div>

            <div className="flex items-center gap-2 sm:gap-3">
              <button
                type="button"
                aria-label="Notifications"
                className="hidden h-10 w-10 items-center justify-center rounded-full border border-border/60 bg-card/90 text-muted-foreground shadow-ambient transition hover:text-foreground sm:flex"
              >
                <Bell className="h-4 w-4" />
              </button>

              <div className="flex items-center gap-2 rounded-full border border-border/60 bg-card/90 px-2 py-2 shadow-ambient sm:gap-3 sm:px-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary text-sm font-bold text-primary-foreground">
                  {initials}
                </div>
                <div className="hidden text-right sm:block">
                  <p className="text-sm font-bold text-foreground">{displayName}</p>
                  <p className="text-xs text-muted-foreground">{user.role}</p>
                </div>
                <button
                  type="button"
                  onClick={onLogout}
                  aria-label="Log out"
                  className="flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
                >
                  <LogOut className="h-4 w-4" />
                </button>
              </div>
            </div>
          </div>
        </header>

        {/* Mobile module switcher — the desktop sidebar is hidden below lg. */}
        <HorizontalScrollArea className="px-4 py-3 lg:hidden" contentClassName="gap-2">
          {adminNav.map((item) => {
            const active = item.id === activeParent;
            const Icon = item.icon;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => selectParent(item.id)}
                className={`inline-flex shrink-0 items-center gap-2 rounded-full border px-3 py-2 text-xs font-semibold transition-colors ${
                  active
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground"
                }`}
              >
                <Icon className="h-4 w-4" />
                {item.label}
              </button>
            );
          })}
        </HorizontalScrollArea>

        {/* Mobile sub-nav for the active module. */}
        {activeItem.children ? (
          <HorizontalScrollArea className="px-4 pb-3 lg:hidden" contentClassName="gap-2">
            {activeItem.children.map((child) => {
              const childActive = child.id === activeChild;
              return (
                <button
                  key={child.id}
                  type="button"
                  onClick={() => setActiveChild(child.id)}
                  className={`shrink-0 rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors ${
                    childActive
                      ? "border-primary/40 bg-primary/10 text-primary"
                      : "border-border/60 bg-card text-muted-foreground"
                  }`}
                >
                  {child.label}
                </button>
              );
            })}
          </HorizontalScrollArea>
        ) : null}

        <main className="flex-1 pb-16 lg:pb-10">
          <div className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
            <AdminContent activeChild={activeChild} user={user} onOpen={open} />
          </div>
        </main>
      </div>
    </div>
  );
}

function AdminContent({
  activeChild,
  user,
  onOpen,
}: {
  activeChild: string;
  user: SignedInUser;
  onOpen: (parentId: string, childId: string) => void;
}) {
  switch (activeChild) {
    case "overview":
      return <AdminOverview user={user} onOpen={onOpen} />;

    // Company / Employee
    case "manage-employee":
      return <EmployeesSettings />;
    case "teams":
      return <TeamsSettings />;

    // System Settings
    case "settings-organization":
      return <OrganizationSettings />;
    case "settings-accounts":
      return <AccountsSettings />;
    case "settings-projects":
      return <ProjectsSettings />;
    case "settings-policies":
      return <PoliciesSettings />;
    case "settings-leave":
      return <LeaveTypesSettings />;

    // Org-wide attendance roll-call — the backend already returns every
    // employee's records to admins.
    case "attendance":
      return <AdminAttendance />;

    // Not yet rebuilt — faithful placeholders that mirror the real modules.
    case "claims":
      return (
        <EmptyModule
          title="Claims"
          body="The company claims queue, payroll-ready view, and claim reports will live here."
        />
      );
    case "payroll":
      return (
        <EmptyModule
          title="Payroll"
          body="Payroll runs, annual tax forms and loans will live here. Payroll is being rebuilt last."
        />
      );
    case "leave":
      return (
        <EmptyModule
          title="Leave"
          body="Admin leave overview, balances and leave settings will live here."
        />
      );
    case "audit":
      return (
        <EmptyModule
          title="Activity Log"
          body="A per-organization activity feed of admin and employee actions will live here."
        />
      );

    default:
      return <AdminOverview user={user} onOpen={onOpen} />;
  }
}

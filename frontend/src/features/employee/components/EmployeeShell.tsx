import { useMemo, useState } from "react";
import { Bell, LogOut, Settings, UserCircle } from "lucide-react";
import { AttendanceView } from "@/features/attendance/components/AttendanceView";
import { ClaimsPage } from "@/features/claims/components/ClaimsPage";
import { LeaveView } from "@/features/leave/components/LeaveView";
import { SettingsView } from "@/features/settings/components/SettingsView";
import type { SignedInUser } from "@/shared/types/session";
import { buildInitials, buildName } from "../lib/employee-formatters";
import { employeeNav, mobileMoreNav, mobilePrimaryNav } from "../lib/nav";
import type { EmployeeView } from "../lib/types";
import { DashboardView } from "./DashboardView";
import { EmptyModule } from "./EmptyModule";

export function EmployeeShell({
  user,
  activeView,
  onChangeView,
  onLogout,
}: {
  user: SignedInUser;
  activeView: EmployeeView;
  onChangeView: (view: EmployeeView) => void;
  onLogout: () => void;
}) {
  const activeItem = employeeNav.find((item) => item.id === activeView) ?? employeeNav[0];
  const initials = useMemo(() => buildInitials(user.email), [user.email]);
  const displayName = useMemo(() => buildName(user.email), [user.email]);
  const [moreOpen, setMoreOpen] = useState(false);

  function handleMobileNav(view: EmployeeView) {
    setMoreOpen(false);
    onChangeView(view);
  }

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

            return (
              <button
                key={item.id}
                type="button"
                onClick={() => onChangeView(item.id)}
                className={`flex w-full items-center gap-3 rounded-[22px] border px-4 py-3 text-left text-sm font-semibold transition-all ${
                  active
                    ? "border-primary/40 bg-card text-primary shadow-[0_12px_30px_rgba(76,26,134,0.07)]"
                    : "border-transparent text-muted-foreground hover:bg-muted hover:text-foreground"
                }`}
              >
                <Icon className="h-4 w-4" />
                <span>{item.label}</span>
              </button>
            );
          })}

          {user.role === "Admin" ? (
            <button
              type="button"
              onClick={() => onChangeView("settings")}
              className={`flex w-full items-center gap-3 rounded-[22px] border px-4 py-3 text-left text-sm font-semibold transition-all ${
                activeView === "settings"
                  ? "border-primary/40 bg-card text-primary shadow-[0_12px_30px_rgba(76,26,134,0.07)]"
                  : "border-transparent text-muted-foreground hover:bg-muted hover:text-foreground"
              }`}
            >
              <Settings className="h-4 w-4" />
              <span>Settings</span>
            </button>
          ) : null}
        </nav>
      </aside>

      <div className="flex min-h-screen min-w-0 flex-col">
        <header className="sticky top-0 z-30 border-b border-border/55 bg-background/82 backdrop-blur-xl">
          <div className="mx-auto flex w-full max-w-6xl items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Employee
              </p>
              <h1 className="truncate text-2xl font-black tracking-tight text-foreground">
                {activeView === "settings" ? "Settings" : activeItem.label}
              </h1>
              <p className="mt-1 hidden text-sm text-muted-foreground sm:block">AltomateHR</p>
            </div>

            <div className="flex items-center gap-2 sm:gap-3">
              <button
                type="button"
                aria-label="Notifications"
                className="hidden h-10 w-10 items-center justify-center rounded-full border border-border/60 bg-card/90 text-muted-foreground shadow-[0_12px_30px_rgba(76,26,134,0.07)] transition hover:text-foreground sm:flex"
              >
                <Bell className="h-4 w-4" />
              </button>

              <div className="flex items-center gap-2 rounded-full border border-border/60 bg-card/90 px-2 py-2 shadow-[0_12px_30px_rgba(76,26,134,0.07)] sm:gap-3 sm:px-3">
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

        <main className="flex-1 pb-32 lg:pb-10">
          <div className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
            {activeView === "dashboard" ? <DashboardView user={user} /> : null}
            {activeView === "claims" ? <ClaimsPage role={user.role} /> : null}
            {activeView === "attendance" ? <AttendanceView /> : null}
            {activeView === "leave" ? <LeaveView role={user.role} /> : null}
            {activeView === "payslips" ? (
              <EmptyModule
                title="Payslips"
                body="This area will hold employee payslip history and payroll document downloads."
              />
            ) : null}
            {activeView === "settings" ? <SettingsView /> : null}
          </div>
        </main>

        {moreOpen ? (
          <div className="fixed inset-x-4 bottom-[104px] z-40 rounded-[28px] border border-border/60 bg-white/95 p-2 shadow-[0_18px_48px_rgba(76,26,134,0.10)] backdrop-blur-xl lg:hidden">
            <div className="grid gap-1">
              {mobileMoreNav.map((item) => {
                const Icon = item.icon;
                const active = item.id === activeView;

                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => handleMobileNav(item.id)}
                    className={`flex items-center gap-3 rounded-[22px] px-4 py-3 text-left text-sm font-semibold transition ${
                      active
                        ? "bg-primary text-primary-foreground"
                        : "text-muted-foreground hover:bg-muted hover:text-foreground"
                    }`}
                  >
                    <Icon className="h-4 w-4 shrink-0" />
                    <span>{item.label}</span>
                  </button>
                );
              })}
              {user.role === "Admin" ? (
                <button
                  type="button"
                  onClick={() => handleMobileNav("settings")}
                  className={`flex items-center gap-3 rounded-[22px] px-4 py-3 text-left text-sm font-semibold transition ${
                    activeView === "settings"
                      ? "bg-primary text-primary-foreground"
                      : "text-muted-foreground hover:bg-muted hover:text-foreground"
                  }`}
                >
                  <Settings className="h-4 w-4 shrink-0" />
                  <span>Settings</span>
                </button>
              ) : null}
              <button
                type="button"
                onClick={() => {
                  setMoreOpen(false);
                  onLogout();
                }}
                className="flex items-center gap-3 rounded-[22px] px-4 py-3 text-left text-sm font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground"
              >
                <LogOut className="h-4 w-4 shrink-0" />
                <span>Log out</span>
              </button>
            </div>
          </div>
        ) : null}

        <nav className="fixed inset-x-4 bottom-4 z-40 rounded-[32px] border border-border/60 bg-white/90 px-2 py-2 shadow-[0_18px_48px_rgba(76,26,134,0.10)] backdrop-blur-xl lg:hidden">
          <div className="grid grid-cols-4 gap-1">
            {mobilePrimaryNav.map((item) => {
              const Icon = item.icon;
              const active = item.id === activeView;

              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => handleMobileNav(item.id)}
                  className={`flex min-h-[64px] flex-col items-center justify-center gap-1 rounded-[24px] px-1 text-center text-[10px] font-semibold leading-tight transition ${
                    active ? "bg-primary text-primary-foreground" : "text-muted-foreground"
                  }`}
                >
                  <Icon className="h-4 w-4 shrink-0" />
                  <span className="line-clamp-2">{item.label}</span>
                </button>
              );
            })}
            <button
              type="button"
              onClick={() => setMoreOpen((open) => !open)}
              className={`flex min-h-[64px] flex-col items-center justify-center gap-1 rounded-[24px] px-1 text-center text-[10px] font-semibold leading-tight transition ${
                mobileMoreNav.some((item) => item.id === activeView) || activeView === "settings"
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground"
              }`}
            >
              <UserCircle className="h-4 w-4 shrink-0" />
              <span>More</span>
            </button>
          </div>
        </nav>
      </div>
    </div>
  );
}

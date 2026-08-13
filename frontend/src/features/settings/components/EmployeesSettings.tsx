import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import { getEmployees, ROLES, updateEmployee, type Employee } from "@/features/employees/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm sm:p-6";
const TH = "h-11 px-3 text-left text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";
const NONE = "__none__";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function EmployeesSettings() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);

  useEffect(() => {
    getEmployees()
      .then(setEmployees)
      .catch((e: unknown) => setError(message(e, "Could not load employees.")))
      .finally(() => setLoading(false));
  }, []);

  async function save(emp: Employee, patch: { role?: string; supervisorId?: string | null }) {
    setSavingId(emp.id);
    setError(null);
    try {
      const updated = await updateEmployee(emp.id, {
        role: patch.role ?? emp.role,
        supervisorId: patch.supervisorId !== undefined ? patch.supervisorId : emp.supervisorId,
      });
      setEmployees((cur) => cur.map((e) => (e.id === updated.id ? updated : e)));
    } catch (err) {
      setError(message(err, "Could not update the employee."));
    } finally {
      setSavingId(null);
    }
  }

  return (
    <div className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Employees</h2>
        <p className="text-sm text-muted-foreground">
          Set each person's role and approving supervisor. Leave and claim approvals route to the
          assigned supervisor.
        </p>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading employees…</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-border/60">
                <th className={TH}>Email</th>
                <th className={TH}>Role</th>
                <th className={TH}>Supervisor</th>
              </tr>
            </thead>
            <tbody>
              {employees.map((emp) => (
                <tr key={emp.id} className="border-b border-border/60">
                  <td className="px-3 py-3">
                    <span className="inline-flex items-center gap-2 font-semibold text-foreground">
                      {emp.email}
                      {savingId === emp.id ? (
                        <LoaderCircle className="h-3.5 w-3.5 animate-spin text-muted-foreground" />
                      ) : null}
                    </span>
                  </td>
                  <td className="px-3 py-2">
                    <Select value={emp.role} onValueChange={(role) => save(emp, { role })}>
                      <SelectTrigger className="h-10 w-[150px] bg-card">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {ROLES.map((r) => (
                          <SelectItem key={r} value={r}>
                            {r}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </td>
                  <td className="px-3 py-2">
                    <Select
                      value={emp.supervisorId ?? NONE}
                      onValueChange={(v) => save(emp, { supervisorId: v === NONE ? null : v })}
                    >
                      <SelectTrigger className="h-10 w-[220px] bg-card">
                        <SelectValue placeholder="No supervisor" />
                      </SelectTrigger>
                      <SelectContent searchPlaceholder="Search people…">
                        <SelectItem value={NONE}>No supervisor</SelectItem>
                        {employees
                          .filter((o) => o.id !== emp.id)
                          .map((o) => (
                            <SelectItem key={o.id} value={o.id}>
                              {o.email}
                            </SelectItem>
                          ))}
                      </SelectContent>
                    </Select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

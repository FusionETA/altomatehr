import { apiGet, apiPut } from "@/shared/lib/api-client";

export type Employee = {
  id: string;
  email: string;
  role: string;
  supervisorId: string | null;
  supervisorEmail: string | null;
  policyId: string | null;
};

export type UpdateEmployee = {
  role: string;
  supervisorId?: string | null;
  policyId?: string | null;
};

export const ROLES = ["Employee", "Supervisor", "Admin", "Owner"] as const;

export const getEmployees = () => apiGet<Employee[]>("/employees");
export const updateEmployee = (id: string, body: UpdateEmployee) =>
  apiPut<Employee>(`/employees/${id}`, body);

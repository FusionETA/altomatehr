import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

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

export type CreateEmployee = {
  email: string;
  // Only needed for a brand-new account; ignored if the email already exists (multi-org reuse).
  password?: string;
  role: string;
  supervisorId?: string | null;
  policyId?: string | null;
};

export const createEmployee = (body: CreateEmployee) => apiPost<Employee>("/employees", body);

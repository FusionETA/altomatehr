import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

export type Employee = {
  id: string;
  email: string;
  // The person's real name, stored on the global User. The app derived names
  // from email addresses for a long time because this was never read.
  name: string;
  role: string;
  employeeNumber: string | null;
  jobTitle: string | null;
  joinDate: string | null;
  supervisorId: string | null;
  supervisorEmail: string | null;
  policyId: string | null;
  shiftId: string | null;
};

// Patch semantics on the profile fields: omit to leave unchanged, send an
// empty string to clear. Role is always required.
export type UpdateEmployee = {
  role: string;
  name?: string;
  employeeNumber?: string;
  jobTitle?: string;
  joinDate?: string | null;
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
  // Required for a new account; ignored when reusing an existing identity,
  // since that person keeps the name they already have.
  name?: string;
  employeeNumber?: string;
  jobTitle?: string;
  joinDate?: string | null;
  role: string;
  supervisorId?: string | null;
  policyId?: string | null;
};

export const createEmployee = (body: CreateEmployee) => apiPost<Employee>("/employees", body);

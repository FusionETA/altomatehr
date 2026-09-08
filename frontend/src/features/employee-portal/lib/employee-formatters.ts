// Prefers a real name when one is known — "Evan Employee" reads as EE, where
// the email local part would give EM.
export function buildInitials(email: string, name?: string | null) {
  const source = name?.trim() ? name.trim() : email.split("@")[0];
  return (
    source
      .split(/[\s._-]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join("") || "EH"
  );
}

export function buildName(email: string) {
  return (
    email
      .split("@")[0]
      .split(/[._-]/)
      .filter(Boolean)
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(" ") || "Employee"
  );
}

// A label the server already resolved to a person — a real name where the
// directory has one, an email where it doesn't. Prettifies the email case so an
// admin never reads "aisha.rahman@acme.com" in a list of people.
export function displayPerson(label: string) {
  return label.includes("@") ? buildName(label) : label;
}

// Marks the org's default policy wherever a policy is listed or picked, so an
// admin can see which one a new hire lands on without a separate "default"
// entry in the list.
export function DefaultPolicyTag() {
  return (
    <span className="ml-2 inline-flex items-center rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-semibold text-primary">
      Default
    </span>
  );
}

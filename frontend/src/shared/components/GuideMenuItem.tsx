import { CircleHelp } from "lucide-react";

// The published user guides — user-guide/*.html in this repo, served at
// /admin and /employee on this host. Admins get the admin guide, everyone in
// the employee portal (supervisors included) the employee one.
const GUIDE_BASE_URL = "https://hr-guide.altomate.io";

export type GuideAudience = "admin" | "employee";

export const guideUrl = (audience: GuideAudience) => `${GUIDE_BASE_URL}/${audience}`;

// A link rather than a button, so it opens in a new tab and keeps the app
// where it was — and middle-click / long-press work as people expect.
export function GuideMenuItem({
  audience,
  onSelect,
  className,
}: {
  audience: GuideAudience;
  onSelect: () => void;
  className: string;
}) {
  return (
    <a href={guideUrl(audience)} target="_blank" rel="noopener noreferrer" onClick={onSelect} className={className}>
      <CircleHelp className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      Guide
    </a>
  );
}

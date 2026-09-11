import { Check } from "lucide-react";

// A checkbox that looks like the rest of the app.
//
// The native control paints itself with the operating system's accent —
// bright blue on macOS, square-ish, and immune to most styling once
// checked. So the real input is visually hidden and a span is drawn in its
// place: rounded, in the app's primary, with a lucide tick. The input is
// still a real focusable checkbox, so keyboard and screen-reader behaviour
// are unchanged.
export function CheckBox({
  id,
  checked,
  onChange,
  disabled,
  ariaLabel,
}: {
  id?: string;
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
  ariaLabel?: string;
}) {
  return (
    <span className="relative inline-flex shrink-0 items-center">
      <input
        id={id}
        type="checkbox"
        aria-label={ariaLabel}
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="peer absolute inset-0 size-5 cursor-pointer opacity-0 disabled:cursor-not-allowed"
      />
      <span
        aria-hidden
        className={[
          "flex size-5 items-center justify-center rounded-md border bg-background text-transparent shadow-sm transition",
          "peer-checked:border-primary peer-checked:bg-primary peer-checked:text-primary-foreground",
          "peer-focus-visible:ring-2 peer-focus-visible:ring-primary peer-focus-visible:ring-offset-2 ring-offset-background",
          "peer-disabled:opacity-50",
          checked ? "border-primary" : "border-border",
        ].join(" ")}
      >
        <Check className="size-3.5" strokeWidth={3} />
      </span>
    </span>
  );
}

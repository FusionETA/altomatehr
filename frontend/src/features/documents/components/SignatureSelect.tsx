import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { SignatureThumbnail } from "@/features/settings/components/SignatureThumbnail";
import { signatureLabel } from "@/features/settings/lib/signatures";
import type { LetterSignature } from "../lib/letter-signature";

// A dropdown of the company's saved signatures, with a thumbnail of the one
// picked. `extra` is the option that isn't a signature — "Company default" in
// the template editor (listed first), "No signature (sign by hand)" in the
// Generate dialog (listed last).
export function SignatureSelect({
  value,
  onChange,
  signatures,
  extra,
  disabled,
  loading,
}: {
  value: string;
  onChange: (value: string) => void;
  signatures: LetterSignature[];
  extra: { value: string; label: string; position: "first" | "last" };
  disabled?: boolean;
  loading?: boolean;
}) {
  const selected = signatures.find((s) => s.id === value) ?? null;
  const extraItem = <SelectItem value={extra.value}>{extra.label}</SelectItem>;

  return (
    <div className="mt-1.5 flex items-center gap-3">
      {selected ? <SignatureThumbnail id={selected.id} name={selected.name} className="h-11 w-24" /> : null}
      <Select value={value} onValueChange={onChange} disabled={disabled}>
        <SelectTrigger className="h-11 min-w-0 flex-1 bg-card">
          <SelectValue placeholder={loading ? "Loading…" : "Pick a signature"} />
        </SelectTrigger>
        <SelectContent>
          {extra.position === "first" ? extraItem : null}
          {signatures.map((s) => (
            <SelectItem key={s.id} value={s.id}>
              {signatureLabel(s)}
            </SelectItem>
          ))}
          {extra.position === "last" ? extraItem : null}
        </SelectContent>
      </Select>
    </div>
  );
}

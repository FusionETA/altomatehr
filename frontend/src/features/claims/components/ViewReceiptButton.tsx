import { useState, type MouseEvent } from "react";
import { openClaimReceipt } from "../api";

export function ViewReceiptButton({
  receiptUrl,
  className,
  label = "View receipt",
}: {
  receiptUrl: string;
  className: string;
  label?: string;
}) {
  const [opening, setOpening] = useState(false);

  async function handleOpen(event: MouseEvent<HTMLButtonElement>) {
    event.stopPropagation();
    setOpening(true);
    try {
      await openClaimReceipt(receiptUrl);
    } finally {
      setOpening(false);
    }
  }

  return (
    <button type="button" onClick={handleOpen} disabled={opening} className={className}>
      {opening ? "Opening..." : label}
    </button>
  );
}

import type { HTMLAttributes, ReactNode } from "react";
import { cn } from "@/shared/lib/utils";

type HorizontalScrollAreaProps = HTMLAttributes<HTMLDivElement> & {
  children: ReactNode;
  contentClassName?: string;
};

export function HorizontalScrollArea({
  children,
  className,
  contentClassName,
  ...props
}: HorizontalScrollAreaProps) {
  return (
    <div className={cn("horizontal-scrollbar overflow-x-auto pb-2", className)} {...props}>
      <div className={cn("flex min-w-max", contentClassName)}>{children}</div>
    </div>
  );
}

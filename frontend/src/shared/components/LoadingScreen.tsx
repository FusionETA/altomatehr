import { LoaderCircle } from "lucide-react";

export function LoadingScreen() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background text-foreground">
      <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
        <LoaderCircle className="h-4 w-4 animate-spin" />
        Loading
      </div>
    </div>
  );
}

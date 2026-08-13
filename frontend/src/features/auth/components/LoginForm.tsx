import { useState } from "react";
import { LoaderCircle } from "lucide-react";
import { login, type AuthResponse } from "../api";

export function LoginForm({ onSuccess }: { onSuccess: (res: AuthResponse) => void }) {
  // Prefilled with the demo user so it's easy to test.
  const [email, setEmail] = useState("employee@altomate.com");
  const [password, setPassword] = useState("password123");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const res = await login({ email, password });
      onSuccess(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="flex min-h-[100svh] items-center px-4 py-4 text-foreground sm:min-h-screen sm:px-6 sm:py-10 lg:px-8">
      <div className="mx-auto w-full max-w-xl">
        <form
          onSubmit={handleSubmit}
          className="rounded-[32px] border border-white/60 bg-white/72 px-5 py-5 shadow-[0_18px_48px_rgba(76,26,134,0.10)] backdrop-blur-xl sm:px-8 sm:py-8"
        >
          <div className="space-y-6 pb-3 sm:pb-0">
            <div className="mx-auto flex h-[140px] w-[140px] items-center justify-center rounded-[28px] border border-white/60 bg-white/85 p-4 shadow-[0_12px_30px_rgba(76,26,134,0.07)]">
              <img
                src="/brand-icon.png"
                alt="AltomateHR logo"
                className="h-auto w-[108px] object-contain"
              />
            </div>

            <div className="text-center">
              <h1 className="mt-2 text-[2rem] font-bold leading-none text-[#1f1230] sm:text-[2.4rem]">
                Login
              </h1>
            </div>
          </div>

          <div className="space-y-5 pt-2 sm:pt-6">
            <div className="space-y-2">
              <label htmlFor="email" className="block text-sm font-semibold text-[#2a1d36]">
                Email
              </label>
              <input
                id="email"
                name="email"
                type="email"
                value={email}
                placeholder="your@email.com"
                autoComplete="email"
                aria-invalid={Boolean(error)}
                onChange={(e) => setEmail(e.target.value)}
                className="h-12 w-full rounded-2xl border border-[#d9d0e2] bg-white/80 px-4 text-base text-[#1f1230] shadow-sm transition-colors placeholder:text-[#887d9b] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#4c1a86] focus-visible:ring-offset-2 focus-visible:ring-offset-[#f7f4fa] disabled:cursor-not-allowed disabled:opacity-50 sm:text-sm"
              />
            </div>

            <div className="space-y-2">
              <label htmlFor="password" className="block text-sm font-semibold text-[#2a1d36]">
                Password
              </label>
              <input
                id="password"
                name="password"
                type="password"
                value={password}
                placeholder="Enter your password"
                autoComplete="current-password"
                aria-invalid={Boolean(error)}
                onChange={(e) => setPassword(e.target.value)}
                className="h-12 w-full rounded-2xl border border-[#d9d0e2] bg-white/80 px-4 text-base text-[#1f1230] shadow-sm transition-colors placeholder:text-[#887d9b] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#4c1a86] focus-visible:ring-offset-2 focus-visible:ring-offset-[#f7f4fa] disabled:cursor-not-allowed disabled:opacity-50 sm:text-sm"
              />
            </div>

            {error ? (
              <p className="rounded-2xl border border-red-200/70 bg-red-50/75 px-4 py-3 text-sm font-medium text-red-700">
                {error}
              </p>
            ) : null}

            <div className="flex flex-col gap-3">
              <button
                type="submit"
                disabled={loading}
                className="inline-flex h-12 w-full items-center justify-center gap-2 rounded-2xl bg-[#4c1a86] px-4 py-3 text-sm font-semibold text-white shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-[#572097] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#4c1a86] focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50"
              >
                {loading ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Login
              </button>

              <button
                type="button"
                className="text-center text-sm font-semibold text-[#5b209b] transition hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#4c1a86] focus-visible:ring-offset-2"
              >
                Forgot your password?
              </button>
            </div>
          </div>
        </form>
      </div>
    </main>
  );
}

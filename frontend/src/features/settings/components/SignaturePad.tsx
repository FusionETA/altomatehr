import { useEffect, useRef } from "react";
import { Eraser, Undo2 } from "lucide-react";
import { drawStrokeEnd, drawStrokeTail, redraw, type Point, type Stroke } from "../lib/signature-pad";

// Draw a signature with a finger, mouse or pen.
//
// Controlled: the strokes live with the caller (which exports them with
// strokesToPng), and this only adds to them. Pointer events cover mouse, touch
// and pen alike; `touch-action: none` stops a finger stroke from scrolling the
// page instead of drawing. The canvas backing store follows the box's size
// times devicePixelRatio, so lines stay sharp on a phone, and is redrawn from
// the strokes whenever that size changes.
//
// The pad is white — the paper the signature is printed on — in both themes,
// so the near-black ink looks the way it will on the letter.
export function SignaturePad({
  strokes,
  onChange,
  disabled,
}: {
  strokes: Stroke[];
  onChange: (strokes: Stroke[]) => void;
  disabled?: boolean;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  // The stroke being drawn right now — kept out of React state so a pointer
  // move doesn't re-render; committed to `strokes` when the pointer lifts.
  const active = useRef<{ pointerId: number; stroke: Stroke } | null>(null);
  const size = useRef({ width: 0, height: 0 });
  // Read by the resize observer, which outlives any one render.
  const strokesRef = useRef(strokes);
  strokesRef.current = strokes;

  function context() {
    return canvasRef.current?.getContext("2d") ?? null;
  }

  function repaint() {
    const ctx = context();
    if (!ctx) return;
    const all = active.current ? [...strokesRef.current, active.current.stroke] : strokesRef.current;
    redraw(ctx, all, size.current.width, size.current.height);
  }

  // Size the backing store to the box, sharp at any pixel ratio.
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const fit = () => {
      const rect = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      size.current = { width: rect.width, height: rect.height };
      canvas.width = Math.max(1, Math.round(rect.width * dpr));
      canvas.height = Math.max(1, Math.round(rect.height * dpr));
      canvas.getContext("2d")?.setTransform(dpr, 0, 0, dpr, 0, 0);
      repaint();
    };
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(canvas);
    return () => observer.disconnect();
    // repaint reads refs only; the observer is set up once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Undo and Clear change `strokes` from outside the pointer handlers.
  useEffect(() => {
    repaint();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [strokes]);

  function pointAt(e: { clientX: number; clientY: number }): Point {
    const rect = canvasRef.current!.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }

  function handleDown(e: React.PointerEvent<HTMLCanvasElement>) {
    if (disabled || active.current) return;
    if (e.pointerType === "mouse" && e.button !== 0) return;
    e.preventDefault();
    e.currentTarget.setPointerCapture(e.pointerId);
    active.current = { pointerId: e.pointerId, stroke: [pointAt(e)] };
    const ctx = context();
    if (ctx) drawStrokeTail(ctx, active.current.stroke);
  }

  function handleMove(e: React.PointerEvent<HTMLCanvasElement>) {
    const current = active.current;
    if (!current || current.pointerId !== e.pointerId) return;
    e.preventDefault();
    const ctx = context();
    // Coalesced events are the samples the browser merged into this one — on
    // a fast stroke they're the difference between a curve and a polygon.
    const samples = e.nativeEvent.getCoalescedEvents?.() ?? [];
    for (const sample of samples.length > 0 ? samples : [e.nativeEvent]) {
      const p = pointAt(sample);
      const last = current.stroke[current.stroke.length - 1];
      if (Math.abs(p.x - last.x) < 0.5 && Math.abs(p.y - last.y) < 0.5) continue;
      current.stroke.push(p);
      if (ctx) drawStrokeTail(ctx, current.stroke);
    }
  }

  function handleUp(e: React.PointerEvent<HTMLCanvasElement>) {
    const current = active.current;
    if (!current || current.pointerId !== e.pointerId) return;
    active.current = null;
    const ctx = context();
    if (ctx) drawStrokeEnd(ctx, current.stroke);
    onChange([...strokes, current.stroke]);
  }

  const empty = strokes.length === 0;

  return (
    <div className="space-y-2">
      <div className="relative overflow-hidden rounded-2xl border border-border bg-white">
        <canvas
          ref={canvasRef}
          onPointerDown={handleDown}
          onPointerMove={handleMove}
          onPointerUp={handleUp}
          onPointerCancel={handleUp}
          aria-label="Signature pad — draw your signature here"
          role="img"
          className={`block h-44 w-full touch-none select-none ${disabled ? "cursor-not-allowed" : "cursor-crosshair"}`}
        />
        {/* A guide line to sign on. Outside the canvas, so it's never exported.
            Greys of the ink rather than theme tokens: the pad is white paper in
            both themes, and a dark-mode token would vanish on it. */}
        <div className="pointer-events-none absolute inset-x-6 bottom-10 border-b border-dashed border-black/25" />
        {empty ? (
          <p className="pointer-events-none absolute inset-x-0 bottom-3 text-center text-xs text-black/50">
            Sign above the line
          </p>
        ) : null}
      </div>
      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={() => onChange(strokes.slice(0, -1))}
          disabled={disabled || empty}
          className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-semibold text-foreground transition hover:bg-muted/60 disabled:opacity-50"
        >
          <Undo2 className="h-3.5 w-3.5" /> Undo
        </button>
        <button
          type="button"
          onClick={() => onChange([])}
          disabled={disabled || empty}
          className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-semibold text-foreground transition hover:bg-muted/60 disabled:opacity-50"
        >
          <Eraser className="h-3.5 w-3.5" /> Clear
        </button>
      </div>
    </div>
  );
}

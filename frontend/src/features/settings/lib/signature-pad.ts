// Drawing and exporting for the signature pad (components/SignaturePad.tsx).
//
// A signature is kept as strokes — lists of points in the pad's CSS pixels —
// rather than as pixels, so Undo is just "drop the last stroke", a resized pad
// can redraw itself, and the export can be cropped to exactly what was drawn
// and rendered at a resolution of its own choosing.

export type Point = { x: number; y: number };
export type Stroke = Point[];

// The ink is IMAGE content, not UI: it's printed on white paper, so it is a
// fixed near-black in both themes rather than a theme token.
export const INK_COLOR = "#1c1917";
export const INK_WIDTH = 2.5;

/** Exported at this multiple of the drawn size, so the letter's PDF prints it crisp. */
const EXPORT_SCALE = 3;
/** The server refuses images over 4000 px on either side; the export never makes one. */
const EXPORT_MAX_SIDE = 4000;
/** Clear space kept around the drawn bounds in the export, in CSS px. */
const EXPORT_PADDING = 8;

const mid = (a: Point, b: Point): Point => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });

function setInk(ctx: CanvasRenderingContext2D) {
  ctx.strokeStyle = INK_COLOR;
  ctx.fillStyle = INK_COLOR;
  ctx.lineWidth = INK_WIDTH;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
}

function dot(ctx: CanvasRenderingContext2D, p: Point) {
  ctx.beginPath();
  ctx.arc(p.x, p.y, INK_WIDTH / 2, 0, Math.PI * 2);
  ctx.fill();
}

// Smooths through the points: each point is a quadratic curve's control point
// and the curve runs between the midpoints either side of it, so the line
// bends where the hand did instead of joining the samples with corners.
export function drawStroke(ctx: CanvasRenderingContext2D, stroke: Stroke) {
  if (stroke.length === 0) return;
  setInk(ctx);
  if (stroke.length === 1) {
    dot(ctx, stroke[0]);
    return;
  }
  ctx.beginPath();
  ctx.moveTo(stroke[0].x, stroke[0].y);
  const first = mid(stroke[0], stroke[1]);
  ctx.lineTo(first.x, first.y);
  for (let i = 1; i < stroke.length - 1; i++) {
    const end = mid(stroke[i], stroke[i + 1]);
    ctx.quadraticCurveTo(stroke[i].x, stroke[i].y, end.x, end.y);
  }
  const last = stroke[stroke.length - 1];
  ctx.lineTo(last.x, last.y);
  ctx.stroke();
}

/**
 * Draws just the newest piece of a stroke that is still being drawn — the same
 * curve drawStroke would draw for it, without repainting the whole stroke on
 * every pointer move. Call after pushing the new point.
 */
export function drawStrokeTail(ctx: CanvasRenderingContext2D, stroke: Stroke) {
  const n = stroke.length;
  if (n === 0) return;
  setInk(ctx);
  if (n === 1) {
    dot(ctx, stroke[0]);
    return;
  }
  ctx.beginPath();
  if (n === 2) {
    ctx.moveTo(stroke[0].x, stroke[0].y);
    const end = mid(stroke[0], stroke[1]);
    ctx.lineTo(end.x, end.y);
  } else {
    const start = mid(stroke[n - 3], stroke[n - 2]);
    const end = mid(stroke[n - 2], stroke[n - 1]);
    ctx.moveTo(start.x, start.y);
    ctx.quadraticCurveTo(stroke[n - 2].x, stroke[n - 2].y, end.x, end.y);
  }
  ctx.stroke();
}

/** Finishes a stroke drawn with drawStrokeTail: the last half-segment to its end. */
export function drawStrokeEnd(ctx: CanvasRenderingContext2D, stroke: Stroke) {
  const n = stroke.length;
  if (n < 2) return;
  setInk(ctx);
  const start = mid(stroke[n - 2], stroke[n - 1]);
  ctx.beginPath();
  ctx.moveTo(start.x, start.y);
  ctx.lineTo(stroke[n - 1].x, stroke[n - 1].y);
  ctx.stroke();
}

/** Repaints every stroke on a cleared canvas whose transform is already set. */
export function redraw(ctx: CanvasRenderingContext2D, strokes: Stroke[], width: number, height: number) {
  ctx.clearRect(0, 0, width, height);
  for (const s of strokes) drawStroke(ctx, s);
}

/**
 * The signature as a transparent PNG cropped to what was drawn (plus a little
 * clear space), or null when nothing was. The crop comes from the points, so
 * an empty corner of a wide pad doesn't end up as empty space on the letter.
 */
export function strokesToPng(strokes: Stroke[]): Promise<Blob | null> {
  const points = strokes.flat();
  if (points.length === 0) return Promise.resolve(null);

  // A loop, not Math.min(...points): a long signature is thousands of points.
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const p of points) {
    minX = Math.min(minX, p.x);
    minY = Math.min(minY, p.y);
    maxX = Math.max(maxX, p.x);
    maxY = Math.max(maxY, p.y);
  }
  const reach = INK_WIDTH / 2 + EXPORT_PADDING;
  minX -= reach;
  minY -= reach;
  maxX += reach;
  maxY += reach;

  // Scaled down when needed so neither side passes the server's pixel limit.
  const width = maxX - minX;
  const height = maxY - minY;
  const scale = Math.min(EXPORT_SCALE, EXPORT_MAX_SIDE / width, EXPORT_MAX_SIDE / height);

  const canvas = document.createElement("canvas");
  canvas.width = Math.min(EXPORT_MAX_SIDE, Math.max(1, Math.ceil(width * scale)));
  canvas.height = Math.min(EXPORT_MAX_SIDE, Math.max(1, Math.ceil(height * scale)));
  const ctx = canvas.getContext("2d");
  if (!ctx) return Promise.resolve(null);

  ctx.setTransform(scale, 0, 0, scale, -minX * scale, -minY * scale);
  for (const s of strokes) drawStroke(ctx, s);

  return new Promise((resolve) => canvas.toBlob((blob) => resolve(blob), "image/png"));
}

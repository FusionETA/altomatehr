// Stop the mouse wheel from editing number inputs.
//
// A focused <input type="number"> treats a wheel tick as a spin, so scrolling
// the page with the pointer over one silently changes the value — on this app
// that means a salary, an EPF rate or a TP3 figure edited by accident, with a
// dirty form as the only hint. Nobody adjusts a number by scrolling on
// purpose; everybody scrolls past one.
//
// Blurring rather than preventDefault: the spin needs focus, so dropping focus
// cancels the edit while the page keeps scrolling normally. Cancelling the
// event instead would trap the scroll over every number field.
//
// Registered once, on the document, in capture phase — 22 number inputs across
// ten screens today, and the next one is covered without anyone remembering to
// opt in.
function cancelWheelSpin(event: WheelEvent) {
  const active = document.activeElement;
  if (
    active instanceof HTMLInputElement &&
    active.type === "number" &&
    (event.target === active || active.contains(event.target as Node))
  ) {
    active.blur();
  }
}

export function installNumberInputGuard() {
  document.addEventListener("wheel", cancelWheelSpin, { capture: true, passive: true });
}

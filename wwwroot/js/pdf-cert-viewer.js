// pdf-cert-viewer.js
// Thin wrapper around PDF.js for the cert review panel. Renders one page at
// a time to a canvas and positions an absolutely-placed highlight div over
// it based on normalized (0-1) bounding box fractions returned by the
// extraction model.
//
// Requires pdf.js to be loaded globally (pdfjsLib) — load it via a <script>
// tag pointing at the pdf.js distribution, e.g. from a CDN or wwwroot/lib.

const viewers = {};

export async function initViewer(containerId, pdfUrl) {
    const pdf = await pdfjsLib.getDocument(pdfUrl).promise;
    viewers[containerId] = {
        pdf,
        currentPage: 1,
        // cache the rendered viewport size so highlightRegion can convert
        // fractional bbox coords into actual pixel offsets.
        viewportWidth: 0,
        viewportHeight: 0,
    };
    await renderPage(containerId, 1);
}

export async function renderPage(containerId, pageNumber) {
    const state = viewers[containerId];
    if (!state) return;

    const page = await state.pdf.getPage(pageNumber);
    const canvas = document.getElementById(`pdf-canvas-${containerId}`);
    const context = canvas.getContext("2d");

    // Scale to fit the wrapper's current width — keeps the viewer responsive
    // without needing a fixed page size baked into the component.
    const wrapper = document.getElementById(`pdf-canvas-wrapper-${containerId}`);
    const targetWidth = wrapper?.clientWidth || 800;
    const unscaledViewport = page.getViewport({ scale: 1 });
    const scale = targetWidth / unscaledViewport.width;
    const viewport = page.getViewport({ scale });

    canvas.width = viewport.width;
    canvas.height = viewport.height;

    state.currentPage = pageNumber;
    state.viewportWidth = viewport.width;
    state.viewportHeight = viewport.height;

    await page.render({ canvasContext: context, viewport }).promise;

    // Page changed — any highlight drawn for the previous page is now
    // meaningless, so hide it until the caller explicitly highlights again.
    clearHighlight(containerId);
}

export function highlightRegion(containerId, x, y, w, h) {
    const state = viewers[containerId];
    const highlightEl = document.getElementById(`pdf-highlight-${containerId}`);
    if (!state || !highlightEl) return;

    // Convert normalized fractions to actual pixel position/size based on
    // the canvas's current rendered dimensions. This is what makes the
    // highlight resolution-independent — it doesn't matter what DPI the
    // page image was rasterized at for the LLM call, only the current
    // on-screen canvas size.
    const left = x * state.viewportWidth;
    const top = y * state.viewportHeight;
    const width = w * state.viewportWidth;
    const height = h * state.viewportHeight;

    // Pad slightly — model-reported boxes are "region-level" accurate per
    // the extraction prompt, not pixel-perfect, so a small margin makes the
    // highlight visually forgiving rather than looking like a near-miss.
    const padPx = 4;

    highlightEl.style.left = `${Math.max(0, left - padPx)}px`;
    highlightEl.style.top = `${Math.max(0, top - padPx)}px`;
    highlightEl.style.width = `${width + padPx * 2}px`;
    highlightEl.style.height = `${height + padPx * 2}px`;
    highlightEl.style.display = "block";
}

export function clearHighlight(containerId) {
    const highlightEl = document.getElementById(`pdf-highlight-${containerId}`);
    if (highlightEl) highlightEl.style.display = "none";
}

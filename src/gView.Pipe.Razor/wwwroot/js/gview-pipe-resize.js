// Drags the splitters of the editor in the browser, so the side bars follow the pointer without
// a round trip per move. .NET hears about the new width once, when the pointer is released.

const minWidths = { palette: 160, panel: 240 };
const maxShare = 0.6;

export function init(body, dotNet) {
    const onPointerDown = e => {
        const splitter = e.target.closest('[data-gview-pipe-splitter]');
        if (!splitter || e.button !== 0 || !body.contains(splitter)) {
            return;
        }
        e.preventDefault();

        const side = splitter.dataset.gviewPipeSplitter;
        const variable = `--gview-pipe-${side}-width`;
        const startX = e.clientX;
        const startWidth = parseFloat(getComputedStyle(body).getPropertyValue(variable)) || 0;
        let width = startWidth;
        let frame = 0;

        splitter.setPointerCapture(e.pointerId);
        body.classList.add('gview-pipe-resizing');

        const onMove = m => {
            // the palette grows to the right, the panel to the left
            const delta = m.clientX - startX;
            width = clamp(side, startWidth + (side === 'palette' ? delta : -delta));
            if (!frame) {
                frame = requestAnimationFrame(() => {
                    frame = 0;
                    body.style.setProperty(variable, `${width}px`);
                });
            }
        };
        const onEnd = () => {
            splitter.removeEventListener('pointermove', onMove);
            splitter.removeEventListener('pointerup', onEnd);
            splitter.removeEventListener('lostpointercapture', onEnd);
            cancelAnimationFrame(frame);
            body.style.setProperty(variable, `${width}px`);
            body.classList.remove('gview-pipe-resizing');
            if (width !== startWidth) {
                dotNet.invokeMethodAsync('WidthChanged', side, width);
            }
        };

        splitter.addEventListener('pointermove', onMove);
        splitter.addEventListener('pointerup', onEnd);
        splitter.addEventListener('lostpointercapture', onEnd);
    };

    body.addEventListener('pointerdown', onPointerDown);
    return { dispose: () => body.removeEventListener('pointerdown', onPointerDown) };
}

export function clamp(side, width) {
    const max = Math.max(minWidths[side], window.innerWidth * maxShare);
    return Math.round(Math.min(Math.max(width, minWidths[side]), max));
}

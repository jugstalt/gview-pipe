// Browser side of the workflow editor:
// - drags the splitters, so the side bars follow the pointer without a round trip per move;
//   .NET hears about the new width once, when the pointer is released
// - catches Ctrl+S before the browser opens its save dialog
// - marks {{ }} expressions in expression fields: a mirror of the text lies behind the input and
//   tints the expressions, the input itself stays a normal field

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

    const onKeyDown = e => {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 's') {
            e.preventDefault();
            // leaving the field passes on a value still waiting for its debounce, before the save
            const active = document.activeElement;
            active?.blur?.();
            dotNet.invokeMethodAsync('SaveShortcut').finally(() => active?.focus?.());
        }
    };

    const stopHighlighting = highlightExpressions(body);

    body.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return {
        dispose: () => {
            body.removeEventListener('pointerdown', onPointerDown);
            document.removeEventListener('keydown', onKeyDown);
            stopHighlighting();
        }
    };
}

export function clamp(side, width) {
    const max = Math.max(minWidths[side], window.innerWidth * maxShare);
    return Math.round(Math.min(Math.max(width, minWidths[side]), max));
}

const expressionPattern = /\{\{[\s\S]*?\}\}/g;
const mirroredStyles = ['fontFamily', 'fontSize', 'fontWeight', 'letterSpacing', 'lineHeight', 'paddingTop', 'paddingRight',
    'paddingBottom', 'paddingLeft', 'borderTopWidth', 'borderLeftWidth', 'textIndent', 'whiteSpace', 'wordBreak', 'overflowWrap'];

function highlightExpressions(root) {
    const fields = new Map();

    const escape = text => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

    const update = (input, entry) => {
        if (!input.isConnected) {
            entry.mirror.remove();
            fields.delete(input);
            return;
        }
        if (input.value !== entry.value) {
            entry.value = input.value;
            // a trailing line break needs content, or the mirror is one line short
            entry.mirror.innerHTML = escape(input.value).replace(expressionPattern, m => `<mark>${m}</mark>`) + '​';
        }
        const box = input.getBoundingClientRect();
        const parent = input.parentElement.getBoundingClientRect();
        entry.mirror.style.left = `${box.left - parent.left}px`;
        entry.mirror.style.top = `${box.top - parent.top}px`;
        entry.mirror.style.width = `${input.clientWidth}px`;
        entry.mirror.style.height = `${input.clientHeight}px`;
        entry.mirror.scrollTop = input.scrollTop;
        entry.mirror.scrollLeft = input.scrollLeft;
    };

    const attach = () => {
        for (const input of root.querySelectorAll('.gview-pipe-expression-field input.mud-input-slot, .gview-pipe-expression-field textarea.mud-input-slot')) {
            if (fields.has(input)) {
                continue;
            }
            const mirror = document.createElement('div');
            mirror.className = 'gview-pipe-expression-mirror';
            mirror.setAttribute('aria-hidden', 'true');
            const style = getComputedStyle(input);
            for (const name of mirroredStyles) {
                mirror.style[name] = style[name];
            }
            if (input.tagName === 'INPUT') {
                mirror.style.whiteSpace = 'pre';
            }
            if (getComputedStyle(input.parentElement).position === 'static') {
                input.parentElement.style.position = 'relative';
            }
            // appended, not inserted: Blazor keeps track of its own nodes before it
            input.parentElement.appendChild(mirror);
            const entry = { mirror, value: null };
            fields.set(input, entry);
            const sync = () => update(input, entry);
            for (const type of ['input', 'scroll', 'keyup', 'select', 'focus']) {
                input.addEventListener(type, sync);
            }
            sync();
        }
    };

    // Blazor replaces fields and sets values without events: look again after DOM changes and now and then
    const observer = new MutationObserver(attach);
    observer.observe(root, { childList: true, subtree: true });
    const timer = setInterval(() => fields.forEach((entry, input) => update(input, entry)), 400);
    attach();

    return () => {
        observer.disconnect();
        clearInterval(timer);
        fields.forEach(entry => entry.mirror.remove());
        fields.clear();
    };
}

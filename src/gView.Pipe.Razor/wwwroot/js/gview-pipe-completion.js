// Autocomplete for {{ }} expressions (ADR 0019).
// .NET sends a catalog of paths for the selected node (setCompletions); filtering happens here, without a round trip
// per key. Expression fields get a popup under the field; Monaco code fields get the same paths from a completion provider.

let catalog = { items: [], hint: null };

export function setCompletions(value) {
    catalog = value ?? { items: [], hint: null };
}

/** The path being typed before the cursor, e.g. $json.cust or $node["Due Da (an open [ includes everything up to it). */
export function tokenOf(text) {
    let i = text.length;
    let inBracket = text.lastIndexOf('[') > text.lastIndexOf(']');
    while (i > 0) {
        const c = text[i - 1];
        if (inBracket) {
            if (c === '[') {
                inBracket = false;
            }
            i--;
            continue;
        }
        if (c === ']') {
            inBracket = true;
            i--;
            continue;
        }
        if (/[\w$.()]/.test(c)) {
            i--;
            continue;
        }
        break;
    }
    return text.slice(i);
}

/** The direct children of what is typed: for "$json.cu" the fields of $json starting with "cu". */
export function suggest(token, items) {
    const separator = Math.max(token.lastIndexOf('.'), lastOpenBracket(token));
    const parent = separator < 0 ? '' : token.slice(0, separator);
    const typed = token.slice(parent.length).toLowerCase();
    return items.filter(item => {
        if (!item.path.startsWith(parent) || item.path.length === parent.length) {
            return false;
        }
        const rest = item.path.slice(parent.length);
        // only one level deeper: exactly one segment (.name, [0], ["a b"]), a call like .all() counts as one
        const oneLevel = parent ? /^(\.[\w$]+|\[[^\]]*\])(\(\))?$/.test(rest) : /^[\w$]+(\(\))?$/.test(rest);
        return oneLevel && rest.toLowerCase().startsWith(typed);
    });
}

function lastOpenBracket(token) {
    // a [ that belongs to the token, not one inside quotes
    let index = -1, quoted = false;
    for (let i = 0; i < token.length; i++) {
        if (token[i] === '"') {
            quoted = !quoted;
        } else if (token[i] === '[' && !quoted) {
            index = i;
        }
    }
    return index;
}

/** The text inside the {{ }} the cursor is in, up to the cursor; null outside of an expression. */
function expressionBefore(value, cursor) {
    const before = value.slice(0, cursor);
    const open = before.lastIndexOf('{{');
    return open >= 0 && before.indexOf('}}', open) < 0 ? before.slice(open + 2) : null;
}

const fieldSelector = '.gview-pipe-expression-field input.mud-input-slot, .gview-pipe-expression-field textarea.mud-input-slot';

export function attach(root) {
    const popup = document.createElement('div');
    popup.className = 'gview-pipe-completion';
    popup.setAttribute('role', 'listbox');
    popup.hidden = true;
    document.body.appendChild(popup);

    let state = null; // { input, token, matches, index }

    const close = () => {
        popup.hidden = true;
        state = null;
    };

    const render = () => {
        popup.replaceChildren();
        state.matches.slice(0, 50).forEach((item, i) => {
            const row = document.createElement('div');
            row.className = 'gview-pipe-completion-item' + (i === state.index ? ' selected' : '');
            row.setAttribute('role', 'option');
            const label = document.createElement('span');
            label.className = 'gview-pipe-completion-label';
            label.textContent = item.path;
            const type = document.createElement('span');
            type.className = 'gview-pipe-completion-type';
            type.textContent = item.detail ? `${item.type} · ${item.detail}` : item.type;
            row.append(label, type);
            if (item.sample != null) {
                const sample = document.createElement('span');
                sample.className = 'gview-pipe-completion-sample';
                sample.textContent = item.sample;
                row.append(sample);
            }
            // mousedown, not click: the input must keep its focus
            row.addEventListener('mousedown', e => {
                e.preventDefault();
                state.index = i;
                accept();
            });
            popup.append(row);
        });
        if (catalog.hint) {
            const hint = document.createElement('div');
            hint.className = 'gview-pipe-completion-hint';
            hint.textContent = catalog.hint;
            popup.append(hint);
        }
        const box = state.input.getBoundingClientRect();
        popup.style.left = `${box.left}px`;
        popup.style.top = `${box.bottom + 2}px`;
        popup.style.minWidth = `${Math.min(box.width, 420)}px`;
        popup.hidden = false;
        popup.querySelector('.selected')?.scrollIntoView({ block: 'nearest' });
    };

    const open = (input, force) => {
        const expression = expressionBefore(input.value, input.selectionStart ?? input.value.length);
        if (expression === null) {
            close();
            return;
        }
        const token = tokenOf(expression);
        const matches = suggest(token, catalog.items);
        if (matches.length === 0 && !(force && catalog.hint)) {
            close();
            return;
        }
        state = { input, token, matches, index: 0 };
        render();
    };

    const setValue = (input, value, cursor) => {
        input.value = value;
        input.setSelectionRange(cursor, cursor);
        // Blazor binds on input events, like typed text
        input.dispatchEvent(new Event('input', { bubbles: true }));
    };

    const accept = () => {
        const item = state?.matches[state.index];
        if (!item) {
            close();
            return;
        }
        const input = state.input;
        const cursor = input.selectionStart ?? input.value.length;
        const start = cursor - state.token.length;
        setValue(input, input.value.slice(0, start) + item.path + input.value.slice(cursor), start + item.path.length);
        close();
        // objects have children: offer them right away
        if (item.type === 'object' || item.type === 'array') {
            open(input, false);
        }
    };

    const onInput = e => {
        const input = e.target;
        if (!(input instanceof HTMLElement) || !input.matches(fieldSelector)) {
            return;
        }
        // {{ gets its closing }}
        const cursor = input.selectionStart ?? 0;
        if (e.inputType === 'insertText' && e.data === '{' && input.value.slice(cursor - 2, cursor) === '{{'
            && !input.value.slice(cursor).trimStart().startsWith('}}')) {
            setValue(input, input.value.slice(0, cursor) + '  }}' + input.value.slice(cursor), cursor + 1);
        }
        if (state?.input === input || /[.[$"{ ]/.test(e.data ?? '') || /\w/.test(e.data ?? '')) {
            open(input, false);
        }
    };

    const onKeyDown = e => {
        const input = e.target;
        if (!(input instanceof HTMLElement) || !input.matches(fieldSelector)) {
            return;
        }
        if (e.key === ' ' && e.ctrlKey) {
            e.preventDefault();
            open(input, true);
            return;
        }
        if (!state || state.input !== input) {
            return;
        }
        switch (e.key) {
            case 'ArrowDown':
                state.index = (state.index + 1) % Math.max(1, Math.min(state.matches.length, 50));
                break;
            case 'ArrowUp':
                state.index = (state.index + Math.min(state.matches.length, 50) - 1) % Math.max(1, Math.min(state.matches.length, 50));
                break;
            case 'Enter':
            case 'Tab':
                if (state.matches.length === 0) {
                    close();
                    return;
                }
                accept();
                break;
            case 'Escape':
                close();
                break;
            default:
                return;
        }
        e.preventDefault();
        e.stopPropagation();
        if (state) {
            render();
        }
    };

    const onFocusOut = e => {
        if (state?.input === e.target) {
            close();
        }
    };

    root.addEventListener('input', onInput, true);
    root.addEventListener('keydown', onKeyDown, true);
    root.addEventListener('focusout', onFocusOut, true);
    const monaco = registerMonaco();

    return () => {
        root.removeEventListener('input', onInput, true);
        root.removeEventListener('keydown', onKeyDown, true);
        root.removeEventListener('focusout', onFocusOut, true);
        monaco?.dispose();
        popup.remove();
    };
}

/** The same paths in JavaScript code fields; the Code node also has items[0] for $json in "all items" mode. */
function registerMonaco() {
    const monaco = window.monaco;
    if (!monaco?.languages?.registerCompletionItemProvider) {
        return null;
    }
    return monaco.languages.registerCompletionItemProvider('javascript', {
        triggerCharacters: ['.', '$', '[', '"'],
        provideCompletionItems: (model, position) => {
            const line = model.getLineContent(position.lineNumber).slice(0, position.column - 1);
            const token = tokenOf(line);
            const items = catalog.items.concat(
                [{ path: 'items', type: 'array', detail: 'all input items' }],
                catalog.items.filter(i => i.path.startsWith('$json.') || i.path.startsWith('$json[')).map(i => ({ ...i, path: 'items[0]' + i.path.slice(5) })));
            const range = {
                startLineNumber: position.lineNumber, endLineNumber: position.lineNumber,
                startColumn: position.column - token.length, endColumn: position.column
            };
            return {
                suggestions: suggest(token, items).map(item => ({
                    label: item.path,
                    kind: item.type === 'object' || item.type === 'array' ? monaco.languages.CompletionItemKind.Module : monaco.languages.CompletionItemKind.Field,
                    detail: item.detail ? `${item.type} · ${item.detail}` : item.type,
                    documentation: item.sample ?? undefined,
                    insertText: item.path,
                    filterText: item.path,
                    range
                }))
            };
        }
    });
}

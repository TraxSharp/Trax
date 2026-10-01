window.traxDashboard = {
    copyToClipboard: async (text) => {
        await navigator.clipboard.writeText(text);
    },

    // A LargeTextArea registers itself here after its first render. From then on a change to it
    // reaches the server as a sequence of small interop calls instead of one change event, whose
    // whole value would otherwise have to fit in a single hub message (32 KB by default, and a
    // larger message closes the circuit).
    registerLargeText: (element, dotNetRef, chunkLength) => {
        if (element) {
            element.__traxLargeText = { dotNetRef, chunkLength, sequence: 0 };
        }
    }
};

// Blazor listens for change in the capture phase on the document, so a listener on the element
// would run too late to stop it. Capturing on the window runs first.
window.addEventListener('change', (event) => {
    const element = event.target;
    const registration = element && element.__traxLargeText;
    if (!registration) {
        return;
    }

    event.stopPropagation();

    const text = element.value;
    const id = ++registration.sequence;
    const { dotNetRef, chunkLength } = registration;

    // Sent back to back, without waiting, so they reach the server, in order, before any click
    // that follows the change.
    dotNetRef.invokeMethodAsync('BeginText', id, text.length);
    for (let start = 0; start < text.length;) {
        let end = Math.min(start + chunkLength, text.length);
        // Never split a surrogate pair across two chunks.
        if (end < text.length) {
            const code = text.charCodeAt(end - 1);
            if (code >= 0xd800 && code <= 0xdbff) {
                end -= 1;
            }
        }
        dotNetRef.invokeMethodAsync('AppendText', id, text.slice(start, end));
        start = end;
    }
    dotNetRef.invokeMethodAsync('CommitText', id);
}, true);

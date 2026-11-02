// Lets the browser save text built on the server as a file (e.g. the CSV export of the audit log).

export function save(fileName, text, mimeType) {
    const url = URL.createObjectURL(new Blob([text], { type: mimeType || 'text/plain' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

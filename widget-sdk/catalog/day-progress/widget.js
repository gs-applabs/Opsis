const output = document.querySelector('#elapsed');
const progress = document.querySelector('#progress');
let language = navigator.language;
function render() {
    const now = new Date();
    const y = now.getFullYear(), m = now.getMonth(), d = now.getDate();
    const start = new Date(y, m, d), end = new Date(y, m, d + 1);
    progress.value = (now - start) / (end - start);
    output.value = new Intl.NumberFormat(language, { style: 'percent', maximumFractionDigits: 1 }).format(progress.value);
}
window.chrome?.webview?.addEventListener('message', ({ data }) => {
    if (data.type !== 'opsis:state' || data.api !== 1) return;
    language = data.language;
    document.documentElement.dataset.theme = data.dark ? 'dark' : 'light';
    document.documentElement.style.setProperty('--accent', data.accent);
    render();
});
window.chrome?.webview?.postMessage({ type: 'opsis:ready' });
render();
setInterval(render, 15000);

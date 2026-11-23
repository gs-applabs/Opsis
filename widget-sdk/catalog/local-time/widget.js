const time = document.querySelector('#time');
const date = document.querySelector('#date');
let language = navigator.language;
let refreshPercent = 100;
let refreshTimer;
function updateRefresh(percent) {
    refreshPercent = Math.max(25, Math.min(400, Number(percent) || 100));
    clearInterval(refreshTimer);
    refreshTimer = setInterval(render, Math.max(250, Math.round(1000 * refreshPercent / 100)));
}
function render() {
    const now = new Date();
    time.value = now.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    date.textContent = now.toLocaleDateString(language, { weekday: 'long', month: 'long', day: 'numeric' });
}
window.chrome?.webview?.addEventListener('message', ({ data }) => {
    if (data.type === 'opsis:refresh') { updateRefresh(data.refreshPercent); render(); return; }
    if (data.type !== 'opsis:state' || data.api !== 1) return;
    updateRefresh(data.refreshPercent);
    language = data.language;
    document.documentElement.dataset.theme = data.dark ? 'dark' : 'light';
    render();
});
window.chrome?.webview?.postMessage({ type: 'opsis:ready' });
render();
updateRefresh(100);

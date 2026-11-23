const time = document.querySelector('#time');
const date = document.querySelector('#date');
let language = navigator.language;
function render() {
    const now = new Date();
    time.value = now.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    date.textContent = now.toLocaleDateString(language, { weekday: 'long', month: 'long', day: 'numeric' });
}
window.chrome?.webview?.addEventListener('message', ({ data }) => {
    if (data.type !== 'opsis:state' || data.api !== 1) return;
    language = data.language;
    document.documentElement.dataset.theme = data.dark ? 'dark' : 'light';
    render();
});
window.chrome?.webview?.postMessage({ type: 'opsis:ready' });
render();
setInterval(render, 1000);

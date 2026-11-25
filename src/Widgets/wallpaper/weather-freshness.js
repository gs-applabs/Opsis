// Pure formatting logic shared by the wallpaper overlay and headless regression tests.
// The host supplies timestamps/source: the browser does not send extra network requests.
'use strict';
(function (root) {
    function format(info, nowMs, language) {
        if (!info || !info.source) return '';
        const el = String(language).startsWith('el');
        const source = String(info.source);
        const date = Date.parse(info.observedAt || info.fetchedAt || '');
        if (!Number.isFinite(date)) return (el ? 'Πηγή: ' : 'Source: ') + source;
        const minutes = Math.max(0, Math.floor((nowMs - date) / 60000));
        const age = minutes < 60
            ? (el ? `${minutes} λ. πριν` : `${minutes} min ago`)
            : (el ? `${Math.floor(minutes / 60)} ώ. πριν` : `${Math.floor(minutes / 60)} h ago`);
        const stale = info.stale || minutes >= 30;
        return source + ' · ' + age + (stale ? (el ? ' · Παλαιά δεδομένα' : ' · Outdated data') : '');
    }
    root.OpsisWeatherFreshness = { format };
})(window);

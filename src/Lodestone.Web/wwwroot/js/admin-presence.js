(function () {
    "use strict";

    // Keeps the Online status column live on the admin people pages. The server already sorts online
    // members first, so refreshing the table also re-orders it as people come and go.
    var refreshMs = 15000;
    var busy = false;

    function container() {
        return document.querySelector("[data-presence-live]");
    }

    // Do not swap the table out from under an administrator who is using a control inside it.
    function isInteracting(host) {
        var active = document.activeElement;
        return (active && active !== document.body && host.contains(active)) ||
            host.querySelector("details[open], dialog[open]") !== null;
    }

    function refresh() {
        var host = container();
        if (!host || busy || document.hidden || isInteracting(host)) return;
        busy = true;
        fetch(window.location.href, { credentials: "same-origin", headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (response) {
                if (!response.ok) throw new Error("Refresh failed");
                return response.text();
            })
            .then(function (html) {
                var fresh = new DOMParser().parseFromString(html, "text/html").querySelector("[data-presence-live]");
                var current = container();
                if (fresh && current && !isInteracting(current)) current.innerHTML = fresh.innerHTML;
            })
            .catch(function () { /* keep the current table; the next tick retries */ })
            .then(function () { busy = false; });
    }

    if (!container()) return;
    setInterval(refresh, refreshMs);
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) refresh();
    });
})();

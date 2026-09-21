(function () {
    "use strict";

    // Keeps an open page listed as online for administrators. The layouts include this script only
    // for signed-in users.
    var intervalMs = 30000;

    function ping() {
        fetch("/Presence/Ping", { method: "POST", credentials: "same-origin", keepalive: true })
            .catch(function () { /* offline or signed out: the next tick simply retries */ });
    }

    setInterval(ping, intervalMs);
})();

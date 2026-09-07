(function () {
    "use strict";

    var root = document.querySelector("[data-peer-support-live]");
    if (!root) return;

    var liveRegion = document.querySelector("[data-peer-support-status]");
    var reloadTimer = 0;

    function announce(message) {
        if (liveRegion) liveRegion.textContent = message;
    }

    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        // The page is still correct as rendered; it just will not update on its own.
        return;
    }

    var connection = new window.signalR.HubConnectionBuilder()
        .withUrl("/hubs/peer-support")
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .build();

    connection.on("PeerSupportChanged", function () {
        if (reloadTimer) return;

        // Never interrupt someone mid-sentence: if a message is part-written, tell them instead of
        // reloading it away. The next signal after they submit will refresh the page.
        var draft = root.querySelector("textarea");
        if (draft && draft.value.trim().length > 0) {
            announce("This conversation was updated. Send or clear your message to see the latest.");
            return;
        }

        announce("This page was updated. Refreshing now.");
        reloadTimer = window.setTimeout(function () {
            window.location.reload();
        }, 600);
    });

    // A reconnect may have spanned missed signals, so refresh rather than trusting what is shown.
    connection.onreconnected(function () {
        if (reloadTimer) return;
        var draft = root.querySelector("textarea");
        if (draft && draft.value.trim().length > 0) return;
        window.location.reload();
    });

    connection.start().catch(function () {
        // Live updates unavailable; the page stands as rendered.
    });
})();

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

    // A page with its own open conversation already carries a live hub connection of its own
    // (peer-chat.js), and that connection matters far more than this one: it's the actual messaging
    // feature, this is a "the page might be stale, reload it" convenience. Opening a second
    // simultaneous live connection here competed with peer-chat.js's for the one connection some
    // hosting setups (a free tunnel among them) can reliably keep open at once — on those, the
    // second connection could sit negotiated but never actually connect, at which point THIS script
    // failing silently was fine, but it was starving the chat connection of the slot instead. So on
    // these pages, peer-chat.js alone carries live updates; the actions that change this page's
    // status (accept, decline, complete, escalate) are ordinary form posts that already redirect
    // back to a fresh copy of the page on the actor's own screen. What's lost is near-real-time
    // notice to the *other* participant that one of those happened while they have the page open;
    // they still see it next time they load or refresh the page.
    if (root.querySelector("[data-peer-chat]")) return;

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

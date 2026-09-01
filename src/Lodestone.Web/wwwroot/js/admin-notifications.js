(function () {
    "use strict";

    var trigger = document.querySelector("[data-admin-notifications]");
    if (!trigger) return;

    var countUrl = trigger.getAttribute("data-notification-count-url");
    if (!countUrl) return;

    var badge = trigger.querySelector("[data-notification-badge]");
    var liveRegion = document.querySelector("[data-admin-notifications-live]");
    var pending = false;

    function label(count) {
        return "Notifications, " + count + (count === 1 ? " unread" : " unread");
    }

    function render(count) {
        trigger.setAttribute("aria-label", label(count));

        if (count > 0) {
            if (!badge) {
                badge = document.createElement("span");
                badge.className = "ls-admin-icon-button__badge";
                badge.setAttribute("aria-hidden", "true");
                badge.setAttribute("data-notification-badge", "");
                trigger.appendChild(badge);
            }
            badge.textContent = count > 99 ? "99+" : String(count);
            badge.hidden = false;
        } else if (badge) {
            badge.hidden = true;
        }
    }

    function announce(count) {
        if (!liveRegion) return;
        liveRegion.textContent = count > 0
            ? "You have " + count + (count === 1 ? " unread notification." : " unread notifications.")
            : "No unread notifications.";
    }

    function refresh() {
        if (pending) return;
        pending = true;

        window.fetch(countUrl, {
            headers: { "Accept": "application/json" },
            credentials: "same-origin",
            cache: "no-store"
        })
            .then(function (response) {
                if (!response.ok) throw new Error("Unexpected status " + response.status);
                return response.json();
            })
            .then(function (payload) {
                var count = payload && typeof payload.unread === "number" ? payload.unread : 0;
                render(count);
                announce(count);
            })
            .catch(function () {
                // Leave the server-rendered count in place; it is still the last known good value.
            })
            .finally(function () {
                pending = false;
            });
    }

    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        // Server-rendered badge remains correct as of page load; nothing further to do.
        return;
    }

    var connection = new window.signalR.HubConnectionBuilder()
        .withUrl("/hubs/admin-notifications")
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .build();

    connection.on("NotificationsChanged", refresh);

    // A reconnect may have spanned missed signals, so re-read rather than trusting the last count.
    connection.onreconnected(refresh);

    connection.start().catch(function () {
        // Live updates unavailable; the page-load count stands.
    });
})();

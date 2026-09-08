(function () {
    "use strict";

    var trigger = document.querySelector("[data-admin-notifications]");
    var countUrl = trigger ? trigger.getAttribute("data-notification-count-url") : null;

    // The volunteer roster listener shares this page's single admin connection rather than opening
    // a second one to the same hub.
    var roster = document.querySelector("[data-volunteer-roster-live]");
    if (!countUrl && !roster) return;

    var badge = trigger ? trigger.querySelector("[data-notification-badge]") : null;
    var liveRegion = document.querySelector("[data-admin-notifications-live]");
    var pending = false;

    function label(count) {
        return "Notifications, " + count + (count === 1 ? " unread" : " unread");
    }

    function render(count) {
        if (!trigger) return;
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
        if (!countUrl || pending) return;
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

    if (roster) {
        var rosterStatus = document.querySelector("[data-volunteer-roster-status]");
        var rosterTimer = 0;

        // The roster is server-rendered, so picking up a change means reloading the page. Doing
        // that under an administrator who is mid-action would silently discard their work: a typed
        // search, or a replacement volunteer chosen but not yet submitted. In those cases say so
        // and leave the page alone -- their next submit reloads it anyway.
        function adminIsMidAction() {
            var active = document.activeElement;
            if (active && roster.contains(active) && active.closest("form")) return true;

            var choices = roster.querySelectorAll("select");
            for (var i = 0; i < choices.length; i++) {
                if (choices[i].value) return true;
            }
            return false;
        }

        function reloadRoster() {
            if (rosterTimer) return;

            if (adminIsMidAction()) {
                if (rosterStatus) {
                    rosterStatus.textContent =
                        "The volunteer list changed. Finish or clear what you are doing to see the latest.";
                }
                return;
            }

            if (rosterStatus) rosterStatus.textContent = "The volunteer list changed. Refreshing now.";
            rosterTimer = window.setTimeout(function () {
                window.location.reload();
            }, 600);
        }

        connection.on("VolunteerRosterChanged", reloadRoster);
        connection.onreconnected(reloadRoster);
    }

    connection.start().catch(function () {
        // Live updates unavailable; the page-load count stands.
    });
})();

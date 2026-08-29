(function () {
    "use strict";

    var root = document.querySelector("[data-counselor-queue]");
    if (!root) return;

    var liveRegion = root.querySelector("[data-queue-live]");
    var connectionLabel = root.querySelector("[data-queue-connection-label]");
    var connectionDot = root.querySelector("[data-queue-connection-dot]");
    var reloadTimer = 0;

    function announce(message) {
        if (liveRegion) liveRegion.textContent = message;
    }

    function setConnectionState(label, state) {
        if (connectionLabel) connectionLabel.textContent = label;
        if (connectionDot) {
            connectionDot.classList.remove("is-connected", "is-connecting", "is-offline");
            connectionDot.classList.add(state);
        }
    }

    root.querySelectorAll("[data-local-datetime]").forEach(function (element) {
        var value = element.getAttribute("datetime");
        var date = value ? new Date(value) : null;
        if (date && !Number.isNaN(date.getTime())) {
            var prefix = element.textContent.trim().toLowerCase().indexOf("updated") === 0 ? "Updated " : "";
            element.textContent = prefix + new Intl.DateTimeFormat(undefined, {
                dateStyle: prefix ? undefined : "medium",
                timeStyle: "short"
            }).format(date);
        }
    });

    root.addEventListener("submit", function (event) {
        var form = event.target.closest("[data-resolve-form]");
        if (!form) return;

        var message = form.getAttribute("data-queue-confirm");
        if (message && !window.confirm(message)) {
            event.preventDefault();
            return;
        }

        var button = form.querySelector("[data-resolve-button]");
        if (button) {
            button.disabled = true;
            button.textContent = "Resolving...";
        }
        announce("Resolving support case.");
    });

    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        setConnectionState("Live updates unavailable", "is-offline");
        announce("Live updates are unavailable. Refresh the page to check for changes.");
        return;
    }

    var connection = new window.signalR.HubConnectionBuilder()
        .withUrl("/hubs/counselor-queue")
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .build();

    connection.on("QueueUpdated", function () {
        if (reloadTimer) return;
        announce("The support queue changed. Refreshing now.");
        reloadTimer = window.setTimeout(function () {
            window.location.reload();
        }, 700);
    });

    connection.onreconnecting(function () {
        setConnectionState("Reconnecting live updates", "is-connecting");
        announce("Live queue updates are reconnecting.");
    });

    connection.onreconnected(function () {
        setConnectionState("Live updates on", "is-connected");
        announce("Live queue updates reconnected.");
    });

    connection.onclose(function () {
        setConnectionState("Live updates unavailable", "is-offline");
        announce("Live updates are unavailable. Refresh the page to check for changes.");
    });

    connection.start()
        .then(function () {
            setConnectionState("Live updates on", "is-connected");
        })
        .catch(function () {
            setConnectionState("Live updates unavailable", "is-offline");
            announce("Live updates are unavailable. Refresh the page to check for changes.");
        });
})();

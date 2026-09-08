(function () {
    "use strict";

    // Live conversation inside one support request. The page is complete without this script: the
    // reply form posts normally, and the server re-renders the conversation. With it, messages
    // arrive in place and the form sends over the hub instead of reloading.
    var root = document.querySelector("[data-peer-chat]");
    if (!root) return;

    var requestId = parseInt(root.getAttribute("data-request-id"), 10);
    var selfUserId = root.getAttribute("data-user-id") || "";
    var selfLabel = root.getAttribute("data-self-label") || "You";
    var otherLabel = root.getAttribute("data-other-label") || "Them";
    var list = root.querySelector("[data-peer-chat-messages]");
    var empty = root.querySelector("[data-peer-chat-empty]");
    var form = root.querySelector("[data-peer-chat-form]");
    var status = root.querySelector("[data-peer-chat-status]");
    var textarea = form ? form.querySelector("textarea") : null;
    var submit = form ? form.querySelector("button[type=submit]") : null;

    if (!requestId || !list || !window.signalR || !window.signalR.HubConnectionBuilder) return;

    var joined = false;
    var rendered = {};
    list.querySelectorAll("[data-message-id]").forEach(function (node) {
        rendered[node.getAttribute("data-message-id")] = true;
    });

    function announce(text) {
        if (status) status.textContent = text;
    }

    function formatTime(iso) {
        var date = new Date(iso);
        return isNaN(date.getTime()) ? "" : date.toLocaleString(undefined, {
            month: "short", day: "numeric", year: "numeric", hour: "numeric", minute: "2-digit"
        });
    }

    function render(message) {
        if (!message || rendered[message.id]) return;
        rendered[message.id] = true;

        var wrapper = document.createElement("div");
        wrapper.className = "mb-3 p-3 bg-light rounded";
        wrapper.setAttribute("data-message-id", String(message.id));

        var header = document.createElement("div");
        header.className = "d-flex justify-content-between mb-2";

        var who = document.createElement("strong");
        who.textContent = message.senderUserId === selfUserId ? selfLabel : otherLabel;

        var when = document.createElement("small");
        when.className = "text-muted";
        when.textContent = formatTime(message.createdAtUtc);

        header.appendChild(who);
        header.appendChild(when);

        var body = document.createElement("p");
        body.className = "mb-0";
        body.textContent = message.message;

        wrapper.appendChild(header);
        wrapper.appendChild(body);
        list.appendChild(wrapper);

        if (empty) empty.hidden = true;
        wrapper.scrollIntoView({ block: "nearest" });
    }

    var connection = new window.signalR.HubConnectionBuilder()
        .withUrl("/hubs/peer-chat")
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .build();

    connection.on("ReceiveMessage", render);

    function join() {
        return connection.invoke("JoinRequest", requestId).then(function (room) {
            joined = true;
            if (form && !room.canSend) {
                if (textarea) textarea.disabled = true;
                if (submit) submit.disabled = true;
            }
        });
    }

    connection.onreconnected(function () {
        joined = false;
        join().catch(function () { announce("Live updates are unavailable. Reload to see new messages."); });
    });

    connection.onclose(function () { joined = false; });

    if (form && textarea) {
        form.addEventListener("submit", function (event) {
            if (!joined) return; // Let the browser post the form the ordinary way.
            var text = textarea.value.trim();
            if (!text) { event.preventDefault(); return; }

            event.preventDefault();
            if (submit) submit.disabled = true;
            connection.invoke("SendMessage", requestId, text)
                .then(function () {
                    textarea.value = "";
                    announce("Message sent.");
                })
                .catch(function (error) {
                    announce((error && error.message) || "Your message could not be sent.");
                })
                .then(function () {
                    if (submit) submit.disabled = false;
                });
        });
    }

    connection.start()
        .then(join)
        .catch(function () {
            // Live chat unavailable; the form still posts and the page still renders history.
        });
})();

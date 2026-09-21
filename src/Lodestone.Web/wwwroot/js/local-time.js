(function () {
    "use strict";

    // The server stores and renders every timestamp in UTC. This script shows each visitor those
    // times in the time zone their own browser reports, on every page that includes it. It handles:
    //   1. <time datetime="..."> elements (the datetime attribute is always UTC), and
    //   2. plain-text timestamps the server labels "UTC", for example "21 Sep 2026, 02:21 UTC".
    // The detected zone is also saved in the ls_tz cookie (see below) so the server can use it.

    var zone;
    try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch (e) { zone = undefined; }
    if (!zone) return;

    // Tell the server which zone this browser is in, so emails and drafts can use it too. The cookie
    // is only read for signed-in users and is refreshed whenever the browser's zone changes.
    try {
        var stored = (document.cookie.match(/(?:^|;\s*)ls_tz=([^;]*)/) || [])[1];
        if (!stored || decodeURIComponent(stored) !== zone) {
            document.cookie = "ls_tz=" + encodeURIComponent(zone) + "; path=/; max-age=31536000; SameSite=Lax" +
                (location.protocol === "https:" ? "; Secure" : "");
        }
    } catch (e) { /* cookies blocked: display conversion still works */ }

    var MONTHS = { Jan: 0, Feb: 1, Mar: 2, Apr: 3, May: 4, Jun: 5, Jul: 6, Aug: 7, Sep: 8, Oct: 9, Nov: 10, Dec: 11 };

    // ---- formatting ---------------------------------------------------------------------------
    // Built from en-US parts so month names stay "Sep" (some locales write "Sept") and the layout
    // matches what the server prints: "21 Sep 2026, 08:21 GMT+6" or "Sep 21, 2026, 8:21 AM GMT+6".
    function fmt(date, kind, monthFirst, twentyFour) {
        var opts = { timeZone: zone };
        if (kind === "time" || kind === "datetime") {
            opts.hour = twentyFour ? "2-digit" : "numeric";
            opts.minute = "2-digit";
            opts.hourCycle = twentyFour ? "h23" : "h12";
        }
        if (kind !== "time") { opts.day = monthFirst ? "numeric" : "2-digit"; opts.month = "short"; opts.year = "numeric"; }
        if (kind === "datetime") opts.timeZoneName = "short";
        if (kind === "weekday-date") { opts.weekday = "long"; opts.day = "2-digit"; opts.month = "short"; opts.year = "numeric"; }

        var p = {};
        new Intl.DateTimeFormat("en-US", opts).formatToParts(date).forEach(function (part) { p[part.type] = part.value; });

        var clock = p.hour ? p.hour + ":" + p.minute + (p.dayPeriod ? " " + p.dayPeriod : "") : "";
        var day = kind === "time" ? "" : (monthFirst ? p.month + " " + p.day + ", " + p.year : p.day + " " + p.month + " " + p.year);
        if (kind === "weekday-date") return p.weekday + ", " + day;
        if (kind === "time") return clock;
        if (kind === "date") return day;
        return day + ", " + clock + (p.timeZoneName ? " " + p.timeZoneName : "");
    }

    function parseUtc(value) {
        if (!value) return null;
        var text = String(value).trim();
        // Dates read back from the database carry no zone designator; they are UTC.
        if (!/(Z|[+-]\d{2}:?\d{2})$/i.test(text)) text += "Z";
        var date = new Date(text);
        return isNaN(date.getTime()) ? null : date;
    }

    // ---- <time datetime> elements -------------------------------------------------------------
    function kindOf(element, text) {
        if (element.hasAttribute("data-local-time")) return "time";
        if (element.hasAttribute("data-local-date")) return "date";
        if (element.hasAttribute("data-local-datetime")) return "datetime";
        if (/^\s*\d{1,2}:\d{2}(\s?[AP]M)?(\s*UTC)?\s*$/i.test(text)) return "time";
        return /\d{1,2}:\d{2}/.test(text) ? "datetime" : "date";
    }

    function convertTimeElement(element) {
        if (element.getAttribute("data-tz-done") === zone) return;
        var date = parseUtc(element.getAttribute("datetime"));
        if (!date) return;

        // Remember the server's original text once so re-runs pick the same style.
        var original = element.getAttribute("data-tz-original");
        if (original === null) {
            original = element.textContent;
            element.setAttribute("data-tz-original", original);
        }

        var kind = element.getAttribute("data-tz-kind") || kindOf(element, original);
        element.setAttribute("data-tz-kind", kind);
        var trimmed = original.trim();
        var monthFirst = /^[A-Z][a-z]{2}\s\d/.test(trimmed) || /\b[A-Z][a-z]{2} \d{1,2}, \d{4}/.test(trimmed);
        var twentyFour = !/\b[AP]M\b/i.test(original);

        element.textContent = fmt(date, kind, monthFirst, twentyFour);
        element.setAttribute("title", "Shown in your time zone (" + zone + ")");
        element.setAttribute("data-tz-done", zone);
    }

    // ---- plain-text timestamps labelled UTC ---------------------------------------------------
    var TEXT_PATTERNS = [
        // 21 Sep 2026, 02:21 UTC
        {
            re: /(\d{1,2}) ([A-Z][a-z]{2}) (\d{4}), (\d{2}):(\d{2}) UTC/g,
            make: function (m) { return [Date.UTC(+m[3], MONTHS[m[2]], +m[1], +m[4], +m[5]), "datetime", false, true]; }
        },
        // Sep 21, 2026 at 02:21 UTC
        {
            re: /([A-Z][a-z]{2}) (\d{1,2}), (\d{4}) at (\d{2}):(\d{2}) UTC/g,
            make: function (m) { return [Date.UTC(+m[3], MONTHS[m[1]], +m[2], +m[4], +m[5]), "datetime", true, true]; }
        },
        // Monday, 21 Sep 2026 UTC  (today's date header)
        {
            re: /([A-Z][a-z]+day), (\d{1,2}) ([A-Z][a-z]{2}) (\d{4}) UTC/g,
            make: function () { return [Date.now(), "weekday-date"]; }
        },
        // 02:21 UTC
        {
            // Not after "2026, ": that is the tail of a full date-time already handled (or converted).
            re: /(?<!\d{4}, )\b(\d{2}):(\d{2}) UTC\b/g,
            make: function (m) {
                var now = new Date();
                return [Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), +m[1], +m[2]), "time", false, true];
            }
        }
    ];

    function convertText(text) {
        var changed = false;
        TEXT_PATTERNS.forEach(function (pattern) {
            text = text.replace(pattern.re, function () {
                var args = Array.prototype.slice.call(arguments);
                var made = pattern.make(args);
                var date = new Date(made[0]);
                if (isNaN(date.getTime())) return args[0];
                changed = true;
                return fmt(date, made[1], made[2], made[3]);
            });
        });
        return changed ? text : null;
    }

    function convertTextNodes(root) {
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
            acceptNode: function (node) {
                if (!/UTC/.test(node.nodeValue)) return NodeFilter.FILTER_REJECT;
                var parent = node.parentElement;
                if (!parent || /^(SCRIPT|STYLE|TEXTAREA|INPUT)$/.test(parent.tagName)) return NodeFilter.FILTER_REJECT;
                // <time datetime> elements are converted from their attribute instead.
                if (parent.closest("time[datetime]")) return NodeFilter.FILTER_REJECT;
                return NodeFilter.FILTER_ACCEPT;
            }
        });
        var nodes = [];
        while (walker.nextNode()) nodes.push(walker.currentNode);
        nodes.forEach(function (node) {
            var converted = convertText(node.nodeValue);
            if (converted !== null) node.nodeValue = converted;
        });
    }

    function apply(root) {
        root = root || document.body;
        if (!root) return;
        if (root.nodeType === 1 && root.matches && root.matches("time[datetime]")) convertTimeElement(root);
        if (root.querySelectorAll) root.querySelectorAll("time[datetime]").forEach(convertTimeElement);
        convertTextNodes(root);
    }

    window.LodestoneLocalTime = { apply: apply, zone: zone };

    function start() {
        apply(document.body);
        // Pages that re-render rows live (queue refresh, presence table) add nodes after load.
        new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) apply(node);
                });
            });
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
    else start();
})();

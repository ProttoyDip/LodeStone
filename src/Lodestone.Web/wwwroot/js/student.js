(function () {
    "use strict";

    var doc = document;
    var root = doc.querySelector(".student-page");
    if (!root) return;
    root.classList.add("student-motion-ready");

    var prefersReduced = window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    function showRevealItems() {
        root.querySelectorAll(".student-reveal").forEach(function (item) {
            item.classList.add("is-visible");
        });
    }

    function initReveals() {
        if (prefersReduced || !("IntersectionObserver" in window)) {
            showRevealItems();
            return;
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) return;
                entry.target.classList.add("is-visible");
                observer.unobserve(entry.target);
            });
        }, { threshold: 0.16 });

        root.querySelectorAll(".student-reveal").forEach(function (item) {
            observer.observe(item);
        });
    }

    function initPlannerTabs() {
        var tabs = Array.prototype.slice.call(root.querySelectorAll(".planner-tab[role='tab']"));
        if (!tabs.length) return;

        function selectTab(tab) {
            tabs.forEach(function (item) {
                var active = item === tab;
                item.classList.toggle("is-active", active);
                item.setAttribute("aria-selected", active ? "true" : "false");
                item.tabIndex = active ? 0 : -1;

                var panel = doc.getElementById(item.getAttribute("aria-controls"));
                if (!panel) return;
                panel.classList.toggle("is-active", active);
                if (active) panel.removeAttribute("hidden");
                else panel.setAttribute("hidden", "");
            });
        }

        tabs.forEach(function (tab, index) {
            tab.addEventListener("click", function () {
                selectTab(tab);
            });

            tab.addEventListener("keydown", function (event) {
                var direction = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
                if (!direction) return;
                event.preventDefault();
                var next = tabs[(index + direction + tabs.length) % tabs.length];
                next.focus();
                selectTab(next);
            });
        });
    }

    initReveals();
    initPlannerTabs();

    root.querySelectorAll("[data-local-datetime]").forEach(function (element) {
        var date = new Date(element.getAttribute("datetime"));
        if (!Number.isNaN(date.getTime())) {
            element.textContent = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
        }
    });
})();

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

    function initRiskMonitoringChoice() {
        var form = root.querySelector("[data-risk-monitoring-form]");
        if (!form) return;

        var toggle = form.querySelector("[data-risk-monitoring-toggle]");
        var submit = form.querySelector("[data-risk-monitoring-submit]");
        var status = form.querySelector(".privacy-status");
        var initiallyEnabled = form.getAttribute("data-initially-enabled") === "true";
        if (!toggle) return;

        function renderChoice() {
            if (!status) return;
            status.textContent = toggle.checked ? "On" : "Off";
            status.classList.toggle("is-on", toggle.checked);
            status.classList.toggle("is-off", !toggle.checked);
        }

        toggle.addEventListener("change", renderChoice);
        form.addEventListener("submit", function (event) {
            if (initiallyEnabled && !toggle.checked) {
                var confirmed = window.confirm(
                    "Turn off weekly support monitoring? Learning-activity logs, imported snapshots, model scores, and support cases created for monitoring will be permanently deleted."
                );
                if (!confirmed) {
                    event.preventDefault();
                    toggle.checked = true;
                    renderChoice();
                    toggle.focus();
                    return;
                }
            }

            if (submit) {
                submit.disabled = true;
                submit.textContent = "Saving...";
            }
            form.setAttribute("aria-busy", "true");
        });

        renderChoice();
    }

    function initStudentNumberClaim() {
        var form = root.querySelector("[data-student-number-form]");
        if (!form) return;

        var submit = form.querySelector("[data-student-number-submit]");
        form.addEventListener("submit", function (event) {
            if (event.defaultPrevented || !form.checkValidity()) return;
            if (submit) {
                submit.disabled = true;
                submit.textContent = "Submitting...";
            }
            form.setAttribute("aria-busy", "true");
        });
    }

    function initNudgePreference() {
        var form = root.querySelector("[data-nudge-preference-form]");
        if (!form) return;

        var toggle = form.querySelector("[data-nudge-preference-toggle]");
        var status = form.querySelector("[data-nudge-preference-status]");
        var submit = form.querySelector("[data-nudge-preference-submit]");
        if (!toggle) return;

        function renderChoice() {
            if (status) status.textContent = toggle.checked ? "On" : "Off";
        }

        toggle.addEventListener("change", renderChoice);
        form.addEventListener("submit", function (event) {
            if (event.defaultPrevented) return;
            if (submit) {
                submit.disabled = true;
                submit.textContent = "Saving...";
            }
            form.setAttribute("aria-busy", "true");
        });

        renderChoice();
    }

    function initNudgeResponses() {
        root.querySelectorAll("[data-nudge-response-form]").forEach(function (form) {
            form.addEventListener("submit", function (event) {
                var submitter = event.submitter;
                if (submitter && submitter.hasAttribute("data-nudge-dismiss") &&
                    !window.confirm("Dismiss this optional support prompt?")) {
                    event.preventDefault();
                    return;
                }

                form.querySelectorAll("button[type='submit']").forEach(function (button) {
                    button.disabled = true;
                });
                if (submitter) submitter.textContent = "Saving...";
                form.setAttribute("aria-busy", "true");
            });
        });
    }

    initReveals();
    initPlannerTabs();
    initStudentNumberClaim();
    initRiskMonitoringChoice();
    initNudgePreference();
    initNudgeResponses();

    root.querySelectorAll("[data-local-datetime]").forEach(function (element) {
        var date = new Date(element.getAttribute("datetime"));
        if (!Number.isNaN(date.getTime())) {
            element.textContent = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
        }
    });
})();

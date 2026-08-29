(function () {
    "use strict";

    var root = document.querySelector("[data-risk-operations]");
    if (!root) return;

    var fileInput = root.querySelector("[data-risk-file]");
    var fileName = root.querySelector("[data-risk-file-name]");
    if (fileInput && fileName) {
        fileInput.addEventListener("change", function () {
            var selected = fileInput.files && fileInput.files.length ? fileInput.files[0] : null;
            fileName.textContent = selected ? selected.name : "No file selected";
            fileName.title = selected ? selected.name : "";
        });
    }

    root.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        var confirmation = form.getAttribute("data-risk-confirm");
        if (confirmation && !window.confirm(confirmation)) {
            event.preventDefault();
            return;
        }

        var button = form.querySelector("[data-risk-submit]");
        if (!button || event.defaultPrevented) return;
        button.disabled = true;
        button.setAttribute("aria-disabled", "true");
        button.innerHTML = '<i class="bi bi-hourglass-split" aria-hidden="true"></i> Working&hellip;';
        form.setAttribute("aria-busy", "true");
    });

    root.querySelectorAll("[data-local-datetime]").forEach(function (element) {
        var value = element.getAttribute("datetime");
        var date = value ? new Date(value) : null;
        if (date && !Number.isNaN(date.getTime())) {
            element.textContent = new Intl.DateTimeFormat(undefined, {
                dateStyle: "medium",
                timeStyle: "short"
            }).format(date);
        }
    });

    var importResult = root.querySelector("[data-import-result]");
    if (importResult) {
        var reduceMotion = window.matchMedia &&
            window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        window.requestAnimationFrame(function () {
            importResult.focus({ preventScroll: true });
            importResult.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
        });
    }
})();

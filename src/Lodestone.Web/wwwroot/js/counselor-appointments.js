(function () {
    "use strict";

    document.querySelectorAll("[data-appointment-outcome-form]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            var submitter = event.submitter;
            var confirmation = submitter ? submitter.getAttribute("data-confirm") : null;
            if (confirmation && !window.confirm(confirmation)) {
                event.preventDefault();
                return;
            }

            form.querySelectorAll("button[type='submit']").forEach(function (button) {
                button.disabled = true;
            });
            if (submitter) {
                submitter.textContent = "Saving…";
            }
        });
    });

    document.querySelectorAll("[data-manual-nudge-form]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            var submitter = event.submitter;
            form.querySelectorAll("button[type='submit']").forEach(function (button) {
                button.disabled = true;
            });
            if (submitter) {
                submitter.textContent = "Sending...";
            }
            form.setAttribute("aria-busy", "true");
        });
    });
})();

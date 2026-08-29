(function(){"use strict";
function dateFrom(el){var value=el.getAttribute("datetime");var date=new Date(value);return Number.isNaN(date.getTime())?null:date;}
document.querySelectorAll("[data-local-datetime]").forEach(function(el){var d=dateFrom(el);if(d)el.textContent=new Intl.DateTimeFormat(undefined,{dateStyle:"medium",timeStyle:"short"}).format(d);});
document.querySelectorAll("[data-local-date]").forEach(function(el){var d=dateFrom(el);if(d)el.textContent=new Intl.DateTimeFormat(undefined,{weekday:"short",month:"short",day:"numeric"}).format(d);});
document.querySelectorAll("[data-local-time]").forEach(function(el){var d=dateFrom(el);if(d)el.textContent=new Intl.DateTimeFormat(undefined,{hour:"numeric",minute:"2-digit"}).format(d);});
document.querySelectorAll("form[data-confirm]").forEach(function(form){form.addEventListener("submit",function(event){if(!window.confirm(form.getAttribute("data-confirm")))event.preventDefault();});});
document.querySelectorAll("form[data-disable-on-submit]").forEach(function(form){form.addEventListener("submit",function(){var button=form.querySelector("button[type='submit']");if(button){button.disabled=true;button.textContent="Confirming…";}});});
})();

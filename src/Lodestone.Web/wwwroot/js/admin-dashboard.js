(function () {
    'use strict';

    const body = document.body;
    if (!body.classList.contains('ls-admin')) {
        return;
    }

    /* ── Smooth scroll (Lenis) — same feel as the rest of the site ────── */
    const prefersReduced = window.matchMedia &&
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (window.Lenis && !prefersReduced) {
        try {
            const lenis = new window.Lenis({
                duration: 1.1,
                easing: (t) => Math.min(1, 1.001 - Math.pow(2, -10 * t)),
                smoothWheel: true
            });

            const raf = (time) => {
                lenis.raf(time);
                requestAnimationFrame(raf);
            };
            requestAnimationFrame(raf);
        } catch (err) {
            if (window.console && window.console.warn) {
                window.console.warn('Lenis failed to initialize; using native scrolling.', err);
            }
        }
    }

    const mobileQuery = window.matchMedia('(max-width: 767px)');
    const sidebar = document.getElementById('ls-admin-sidebar');
    const toggle = document.getElementById('ls-admin-menu-toggle');
    const overlay = document.getElementById('ls-admin-drawer-overlay');
    const closeButton = sidebar?.querySelector('[data-admin-drawer-close]');
    const accountMenu = document.querySelector('.ls-admin-account');
    let returnFocus = null;

    const focusableSelector = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    const setSidebarState = (isOpen, restoreFocus) => {
        if (!sidebar || !toggle || !overlay) {
            return;
        }

        const mobile = mobileQuery.matches;
        const open = mobile && isOpen;
        body.classList.toggle('ls-admin-drawer-open', open);
        toggle.setAttribute('aria-expanded', String(open));
        toggle.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
        overlay.setAttribute('aria-hidden', String(!open));
        overlay.tabIndex = open ? 0 : -1;

        if (mobile) {
            sidebar.setAttribute('aria-hidden', String(!open));
            if (open) {
                sidebar.removeAttribute('inert');
            } else {
                sidebar.setAttribute('inert', '');
            }
        } else {
            sidebar.removeAttribute('aria-hidden');
            sidebar.removeAttribute('inert');
        }

        if (open) {
            returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : toggle;
            window.requestAnimationFrame(() => {
                (closeButton || sidebar.querySelector('.is-active') || sidebar.querySelector(focusableSelector))?.focus();
            });
        } else if (restoreFocus && returnFocus instanceof HTMLElement) {
            returnFocus.focus();
            returnFocus = null;
        }
    };

    const openSidebar = () => setSidebarState(true, false);
    const closeSidebar = (restoreFocus) => setSidebarState(false, restoreFocus);

    toggle?.addEventListener('click', () => {
        if (body.classList.contains('ls-admin-drawer-open')) {
            closeSidebar(true);
        } else {
            openSidebar();
        }
    });

    closeButton?.addEventListener('click', () => closeSidebar(true));
    overlay?.addEventListener('click', () => closeSidebar(true));

    sidebar?.addEventListener('click', (event) => {
        if (mobileQuery.matches && event.target.closest('a[href]')) {
            closeSidebar(false);
        }
    });

    sidebar?.addEventListener('keydown', (event) => {
        if (!mobileQuery.matches || !body.classList.contains('ls-admin-drawer-open') || event.key !== 'Tab') {
            return;
        }

        const focusable = Array.from(sidebar.querySelectorAll(focusableSelector))
            .filter((element) => element instanceof HTMLElement && !element.hasAttribute('inert'));
        if (focusable.length === 0) {
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') {
            return;
        }

        if (body.classList.contains('ls-admin-drawer-open')) {
            closeSidebar(true);
        }

        if (accountMenu?.open) {
            accountMenu.open = false;
            accountMenu.querySelector('summary')?.focus();
        }
    });

    document.addEventListener('click', (event) => {
        if (accountMenu?.open && !accountMenu.contains(event.target)) {
            accountMenu.open = false;
        }
    });

    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        const message = form.dataset.adminConfirm;
        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });

    const syncViewport = () => setSidebarState(false, false);
    if (typeof mobileQuery.addEventListener === 'function') {
        mobileQuery.addEventListener('change', syncViewport);
    } else {
        mobileQuery.addListener(syncViewport);
    }

    syncViewport();
})();

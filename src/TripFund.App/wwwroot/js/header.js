window.headerLogic = {
    init: function () {
        let lastScrollY = 0;
        const mainElement = document.querySelector('main');
        if (!mainElement) return;

        mainElement.addEventListener('scroll', () => {
            const currentScrollY = mainElement.scrollTop;
            const header = document.querySelector('.app-header');
            if (!header) return;

            // 1. Scroll Down: Hide header
            // 2. Scroll Up: Show header
            // 3. Top of page: Show header
            
            if (currentScrollY <= 0) {
                // At the top
                header.classList.remove('header-hidden');
            } else if (currentScrollY > lastScrollY && currentScrollY > 50) {
                // Scrolling down and not at the very top
                header.classList.add('header-hidden');
            } else if (currentScrollY < lastScrollY) {
                // Scrolling up
                header.classList.remove('header-hidden');
            }
            
            lastScrollY = currentScrollY;
        }, { passive: true });

        // Global fix for date/time pickers focus issue in Hybrid apps
        // We blur the element on change to handle selection.
        // We also use a global listener to blur it when clicking outside or dismissing the picker.
        document.addEventListener('change', (e) => {
            if (e.target && (e.target.type === 'date' || e.target.type === 'time')) {
                e.target.blur();
            }
        });

        // 'input' event often fires earlier than 'change' on mobile
        document.addEventListener('input', (e) => {
            if (e.target && (e.target.type === 'date' || e.target.type === 'time')) {
                e.target.blur();
            }
        });

        // This handles the "cancel" case where the user taps the overlay.
        // Most OS native pickers will trigger a click/touchstart on the document 
        // after being dismissed, even if the tap was on the native overlay.
        document.addEventListener('touchstart', (e) => {
            const active = document.activeElement;
            if (active && (active.type === 'date' || active.type === 'time')) {
                // If we're clicking the picker itself, don't blur it yet
                if (e.target !== active) {
                    active.blur();
                }
            }
        }, { passive: true });
    },
    resetScroll: function () {
        const mainElement = document.querySelector('main');
        if (mainElement) {
            mainElement.scrollTop = 0;
        }
    },
    selectText: function (element) {
        if (element) {
            setTimeout(() => {
                element.select();
                // Fallback for some mobile browsers
                if (element.setSelectionRange) {
                    element.setSelectionRange(0, 9999);
                }
            }, 10);
        }
    },
    scrollIntoView: function (selector, block = 'center') {
        setTimeout(() => {
            const element = document.querySelector(selector);
            if (element) {
                element.scrollIntoView({ behavior: 'smooth', block: block, inline: 'nearest' });
            }
        }, 50);
    }
};

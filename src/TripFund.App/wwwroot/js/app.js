window.appLogic = {
    lastScrollTop: 0,
    init: function () {
        const mainElement = document.querySelector('main');
        if (mainElement) {
            mainElement.addEventListener('scroll', () => {
                const scrollTop = mainElement.scrollTop;
                const header = document.querySelector('.app-header');
                
                // Smart Header Logic
                if (header) {
                    if (scrollTop > window.appLogic.lastScrollTop && scrollTop > 60) {
                        // Scrolling down - hide header
                        header.classList.add('header-hidden');
                    } else if (scrollTop < window.appLogic.lastScrollTop) {
                        // Scrolling up - show header
                        header.classList.remove('header-hidden');
                    }
                }

                // Keyboard Dismiss Logic
                const activeElement = document.activeElement;
                if (activeElement && (activeElement.tagName === 'INPUT' || activeElement.tagName === 'TEXTAREA')) {
                    if (Math.abs(scrollTop - window.appLogic.lastScrollTop) > 15) {
                        activeElement.blur();
                    }
                }

                window.appLogic.lastScrollTop = scrollTop <= 0 ? 0 : scrollTop;
            }, { passive: true });
        }
    },
    resetScroll: function () {
        const mainElement = document.querySelector('main');
        if (mainElement) {
            mainElement.scrollTop = 0;
            const header = document.querySelector('.app-header');
            if (header) {
                header.classList.remove('header-hidden');
            }
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
    },
    lockScroll: function () {
        document.body.classList.add('no-scroll');
        const main = document.querySelector('main');
        if (main) main.classList.add('no-scroll');
    },
    unlockScroll: function () {
        document.body.classList.remove('no-scroll');
        const main = document.querySelector('main');
        if (main) main.classList.remove('no-scroll');
    },
    positionMenu: function (triggerElement, menuElement) {
        if (!triggerElement || !menuElement) return;

        const container = triggerElement.closest('.vibe-menu-container');
        const isFullWidth = container && container.classList.contains('w-100');

        const triggerRect = triggerElement.getBoundingClientRect();
        
        // If full width, force menu to match trigger width
        if (isFullWidth) {
            menuElement.style.width = `${triggerRect.width}px`;
            menuElement.style.minWidth = 'unset';
        }

        // Measure menu AFTER setting width as it might affect height
        const menuRect = menuElement.getBoundingClientRect();
        const viewportHeight = window.innerHeight;
        const viewportWidth = window.innerWidth;

        let top, left;

        // Determine if there is space below
        const spaceBelow = viewportHeight - triggerRect.bottom;
        const spaceAbove = triggerRect.top;

        if (spaceBelow < menuRect.height && spaceAbove > menuRect.height) {
            // Position above
            top = triggerRect.top - menuRect.height - 4;
        } else {
            // Position below
            top = triggerRect.bottom + 4;
        }

        if (isFullWidth) {
            left = triggerRect.left;
        } else {
            // Horizontal positioning (align right by default, but stay in viewport)
            left = triggerRect.right - menuRect.width;
            if (left < 0) left = 8;
            if (left + menuRect.width > viewportWidth) left = viewportWidth - menuRect.width - 8;
        }

        menuElement.style.top = `${top}px`;
        menuElement.style.left = `${left}px`;
        menuElement.style.position = 'fixed';
        menuElement.style.visibility = 'visible';
    }
};

window.selectElementText = (element) => {
    if (element) {
        element.select();
    }
};

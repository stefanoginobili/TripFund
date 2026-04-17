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
    }
};

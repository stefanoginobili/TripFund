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
    }
};

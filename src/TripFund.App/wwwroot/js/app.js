window.appLogic = {
    init: function () {
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

(function () {
    'use strict';

    window.malievDashboardRefresh = {
        start: function (component, intervalMilliseconds) {
            return window.setInterval(function () {
                component.invokeMethodAsync('RefreshFromBrowserAsync').catch(function () { });
            }, intervalMilliseconds);
        },
        stop: function (handle) {
            window.clearInterval(handle);
        }
    };
})();

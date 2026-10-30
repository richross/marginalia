window.marginaliaNetwork = (() => {
    const subscriptions = new WeakMap();

    return {
        watch(dotnet) {
            const update = () => dotnet.invokeMethodAsync("UpdateOnlineState", navigator.onLine);
            window.addEventListener("online", update);
            window.addEventListener("offline", update);
            subscriptions.set(dotnet, update);
            return navigator.onLine;
        },
        unwatch(dotnet) {
            const update = subscriptions.get(dotnet);
            if (!update) {
                return;
            }

            window.removeEventListener("online", update);
            window.removeEventListener("offline", update);
            subscriptions.delete(dotnet);
        }
    };
})();

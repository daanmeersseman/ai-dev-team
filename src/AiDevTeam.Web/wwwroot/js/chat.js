window.chatInterop = {
    _dotNetRef: null,

    scrollToBottom: function (elementId) {
        requestAnimationFrame(function () {
            const el = document.getElementById(elementId);
            if (el) {
                el.scrollTop = el.scrollHeight;
            }
        });
    },

    registerContextBlockHandler: function (dotNetRef) {
        window.chatInterop._dotNetRef = dotNetRef;
    },

    showContextBlock: function (blockId) {
        if (window.chatInterop._dotNetRef) {
            window.chatInterop._dotNetRef.invokeMethodAsync('ShowContextBlock', blockId);
        }
    }
};

window.traxDashboard = {
    copyToClipboard: async (text) => {
        await navigator.clipboard.writeText(text);
    }
};

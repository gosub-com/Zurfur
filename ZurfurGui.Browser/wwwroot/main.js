

import { dotnet } from './_framework/dotnet.js'

export async function start() {
    // Start dotnet
    try {
        console.log("Starting app...");

        // Run in browser
        const is_browser = typeof window != "undefined";
        if (!is_browser) {
            throw new Error(`Expected to be running in a browser`);
        }

        const dotnetRuntime = await dotnet
            .withDiagnosticTracing(false)
            .withApplicationArgumentsFromQuery()
            .create();
        const config = dotnetRuntime.getConfig();
        const exports = await dotnetRuntime.getAssemblyExports(config.mainAssemblyName);

        await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);

        console.log("App is running");
        document.getElementById("splash").remove();
    }
    catch (e) {
        document.getElementById("splash-text").innerHTML = `Error loading Zurfur Gui: ${e.message}`;
        throw e;
    }
}

function calledFromCs(a) {
    console.log(`Called from C#: ${a}`);
}


// Zurfur GUI Globals
globalThis.zGui = {};
zGui = globalThis.zGui;
zGui.calledFromCs = calledFromCs;
zGui.window = function () { return globalThis.window; }
zGui.fillText = function (context, text, x, y) { context.fillText(text, x, y); };
zGui.fillRect = function (context, x, y, width, height) { context.fillRect(x, y, width, height); }
zGui.getBoundingClientRect = function (canvas) { return canvas.getBoundingClientRect(); }
zGui.getContext = function (context, contextId) { return context.getContext(contextId); }






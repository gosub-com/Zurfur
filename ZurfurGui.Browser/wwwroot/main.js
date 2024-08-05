

import { dotnet } from './_framework/dotnet.js'

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();
const config = dotnetRuntime.getConfig();
const exports = await dotnetRuntime.getAssemblyExports(config.mainAssemblyName);

// Run main
const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);
console.log(`Starting dotnet, assembly='${config.mainAssemblyName}'`);
await dotnetRuntime.runMain(config.mainAssemblyName, [window.location.search]);


export async function startCounter() {
    await exports.ZurfurGui.Program.StartCounter();
}

export function testSynth() {
    const text = document.getElementById("textMessage");
    text.innerHTML = exports.ZurfurGui.SynthTest.TestSynth();
}

export function testDraw() {

    // Default canvas size is 300x150
    const canvas = document.getElementById("canvasMain");

    const { width, height } = canvas.getBoundingClientRect();
    const text = document.getElementById("textMessage");

    let ratio = window.devicePixelRatio;
    text.innerHTML = `Testing 3d a canvas=(${canvas.width}, ${canvas.height}), css=(${width},${height}), ratio=${ratio}`;

    // Canvas size scales by device pixels
    // NOTE: We get high resolution, but css pixels (and font sizes) no longer scale
    canvas.width = width * ratio;
    canvas.height = height * ratio;

    const context = canvas.getContext("2d");

    if (context === null) {
        alert("Unable to initialize canvas 2d.");
        return;
    }

    context.fillStyle = '#228';
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = '#fff';
    context.font = '60px sans-serif';
    context.fillText('Test Draw 2', 10, canvas.height / 2 - 15);
    context.font = '26px sans-serif';
    context.fillStyle = '#88F';
    context.fillText(`Count = ${exports.ZurfurGui.Program.GetCounter()}`, 15, 25);
}


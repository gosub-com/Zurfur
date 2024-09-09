using Microsoft.AspNetCore.Components.RenderTree;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using static ZurfurGui.Browser.OsBrowser;

namespace ZurfurGui;


public class Render
{
    int _frameCount;

    public void RenderFrame()
    {
        var canvas = PrimaryCanvas;
        var context = PrimaryContext;

        _frameCount++;
        var timer = Stopwatch.StartNew();
        var px = PrimaryWindow.DevicePixelRatio;

        var innerSize = PrimaryWindow.InnerSize;

        var cssRect = canvas.GetBoundingClientRect();


        // Canvas size scales by device pixels
        // NOTE: We get high resolution, but css pixels (and font sizes) no longer scale
        canvas.Size = new Size(cssRect.Width * px, cssRect.Height * px);


        var canvasSize = canvas.Size;

        context.FillColor = new Color(0x20, 0x20, 0x80); 
        context.FillRect(0, 0, canvasSize.Width, canvasSize.Height);

        context.FillColor = new Color(0x80, 0x80, 0xF0);
        context.Font = $"{16 * px}px sans-serif";
        for (var i = 0; i < 30; i++)
        {
            for (var j = 0; j < 30; j++)
            {
                var x = i * 50;
                var y = j * 20;
                context.FillText($"{x / 10},{y / 10}", x * px, y * px);
            }
        }

        context.Font = $"{26 * px}px sans-serif";
        context.FillColor = new Color(0xC0, 0xC0, 0xF0);
        context.FillText($"Count = {_frameCount}", 15 * px, 50 * px);

        context.FillText($"Window pixel ratio={px}", 15 * px, 75 * px);
        context.FillText($"Canvas size: ({canvasSize.Width},{canvasSize.Height})", 15 * px, 100 * px);

        context.FillText($"Canvas css size: ({cssRect.Size})", 15 * px, 125 * px);

        context.FillText($"Time: {timer.ElapsedMilliseconds} ms", 15 * px, 200 * px);
        context.FillText($"Window size: {innerSize}", 15 * px, 250 * px);

        FakeButton(context, px, "Generate", 15, 5);
    }

    static void FakeButton(BrowserContext context, double px, string text, double x, double y)
    {
        var fontSize = 16.0;
        context.FillColor = new Color(0xE0, 0xE0, 0xE0);
        context.FillRect(x * px, y * px, 75 * px, 20 * px);
        context.FillColor = new Color(0x10, 0x10, 0x10);

        context.Font = $"{fontSize * px}px sans-serif";
        context.FillText(text, x * px + 5 * px, y * px + fontSize * px);
    }


}

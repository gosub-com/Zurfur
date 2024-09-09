using Microsoft.JSInterop;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using static ZurfurGui.Browser.OsBrowser;

namespace ZurfurGui;

public static class Program
{
    static Render _render = new();

    private static void Main(string[] args)
    {
        Console.WriteLine($"C# Main called args: '{string.Join(" ", args)}'");
        CalledFromCs(0);

        var canvas = PrimaryCanvas;
        var context = PrimaryContext;
        
        _render.RenderFrame();
        StartRendering();
    }

    async static void StartRendering()
    {
        while (true)
        {
            await Task.Delay(1000);
            try
            {
                _render.RenderFrame();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error while rendering: {e.Message}");
            }
        }

    }




}
using Microsoft.JSInterop;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ZurfurGui;

public partial class Program
{
    static int _counter;

    private static void Main(string[] args)
    {
        Console.WriteLine("C# Main called");
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static async void StartCounter()
    {
        while (true)
        {
            Console.WriteLine($"Counting {_counter}");
            _counter++;
            await Task.Delay(1000);
        }
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static int GetCounter()
    {
        return _counter;
    }
}
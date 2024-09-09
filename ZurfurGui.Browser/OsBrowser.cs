//
// Browser specific platform stuff.
// Eventually, this should be the only file that needs to be ported for other platforms.
//


using System.Runtime.InteropServices.JavaScript;

namespace ZurfurGui.Browser;

public static partial class OsBrowser
{
    const string CANVAS_ID = "canvasMain";

    [JSImport("globalThis.document.getElementById")]
    public static partial JSObject? GetElementById(string elementId);

    [JSImport("globalThis.zGui.calledFromCs")]
    public static partial void CalledFromCs(int frameCount);



    //
    // Globals
    //
    
    public static readonly BrowserWindow PrimaryWindow = new BrowserWindow(GetBrowserWindow());

    public static readonly BrowserCanvas PrimaryCanvas = new BrowserCanvas(GetElementById(CANVAS_ID)
            ?? throw new Exception($"Expecting canvas DOM element with ID '{CANVAS_ID}'"), CANVAS_ID);

    public static readonly BrowserContext PrimaryContext = PrimaryCanvas.GetContext2d();

    //
    // BrowserWindow
    //


    [JSImport("globalThis.zGui.window")]
    private static partial JSObject GetBrowserWindow();


    public class BrowserWindow(JSObject _js)
    {
        public double DevicePixelRatio => _js.GetPropertyAsDouble("devicePixelRatio");
        public Size InnerSize
            => new Size(_js.GetPropertyAsDouble("innerWidth"), _js.GetPropertyAsDouble("innerHeight"));
    }


    //
    // BrowserCanvas
    //

    [JSImport("globalThis.zGui.getContext")]
    private static partial JSObject? GetContext(JSObject canvas, string contextId);
    
    [JSImport("globalThis.zGui.getBoundingClientRect")]
    private static partial JSObject GetBoundingClientRect(JSObject canvas);

    public class BrowserCanvas(JSObject _js, string _id)
    {
        public JSObject Js => _js;
        public string Id => _id;
        public BrowserContext GetContext2d() => new BrowserContext(GetContext(_js, "2d")
            ?? throw new Exception($"Can't get canvas ID '{_id}' 2d context"), Id);

        public Rect GetBoundingClientRect()
        {
            var r = OsBrowser.GetBoundingClientRect(_js);
            return new Rect(
                r.GetPropertyAsDouble("x"),
                r.GetPropertyAsDouble("y"),
                r.GetPropertyAsDouble("width"),
                r.GetPropertyAsDouble("height"));
        }

        public Size Size
        {
            get => new Size(_js.GetPropertyAsDouble("width"), _js.GetPropertyAsDouble("height"));
            set { _js.SetProperty("width", value.Width); _js.SetProperty("height", value.Height); }
        }


    }

    //
    // BrowserContext
    //

    [JSImport("globalThis.zGui.fillRect")]
    private static partial void FillRect(JSObject context, double x, double y, double width, double height);

    [JSImport("globalThis.zGui.fillText")]
    private static partial void FillText(JSObject context, string text, double x, double y);


    public class BrowserContext(JSObject _js, string _canvasId)
    {
        public JSObject Js => _js;
        public string Id => _canvasId;

        public void FillRect(double x, double y, double width, double height)
            => OsBrowser.FillRect(_js, x, y, width, height);

        public void FillText(string text, double x, double y)
            => OsBrowser.FillText(_js, text, x, y);

        public Color FillColor
        { 
            set => _js.SetProperty("fillStyle", value.CssColor);
        }

        // TBD: Create a font type rather than just use string
        public string Font
        {
            get => _js.GetPropertyAsString("font")??"";
            set => _js.SetProperty("font", value);
        }
    }

}

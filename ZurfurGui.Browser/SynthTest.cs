using Microsoft.JSInterop;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ZurfurGui;

public partial class SynthTest
{
    const int NUM_SAMPLES = 44000 * 60 / 1024 * 1024; // ~1 minute

    class WavInfo
    {
        public int Mask = 1023;
        public int Index; // Fixed*65536
        public int Pitch = 3 * 65536; // Fixed * 65536
        public int Vol = 32000; // Fixed * 65536
        public int[] WavInt = new int[1024];
        public float[] WavFloat = new float[1024];
        public double[] WavDouble = new double[1024];

        public override string ToString()
        {
            return $"Pitch={Pitch}, Vol={Vol}";
        }
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static string QuickTest()
    {
        return "Hello world\r\nTest!";
    }

    [SupportedOSPlatform("browser")]
    [JSExport]
    public static string TestSynth()
    {
        var result = "";
        var waves1 = MakeWaves();
        var out1 = new int[NUM_SAMPLES];
        var ts = Stopwatch.StartNew();
        MakeInts(waves1, out1);
        result += $"{ts.ElapsedMilliseconds} ms - Integer\r\n";

        var waves2 = MakeWaves();
        var out2 = new float[NUM_SAMPLES];
        ts = Stopwatch.StartNew();
        MakeFloats(waves2, out2);
        result += $"{ts.ElapsedMilliseconds} ms - Float\r\n";

        var waves3 = MakeWaves();
        var out3 = new double[NUM_SAMPLES];
        ts = Stopwatch.StartNew();
        MakeDoubles(waves3, out3);
        result += $"{ts.ElapsedMilliseconds} ms - Double\r\n";

        return result;
    }


    static WavInfo[] MakeWaves()
    {
        var waves = new List<WavInfo>();
        for (int i = 0; i < 64; i++)
        {
            var w = new WavInfo();
            w.Pitch = i * 65536 / 16;
            w.Vol = i * 400 + 32000;
            waves.Add(w);
            for (int wi = 0; wi < w.WavInt.Length; wi++)
            {
                w.WavInt[wi] = (wi - 512) * 64;
                w.WavFloat[wi] = (wi - 512) * 64 / (float)65536.0;
                w.WavDouble[wi] = (wi - 512) * 64 / 65536.0;
            }
        }
        return waves.ToArray();
    }

    static void MakeInts(WavInfo[] wave, int[] output)
    {
        int BLOCK_SIZE = 1024;
        for (int i = 0; i < output.Length; i += BLOCK_SIZE)
        {
            var fullSpan = new Span<int>(output, i, BLOCK_SIZE);
            foreach (var w in wave)
            {
                MakeInt(w, fullSpan);
            }
        }
    }

    static void MakeInt(WavInfo wave, Span<int> output)
    {
        var mask = wave.Mask;
        var index = wave.Index;
        var vol = wave.Vol;
        var pitch = wave.Pitch;
        var wav = wave.WavInt;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] += (wav[(index >> 16) & mask] * vol) >> 16;
            index += pitch;
        }
        wave.Index = index;
    }

    static void MakeFloats(WavInfo[] wave, float[] output)
    {
        int BLOCK_SIZE = 1024;
        for (int i = 0; i < output.Length; i += BLOCK_SIZE)
        {
            var fullSpan = new Span<float>(output, i, BLOCK_SIZE);
            foreach (var w in wave)
            {
                MakeFloat(w, fullSpan);
            }
        }
    }
    
    static void MakeFloat(WavInfo wave, Span<float> output)
    {
        var mask = wave.Mask;
        var index = wave.Index;
        var vol = wave.Vol / (float)65536;
        var pitch = wave.Pitch;
        var wav = wave.WavFloat;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] += wav[(index >> 16) & mask] * vol;
            index += pitch;
        }
        wave.Index = index;
    }

    static void MakeDoubles(WavInfo[] wave, double[] output)
    {
        int BLOCK_SIZE = 1024;
        for (int i = 0; i < output.Length; i += BLOCK_SIZE)
        {
            var fullSpan = new Span<double>(output, i, BLOCK_SIZE);
            foreach (var w in wave)
            {
                MakeDouble(w, fullSpan);
            }
        }
    }
    
    static void MakeDouble(WavInfo wave, Span<double> output)
    {
        var mask = wave.Mask;
        var index = wave.Index;
        var vol = wave.Vol / (double)65536;
        var pitch = wave.Pitch;
        var wav = wave.WavFloat;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] += wav[(index >> 16) & mask] * vol;
            index += pitch;
        }
        wave.Index = index;
    }

}

using System;

namespace Island
{
    public struct AudioFeatures
    {
        public float Rms, Energy, Bass, Mid, High, Beat;
        public long Stamp, Sequence;
        public bool Spectrum;
    }

    // Worker-owned, stereo power spectrum: opposite-phase stereo cannot cancel.
    // 2048 Hann samples at 44.1 kHz, 1024 hop (~43 analyses/s). All memory reused.
    public sealed class AudioSpectrum
    {
        public const int Size = 2048, Hop = 1024, Rate = 44100;
        private readonly float[] left = new float[Size], right = new float[Size];
        private readonly double[] real = new double[Size], imaginary = new double[Size], power = new double[Size / 2];
        private readonly double[] window = new double[Size], cosine = new double[Size / 2], sine = new double[Size / 2], previous = new double[Size / 2];
        private readonly int[] reverse = new int[Size];
        private int cursor, pending, analyses;
        private long samples;
        private double fluxMean, fluxVariance, lastOnset = -10;
        public AudioFeatures Current;
        public AudioSpectrum()
        {
            for (int i = 0; i < Size; i++)
            {
                window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1));
                int n = i, r = 0; for (int b = 0; b < 11; b++) { r = (r << 1) | (n & 1); n >>= 1; } reverse[i] = r;
            }
            for (int i = 0; i < Size / 2; i++) { cosine[i] = Math.Cos(-2 * Math.PI * i / Size); sine[i] = Math.Sin(-2 * Math.PI * i / Size); }
        }
        public bool Push(float l, float r)
        {
            left[cursor] = Finite(l); right[cursor] = Finite(r); cursor = (cursor + 1) & (Size - 1); samples++; pending++;
            if (samples < Size || pending < Hop) return false;
            pending = 0; Analyse(); return true;
        }
        private static float Finite(float v) { return float.IsNaN(v) || float.IsInfinity(v) ? 0 : Math.Max(-1, Math.Min(1, v)); }
        private void Transform(float[] input, bool add)
        {
            for (int i = 0; i < Size; i++) { int j = reverse[i]; real[j] = input[(cursor + i) & (Size - 1)] * window[i]; imaginary[j] = 0; }
            for (int length = 2; length <= Size; length <<= 1)
            {
                int half = length / 2, stride = Size / length;
                for (int block = 0; block < Size; block += length) for (int j = 0; j < half; j++)
                {
                    int a = block + j, b = a + half, k = j * stride;
                    double r = real[b] * cosine[k] - imaginary[b] * sine[k], im = real[b] * sine[k] + imaginary[b] * cosine[k];
                    real[b] = real[a] - r; imaginary[b] = imaginary[a] - im; real[a] += r; imaginary[a] += im;
                }
            }
            for (int i = 0; i < Size / 2; i++) { double p = (real[i] * real[i] + imaginary[i] * imaginary[i]) / (Size * Size * 0.375); power[i] = add ? (power[i] + p) * 0.5 : p; }
        }
        private void Analyse()
        {
            double rms = 0; for (int i = 0; i < Size; i++) rms += (left[i] * left[i] + right[i] * right[i]) * 0.5;
            Transform(left, false); Transform(right, true);
            double bass = 0, mid = 0, high = 0, flux = 0;
            for (int i = 1; i < Size / 2; i++)
            {
                double hz = i * (double)Rate / Size, p = power[i] * 2;
                if (hz < 250) bass += p; else if (hz < 4000) mid += p; else if (hz < 16000) high += p;
                // Positive log spectral change, not absolute level. Quiet and
                // compressed tracks retain onsets; a steady loud tone has none.
                double magnitude = Math.Log(1 + 150 * Math.Sqrt(p));
                if (hz < 8000) flux += Math.Max(0, magnitude - previous[i]) * (hz < 250 ? 1.5 : 1);
                previous[i] = magnitude;
            }
            const double dt = Hop / (double)Rate;
            double time = samples / (double)Rate, delta = flux - fluxMean;
            double threshold = fluxMean + Math.Max(0.12, 1.6 * Math.Sqrt(Math.Max(0, fluxVariance)));
            float onset = 0;
            if (analyses > 20 && rms > 0.000001 && flux > threshold && time - lastOnset > 0.14)
            { onset = (float)Math.Min(1, (flux - threshold) / Math.Max(0.3, threshold)); lastOnset = time; }
            double alpha = 1 - Math.Exp(-dt / 0.75); fluxMean += alpha * delta; fluxVariance += alpha * (delta * delta - fluxVariance);
            Current.Rms = (float)Math.Sqrt(rms / Size);
            Current.Energy = AudioMath.Follow(Current.Energy, Level(Current.Rms), dt, 0.035, 0.28);
            Current.Bass = AudioMath.Follow(Current.Bass, Level(Math.Sqrt(bass)), dt, 0.028, 0.22);
            Current.Mid = AudioMath.Follow(Current.Mid, Level(Math.Sqrt(mid)), dt, 0.045, 0.24);
            Current.High = AudioMath.Follow(Current.High, Level(Math.Sqrt(high)), dt, 0.025, 0.16);
            Current.Beat = Math.Max(onset, Current.Beat * (float)Math.Exp(-dt / 0.19));
            Current.Spectrum = true; Current.Sequence++; Current.Stamp = System.Diagnostics.Stopwatch.GetTimestamp(); analyses++;
        }
        private static float Level(double amplitude) { return (float)Math.Min(1, Math.Log(1 + 30 * amplitude) / Math.Log(16)); }
    }
}

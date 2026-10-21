using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A minute of timings, then one summary. Sailwind does not record Unity
    // Profiler samples, so each hanger is a Stopwatch. The summary is the same
    // four numbers a Micrometer timer publishes: mean, median, p90, and p99.
    internal static class Profile
    {
        internal const float WindowSeconds = 60f;

        internal enum Kind
        {
            Stem = 0,
            Sausage,
            Bag,
            Pot,
            Rack,
            Loop,
            Hammer,
            Glass,
            Line,
            Late,
        }

        private static readonly string[] Names =
        {
            "stem", "sausage", "bag", "pot", "rack", "loop", "hammer", "glass", "line", "late",
        };

        private static readonly List<long>[] _samples = new List<long>[Names.Length];
        private static readonly List<float> _frames = new List<float>(4096);
        private static readonly int[] _builds = new int[Names.Length];

        private static int _window;
        private static float _windowStart;
        private static float _nextReport;
        private static bool _primed;
        private static string _text = "clothesline\ngathering a minute";
        private static GUIStyle _style;

        static Profile()
        {
            for (int i = 0; i < _samples.Length; i++)
                _samples[i] = new List<long>(256);
        }

        internal static long Stamp()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static void Add(Kind kind, long stamp)
        {
            if (!On)
                return;
            _samples[(int)kind].Add(Stopwatch.GetTimestamp() - stamp);
        }

        internal static void Built(Kind kind)
        {
            _builds[(int)kind]++;
        }

        internal static int Builds(Kind kind)
        {
            return _builds[(int)kind];
        }

        internal static void Clear()
        {
            for (int i = 0; i < Names.Length; i++)
                _builds[i] = 0;
        }

        private static bool On
        {
            get { return ClotheslineConfig.Metrics == null || ClotheslineConfig.Metrics.Value; }
        }

        internal static void Frame()
        {
            if (!On)
            {
                // Turning it off publishes the minute already gathered, then stops.
                if (_primed)
                    Stop();
                return;
            }

            // The first update after a load is the pause, not a frame of play.
            if (!_primed)
            {
                _primed = true;
                _windowStart = Time.unscaledTime;
                _nextReport = _windowStart + WindowSeconds;
                _text = "clothesline\ngathering a minute";
                return;
            }

            _frames.Add(Time.unscaledDeltaTime);
            if (Time.unscaledTime < _nextReport)
                return;
            Report();
        }

        // The last partial minute, so quitting still leaves a summary in the log.
        internal static void Flush()
        {
            if (!_primed || (_frames.Count == 0 && !AnySamples()))
                return;
            Report();
        }

        internal static void Draw()
        {
            if (!On)
                return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box);
                _style.fontSize = 13;
                _style.alignment = TextAnchor.UpperLeft;
                _style.normal.textColor = Color.white;
                _style.wordWrap = false;
            }
            GUI.Label(new Rect(8f, 8f, 760f, 280f), _text, _style);
        }

        private static void Stop()
        {
            Flush();
            _primed = false;
            _text = "clothesline\ngathering a minute";
        }

        private static bool AnySamples()
        {
            for (int i = 0; i < _samples.Length; i++)
            {
                if (_samples[i].Count > 0)
                    return true;
            }
            return false;
        }

        private static void Report()
        {
            _window++;
            float seconds = Mathf.Max(0f, Time.unscaledTime - _windowStart);
            _windowStart = Time.unscaledTime;
            _nextReport = _windowStart + WindowSeconds;

            var screen = new StringBuilder();
            screen.Append("clothesline ").Append(Plugin.PluginVersion);
            screen.Append("  ").Append(seconds.ToString("0")).Append("s");

            if (_frames.Count > 0)
                Publish(screen, "frame", FromSeconds(_frames));
            for (int i = 0; i < Names.Length; i++)
            {
                if (_samples[i].Count == 0)
                    continue;
                Publish(screen, Names[i], FromTicks(_samples[i]));
            }

            _text = screen.ToString();
        }

        private static void Publish(StringBuilder screen, string name, Stats stats)
        {
            string mean = stats.Mean.ToString("0.000");
            string median = stats.Median.ToString("0.000");
            string p90 = stats.P90.ToString("0.000");
            string p99 = stats.P99.ToString("0.000");

            screen.Append('\n').Append(name);
            screen.Append(' ', Math.Max(1, 8 - name.Length));
            screen.Append(" n=").Append(stats.N.ToString().PadLeft(5));
            screen.Append("  mean=").Append(mean.PadLeft(8));
            screen.Append("  median=").Append(median.PadLeft(8));
            screen.Append("  p90=").Append(p90.PadLeft(8));
            screen.Append("  p99=").Append(p99.PadLeft(8));
            screen.Append(" ms");

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "clothesline " + Plugin.PluginVersion
                    + " window=" + _window
                    + " " + name
                    + " n=" + stats.N
                    + " mean=" + mean
                    + " median=" + median
                    + " p90=" + p90
                    + " p99=" + p99
                    + " ms");
            }
        }

        private struct Stats
        {
            public int N;
            public double Mean;
            public double Median;
            public double P90;
            public double P99;
        }

        private static Stats FromTicks(List<long> samples)
        {
            int n = samples.Count;
            var sorted = new long[n];
            long sum = 0;
            for (int i = 0; i < n; i++)
            {
                sorted[i] = samples[i];
                sum += sorted[i];
            }
            samples.Clear();
            Array.Sort(sorted);
            return new Stats
            {
                N = n,
                Mean = Ms((double)sum / n),
                Median = Ms(Rank(sorted, 0.50)),
                P90 = Ms(Rank(sorted, 0.90)),
                P99 = Ms(Rank(sorted, 0.99)),
            };
        }

        private static Stats FromSeconds(List<float> samples)
        {
            int n = samples.Count;
            var sorted = new float[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sorted[i] = samples[i];
                sum += sorted[i];
            }
            samples.Clear();
            Array.Sort(sorted);
            return new Stats
            {
                N = n,
                Mean = sum / n * 1000.0,
                Median = Rank(sorted, 0.50) * 1000.0,
                P90 = Rank(sorted, 0.90) * 1000.0,
                P99 = Rank(sorted, 0.99) * 1000.0,
            };
        }

        // Linear rank, so an even count's median sits between the two middle samples.
        private static double Rank(long[] sorted, double p)
        {
            if (sorted.Length == 1)
                return sorted[0];
            double rank = p * (sorted.Length - 1);
            int lo = (int)rank;
            int hi = lo + 1;
            if (hi >= sorted.Length)
                return sorted[sorted.Length - 1];
            double w = rank - lo;
            return sorted[lo] * (1.0 - w) + sorted[hi] * w;
        }

        private static double Rank(float[] sorted, double p)
        {
            if (sorted.Length == 1)
                return sorted[0];
            double rank = p * (sorted.Length - 1);
            int lo = (int)rank;
            int hi = lo + 1;
            if (hi >= sorted.Length)
                return sorted[sorted.Length - 1];
            double w = rank - lo;
            return sorted[lo] * (1.0 - w) + sorted[hi] * w;
        }

        private static double Ms(double ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}

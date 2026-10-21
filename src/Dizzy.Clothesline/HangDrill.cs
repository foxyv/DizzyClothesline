using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // One hanging item, rocked for a few seconds. The first frame may build the
    // cord. The frames after that are the ones that hitch when a heel rebuilds it.
    internal struct HangSample
    {
        public string Item;
        public int Frames;
        public double FirstMs;
        public double SteadyMs;
        public int SteadyBuilds;
        public int TotalBuilds;
        public bool Pass;
    }

    // Hangs every item the line knows, with the boat rolling under it, and times
    // the cord update. The numbers are the config defaults: 2 cm rope, 4 cm of
    // sag per metre, items 2 cm under the rope. 180 frames is three seconds at
    // 60 fps. After the first frame the cord may cost 2 ms altogether, and it may
    // rebuild at most three times (the hammer ring's one-second check).
    internal static class HangDrill
    {
        internal const int Frames = 180;
        internal const double SteadyMsBudget = 2.0;
        internal const int SteadyBuildBudget = 3;

        private const float Thickness = 0.02f;
        private const float Sag = 0.04f;
        private const float HangDrop = 0.02f;
        private const float RopeRadius = Thickness * 0.5f;

        internal static HangSample[] Run()
        {
            return new[]
            {
                Banana(),
                Sausage(),
                Bag("Apple bag"),
                Bag("Orange bag"),
                Bag("Goat cheese bag"),
                Pot("Small soup pot", 0.1613f),
                Pot("Big soup pot", 0.2097f),
                Rack(),
                Handle("Emerald kettle", new Vector3(0f, 0.222f, 0f), 0.012f, 0.006f),
                Handle("Medi kettle", new Vector3(0f, 0.268f, 0f), 0.011f, 0.020f),
                Handle("Al'Ankh kettle", new Vector3(0f, 0.294f, 0f), 0.014f, 0.013f),
                Handle("Bucket", new Vector3(0f, 0.20f, 0f), 0.011f, 0.014f),
                Handle("Windicator", new Vector3(0f, -0.010f, 0f), 0.014f, 0.028f),
                Handle("Hook line", Vector3.zero, 0.0014f, 0.0028f),
                HammerRing(),
                HammerEye(),
                Spyglass("Spyglass", 0.045f, -0.42f, 0f),
                Spyglass("Little spyglass", 0.030f, -0.22f, RopeRadius * 2f),
                Line(),
            };
        }

        // The rope's offset from the passage is turned, not stretched, so a ring
        // that is keyed on length does not see a new gap. The axis yaws and rolls
        // the way a world-upright item sees a rope fixed to the boat.
        private static void Rock(int frame, Vector3 passage, Vector3 offset, out Vector3 rope, out Vector3 axis)
        {
            float yaw = 25f * Mathf.Deg2Rad * Mathf.Sin(frame * 0.09f);
            float roll = 18f * Mathf.Deg2Rad * Mathf.Sin(frame * 0.17f);
            float cy = Mathf.Cos(yaw);
            float sy = Mathf.Sin(yaw);
            float cr = Mathf.Cos(roll);
            float sr = Mathf.Sin(roll);
            Vector3 yawed = new Vector3(
                offset.x * cy + offset.z * sy,
                offset.y,
                -offset.x * sy + offset.z * cy);
            Vector3 turned = new Vector3(yawed.x, yawed.y * cr - yawed.z * sr, yawed.y * sr + yawed.z * cr);
            rope = passage + turned;
            axis = new Vector3(cy, sr * 0.2f, sy);
            if (axis.sqrMagnitude < 1e-8f)
                axis = Vector3.right;
            else
                axis.Normalize();
        }

        private static HangSample Measure(string item, Profile.Kind kind, Action reset, Action<int> frame)
        {
            reset();
            for (int i = 0; i < Frames; i++)
                frame(i);

            reset();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Profile.Clear();

            long origin = Stopwatch.GetTimestamp();
            frame(0);
            long firstEnd = Stopwatch.GetTimestamp();
            int firstBuilds = Profile.Builds(kind);
            long steady = 0;
            for (int i = 1; i < Frames; i++)
            {
                long stamp = Stopwatch.GetTimestamp();
                frame(i);
                steady += Stopwatch.GetTimestamp() - stamp;
            }

            int steadyBuilds = Profile.Builds(kind) - firstBuilds;
            double firstMs = ToMs(firstEnd - origin);
            double steadyMs = ToMs(steady);
            return new HangSample
            {
                Item = item,
                Frames = Frames,
                FirstMs = firstMs,
                SteadyMs = steadyMs,
                SteadyBuilds = steadyBuilds,
                TotalBuilds = firstBuilds + steadyBuilds,
                Pass = steadyBuilds <= SteadyBuildBudget && steadyMs <= SteadyMsBudget,
            };
        }

        private static double ToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static HangSample Banana()
        {
            var hold = new Hold<StemTie.Motion>();
            Vector3 top = new Vector3(0f, 0.12f, 0f);
            Vector3 offset = new Vector3(0.04f, 0.07f, 0.02f);
            return Measure("Banana bunch", Profile.Kind.Stem,
                () => hold.Value = default(StemTie.Motion),
                frame =>
                {
                    Vector3 rope;
                    Vector3 axis;
                    Rock(frame, top, offset, out rope, out axis);
                    StemTie.Step(ref hold.Value, rope, axis, top, Vector3.down, 0.018f, RopeRadius, null, null, null);
                });
        }

        private static HangSample Sausage()
        {
            return Knot("Sausage bunch", Profile.Kind.Sausage);
        }

        private static HangSample Bag(string item)
        {
            return Knot(item, Profile.Kind.Bag);
        }

        private static HangSample Knot(string item, Profile.Kind kind)
        {
            var hold = new Hold<LineEye.Aim>();
            return Measure(item, kind,
                () => hold.Value = default(LineEye.Aim),
                frame =>
                {
                    Vector3 rope;
                    Vector3 axis;
                    Rock(frame, Vector3.zero, Vector3.right, out rope, out axis);
                    LineEye.Step(ref hold.Value, axis, HangDrop, RopeRadius, null, kind);
                });
        }

        private static HangSample Pot(string item, float wall)
        {
            var hold = new Hold<PotTie.Motion>();
            Vector3 offset = new Vector3(0.02f, 0.08f, 0.015f);
            return Measure(item, Profile.Kind.Pot,
                () => hold.Value = default(PotTie.Motion),
                frame =>
                {
                    Vector3 rope;
                    Vector3 axis;
                    Rock(frame, Vector3.zero, offset, out rope, out axis);
                    PotTie.Step(ref hold.Value, rope, axis, RopeRadius, wall, null, null, null);
                });
        }

        private static HangSample Rack()
        {
            var hold = new Hold<RackHook.Motion>();
            Vector3 offset = new Vector3(0.02f, 0.09f, 0.03f);
            return Measure("Date rack", Profile.Kind.Rack,
                () => hold.Value = default(RackHook.Motion),
                frame =>
                {
                    Vector3 rope;
                    Vector3 axis;
                    Rock(frame, Vector3.zero, offset, out rope, out axis);
                    RackHook.Step(ref hold.Value, rope, axis, RopeRadius, null, null);
                });
        }

        private static HangSample Handle(string item, Vector3 passage, float metal, float wire)
        {
            var hold = new Hold<LoopTie.HandleMotion>();
            Vector3 offset = new Vector3(0.012f, 0.055f, 0.018f);
            return Measure(item, Profile.Kind.Loop,
                () => hold.Value = default(LoopTie.HandleMotion),
                frame =>
                {
                    Vector3 rope;
                    Vector3 axis;
                    Rock(frame, passage, offset, out rope, out axis);
                    LoopTie.StepHandle(ref hold.Value, passage, rope, axis, RopeRadius, metal, wire, null);
                });
        }

        private static HangSample HammerRing()
        {
            var hold = new Hold<LoopTie.RingMotion>();
            Vector3 passage = new Vector3(0.10f, -0.002f, 0.455f);
            float cord = RopeRadius / 3f;
            float gap = RopeRadius + 0.012f;
            return Measure("Hammer ring", Profile.Kind.Loop,
                () => hold.Value = default(LoopTie.RingMotion),
                frame => LoopTie.StepHammer(ref hold.Value, passage, gap, RopeRadius, cord, cord * 2f, frame / 60f, null));
        }

        private static HangSample HammerEye()
        {
            var hold = new Hold<HammerTie.EyeMotion>();
            float cord = RopeRadius / 3f;
            return Measure("Hammer eye", Profile.Kind.Hammer,
                () => hold.Value = default(HammerTie.EyeMotion),
                frame => HammerTie.Step(ref hold.Value, cord, null));
        }

        private static HangSample Spyglass(string item, float cupRadius, float eyeZ, float lift)
        {
            var hold = new Hold<SpyglassTie.GlassMotion>();
            float gap = HangDrop + Thickness;
            Vector3 rope = new Vector3(0f, 0f, eyeZ - gap);
            float cord = RopeRadius / 3f;
            return Measure(item, Profile.Kind.Glass,
                () => hold.Value = default(SpyglassTie.GlassMotion),
                frame => SpyglassTie.Step(
                    ref hold.Value, rope, Vector3.right, RopeRadius, cord, cupRadius, eyeZ, lift, null));
        }

        private static HangSample Line()
        {
            var run = new LineRun();
            Vector3 a = new Vector3(-1.2f, 0.5f, 0f);
            Vector3 b = new Vector3(1.15f, 0.5f, 0.25f);
            return Measure("Clothesline", Profile.Kind.Line,
                () => run.Built = false,
                frame =>
                {
                    // A heel does not move the hooks in boat space. This wiggle stays
                    // under the millimetre that counts as the hook having moved.
                    float wiggle = Mathf.Sin(frame * 0.3f) * 0.0002f;
                    ClotheslineLine.StepMesh(
                        ref run.Built, ref run.A, ref run.B,
                        a + new Vector3(wiggle, 0f, 0f), b,
                        Thickness, Sag, Vector3.up, Vector3.up,
                        run.Points, run.Paths, null);
                });
        }

        private sealed class Hold<T> where T : struct
        {
            public T Value;
        }

        private sealed class LineRun
        {
            public bool Built;
            public Vector3 A;
            public Vector3 B;
            public readonly List<Vector3[]> Paths = new List<Vector3[]>();
            public readonly Vector3[] Points = new Vector3[25];
        }
    }
}

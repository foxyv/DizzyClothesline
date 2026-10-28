using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Dizzy.Clothesline.Tests
{
    // The hammer ring once grew to cover a gap that a bad physics or scale sample opened for
    // a frame. This feeds every hanging item that follows a live rope point the same kinds of
    // bad frame, between normal ones, and checks that its cord stays near its usual size and
    // comes back on the next normal frame.
    public class BadSampleTests
    {
        private const string Managed = @"D:\SteamLibrary\steamapps\common\Sailwind\Sailwind_Data\Managed";

        static BadSampleTests()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            string path = Path.Combine(Managed, file);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        [Test]
        public void ARopeThatStaysFarIsFollowedAfterTheGuardsPatience()
        {
            Assert.That(BadSampleDrill.FramesBeforeAFarRopeIsFollowed(), Is.EqualTo(CordGuard.Patience + 1));
        }

        [Test]
        public void ARopeThatIsNotANumberIsNeverFollowed()
        {
            Assert.That(BadSampleDrill.NaNRopeFollowedWithin(100), Is.False);
        }

        [Test]
        public void EachHangingItemStaysNearItsSizeThroughABadFrame()
        {
            List<BadSampleDrill.Result> results = BadSampleDrill.Run();
            var text = new StringBuilder();
            text.AppendLine("item | bad frame | usual reach | during bad | after | bounded | recovers");
            bool all = true;
            foreach (BadSampleDrill.Result r in results)
            {
                bool pass = r.Bounded && r.Recovers;
                all &= pass;
                text.Append(pass ? "PASS  " : "FAIL  ");
                text.Append(r.Item.PadRight(14)).Append(r.Bad.PadRight(16));
                text.Append(r.Baseline.ToString("0.000").PadLeft(8));
                text.Append(r.DuringBad.ToString("0.000").PadLeft(10));
                text.Append(r.After.ToString("0.000").PadLeft(10));
                text.Append(r.Bounded ? "  bounded" : "  UNBOUNDED");
                text.Append(r.Recovers ? "  recovers" : "  STAYS BAD");
                text.AppendLine();
            }

            TestContext.WriteLine(text.ToString());
            Assert.That(all, Is.True, "A hanging item's cord went far past its usual size on a bad frame, or did not come back."
                + Environment.NewLine + text);
        }
    }

    // Holds the Unity types, so the test class itself has none in its signatures and NUnit
    // can load it before the assembly resolver is set up.
    internal static class BadSampleDrill
    {
        private const float RopeRadius = 0.01f;
        private const int Warmup = 30;

        // One frame of an item: where the rope is, which way it runs, and how much wider the
        // item's own live size reads than usual (1 is normal). It returns how far the cord
        // reaches, or infinity when any part of it is not a number.
        private delegate float Frame(Vector3 rope, Vector3 axis, float size);

        private sealed class Item
        {
            public string Name;
            public Func<Frame> Start;
            public bool ReadsSize;
        }

        private static IEnumerable<Item> Items()
        {
            yield return new Item { Name = "Banana bunch", ReadsSize = true, Start = Banana };
            yield return new Item { Name = "Soup pot", Start = Pot };
            yield return new Item { Name = "Date rack", Start = Rack };
            yield return new Item { Name = "Kettle handle", Start = Handle };
        }

        private static Frame Banana()
        {
            StemTie.Motion motion = default(StemTie.Motion);
            Vector3 top = new Vector3(0f, 0.12f, 0f);
            return (rope, axis, size) =>
            {
                StemTie.Step(ref motion, rope, axis, top, Vector3.down, 0.018f * size, RopeRadius, null, null, null);
                return Reach(motion.SpanScale.z, motion.WrapEnd, motion.WrapPos, motion.LoopPos);
            };
        }

        private static Frame Pot()
        {
            PotTie.Motion motion = default(PotTie.Motion);
            return (rope, axis, size) =>
            {
                PotTie.Step(ref motion, rope, axis, RopeRadius, 0.1613f, null, null, null);
                return Reach(motion.LeftScale.z, motion.RightScale.z, motion.LeftPos, motion.RightPos);
            };
        }

        private static Frame Rack()
        {
            RackHook.Motion motion = default(RackHook.Motion);
            return (rope, axis, size) =>
            {
                RackHook.Step(ref motion, rope, axis, RopeRadius, null, null);
                return Reach(motion.RiseScale.z, motion.ArcStart, motion.ArcPos, motion.RisePos);
            };
        }

        private static Frame Handle()
        {
            LoopTie.HandleMotion motion = default(LoopTie.HandleMotion);
            Vector3 passage = new Vector3(0f, 0.222f, 0f);
            return (rope, axis, size) =>
            {
                LoopTie.StepHandle(ref motion, passage, rope, axis, RopeRadius, 0.012f * size, 0.006f * size, null);
                return Reach(motion.Length, motion.Position);
            };
        }

        private static float Reach(params object[] parts)
        {
            float reach = 0f;
            foreach (object part in parts)
            {
                float[] values;
                if (part is float)
                    values = new[] { (float)part };
                else
                {
                    Vector3 v = (Vector3)part;
                    values = new[] { v.x, v.y, v.z };
                }

                foreach (float value in values)
                {
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        return float.PositiveInfinity;
                    reach = Mathf.Max(reach, Mathf.Abs(value));
                }
            }

            return reach;
        }

        private static Vector3 Normal(int frame)
        {
            float turn = frame * 0.09f;
            return new Vector3(0.04f * Mathf.Cos(turn), 0.07f, 0.02f * Mathf.Sin(turn));
        }

        private static Vector3 Axis(int frame)
        {
            float yaw = 25f * Mathf.Deg2Rad * Mathf.Sin(frame * 0.09f);
            return new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        }

        internal struct Result
        {
            public string Item;
            public string Bad;
            public float Baseline;
            public float DuringBad;
            public float After;
            public bool Bounded;
            public bool Recovers;
        }

        // Frames a rope that stays 3 m away is refused before the cord follows it.
        internal static int FramesBeforeAFarRopeIsFollowed()
        {
            int bad = 0;
            for (int frame = 1; frame <= 100; frame++)
            {
                if (CordGuard.Usable(ref bad, new Vector3(3f, 0f, 0f), Vector3.right))
                    return frame;
            }

            return -1;
        }

        internal static bool NaNRopeFollowedWithin(int frames)
        {
            int bad = 0;
            Vector3 nan = new Vector3(float.NaN, 0f, 0f);
            for (int frame = 0; frame < frames; frame++)
            {
                if (CordGuard.Usable(ref bad, nan, Vector3.right))
                    return true;
            }

            return false;
        }

        internal static List<Result> Run()
        {
            var results = new List<Result>();
            foreach (Item item in Items())
            {
                results.Add(Try(item, "rope 3 m away", frame => Normal(frame) + new Vector3(3f, 0f, 0f), 1f));
                results.Add(Try(item, "rope NaN", frame => new Vector3(float.NaN, float.NaN, float.NaN), 1f));
                if (item.ReadsSize)
                    results.Add(Try(item, "size read x20", frame => Normal(frame), 20f));
            }

            return results;
        }

        private static Result Try(Item item, string bad, Func<int, Vector3> badRope, float badSize)
        {
            Frame frame = item.Start();
            float baseline = 0f;
            for (int i = 0; i < Warmup; i++)
                baseline = Mathf.Max(baseline, frame(Normal(i), Axis(i), 1f));

            float during = frame(badRope(Warmup), Axis(Warmup), badSize);
            float after = frame(Normal(Warmup + 1), Axis(Warmup + 1), 1f);
            float limit = baseline * 3f + 0.05f;
            return new Result
            {
                Item = item.Name,
                Bad = bad,
                Baseline = baseline,
                DuringBad = during,
                After = after,
                Bounded = during <= limit,
                Recovers = after <= baseline * 1.5f + 0.01f,
            };
        }

    }
}

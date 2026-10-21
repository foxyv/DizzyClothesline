using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace Dizzy.Clothesline.Tests
{
    public class HangBudgetTests
    {
        private const string Managed = @"D:\SteamLibrary\steamapps\common\Sailwind\Sailwind_Data\Managed";

        static HangBudgetTests()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            string path = Path.Combine(Managed, file);
            if (!File.Exists(path))
                return null;
            return Assembly.LoadFrom(path);
        }

        [Test]
        public void EachHangingItemStaysCheapWhileTheBoatRocks()
        {
            HangSample[] samples = HangDrill.Run();
            var text = new StringBuilder();
            text.AppendLine("item | first ms | steady ms | us/frame | steady builds | total builds | pass");
            foreach (HangSample sample in samples)
            {
                double perFrame = sample.SteadyMs / (sample.Frames - 1) * 1000.0;
                text.Append(sample.Pass ? "PASS  " : "FAIL  ");
                text.Append(sample.Item.PadRight(20));
                text.Append("  first ").Append(sample.FirstMs.ToString("0.000")).Append(" ms");
                text.Append("  steady ").Append(sample.SteadyMs.ToString("0.000")).Append(" ms");
                text.Append("  ").Append(perFrame.ToString("0.0")).Append(" us/frame");
                text.Append("  builds ").Append(sample.SteadyBuilds);
                text.Append(" / ").Append(sample.TotalBuilds);
                text.AppendLine();
            }

            TestContext.WriteLine(text.ToString());
            Assert.That(Array.TrueForAll(samples, sample => sample.Pass), Is.True,
                "A hanging item rebuilt its cord while the boat rocked, or the steady frames cost more than "
                + HangDrill.SteadyMsBudget.ToString("0.0") + " ms." + Environment.NewLine + text);
        }
    }
}

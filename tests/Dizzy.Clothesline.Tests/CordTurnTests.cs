using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Dizzy.Clothesline.Tests
{
    public class CordTurnTests
    {
        private const string Managed = @"D:\SteamLibrary\steamapps\common\Sailwind\Sailwind_Data\Managed";

        static CordTurnTests()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string path = Path.Combine(Managed, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        // A knot is aimed along the rope with a turn about the vertical. It must never
        // end up upside down, whichever way the rope runs across the item.
        [TestCase(1f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(0f, -1f)]
        [TestCase(0.7071f, 0.7071f)]
        [TestCase(-0.7071f, -0.7071f)]
        [TestCase(-0.9999f, 0.0141f)]
        [TestCase(-1f, 0.0000001f)]
        public void AimingAKnotAlongTheRope_NeverTurnsItUpsideDown(float x, float z)
        {
            Vector3 along = new Vector3(x, 0f, z).normalized;
            Quaternion turn = CordTurn.FromTo(Vector3.right, along);

            Vector3 right = turn * Vector3.right;
            Vector3 up = turn * Vector3.up;
            Assert.That(Vector3.Distance(right, along), Is.LessThan(0.001f), "The knot should point along the rope.");
            Assert.That(up.y, Is.GreaterThan(0.999f), "The knot should stay the right way up: its up was " + up + ".");
        }

        [Test]
        public void ARopeStraightBackAcrossTheItem_TurnsHalfWayRoundTheVertical()
        {
            Quaternion turn = CordTurn.FromTo(Vector3.right, Vector3.left);
            Vector3 forward = turn * Vector3.forward;
            Assert.That(forward.z, Is.LessThan(-0.999f), "A half turn about the vertical swings forward round to back.");
        }

        [Test]
        public void ARopeStraightUpOrDown_StillGetsATurnThatCarriesOneToTheOther()
        {
            Quaternion turn = CordTurn.FromTo(Vector3.up, Vector3.down);
            Assert.That(Vector3.Distance(turn * Vector3.up, Vector3.down), Is.LessThan(0.001f));
        }
    }
}

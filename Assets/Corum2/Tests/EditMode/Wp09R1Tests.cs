using Corum2.Core;
using Corum2.Unity;
using NUnit.Framework;
using UnityEngine;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-09-R1 regression for WP09-M01: the rendered enemy depth check classifies each sample by the rendered root's own
    /// movement, so an AI walking on the ground through MoveVelX (VelX = 0) counts as ground-moving, not as still.</summary>
    public class Wp09R1Tests
    {
        static (SimHarness h, EnemyActor e) Chaser(float moveSpeed)
        {
            var ch = Hitter("ch");
            ch.Ai = new EnemyAIProfile { Type = AiType.Chaser, MoveSpeed = moveSpeed, TrackRange = 15 };
            var h = Harness("WP09-R1", World(("enemy", "ch", 16f)), Defs(ch));
            return (h, h.C.Find("enemy"));
        }

        [Test]
        public void M01_GroundChaser_WithZeroVelX_IsCountedAsGroundMoving()
        {
            var root = new GameObject("Wp09R1Root");
            try
            {
                var (h, e) = Chaser(3f);
                var view = new CombatView(root.transform, new WorldView(root.transform), new EnemyDefinitionAsset[0]);
                view.Sync(h.Sim.Combat, 0f); // first sample: no previous frame
                var x0 = e.Position.X;
                h.Step();
                Assert.AreNotEqual(x0, e.Position.X, "the chaser walks");
                Assert.IsTrue(e.Grounded);
                Assert.AreEqual(0f, e.VelX, "walking uses MoveVelX, not VelX (the WP-09 predicate missed this)");
                Assert.Less(e.MoveVelX, -0.01f);
                view.Sync(h.Sim.Combat, 0f);
                view.Sync(h.Sim.Combat, 0f); // a second frame on the same tick: no movement since the previous frame
                Assert.AreEqual(3, view.EnemySamples);
                Assert.AreEqual(1, view.FirstSamples);
                Assert.AreEqual(1, view.GroundMovingSamples);
                Assert.AreEqual(1, view.GroundStillSamples);
                Assert.AreEqual(0, view.AirborneSamples);
                Assert.AreEqual(0, view.TeleportSamples);
                Assert.AreEqual(2, view.PropelledSamples, "MoveVelX is set on both frames after the step");
                Assert.AreEqual(view.EnemySamples, view.FirstSamples + view.AirborneSamples + view.TeleportSamples + view.GroundMovingSamples + view.GroundStillSamples);
                Assert.AreEqual(0f, view.MaxZGroundMoving);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void M01_Control_StandingEnemy_IsGroundStill_AndPlacementIsTeleport()
        {
            var root = new GameObject("Wp09R1Root");
            try
            {
                var (h, e) = Chaser(0f);
                var view = new CombatView(root.transform, new WorldView(root.transform), new EnemyDefinitionAsset[0]);
                view.Sync(h.Sim.Combat, 0f);
                h.Step();
                view.Sync(h.Sim.Combat, 0f);
                Assert.AreEqual(1, view.GroundStillSamples, "MoveSpeed 0: no rendered movement");
                Assert.AreEqual(0, view.GroundMovingSamples);
                e.Position = new Vec2(e.Position.X + 5f, e.Position.Y); // test placement
                view.Sync(h.Sim.Combat, 0f);
                Assert.AreEqual(1, view.TeleportSamples, "a jump of >= 1 m between frames is placement, not self-movement");
                Assert.AreEqual(0, view.GroundMovingSamples);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

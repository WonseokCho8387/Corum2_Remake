using Corum2.Core;
using Corum2.Unity;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>WP-11 TC-UNITY-02: the view diagnostic that compares the drawn body with the judged body and the map solids.</summary>
    public class Wp11Tests
    {
        [Test]
        public void Penetration_TouchingIsZero_OverlapIsTheSmallerAxis()
        {
            var wall = new Aabb(5f, 0f, 6f, 5f);
            Assert.AreEqual(0f, ViewTrack.Penetration(Aabb.FromFeet(new Vec2(4.7f, 0f), 0.6f, 1.6f), wall), 1e-6f, "right edge exactly at the wall");
            Assert.AreEqual(0f, ViewTrack.Penetration(Aabb.FromFeet(new Vec2(2f, 0f), 0.6f, 1.6f), new Aabb(-1f, -2f, 10f, 0f)), 1e-6f, "standing on the ground");
            Assert.AreEqual(0.1f, ViewTrack.Penetration(Aabb.FromFeet(new Vec2(4.8f, 0f), 0.6f, 1.6f), wall), 1e-5f, "0.1 m into the wall");
            Assert.AreEqual(0.05f, ViewTrack.Penetration(Aabb.FromFeet(new Vec2(4.8f, 4.95f), 0.6f, 1.6f), wall), 1e-5f, "smaller of x 0.1 / y 0.05");
        }

        [Test]
        public void OutsideSegment_IsZeroBetweenTheTwoTicks_AndTheDistanceOutside()
        {
            var a = new Vec2(0f, 0f);
            var b = new Vec2(1f, 0f);
            Assert.AreEqual(0f, ViewTrack.OutsideSegment(new Vec2(0.4f, 0f), a, b), 1e-6f);
            Assert.AreEqual(0.5f, ViewTrack.OutsideSegment(new Vec2(1.5f, 0f), a, b), 1e-6f, "past the current tick");
            Assert.AreEqual(0.2f, ViewTrack.OutsideSegment(new Vec2(0.5f, 0.2f), a, b), 1e-6f, "off the line");
            Assert.AreEqual(0.3f, ViewTrack.OutsideSegment(new Vec2(0.3f, 0f), a, a), 1e-6f, "no movement: distance to the point");
        }

        [Test]
        public void Table_AddsPerClassMaxima()
        {
            var t = new ViewTrack();
            t.Add("dive", 0f, 0f, 0f, 0f, 0.28f);
            t.Add("dive", 0.01f, 0f, 0f, 0.002f, 0.1f);
            t.Add("air", 0f, 0f, 0f, 0f, 0.2f);
            Assert.AreEqual("air:1/0.0000/0.0000/0.0000/0.0000/0.2000;dive:2/0.0100/0.0000/0.0000/0.0020/0.2800", t.Table());
        }

        [Test]
        public void PlayerClass_SplitsHitStunByKnockbackVelocity()
        {
            Assert.AreEqual("knockback", ViewTrack.PlayerClass(MotorState.HitStun, -1.5f));
            Assert.AreEqual("hitStun", ViewTrack.PlayerClass(MotorState.HitStun, 0f));
            Assert.AreEqual("airDash", ViewTrack.PlayerClass(MotorState.AirDash, 12f));
            Assert.AreEqual("walkRun", ViewTrack.PlayerClass(MotorState.Run, 7f));
        }
    }
}

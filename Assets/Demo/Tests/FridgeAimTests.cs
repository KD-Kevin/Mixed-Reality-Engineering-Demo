using NUnit.Framework;
using UnityEngine;

namespace EngineeringDemo.Tests
{
    public sealed class FridgeAimTests
    {
        private static readonly Rect Fridge = new Rect(0.4f, 0.3f, 0.2f, 0.3f);

        [Test]
        public void AimPointStaysInsideTheVisibleSegment()
        {
            var origin = new Vector2(0.2f, 0.45f);
            Assert.That(FridgeAim.TryAimAtFridge(Fridge, origin, Vector2.right, 0.25f, out var point), Is.True);
            Assert.That(point.x, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(point.y, Is.EqualTo(origin.y).Within(0.0001f));
        }

        [Test]
        public void AimRejectsBoxesBeyondTheRayOrBehindIt()
        {
            var origin = new Vector2(0.2f, 0.45f);
            Assert.That(FridgeAim.TryAimAtFridge(Fridge, origin, Vector2.right, 0.1f, out _), Is.False);
            Assert.That(FridgeAim.TryAimAtFridge(Fridge, origin, Vector2.left, 1f, out _), Is.False);
        }

        [Test]
        public void StationaryAimOnlyHitsInsideTheBox()
        {
            Assert.That(FridgeAim.TryAimAtFridge(Fridge, Fridge.center, Vector2.zero, 0f, out var point), Is.True);
            Assert.That(point, Is.EqualTo(Fridge.center));
            Assert.That(FridgeAim.TryAimAtFridge(Fridge, Vector2.zero, Vector2.zero, 0f, out _), Is.False);
        }

        [Test]
        public void RayToImageEdgeHandlesAxisAlignedAndStationaryAim()
        {
            var origin = new Vector2(0.25f, 0.75f);
            Assert.That(FridgeAim.DistanceToImageEdge(origin, Vector2.right), Is.EqualTo(0.75f));
            Assert.That(FridgeAim.DistanceToImageEdge(origin, Vector2.down), Is.EqualTo(0.75f));
            Assert.That(FridgeAim.DistanceToImageEdge(origin, Vector2.zero), Is.Zero);
        }
    }
}

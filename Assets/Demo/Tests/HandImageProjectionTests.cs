using NUnit.Framework;
using UnityEngine;

namespace EngineeringDemo.Tests
{
    public sealed class HandImageProjectionTests
    {
        [TestCase(0)]
        [TestCase(90)]
        [TestCase(180)]
        [TestCase(270)]
        [TestCase(-90)]
        public void ImageToViewportCanBeInvertedInAnAspectFittedView(int rotation)
        {
            var viewport = HandImageProjection.GetImageViewport(4f / 3f, 16f / 9f, rotation);
            var point = new Vector2(0.18f, 0.76f);
            var screenPoint = HandImageProjection.ImageToViewport(point, viewport, rotation);
            var recovered = HandImageProjection.ViewportToImage(screenPoint, viewport, rotation);
            Assert.That(recovered.x, Is.EqualTo(point.x).Within(0.0001f));
            Assert.That(recovered.y, Is.EqualTo(point.y).Within(0.0001f));
        }

        [Test]
        public void QuarterTurnSwapsPhysicalPreviewDimensionsAndPreservesItsCenter()
        {
            const float cameraAspect = 16f / 9f;
            var viewport = HandImageProjection.GetImageViewport(4f / 3f, cameraAspect, 90);
            var preview = HandImageProjection.GetPreviewRect(viewport, cameraAspect, 90);
            Assert.That(viewport.width, Is.EqualTo(0.75f / cameraAspect).Within(0.0001f));
            Assert.That(viewport.height, Is.EqualTo(1f));
            Assert.That(preview.center.x, Is.EqualTo(viewport.center.x).Within(0.0001f));
            Assert.That(preview.center.y, Is.EqualTo(viewport.center.y).Within(0.0001f));
            Assert.That(preview.width * cameraAspect, Is.EqualTo(viewport.height).Within(0.0001f));
            Assert.That(preview.height, Is.EqualTo(viewport.width * cameraAspect).Within(0.0001f));
        }

        [Test]
        public void QuarterTurnKeepsTheDetectionBoxExtentsOrdered()
        {
            var box = new Rect(0.1f, 0.2f, 0.3f, 0.4f);
            var rotated = HandImageProjection.GetDisplayBox(box, 90);
            Assert.That(rotated.xMin, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(rotated.yMin, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(rotated.width, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(rotated.height, Is.EqualTo(0.3f).Within(0.0001f));
        }
    }
}

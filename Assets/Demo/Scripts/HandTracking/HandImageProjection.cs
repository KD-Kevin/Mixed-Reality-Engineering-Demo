using UnityEngine;

namespace EngineeringDemo
{
    internal static class HandImageProjection
    {
        internal static Rect GetImageViewport(float imageAspect, float cameraAspect, int rotation)
        {
            float aspect = rotation % 180 == 0 ? imageAspect : 1f / imageAspect;
            float width = Mathf.Min(1f, aspect / cameraAspect);
            float height = Mathf.Min(1f, cameraAspect / aspect);
            return new Rect((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
        }

        internal static Rect GetPreviewRect(Rect imageViewport, float cameraAspect, int rotation)
        {
            if (rotation % 180 == 0)
                return imageViewport;

            // Rotate physical pixels around the center of the UI rect.
            var size = new Vector2(imageViewport.height / cameraAspect, imageViewport.width * cameraAspect);
            return new Rect(imageViewport.center - size * 0.5f, size);
        }

        // MediaPipe's image Y points down while Unity's viewport Y points up.
        internal static Vector2 ImageToViewport(Vector2 point, Rect viewport, int rotation)
        {
            var displayPoint = RotateImagePoint(point, rotation);
            return new Vector2(viewport.xMin + displayPoint.x * viewport.width, viewport.yMax - displayPoint.y * viewport.height);
        }

        internal static Vector2 ViewportToImage(Vector2 point, Rect viewport, int rotation)
        {
            var displayPoint = new Vector2((point.x - viewport.xMin) / viewport.width, (viewport.yMax - point.y) / viewport.height);
            return RotateImagePoint(displayPoint, -rotation);
        }

        internal static Rect GetDisplayBox(Rect box, int rotation)
        {
            var first = RotateImagePoint(box.min, rotation);
            var second = RotateImagePoint(box.max, rotation);
            var min = Vector2.Min(first, second);
            return new Rect(min, Vector2.Max(first, second) - min);
        }

        private static Vector2 RotateImagePoint(Vector2 point, int clockwiseDegrees)
        {
            return ((clockwiseDegrees % 360 + 360) % 360) switch
            {
                90 => new Vector2(1f - point.y, point.x),
                180 => Vector2.one - point,
                270 => new Vector2(point.y, 1f - point.x),
                _ => point
            };
        }
    }
}

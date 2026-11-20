using UnityEngine;

namespace EngineeringDemo
{
    internal static class FridgeAim
    {
        internal static float DistanceToImageEdge(Vector2 origin, Vector2 direction)
        {
            if (direction == Vector2.zero)
                return 0f;

            var edgeX = direction.x >= 0 ? 1f : 0f;
            var edgeY = direction.y >= 0 ? 1f : 0f;
            var distanceX = Mathf.Abs(direction.x) > 0.0001f ? (edgeX - origin.x) / direction.x : float.PositiveInfinity;
            var distanceY = Mathf.Abs(direction.y) > 0.0001f ? (edgeY - origin.y) / direction.y : float.PositiveInfinity;
            return Mathf.Clamp(Mathf.Min(distanceX, distanceY), 0f, 1.42f);
        }

        internal static bool TryAimAtFridge(Rect fridge, Vector2 origin, Vector2 direction, float maxDistance, out Vector2 aimPoint)
        {
            aimPoint = origin;
            if (direction == Vector2.zero)
                return fridge.Contains(origin);

            float enter = 0f;
            float exit = maxDistance;
            if (!ClipAxis(origin.x, direction.x, fridge.xMin, fridge.xMax, ref enter, ref exit) ||
                !ClipAxis(origin.y, direction.y, fridge.yMin, fridge.yMax, ref enter, ref exit))
                return false;

            // Project the fridge center onto the portion of the aim ray inside the box.
            float centerDistance = Vector2.Dot(fridge.center - origin, direction);
            aimPoint = origin + direction * Mathf.Clamp(centerDistance, enter, exit);
            return true;
        }

        private static bool ClipAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
        {
            if (direction == 0f)
                return origin >= min && origin <= max;

            float first = (min - origin) / direction;
            float second = (max - origin) / direction;
            enter = Mathf.Max(enter, Mathf.Min(first, second));
            exit = Mathf.Min(exit, Mathf.Max(first, second));
            return enter <= exit;
        }
    }
}

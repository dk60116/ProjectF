using UnityEngine;

namespace ProjectF.Conveyors
{
    internal static class ConveyorBelt2FPath
    {
        public static Vector3 ConformItemPosition(
            Vector3 localPosition,
            bool pathUsesLocalX,
            float pathHalfLength,
            float pathHighHalfLength,
            float pathLowHeight,
            float pathHighHeight)
        {
            float pathCoordinate = pathUsesLocalX ? localPosition.x : localPosition.z;
            localPosition.y = ResolveItemHeight(
                pathCoordinate,
                pathHalfLength,
                pathHighHalfLength,
                pathLowHeight,
                pathHighHeight);
            return localPosition;
        }

        public static float ResolveItemHeight(
            float pathCoordinate,
            float pathHalfLength,
            float pathHighHalfLength,
            float pathLowHeight,
            float pathHighHeight)
        {
            float absoluteCoordinate = Mathf.Abs(pathCoordinate);
            if (absoluteCoordinate <= pathHighHalfLength)
            {
                return pathHighHeight;
            }

            float slope01 = Mathf.InverseLerp(
                pathHighHalfLength,
                Mathf.Max(pathHalfLength, pathHighHalfLength + 0.0001f),
                absoluteCoordinate);
            return Mathf.Lerp(pathHighHeight, pathLowHeight, slope01);
        }
    }
}

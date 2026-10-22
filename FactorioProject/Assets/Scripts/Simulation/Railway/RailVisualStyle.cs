using System;
using UnityEngine;

namespace ProjectF.Railway
{
    // Presentation settings never change the rail path used by trains or saves.
    internal readonly struct RailVisualStyle : IEquatable<RailVisualStyle>
    {
        internal readonly float Width, HalfSpacing, Thickness;
        internal readonly float SleeperLength, SleeperWidth, SleeperSpacing, SleeperThickness;
        internal float BallastHalfWidth => Mathf.Max(SleeperLength * .5f, Mathf.Abs(HalfSpacing) + Width) + .22f;

        internal RailVisualStyle(float width, float halfSpacing, float thickness,
            float sleeperLength, float sleeperWidth, float sleeperSpacing, float sleeperThickness)
        {
            Width = width; HalfSpacing = halfSpacing; Thickness = thickness;
            SleeperLength = sleeperLength; SleeperWidth = sleeperWidth;
            SleeperSpacing = sleeperSpacing; SleeperThickness = sleeperThickness;
        }

        public bool Equals(RailVisualStyle other) => Width == other.Width && HalfSpacing == other.HalfSpacing
            && Thickness == other.Thickness && SleeperLength == other.SleeperLength
            && SleeperWidth == other.SleeperWidth && SleeperSpacing == other.SleeperSpacing
            && SleeperThickness == other.SleeperThickness;
        public override bool Equals(object obj) => obj is RailVisualStyle other && Equals(other);
        public override int GetHashCode() => Width.GetHashCode() ^ HalfSpacing.GetHashCode() ^ SleeperSpacing.GetHashCode();
    }

    internal readonly struct RailSleeperLayout
    {
        internal readonly int Count;
        internal readonly float Start, Step;
        private RailSleeperLayout(int count, float start, float step) { Count = count; Start = start; Step = step; }

        internal static RailSleeperLayout Create(float length, float spacing, bool closed,
            float startClearance = 0f, float endClearance = 0f)
        {
            if (length <= 0.001f) return default;
            spacing = Mathf.Max(0.1f, spacing);
            if (closed)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(length / spacing));
                float step = length / count;
                return new RailSleeperLayout(count, step * 0.5f, step);
            }
            startClearance = Mathf.Max(spacing * 0.5f, startClearance);
            endClearance = Mathf.Max(spacing * 0.5f, endClearance);
            float usable = length - startClearance - endClearance;
            if (usable < 0f)
                return startClearance <= spacing * 0.5f && endClearance <= spacing * 0.5f
                    ? new RailSleeperLayout(1, length * 0.5f, 0f) : default;
            int n = Mathf.FloorToInt((usable + 0.0001f) / spacing) + 1;
            return new RailSleeperLayout(n, startClearance + (usable - (n - 1) * spacing) * 0.5f, spacing);
        }
    }
}

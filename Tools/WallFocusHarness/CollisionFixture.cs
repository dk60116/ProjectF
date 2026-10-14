using System;
using System.Collections.Generic;

namespace WallCollisionHarness
{
    // Only math boundaries are substituted; box emission comes from BuildingWorld.cs.
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Matrix4x4
    {
        public float Angle;
        public Vector3 Scale, Position;
        public float determinant => Scale.x * Scale.y * Scale.z;
        public Vector3 MultiplyPoint3x4(Vector3 p)
        {
            float c = (float)Math.Cos(Angle), s = (float)Math.Sin(Angle);
            return Position + new Vector3(c*p.x*Scale.x + s*p.z*Scale.z, p.y*Scale.y, -s*p.x*Scale.x + c*p.z*Scale.z);
        }
    }
    public static class GeometryChecks
    {
        // PRODUCTION_TRIANGLES
        // PRODUCTION_APPEND_BOX

        private static void Require(bool value, string message, ref int checks)
        { checks++; if (!value) throw new Exception(message); }

        public static int Run()
        {
            int checks = 0;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int mirror = 0; mirror < 4; mirror++)
            for (int turn = 0; turn < 4; turn++)
            {
                var matrix = new Matrix4x4
                {
                    Angle = turn * (float)Math.PI / 2f,
                    Scale = new Vector3(mirror == 1 ? -2 : 2, mirror == 2 ? -1 : 1, mirror == 3 ? -3 : 3),
                    Position = new Vector3(10 + turn, 2, -8)
                };
                var center = new Vector3(0.25f, 0.5f, 0.1f);
                var size = new Vector3(1, 1, 0.2f);
                int start = vertices.Count, triangleStart = triangles.Count;
                AppendBox(matrix, center, size, vertices, triangles);
                Require(vertices.Count == start + 8, "Eight box vertices", ref checks);
                Require(triangles.Count == triangleStart + 36, "Twelve box faces", ref checks);
                Vector3 worldCenter = matrix.MultiplyPoint3x4(center);
                for (int i = triangleStart; i < triangles.Count; i += 3)
                {
                    int ia = triangles[i], ib = triangles[i+1], ic = triangles[i+2];
                    Require(ia >= start && ia < start + 8 && ib >= start && ib < start + 8 && ic >= start && ic < start + 8,
                        "Indices stay within appended box", ref checks);
                    Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    Vector3 normal = Vector3.Cross(b-a, c-a);
                    Vector3 offset = (a+b+c) * (1f/3f) - worldCenter;
                    Require(Vector3.Dot(normal, offset) > 0f,
                        "Collision face must point outward: mirror=" + mirror + " turn=" + turn + " triangle=" + i, ref checks);
                }
            }
            return checks;
        }
    }
}

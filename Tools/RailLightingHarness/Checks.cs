using System;
using System.Collections.Generic;
using UnityEngine;

static class Checks
{
    static int passed;

    static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }

        passed++;
    }

    static void Main()
    {
        ValidateSleeper(Vector3.forward, "vertical");
        ValidateSleeper(Vector3.right, "horizontal");

        Vector3 nightLight = new Vector3(0.37f, 0.81f, 0.45f).normalized;
        float verticalLight = GetTopLight(Vector3.forward, nightLight);
        float horizontalLight = GetTopLight(Vector3.right, nightLight);
        Check(MathF.Abs(verticalLight - horizontalLight) < 0.00001f,
            "A 90-degree rail rotation must not change top-surface lighting");

        Console.WriteLine($"Rail lighting harness passed: {passed} checks");
    }

    static void ValidateSleeper(Vector3 tangent, string label)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Railload.BuildSleeper(vertices, triangles, new Vector3(2f, 0.5f, -3f), tangent);

        Check(vertices.Count == 24, $"{label}: every box face must own four vertices");
        Check(triangles.Count == 36, $"{label}: sleeper must contain twelve triangles");

        Vector3[] normals = RecalculateNormals(vertices, triangles);
        for (int face = 0; face < 6; face++)
        {
            Vector3 expected = normals[face * 4];
            Check(expected.sqrMagnitude > 0.99f, $"{label}: face {face} normal must be valid");
            for (int corner = 1; corner < 4; corner++)
            {
                Check(Vector3.Dot(expected, normals[face * 4 + corner]) > 0.9999f,
                    $"{label}: face {face} must remain flat shaded");
            }
        }

        for (int corner = 0; corner < 4; corner++)
        {
            Check(Vector3.Dot(normals[corner], Vector3.up) > 0.9999f,
                $"{label}: top normal must point straight up");
            Check(Vector3.Dot(normals[4 + corner], Vector3.down) > 0.9999f,
                $"{label}: bottom normal must point straight down");
        }

        Vector3 center = new Vector3(2f, 0.5f, -3f);
        for (int face = 2; face < 6; face++)
        {
            int start = face * 4;
            Vector3 faceCenter = (vertices[start] + vertices[start + 1] + vertices[start + 2] + vertices[start + 3]) / 4f;
            Vector3 outward = faceCenter - center;
            outward.y = 0f;
            Check(Vector3.Dot(normals[start], outward.normalized) > 0.9999f,
                $"{label}: side face {face} normal must point outward");
        }
    }

    static float GetTopLight(Vector3 tangent, Vector3 lightDirection)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Railload.BuildSleeper(vertices, triangles, Vector3.zero, tangent);
        Vector3[] normals = RecalculateNormals(vertices, triangles);
        float total = 0f;
        for (int i = 0; i < 4; i++)
        {
            total += MathF.Max(0f, Vector3.Dot(normals[i], lightDirection));
        }

        return total * 0.25f;
    }

    static Vector3[] RecalculateNormals(List<Vector3> vertices, List<int> triangles)
    {
        var normals = new Vector3[vertices.Count];
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].normalized;
        }

        return normals;
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => sqrMagnitude > 0.0000001f ? this / magnitude : zero;

        public void Normalize()
        {
            this = normalized;
        }

        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) => new Vector3(a.x / b, a.y / b, a.z / b);
    }
}

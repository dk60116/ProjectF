using System;
using UnityEditor;
using UnityEngine;

internal static class MeshSymmetryValidation
{
    private const float Epsilon = 0.0001f;

    [MenuItem("Tools/MapObject/Validate Mesh Transform Symmetry")]
    public static void Run()
    {
        ValidateOppositeSideReplacementAndSeamWelding();
        ValidateDualAxisReplication();
        ValidateTripleAxisReplication();
        ValidateCrossingTriangleClipping();
        ValidateUnreadableMeshCopy();
        Debug.Log("Mesh Transform Symmetry validation passed: side replacement, seam welding, dual/triple-axis replication, plane clipping, and unreadable mesh copy.");
    }

    public static void RunFromCommandLine()
    {
        try
        {
            Run();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateOppositeSideReplacementAndSeamWelding()
    {
        Mesh mesh = CreateMesh(
            new[]
            {
                new Vector3(-2f, -1f, 0f),
                new Vector3(0f, -1f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(-2f, 1f, 0f),
                new Vector3(5f, -0.5f, 0f),
                new Vector3(6f, 0f, 0f),
                new Vector3(5f, 0.5f, 0f)
            },
            new[]
            {
                0, 1, 2,
                0, 2, 3,
                4, 5, 6
            });

        try
        {
            ApplyOrThrow(mesh, new MeshSymmetrySettings(
                true,
                MeshSymmetrySide.Negative,
                false,
                MeshSymmetrySide.Positive,
                false,
                MeshSymmetrySide.Negative,
                Epsilon));

            AssertApproximately(mesh.bounds.min.x, -2f, "X min");
            AssertApproximately(mesh.bounds.max.x, 2f, "X max");
            Assert(mesh.GetTriangles(0).Length == 12, "반대편의 기존 삼각형이 제거되지 않았거나 복제 수가 올바르지 않습니다.");
            Assert(mesh.uv.Length == mesh.vertexCount, "UV 채널이 버텍스 수와 함께 보존되지 않았습니다.");

            int seamVertexCount = 0;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Mathf.Abs(vertices[i].x) <= Epsilon)
                {
                    seamVertexCount++;
                }
            }

            Assert(seamVertexCount == 2, $"중앙선 버텍스가 용접되지 않았습니다. seam vertices={seamVertexCount}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void ValidateDualAxisReplication()
    {
        Mesh mesh = CreateMesh(
            new[]
            {
                new Vector3(-2f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 3f, 0f),
                new Vector3(-2f, 3f, 0f)
            },
            new[]
            {
                0, 1, 2,
                0, 2, 3
            });

        try
        {
            ApplyOrThrow(mesh, new MeshSymmetrySettings(
                true,
                MeshSymmetrySide.Negative,
                true,
                MeshSymmetrySide.Positive,
                false,
                MeshSymmetrySide.Negative,
                Epsilon));

            AssertApproximately(mesh.bounds.min.x, -2f, "dual X min");
            AssertApproximately(mesh.bounds.max.x, 2f, "dual X max");
            AssertApproximately(mesh.bounds.min.y, -3f, "dual Y min");
            AssertApproximately(mesh.bounds.max.y, 3f, "dual Y max");
            Assert(mesh.GetTriangles(0).Length == 24, "두 축 대칭이 네 사분면을 만들지 못했습니다.");
            Assert(mesh.vertexCount == 9, $"두 축 중앙선 용접 결과가 올바르지 않습니다. vertices={mesh.vertexCount}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void ValidateTripleAxisReplication()
    {
        Mesh mesh = CreateMesh(
            new[]
            {
                new Vector3(-2f, 0f, 0f),
                Vector3.zero,
                new Vector3(0f, 3f, -4f)
            },
            new[] { 0, 1, 2 });

        try
        {
            ApplyOrThrow(mesh, new MeshSymmetrySettings(
                true,
                MeshSymmetrySide.Negative,
                true,
                MeshSymmetrySide.Positive,
                true,
                MeshSymmetrySide.Negative,
                Epsilon));

            AssertApproximately(mesh.bounds.min.x, -2f, "triple X min");
            AssertApproximately(mesh.bounds.max.x, 2f, "triple X max");
            AssertApproximately(mesh.bounds.min.y, -3f, "triple Y min");
            AssertApproximately(mesh.bounds.max.y, 3f, "triple Y max");
            AssertApproximately(mesh.bounds.min.z, -4f, "triple Z min");
            AssertApproximately(mesh.bounds.max.z, 4f, "triple Z max");
            Assert(mesh.GetTriangles(0).Length == 24, "세 축 대칭이 여덟 영역을 만들지 못했습니다.");
            Assert(mesh.vertexCount == 7, $"세 축 중앙선 용접 결과가 올바르지 않습니다. vertices={mesh.vertexCount}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void ValidateCrossingTriangleClipping()
    {
        Mesh mesh = CreateMesh(
            new[]
            {
                new Vector3(-2f, -1f, 0f),
                new Vector3(3f, -1f, 0f),
                new Vector3(-1f, 2f, 0f)
            },
            new[] { 0, 1, 2 });

        try
        {
            ApplyOrThrow(mesh, new MeshSymmetrySettings(
                true,
                MeshSymmetrySide.Negative,
                false,
                MeshSymmetrySide.Positive,
                false,
                MeshSymmetrySide.Negative,
                Epsilon));

            AssertApproximately(mesh.bounds.min.x, -2f, "clipped X min");
            AssertApproximately(mesh.bounds.max.x, 2f, "clipped X max");
            Assert(mesh.GetTriangles(0).Length == 12, "중앙선을 가로지르는 삼각형이 평면에서 분할되지 않았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void ValidateUnreadableMeshCopy()
    {
        Mesh source = CreateMesh(
            new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(0f, 1f, 0f)
            },
            new[] { 0, 1, 2 });
        Mesh editableCopy = null;

        try
        {
            source.UploadMeshData(true);
            Assert(!source.isReadable, "검증용 Mesh의 읽기 비활성화에 실패했습니다.");

            editableCopy = MeshSymmetryUtility.CreateEditableCopy(
                source,
                "UnreadableMeshCopyValidation",
                HideFlags.HideAndDontSave,
                out string error);
            Assert(editableCopy != null, error);
            Assert(editableCopy.isReadable, "읽기 비활성 Mesh의 편집 가능한 복사본이 생성되지 않았습니다.");
            Assert(editableCopy.vertexCount == 3, "읽기 비활성 Mesh 복사 과정에서 버텍스가 보존되지 않았습니다.");
            Assert(editableCopy.GetTriangles(0).Length == 3, "읽기 비활성 Mesh 복사 과정에서 인덱스가 보존되지 않았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(editableCopy);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static Mesh CreateMesh(Vector3[] vertices, int[] triangles)
    {
        Vector2[] uv = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            uv[i] = new Vector2(vertices[i].x, vertices[i].y);
        }

        Mesh mesh = new Mesh
        {
            name = "MeshSymmetryValidationMesh",
            vertices = vertices,
            uv = uv,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ApplyOrThrow(Mesh mesh, MeshSymmetrySettings settings)
    {
        if (!MeshSymmetryUtility.TryApply(mesh, Matrix4x4.identity, Vector3.zero, settings, out string error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertApproximately(float actual, float expected, string label)
    {
        if (!Mathf.Approximately(actual, expected) && Mathf.Abs(actual - expected) > Epsilon)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
        }
    }
}

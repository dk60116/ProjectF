using UnityEditor;
using UnityEngine;

public static class PackageMeshTools
{
    private const string PackageMeshPath = "Assets/MapObject/Package.mesh";

    [MenuItem("Tools/MapObject/Rotate Package Mesh (-90, 0, 0)")]
    public static void RotatePackageMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PackageMeshPath);
        if (mesh == null)
        {
            Debug.LogError($"PackageMeshTools: Mesh not found at '{PackageMeshPath}'.");
            return;
        }

        Undo.RegisterCompleteObjectUndo(mesh, "Rotate Package Mesh");

        Quaternion rotation = Quaternion.Euler(-90f, 0f, 0f);
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = rotation * vertices[i];
        }

        mesh.vertices = vertices;

        Vector3[] normals = mesh.normals;
        if (normals != null && normals.Length == vertices.Length)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = rotation * normals[i];
            }

            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }

        Vector4[] tangents = mesh.tangents;
        if (tangents != null && tangents.Length == vertices.Length)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                tangent = rotation * tangent;
                tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
            }

            mesh.tangents = tangents;
        }

        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectF.EditorTools
{
    [InitializeOnLoad]
    internal static class CdPortableMeshGenerator
    {
        internal const string CdPortableMeshPath = BookItemAssetGenerator.CdPortableMeshPath;

        private static readonly Vector2 SideUv = new Vector2(0.01f, 0.01f);

        static CdPortableMeshGenerator()
        {
            EditorApplication.delayCall += EnsureAfterDomainReload;
        }

        private static void EnsureAfterDomainReload()
        {
            EnsureAssetAndBindings();
        }

        [MenuItem("Tools/ProjectF/Items/Fix CD Portable Mesh UV")]
        private static void FixFromMenu()
        {
            Mesh mesh = EnsureAssetAndBindings();
            if (mesh == null)
            {
                Debug.LogError($"CD Portable Mesh를 찾을 수 없습니다: {CdPortableMeshPath}");
                return;
            }

            Selection.activeObject = mesh;
            EditorGUIUtility.PingObject(mesh);
        }

        internal static Mesh EnsureAssetAndBindings()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(CdPortableMeshPath);
            if (mesh == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return mesh;
            }

            Vector3[] normals = mesh.normals;
            var uvs = new List<Vector2>(mesh.vertexCount);
            mesh.GetUVs(0, uvs);

            if (normals.Length != mesh.vertexCount || uvs.Count != mesh.vertexCount)
            {
                Debug.LogError($"CD Portable Mesh의 노멀 또는 UV 데이터가 올바르지 않습니다: {CdPortableMeshPath}");
                return mesh;
            }

            bool changed = false;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                // 원기둥 옆면은 수평 노멀을 사용한다. 회색 전용 텍스처 모서리를 한 점으로 샘플링한다.
                if (Mathf.Abs(normals[i].y) >= 0.5f || uvs[i] == SideUv)
                {
                    continue;
                }

                uvs[i] = SideUv;
                changed = true;
            }

            if (changed)
            {
                mesh.SetUVs(0, uvs);
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }

            return mesh;
        }
    }
}

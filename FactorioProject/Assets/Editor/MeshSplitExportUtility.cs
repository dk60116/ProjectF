using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

namespace ProjectF.EditorTools.MeshSplit
{
    internal enum MeshSplitExportFormat
    {
        Fbx,
        Obj
    }

    internal static class MeshSplitExportUtility
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public static string GetExtension(MeshSplitExportFormat format)
        {
            return format == MeshSplitExportFormat.Fbx ? ".fbx" : ".obj";
        }

        public static string GetAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        public static void ExportFbx(string absolutePath, IReadOnlyList<MeshSplitOutput> outputs, string rootName)
        {
            GameObject root = CreateExportRoot(outputs, rootName);
            try
            {
                string exportedPath = ModelExporter.ExportObject(absolutePath, root);
                if (string.IsNullOrWhiteSpace(exportedPath)
                    || (!File.Exists(exportedPath) && !File.Exists(absolutePath)))
                {
                    throw new InvalidOperationException("FBX 내보내기에 실패했습니다.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void ExportObj(
            string absolutePath,
            IReadOnlyList<MeshSplitOutput> outputs,
            string materialLibraryFileName = null)
        {
            if (outputs == null || outputs.Count == 0)
            {
                throw new InvalidOperationException("OBJ로 내보낼 Mesh가 없습니다.");
            }

            string directory = Path.GetDirectoryName(absolutePath) ?? string.Empty;
            string materialFileName = string.IsNullOrWhiteSpace(materialLibraryFileName)
                ? Path.GetFileNameWithoutExtension(absolutePath) + ".mtl"
                : Path.GetFileName(materialLibraryFileName);
            string materialPath = Path.Combine(directory, materialFileName);
            BuildMaterialTable(
                outputs,
                out Dictionary<Material, string> materialNames,
                out string nullMaterialName,
                out List<MaterialRecord> records);
            string objText = BuildObjText(outputs, materialFileName, materialNames, nullMaterialName);
            string materialText = BuildMaterialText(records);

            try
            {
                File.WriteAllText(materialPath, materialText, Utf8WithoutBom);
                File.WriteAllText(absolutePath, objText, Utf8WithoutBom);
            }
            catch
            {
                TryDeleteFile(absolutePath);
                TryDeleteFile(materialPath);
                throw;
            }
        }

        public static string GetObjMaterialPath(string objPath)
        {
            return Path.ChangeExtension(objPath, ".mtl");
        }

        public static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static void TryDeleteEmptyDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                if (Directory.GetFileSystemEntries(path).Length == 0)
                {
                    Directory.Delete(path, false);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static GameObject CreateExportRoot(IReadOnlyList<MeshSplitOutput> outputs, string rootName)
        {
            if (outputs == null || outputs.Count == 0)
            {
                throw new InvalidOperationException("FBX로 내보낼 Mesh가 없습니다.");
            }

            GameObject root = new GameObject(rootName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                MeshSplitOutput output = outputs[outputIndex];
                Color32 color = output.GroupColor;
                string childName = $"Group_{outputIndex + 1:00}_{color.r:X2}{color.g:X2}{color.b:X2}";
                GameObject child = new GameObject(childName);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = output.Mesh;
                child.AddComponent<MeshRenderer>().sharedMaterials = output.Materials;
            }

            return root;
        }

        private static string BuildObjText(
            IReadOnlyList<MeshSplitOutput> outputs,
            string materialFileName,
            Dictionary<Material, string> materialNames,
            string nullMaterialName)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Exported by ProjectF Mesh Split");
            builder.Append("mtllib ").AppendLine(materialFileName);

            int vertexOffset = 0;
            int uvOffset = 0;
            int normalOffset = 0;
            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                MeshSplitOutput output = outputs[outputIndex];
                Mesh mesh = output.Mesh;
                if (mesh == null)
                {
                    throw new InvalidOperationException($"OBJ Group {outputIndex + 1}의 Mesh가 없습니다.");
                }

                string objectName = MakeObjIdentifier(mesh.name);
                builder.Append("o ").AppendLine(objectName);

                Vector3[] vertices = mesh.vertices;
                Vector2[] uv = mesh.uv;
                Vector3[] normals = mesh.normals;
                bool hasUv = uv != null && uv.Length == vertices.Length;
                bool hasNormals = normals != null && normals.Length == vertices.Length;

                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    Vector3 vertex = vertices[vertexIndex];
                    builder.Append("v ");
                    AppendFloat(builder, vertex.x).Append(' ');
                    AppendFloat(builder, vertex.y).Append(' ');
                    AppendFloat(builder, vertex.z).AppendLine();
                }

                if (hasUv)
                {
                    for (int uvIndex = 0; uvIndex < uv.Length; uvIndex++)
                    {
                        builder.Append("vt ");
                        AppendFloat(builder, uv[uvIndex].x).Append(' ');
                        AppendFloat(builder, uv[uvIndex].y).AppendLine();
                    }
                }

                if (hasNormals)
                {
                    for (int normalIndex = 0; normalIndex < normals.Length; normalIndex++)
                    {
                        Vector3 normal = normals[normalIndex];
                        builder.Append("vn ");
                        AppendFloat(builder, normal.x).Append(' ');
                        AppendFloat(builder, normal.y).Append(' ');
                        AppendFloat(builder, normal.z).AppendLine();
                    }
                }

                for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    Material material = output.Materials != null && output.Materials.Length > 0
                        ? output.Materials[Mathf.Min(subMeshIndex, output.Materials.Length - 1)]
                        : null;
                    string materialName = material != null && materialNames.TryGetValue(material, out string resolvedName)
                        ? resolvedName
                        : nullMaterialName;
                    builder.Append("g ").Append(objectName).Append("_submesh_").Append(subMeshIndex + 1).AppendLine();
                    builder.Append("usemtl ").AppendLine(materialName);

                    int[] triangles = mesh.GetTriangles(subMeshIndex);
                    for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                    {
                        builder.Append("f ");
                        AppendFaceVertex(builder, triangles[triangleIndex], vertexOffset, uvOffset, normalOffset, hasUv, hasNormals);
                        builder.Append(' ');
                        AppendFaceVertex(builder, triangles[triangleIndex + 1], vertexOffset, uvOffset, normalOffset, hasUv, hasNormals);
                        builder.Append(' ');
                        AppendFaceVertex(builder, triangles[triangleIndex + 2], vertexOffset, uvOffset, normalOffset, hasUv, hasNormals);
                        builder.AppendLine();
                    }
                }

                vertexOffset += vertices.Length;
                if (hasUv)
                {
                    uvOffset += uv.Length;
                }

                if (hasNormals)
                {
                    normalOffset += normals.Length;
                }
            }

            return builder.ToString();
        }

        private static void BuildMaterialTable(
            IReadOnlyList<MeshSplitOutput> outputs,
            out Dictionary<Material, string> materialNames,
            out string nullMaterialName,
            out List<MaterialRecord> records)
        {
            materialNames = new Dictionary<Material, string>();
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            records = new List<MaterialRecord>();
            nullMaterialName = AddUniqueMaterialName("default_material", usedNames);
            records.Add(new MaterialRecord(null, nullMaterialName));

            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                Material[] materials = outputs[outputIndex].Materials;
                if (materials == null)
                {
                    continue;
                }

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || materialNames.ContainsKey(material))
                    {
                        continue;
                    }

                    string materialName = AddUniqueMaterialName(MakeObjIdentifier(material.name), usedNames);
                    materialNames.Add(material, materialName);
                    records.Add(new MaterialRecord(material, materialName));
                }
            }
        }

        private static string BuildMaterialText(List<MaterialRecord> records)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Exported by ProjectF Mesh Split");
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                MaterialRecord record = records[recordIndex];
                Color color = ResolveMaterialColor(record.Material);
                builder.Append("newmtl ").AppendLine(record.Name);
                builder.Append("Kd ");
                AppendFloat(builder, color.r).Append(' ');
                AppendFloat(builder, color.g).Append(' ');
                AppendFloat(builder, color.b).AppendLine();
                builder.Append("d ");
                AppendFloat(builder, color.a).AppendLine();
                builder.AppendLine("illum 2");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static Color ResolveMaterialColor(Material material)
        {
            if (material == null)
            {
                return Color.white;
            }

            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }

            return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        }

        private static string AddUniqueMaterialName(string requestedName, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "material" : requestedName;
            string candidate = baseName;
            int suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = $"{baseName}_{suffix++}";
            }

            return candidate;
        }

        private static string MakeObjIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "MeshSplit";
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
            {
                char character = value[characterIndex];
                builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.'
                    ? character
                    : '_');
            }

            return builder.Length > 0 ? builder.ToString() : "MeshSplit";
        }

        private static StringBuilder AppendFloat(StringBuilder builder, float value)
        {
            return builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendFaceVertex(
            StringBuilder builder,
            int localIndex,
            int vertexOffset,
            int uvOffset,
            int normalOffset,
            bool hasUv,
            bool hasNormals)
        {
            builder.Append(vertexOffset + localIndex + 1);
            if (hasUv)
            {
                builder.Append('/').Append(uvOffset + localIndex + 1);
                if (hasNormals)
                {
                    builder.Append('/').Append(normalOffset + localIndex + 1);
                }
            }
            else if (hasNormals)
            {
                builder.Append("//").Append(normalOffset + localIndex + 1);
            }
        }

        private readonly struct MaterialRecord
        {
            public MaterialRecord(Material material, string name)
            {
                Material = material;
                Name = name;
            }

            public Material Material { get; }
            public string Name { get; }
        }
    }
}

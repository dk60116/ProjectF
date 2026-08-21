using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectF.EditorTools
{
    internal static class BucketPortableAssetGenerator
    {
        private const string OutputFolder = "Assets/Items/Fluid/Bucket";
        private const string MeshPath = OutputFolder + "/Bucket_P.mesh";
        private const string MaterialPath = OutputFolder + "/M_Bucket_P.mat";
        private const string ItemDefinitionFolder = "Assets/Data/Items";
        private const string BucketItemName = "Bucket";
        private const string WaterBucketItemName = "Water Bucket";

        private const int RadialSegments = 10;
        private const int HandleSegments = 6;
        private const int ExpectedVertexCount = 226;
        private const int ExpectedTriangleCount = 196;

        private const float BucketBottomY = -0.1f;
        private const float BucketShoulderY = 0.082f;
        private const float BucketRimTopY = 0.097f;
        private const float InnerFloorY = -0.078f;
        private const float BottomOuterRadius = 0.082f;
        private const float BodyTopRadius = 0.101f;
        private const float RimOuterRadius = 0.109f;
        private const float RimInnerRadius = 0.087f;
        private const float InnerFloorRadius = 0.068f;

        private const float HandleRadius = 0.103f;
        private const float HandlePivotY = 0.069f;
        private const float HandleRise = 0.18f;
        private const float HandleHalfWidth = 0.0065f;
        private const float HandleHalfDepth = 0.006f;

        [MenuItem("Tools/ProjectF/Generate Bucket Portable Model")]
        public static void GenerateBucketPortableAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Bucket_P: exit Play Mode before generating assets.");
                return;
            }

            EnsureOutputFolder();
            Mesh mesh = CreateOrUpdateMesh();
            Material material = CreateOrUpdateMaterial();
            int assignedDefinitionCount = AssignItemDefinitions(mesh, material);
            int assignedSceneItemCount = AssignLoadedItemManagers(mesh, material);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ItemDataEditorWindow.DefinitionCatalog.NotifyChanged();
            Selection.activeObject = mesh;

            Debug.Log(
                $"Bucket_P: generated an open ten-sided iron bucket with {mesh.vertexCount} vertices "
                + $"and {mesh.triangles.Length / 3} triangles. Assigned {assignedDefinitionCount} "
                + $"ItemDefinition(s) and {assignedSceneItemCount} loaded scene item(s).");
        }

        [MenuItem("Tools/ProjectF/Generate Bucket Portable Model", true)]
        private static bool CanGenerateBucketPortableAssets()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("Tools/ProjectF/Validation/Bucket Portable Model")]
        public static void ValidateGeneratedBucketAssets()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mesh == null || material == null)
            {
                throw new InvalidOperationException(
                    "Bucket_P validation failed: generate the mesh and material first.");
            }

            ValidateMesh(mesh);
            if (!material.enableInstancing)
            {
                throw new InvalidOperationException(
                    "Bucket_P validation failed: the portable material must enable GPU instancing.");
            }

            Debug.Log(
                $"Bucket_P validation passed: {mesh.vertexCount} vertices, "
                + $"{mesh.triangles.Length / 3} triangles, symmetric bounds {mesh.bounds.size}.");
        }

        private static void EnsureOutputFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder))
            {
                return;
            }

            string[] folderParts = OutputFolder.Split('/');
            string currentFolder = folderParts[0];
            for (int i = 1; i < folderParts.Length; i++)
            {
                string nextFolder = currentFolder + "/" + folderParts[i];
                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    AssetDatabase.CreateFolder(currentFolder, folderParts[i]);
                }

                currentFolder = nextFolder;
            }
        }

        private static Mesh CreateOrUpdateMesh()
        {
            Mesh generatedMesh = BuildBucketMesh();
            ValidateMesh(generatedMesh);

            Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (assetMesh == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, MeshPath);
                return generatedMesh;
            }

            EditorUtility.CopySerialized(generatedMesh, assetMesh);
            UnityEngine.Object.DestroyImmediate(generatedMesh);
            EditorUtility.SetDirty(assetMesh);
            return assetMesh;
        }

        private static Mesh BuildBucketMesh()
        {
            List<Vector3> vertices = new List<Vector3>(ExpectedVertexCount);
            List<Vector2> uv = new List<Vector2>(ExpectedVertexCount);
            List<int> triangles = new List<int>(ExpectedTriangleCount * 3);

            AddBucketBody(vertices, uv, triangles);
            AddHandle(vertices, uv, triangles);
            AddHandleMounts(vertices, uv, triangles);

            Mesh mesh = new Mesh
            {
                name = "Bucket_P"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddBucketBody(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            for (int i = 0; i < RadialSegments; i++)
            {
                float currentAngle = Mathf.PI * 2f * i / RadialSegments;
                float nextAngle = Mathf.PI * 2f * (i + 1) / RadialSegments;

                Vector3 outerBottomCurrent = RingPoint(BottomOuterRadius, BucketBottomY, currentAngle);
                Vector3 outerBottomNext = RingPoint(BottomOuterRadius, BucketBottomY, nextAngle);
                Vector3 bodyTopCurrent = RingPoint(BodyTopRadius, BucketShoulderY, currentAngle);
                Vector3 bodyTopNext = RingPoint(BodyTopRadius, BucketShoulderY, nextAngle);
                Vector3 rimOuterTopCurrent = RingPoint(RimOuterRadius, BucketRimTopY, currentAngle);
                Vector3 rimOuterTopNext = RingPoint(RimOuterRadius, BucketRimTopY, nextAngle);
                Vector3 rimInnerTopCurrent = RingPoint(RimInnerRadius, BucketRimTopY, currentAngle);
                Vector3 rimInnerTopNext = RingPoint(RimInnerRadius, BucketRimTopY, nextAngle);
                Vector3 innerFloorCurrent = RingPoint(InnerFloorRadius, InnerFloorY, currentAngle);
                Vector3 innerFloorNext = RingPoint(InnerFloorRadius, InnerFloorY, nextAngle);

                // Tapered outer wall.
                AddQuadFace(
                    vertices,
                    uv,
                    triangles,
                    outerBottomCurrent,
                    bodyTopCurrent,
                    bodyTopNext,
                    outerBottomNext);

                // The outer lip keeps hard faceted sides while its horizontal rings share vertices.
                AddQuadFace(
                    vertices,
                    uv,
                    triangles,
                    RingPoint(RimOuterRadius, BucketShoulderY, currentAngle),
                    rimOuterTopCurrent,
                    rimOuterTopNext,
                    RingPoint(RimOuterRadius, BucketShoulderY, nextAngle));

                // Reversed winding keeps the inside of the open bucket visible with back-face culling.
                AddQuadFace(
                    vertices,
                    uv,
                    triangles,
                    innerFloorCurrent,
                    innerFloorNext,
                    rimInnerTopNext,
                    rimInnerTopCurrent);

            }

            AddSharedAnnulus(
                vertices,
                uv,
                triangles,
                BodyTopRadius,
                RimOuterRadius,
                BucketShoulderY,
                false);
            AddSharedAnnulus(
                vertices,
                uv,
                triangles,
                RimInnerRadius,
                RimOuterRadius,
                BucketRimTopY,
                true);
            AddSharedDisc(
                vertices,
                uv,
                triangles,
                BottomOuterRadius,
                BucketBottomY,
                false);
            AddSharedDisc(
                vertices,
                uv,
                triangles,
                InnerFloorRadius,
                InnerFloorY,
                true);
        }

        private static void AddHandle(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            int handleStart = vertices.Count;
            for (int i = 0; i <= HandleSegments; i++)
            {
                Vector3 center = HandlePoint(i);
                Vector3 tangent;
                if (i == 0)
                {
                    tangent = (HandlePoint(1) - center).normalized;
                }
                else if (i == HandleSegments)
                {
                    tangent = (center - HandlePoint(i - 1)).normalized;
                }
                else
                {
                    tangent = (HandlePoint(i + 1) - HandlePoint(i - 1)).normalized;
                }

                Vector3 planarNormal = new Vector3(-tangent.y, tangent.x, 0f) * HandleHalfWidth;
                Vector3 depth = Vector3.forward * HandleHalfDepth;
                AddSharedVertex(vertices, uv, center - planarNormal - depth);
                AddSharedVertex(vertices, uv, center + planarNormal - depth);
                AddSharedVertex(vertices, uv, center + planarNormal + depth);
                AddSharedVertex(vertices, uv, center - planarNormal + depth);
            }

            for (int i = 0; i < HandleSegments; i++)
            {
                int current = handleStart + i * 4;
                int next = current + 4;
                AddQuadIndices(triangles, current, current + 1, next + 1, next);
                AddQuadIndices(triangles, current + 1, current + 2, next + 2, next + 1);
                AddQuadIndices(triangles, current + 3, next + 3, next + 2, current + 2);
                AddQuadIndices(triangles, current, next, next + 3, current + 3);
            }

            AddQuadIndices(
                triangles,
                handleStart,
                handleStart + 3,
                handleStart + 2,
                handleStart + 1);
            int endStart = handleStart + HandleSegments * 4;
            AddQuadIndices(
                triangles,
                endStart,
                endStart + 1,
                endStart + 2,
                endStart + 3);
        }

        private static Vector3 HandlePoint(int index)
        {
            float progress = index / (float)HandleSegments;
            float angle = Mathf.PI * (1f - progress);
            return new Vector3(
                Mathf.Cos(angle) * HandleRadius,
                HandlePivotY + Mathf.Sin(angle) * HandleRise,
                0f);
        }

        private static void AddHandleMounts(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles)
        {
            AddBox(
                vertices,
                uv,
                triangles,
                new Vector3(-HandleRadius, HandlePivotY, 0f),
                new Vector3(0.012f, 0.015f, 0.01f));
            AddBox(
                vertices,
                uv,
                triangles,
                new Vector3(HandleRadius, HandlePivotY, 0f),
                new Vector3(0.012f, 0.015f, 0.01f));
        }

        private static void AddBox(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 center,
            Vector3 halfExtents)
        {
            Vector3 leftBottomBack = center + new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z);
            Vector3 rightBottomBack = center + new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z);
            Vector3 rightTopBack = center + new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z);
            Vector3 leftTopBack = center + new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z);
            Vector3 leftBottomFront = center + new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z);
            Vector3 rightBottomFront = center + new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z);
            Vector3 rightTopFront = center + new Vector3(halfExtents.x, halfExtents.y, halfExtents.z);
            Vector3 leftTopFront = center + new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z);

            int start = vertices.Count;
            AddSharedVertex(vertices, uv, leftBottomBack);
            AddSharedVertex(vertices, uv, rightBottomBack);
            AddSharedVertex(vertices, uv, rightTopBack);
            AddSharedVertex(vertices, uv, leftTopBack);
            AddSharedVertex(vertices, uv, leftBottomFront);
            AddSharedVertex(vertices, uv, rightBottomFront);
            AddSharedVertex(vertices, uv, rightTopFront);
            AddSharedVertex(vertices, uv, leftTopFront);

            AddQuadIndices(triangles, start, start + 3, start + 2, start + 1);
            AddQuadIndices(triangles, start + 4, start + 5, start + 6, start + 7);
            AddQuadIndices(triangles, start, start + 4, start + 7, start + 3);
            AddQuadIndices(triangles, start + 1, start + 2, start + 6, start + 5);
            AddQuadIndices(triangles, start + 3, start + 7, start + 6, start + 2);
            AddQuadIndices(triangles, start, start + 1, start + 5, start + 4);
        }

        private static void AddSharedAnnulus(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float innerRadius,
            float outerRadius,
            float y,
            bool upward)
        {
            int start = vertices.Count;
            for (int i = 0; i < RadialSegments; i++)
            {
                float angle = Mathf.PI * 2f * i / RadialSegments;
                AddSharedVertex(vertices, uv, RingPoint(outerRadius, y, angle));
                AddSharedVertex(vertices, uv, RingPoint(innerRadius, y, angle));
            }

            for (int i = 0; i < RadialSegments; i++)
            {
                int currentOuter = start + i * 2;
                int currentInner = currentOuter + 1;
                int nextOuter = start + ((i + 1) % RadialSegments) * 2;
                int nextInner = nextOuter + 1;
                if (upward)
                {
                    AddQuadIndices(
                        triangles,
                        currentOuter,
                        currentInner,
                        nextInner,
                        nextOuter);
                }
                else
                {
                    AddQuadIndices(
                        triangles,
                        currentOuter,
                        nextOuter,
                        nextInner,
                        currentInner);
                }
            }
        }

        private static void AddSharedDisc(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float radius,
            float y,
            bool upward)
        {
            int center = vertices.Count;
            AddSharedVertex(vertices, uv, new Vector3(0f, y, 0f));
            int ringStart = vertices.Count;
            for (int i = 0; i < RadialSegments; i++)
            {
                float angle = Mathf.PI * 2f * i / RadialSegments;
                AddSharedVertex(vertices, uv, RingPoint(radius, y, angle));
            }

            for (int i = 0; i < RadialSegments; i++)
            {
                int current = ringStart + i;
                int next = ringStart + (i + 1) % RadialSegments;
                triangles.Add(center);
                triangles.Add(upward ? next : current);
                triangles.Add(upward ? current : next);
            }
        }

        private static Vector3 RingPoint(float radius, float y, float angle)
        {
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        private static void AddQuadFace(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 1f));
            uv.Add(new Vector2(1f, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddSharedVertex(
            List<Vector3> vertices,
            List<Vector2> uv,
            Vector3 vertex)
        {
            vertices.Add(vertex);
            uv.Add(new Vector2(
                0.5f + vertex.x / (RimOuterRadius * 2f),
                0.5f + vertex.z / (RimOuterRadius * 2f)));
        }

        private static void AddQuadIndices(
            List<int> triangles,
            int a,
            int b,
            int c,
            int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        private static Material CreateOrUpdateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("Bucket_P: no supported Lit shader is available.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_Bucket_P"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Color ironColor = new Color(0.39f, 0.44f, 0.5f, 1f);
            material.color = ironColor;
            material.mainTexture = null;
            material.enableInstancing = true;
            SetColorIfPresent(material, "_BaseColor", ironColor);
            SetTextureIfPresent(material, "_BaseMap", null);
            SetFloatIfPresent(material, "_Metallic", 0.78f);
            SetFloatIfPresent(material, "_Smoothness", 0.3f);
            SetFloatIfPresent(material, "_Surface", 0f);
            SetFloatIfPresent(material, "_Cull", 2f);
            SetFloatIfPresent(material, "_ZWrite", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static int AssignItemDefinitions(Mesh mesh, Material material)
        {
            int assignedCount = 0;
            string[] guids = AssetDatabase.FindAssets(
                "t:ItemDefinition",
                new[] { ItemDefinitionFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (!IsBucketDefinition(definition)
                    || (definition.portableMesh == mesh && definition.portableMat == material))
                {
                    continue;
                }

                Undo.RecordObject(definition, "Assign Bucket_P Portable Assets");
                definition.portableMesh = mesh;
                definition.portableMat = material;
                EditorUtility.SetDirty(definition);
                assignedCount++;
            }

            return assignedCount;
        }

        private static int AssignLoadedItemManagers(Mesh mesh, Material material)
        {
            int assignedCount = 0;
            ItemManager[] itemManagers = UnityEngine.Object.FindObjectsByType<ItemManager>(
                FindObjectsInactive.Include);
            for (int managerIndex = 0; managerIndex < itemManagers.Length; managerIndex++)
            {
                ItemManager itemManager = itemManagers[managerIndex];
                List<ItemManager.ItemSet> itemSets = itemManager != null ? itemManager.ItemSets : null;
                if (itemSets == null)
                {
                    continue;
                }

                bool recordedUndo = false;
                for (int itemIndex = 0; itemIndex < itemSets.Count; itemIndex++)
                {
                    ItemManager.ItemSet itemSet = itemSets[itemIndex];
                    if (!IsBucketName(itemSet.name)
                        || (itemSet.portableMesh == mesh && itemSet.portableMat == material))
                    {
                        continue;
                    }

                    if (!recordedUndo)
                    {
                        Undo.RecordObject(itemManager, "Assign Bucket_P Portable Assets");
                        recordedUndo = true;
                    }

                    itemSet.portableMesh = mesh;
                    itemSet.portableMat = material;
                    itemSets[itemIndex] = itemSet;
                    assignedCount++;
                }

                if (!recordedUndo)
                {
                    continue;
                }

                EditorUtility.SetDirty(itemManager);
                if (itemManager.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(itemManager.gameObject.scene);
                }
            }

            return assignedCount;
        }

        private static bool IsBucketDefinition(ItemDefinition definition)
        {
            return definition != null
                   && (IsBucketName(definition.itemName)
                       || IsBucketName(definition.name)
                       || definition.mapObject is Bucket);
        }

        private static bool IsBucketName(string itemName)
        {
            string normalizedName = itemName != null ? itemName.Trim() : string.Empty;
            return string.Equals(normalizedName, BucketItemName, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalizedName, WaterBucketItemName, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                throw new InvalidOperationException("Bucket_P validation failed: mesh is null.");
            }

            int triangleCount = mesh.triangles.Length / 3;
            if (mesh.vertexCount != ExpectedVertexCount || triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Bucket_P topology changed unexpectedly. Expected {ExpectedVertexCount} vertices/"
                    + $"{ExpectedTriangleCount} triangles, got {mesh.vertexCount}/{triangleCount}.");
            }

            if (mesh.subMeshCount != 1)
            {
                throw new InvalidOperationException(
                    $"Bucket_P validation failed: expected one submesh, got {mesh.subMeshCount}.");
            }

            Bounds bounds = mesh.bounds;
            if (Mathf.Abs(bounds.center.x) > 0.0001f || Mathf.Abs(bounds.center.z) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Bucket_P validation failed: asymmetric X/Z bounds center {bounds.center}.");
            }

            if (bounds.min.y > BucketBottomY + 0.0001f
                || bounds.max.y < HandlePivotY + HandleRise - 0.001f)
            {
                throw new InvalidOperationException(
                    $"Bucket_P validation failed: unexpected vertical bounds {bounds}.");
            }

            int[] indices = mesh.triangles;
            Vector3[] positions = mesh.vertices;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 edgeA = positions[indices[i + 1]] - positions[indices[i]];
                Vector3 edgeB = positions[indices[i + 2]] - positions[indices[i]];
                if (Vector3.Cross(edgeA, edgeB).sqrMagnitude <= 0.0000000001f)
                {
                    throw new InvalidOperationException(
                        $"Bucket_P validation failed: degenerate triangle at index {i / 3}.");
                }
            }
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetTextureIfPresent(Material material, string propertyName, Texture value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, value);
            }
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}

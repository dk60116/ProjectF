using UnityEngine;

[DisallowMultipleComponent]
public sealed class GeneratedTerrainGrassMeshOwner : MonoBehaviour
{
    private Mesh ownedMesh;

    public void ReplaceOwnedMesh(MeshFilter meshFilter, Mesh replacement)
    {
        if (ownedMesh == replacement)
        {
            return;
        }

        ReleaseOwnedMesh(meshFilter);
        ownedMesh = replacement;
        if (meshFilter != null)
        {
            meshFilter.sharedMesh = replacement;
        }
    }

    private void OnDestroy()
    {
        ReleaseOwnedMesh(GetComponent<MeshFilter>());
    }

    private void ReleaseOwnedMesh(MeshFilter meshFilter)
    {
        if (meshFilter != null && meshFilter.sharedMesh == ownedMesh)
        {
            meshFilter.sharedMesh = null;
        }

        if (ownedMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(ownedMesh);
        }
        else
        {
            DestroyImmediate(ownedMesh);
        }

        ownedMesh = null;
    }
}

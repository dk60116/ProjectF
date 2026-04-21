using UnityEngine;

public class ConveyorBelt : InstallationObject
{
    private static readonly int UvScrollXShaderId = Shader.PropertyToID("_UVScrollX");
    private static readonly int UvScrollYShaderId = Shader.PropertyToID("_UVScrollY");

    [SerializeField, Min(0f)]
    private float conveyorSpeed = 1f;
    [SerializeField]
    private MeshRenderer beltTopRenderer;

    private MaterialPropertyBlock beltTopPropertyBlock;
    private float lastAppliedUvScrollY = float.NaN;

    public float ConveyorSpeed => Mathf.Max(0f, conveyorSpeed);

    protected new void Awake()
    {
        base.Awake();
        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ApplyBeltTopScroll();
    }

    private void ResolveBeltTopRenderer()
    {
        if (beltTopRenderer != null)
        {
            return;
        }

        Transform beltTopTransform = transform.Find("BeltTop");
        if (beltTopTransform != null)
        {
            beltTopRenderer = beltTopTransform.GetComponent<MeshRenderer>();
        }

        if (beltTopRenderer == null)
        {
            MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                MeshRenderer candidate = childRenderers[i];
                if (candidate != null && candidate.name == "BeltTop")
                {
                    beltTopRenderer = candidate;
                    break;
                }
            }
        }
    }

    private void ApplyBeltTopScroll()
    {
        ResolveBeltTopRenderer();
        if (beltTopRenderer == null)
        {
            return;
        }

        float targetUvScrollY = -ConveyorSpeed * 0.75f;
        if (!float.IsNaN(lastAppliedUvScrollY) && Mathf.Approximately(lastAppliedUvScrollY, targetUvScrollY))
        {
            return;
        }

        if (beltTopPropertyBlock == null)
        {
            beltTopPropertyBlock = new MaterialPropertyBlock();
        }

        beltTopRenderer.GetPropertyBlock(beltTopPropertyBlock);
        beltTopPropertyBlock.SetFloat(UvScrollXShaderId, 0f);
        beltTopPropertyBlock.SetFloat(UvScrollYShaderId, targetUvScrollY);
        beltTopRenderer.SetPropertyBlock(beltTopPropertyBlock);
        lastAppliedUvScrollY = targetUvScrollY;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();

        if (conveyorSpeed < 0f)
        {
            conveyorSpeed = 0f;
        }

        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }
#endif
}

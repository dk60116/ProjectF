using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Animal : MonoBehaviour
{
    public enum AnimalGender
    {
        Male,
        Female
    }

    [SerializeField]
    private Animator anim;
    [FormerlySerializedAs("collider")]
    [SerializeField]
    private CapsuleCollider capsuleCollider;

    [Header("Identity")]
    [SerializeField, HideInInspector] private AnimalDefinition animalDefinition;
    [SerializeField] private AnimalGender animalGender = AnimalGender.Male;
    [Header("Age (0..10)")]
    [SerializeField, Range(0f, 10f)] private float DinoAge = 10f;
    [Header("Scale")]
    [Tooltip("Multiplier applied to the animal's original adult scale.")]
    [SerializeField, Min(0f)] private float BaseScale = 1f;
    [SerializeField] private float BabyScale = 0.5f;
    [SerializeField] private SkinnedMeshRenderer dinoRenderer;

    private SkinnedMeshRenderer outlineRenderer;
    private bool hoverOutlineVisible;
    private bool focusedOutlineVisible;

    public GameObject Eye;
    private GameObject eyeLeftGO;
    private GameObject eyeRightGO;
    private SkinnedMeshRenderer eyeLeft;
    private SkinnedMeshRenderer eyeRight;
    [SerializeField] private Transform headBone;

    [SerializeField] private float eyeShapeChangingSpeed = 10f;
    [SerializeField] private Transform dinoTransform;
    [SerializeField] private Transform youngDinoLeftEye;
    [SerializeField] private Transform youngDinoRightEye;
    [SerializeField] private Transform oldDinoLeftEye;
    [SerializeField] private Transform oldDinoRightEye;

    [SerializeField, HideInInspector] private Vector3 adultScale;
    private int dinoState;
    private int blendShapeCount;
    private float[] eyeBlendShapeTargets;
    private int eyeShape;
    private bool growthInitialized;

    public AnimalGender Gender => animalGender;
    public AnimalDefinition Definition => animalDefinition;
    public float Age => DinoAge;
    public float BaseScaleValue => BaseScale;
    public bool HasTerrainInteraction
    {
        get
        {
            TerrainAnimalInstance instance = GetComponentInParent<TerrainAnimalInstance>();
            return instance != null && instance.HasInteracted;
        }
    }

    public void MarkTerrainInteraction()
    {
        TerrainAnimalInstance instance = GetComponentInParent<TerrainAnimalInstance>();
        if (instance != null)
        {
            instance.MarkInteracted();
        }
    }

    private void Reset()
    {
        anim = GetComponent<Animator>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        AlignCapsuleColliderBottomToPivot();
        dinoTransform = transform;
        dinoRenderer = FindGrowthRenderer();
        headBone = FindDescendantByExactName(transform, "head");
        youngDinoLeftEye = FindDescendantByNamePrefix(transform, "EyeBabyPlace_L", "EyePlaceBaby_L");
        youngDinoRightEye = FindDescendantByNamePrefix(transform, "EyeBabyPlace_R", "EyePlaceBaby_R");
        oldDinoLeftEye = FindDescendantByNamePrefix(transform, "EyePlace_L");
        oldDinoRightEye = FindDescendantByNamePrefix(transform, "EyePlace_R");

#if UNITY_EDITOR
        Eye = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/GrigoriyArx/DinoGK/Prefabs/Eyes/Eye.prefab");
        Transform sourceTransform = PrefabUtility.GetCorrespondingObjectFromOriginalSource(dinoTransform);
        adultScale = sourceTransform != null ? sourceTransform.localScale : dinoTransform.localScale;
#else
        adultScale = dinoTransform.localScale;
#endif

        growthInitialized = false;
        if (InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    private void Awake()
    {
        eyeLeftGO = Instantiate(Eye, oldDinoLeftEye.position, oldDinoLeftEye.rotation);
        eyeRightGO = Instantiate(Eye, oldDinoRightEye.position, oldDinoRightEye.rotation);

        if (headBone != null)
        {
            eyeLeftGO.transform.SetParent(headBone, false);
            eyeRightGO.transform.SetParent(headBone, false);
        }
        else
        {
            Debug.LogError("Parent bone is not assigned!");
        }

        eyeLeft = eyeLeftGO.GetComponent<SkinnedMeshRenderer>();
        eyeRight = eyeRightGO.GetComponent<SkinnedMeshRenderer>();
        blendShapeCount = eyeLeft.sharedMesh.blendShapeCount;
        eyeBlendShapeTargets = new float[blendShapeCount];
    }

    private void Start()
    {
        anim = GetComponent<Animator>();
        InitializeGrowth();
        SetAge(DinoAge);
    }

    private void Update()
    {
        if (dinoState == 17)
        {
            dinoState = 0;
        }

        if (dinoState < 0)
        {
            dinoState = 16;
        }

        if (Input.GetKeyDown("d") && dinoState < 18)
        {
            dinoState++;
            if (dinoState == 13)
            {
                dinoState++;
            }

            SwitchAnimation(dinoState);
        }

        if (Input.GetKeyDown("a") && dinoState > 0)
        {
            dinoState--;
            if (dinoState == 13)
            {
                dinoState--;
            }

            SwitchAnimation(dinoState);
        }

        if (Input.GetKeyDown("e"))
        {
            SetAge(DinoAge + 1f);
        }

        if (Input.GetKeyDown("q"))
        {
            SetAge(DinoAge - 1f);
        }

        if (Input.GetKeyDown("c") && blendShapeCount > 0)
        {
            eyeShape++;
            if (eyeShape >= blendShapeCount)
            {
                eyeShape = 0;
            }

            SwitchEyeShape(eyeShape);
        }

        if (Input.GetKeyDown("z") && blendShapeCount > 0)
        {
            eyeShape--;
            if (eyeShape < 0)
            {
                eyeShape = blendShapeCount - 1;
            }

            SwitchEyeShape(eyeShape);
        }

        if (Input.GetKeyDown("1"))
        {
            SwitchAnimation(98);
        }

        if (Input.GetKeyDown("2"))
        {
            SwitchAnimation(99);
        }
    }

    private void FixedUpdate()
    {
        if (eyeLeft == null || eyeRight == null || eyeBlendShapeTargets == null)
        {
            return;
        }

        for (int i = 0; i < blendShapeCount; i++)
        {
            float leftWeight = eyeLeft.GetBlendShapeWeight(i);
            float rightWeight = eyeRight.GetBlendShapeWeight(i);
            float targetWeight = eyeBlendShapeTargets[i];
            float lerp = Time.fixedDeltaTime * eyeShapeChangingSpeed;
            eyeLeft.SetBlendShapeWeight(i, Mathf.Lerp(leftWeight, targetWeight, lerp));
            eyeRight.SetBlendShapeWeight(i, Mathf.Lerp(rightWeight, targetWeight, lerp));
        }
    }

    private void OnDisable()
    {
        if (outlineRenderer != null)
        {
            AnimalScreenSpaceOutline.HideHovered(outlineRenderer);
            AnimalScreenSpaceOutline.HideFocused(outlineRenderer);
        }

        hoverOutlineVisible = false;
        focusedOutlineVisible = false;
    }

    public void SetHoverOutline(bool visible)
    {
        if (hoverOutlineVisible == visible)
        {
            return;
        }

        if (!TryResolveOutlineRenderer())
        {
            return;
        }

        hoverOutlineVisible = visible;
        if (visible)
        {
            AnimalScreenSpaceOutline.ShowHovered(outlineRenderer);
        }
        else
        {
            AnimalScreenSpaceOutline.HideHovered(outlineRenderer);
        }
    }

    public void SetFocusedOutline(bool visible)
    {
        if (focusedOutlineVisible == visible)
        {
            return;
        }

        if (!TryResolveOutlineRenderer())
        {
            return;
        }

        focusedOutlineVisible = visible;
        if (visible)
        {
            AnimalScreenSpaceOutline.ShowFocused(outlineRenderer);
        }
        else
        {
            AnimalScreenSpaceOutline.HideFocused(outlineRenderer);
        }
    }

    private bool TryResolveOutlineRenderer()
    {
        if (outlineRenderer == null)
        {
            outlineRenderer = dinoRenderer != null
                ? dinoRenderer
                : GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        return outlineRenderer != null;
    }

    public int CopyOutlineMaskRenderers(Renderer[] destination)
    {
        if (destination == null || destination.Length == 0 || !TryResolveOutlineRenderer())
        {
            return 0;
        }

        int count = 0;
        count = AddOutlineMaskRenderer(destination, count, outlineRenderer);
        count = AddOutlineMaskRenderer(destination, count, eyeLeft);
        count = AddOutlineMaskRenderer(destination, count, eyeRight);
        return count;
    }

    private static int AddOutlineMaskRenderer(Renderer[] destination, int count, Renderer renderer)
    {
        if (count >= destination.Length
            || renderer == null
            || !renderer.enabled
            || !renderer.gameObject.activeInHierarchy)
        {
            return count;
        }

        for (int i = 0; i < count; i++)
        {
            if (destination[i] == renderer)
            {
                return count;
            }
        }

        destination[count] = renderer;
        return count + 1;
    }

    public void SetAge(float age)
    {
        DinoAge = Mathf.Clamp(age, 0f, 10f);
        if (growthInitialized || InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    public void SetBaseScale(float scale)
    {
        BaseScale = Mathf.Max(0f, scale);
        if (growthInitialized || InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    private void OnValidate()
    {
        DinoAge = Mathf.Clamp(DinoAge, 0f, 10f);
        BaseScale = Mathf.Max(0f, BaseScale);
        AlignCapsuleColliderBottomToPivot();
        if (InitializeGrowth())
        {
            SetGrowth(DinoAge * 0.1f);
        }
    }

    private void AlignCapsuleColliderBottomToPivot()
    {
        if (capsuleCollider == null)
        {
            return;
        }

        float centerOffset = Mathf.Max(capsuleCollider.height, capsuleCollider.radius * 2f) * 0.5f;
        Vector3 center = capsuleCollider.center;
        switch (capsuleCollider.direction)
        {
            case 0:
                center.x = centerOffset;
                break;
            case 2:
                center.z = centerOffset;
                break;
            default:
                center.y = centerOffset;
                break;
        }

        capsuleCollider.center = center;
    }

    private bool InitializeGrowth()
    {
        if (dinoTransform == null)
        {
            growthInitialized = false;
            return false;
        }

        if (adultScale == Vector3.zero)
        {
            adultScale = dinoTransform.localScale;
        }

        growthInitialized = true;
        return true;
    }

    private void SetGrowth(float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 baseAdultScale = adultScale * BaseScale;
        dinoTransform.localScale = Vector3.Lerp(baseAdultScale * BabyScale, baseAdultScale, t);

        if (eyeLeft != null
            && eyeRight != null
            && youngDinoLeftEye != null
            && youngDinoRightEye != null
            && oldDinoLeftEye != null
            && oldDinoRightEye != null)
        {
            eyeLeft.transform.position = Vector3.Lerp(youngDinoLeftEye.position, oldDinoLeftEye.position, t);
            eyeRight.transform.position = Vector3.Lerp(youngDinoRightEye.position, oldDinoRightEye.position, t);

            eyeLeft.transform.localScale = Vector3.Lerp(youngDinoLeftEye.localScale, oldDinoLeftEye.localScale, t);
            eyeRight.transform.localScale = Vector3.Lerp(youngDinoRightEye.localScale, oldDinoRightEye.localScale, t);

            eyeLeft.transform.rotation = Quaternion.Lerp(youngDinoLeftEye.rotation, oldDinoLeftEye.rotation, t);
            eyeRight.transform.rotation = Quaternion.Lerp(youngDinoRightEye.rotation, oldDinoRightEye.rotation, t);
        }

        if (dinoRenderer != null
            && dinoRenderer.sharedMesh != null
            && dinoRenderer.sharedMesh.blendShapeCount > 0)
        {
            dinoRenderer.SetBlendShapeWeight(0, (1f - t) * 100f);
        }
    }

    private void SwitchAnimation(int targetState)
    {
        if (anim == null)
        {
            return;
        }

        int currentState = anim.GetInteger("State");
        if (currentState != 0 && currentState < 97)
        {
            anim.SetTrigger("Reset");
        }

        anim.SetInteger("State", targetState);
    }

    private void SwitchEyeShape(int targetShape)
    {
        if (eyeBlendShapeTargets == null)
        {
            return;
        }

        for (int i = 0; i < eyeBlendShapeTargets.Length; i++)
        {
            eyeBlendShapeTargets[i] = 0f;
        }

        if (targetShape > 0 && targetShape < eyeBlendShapeTargets.Length)
        {
            eyeBlendShapeTargets[targetShape] = 100f;
        }
    }

    private SkinnedMeshRenderer FindGrowthRenderer()
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer candidate = renderers[i];
            if (candidate != null
                && candidate.sharedMesh != null
                && candidate.sharedMesh.blendShapeCount > 0)
            {
                return candidate;
            }
        }

        return renderers.Length > 0 ? renderers[0] : null;
    }

    private static Transform FindDescendantByExactName(Transform root, string targetName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nested = FindDescendantByExactName(child, targetName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Transform FindDescendantByNamePrefix(
        Transform root,
        string primaryPrefix,
        string alternatePrefix = null)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            string childName = child.name;
            if (childName.StartsWith(primaryPrefix, System.StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(alternatePrefix)
                    && childName.StartsWith(alternatePrefix, System.StringComparison.OrdinalIgnoreCase)))
            {
                return child;
            }

            Transform nested = FindDescendantByNamePrefix(child, primaryPrefix, alternatePrefix);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}

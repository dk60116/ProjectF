using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorkableObject : InstallationObject
{
    private static readonly HashSet<WorkableObject> ActiveInstances = new HashSet<WorkableObject>();
    private static float cachedGlobalMaxFocusActivationRadius;
    private static bool globalMaxFocusActivationRadiusDirty = true;

    [SerializeField]
    [Min(0f)]
    private float focusActivationRadius = 1f;

    public override float FocusActivationRadius => Mathf.Max(0f, focusActivationRadius);

    public new static float GlobalMaxFocusActivationRadius
    {
        get
        {
            if (!globalMaxFocusActivationRadiusDirty)
            {
                return cachedGlobalMaxFocusActivationRadius;
            }

            cachedGlobalMaxFocusActivationRadius = 0f;
            foreach (WorkableObject workableObject in ActiveInstances)
            {
                if (workableObject == null)
                {
                    continue;
                }

                cachedGlobalMaxFocusActivationRadius = Mathf.Max(
                    cachedGlobalMaxFocusActivationRadius,
                    workableObject.FocusActivationRadius);
            }

            globalMaxFocusActivationRadiusDirty = false;
            return cachedGlobalMaxFocusActivationRadius;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveInstances.Add(this);
        globalMaxFocusActivationRadiusDirty = true;
    }

    protected override void OnDisable()
    {
        ActiveInstances.Remove(this);
        globalMaxFocusActivationRadiusDirty = true;
        base.OnDisable();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (focusActivationRadius < 0f)
        {
            focusActivationRadius = 0f;
        }

        globalMaxFocusActivationRadiusDirty = true;
    }
#endif
}

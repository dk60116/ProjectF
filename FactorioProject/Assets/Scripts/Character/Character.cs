using UnityEngine;
using UnityEngine.Rendering;

public class Character : BaseObject
{
    protected static readonly int MoveHash = Animator.StringToHash("bMove");

    [SerializeField]
    protected Transform bodyTransform;
    [SerializeField]
    protected Animator animator;

    [System.Serializable]
    public class CharacterStat
    {
        public float moveSpeed = 1f;
        public float currentMoveSpeed = 0f;

        public float rotateSpeed = 720f;
    }

    [SerializeField]
    protected CharacterStat characterStat = new CharacterStat();

    protected void Start()
    {
        if (bodyTransform == null)
        {
            bodyTransform = transform;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        characterStat.currentMoveSpeed = characterStat.moveSpeed;
        ApplyShadowSettings();
    }

    public CharacterStat Stat => characterStat;
    public Transform BodyTransform => bodyTransform;

    protected void ApplyShadowSettings()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer rendererComponent in renderers)
        {
            rendererComponent.shadowCastingMode = ShadowCastingMode.On;
            rendererComponent.receiveShadows = true;
        }
    }
}

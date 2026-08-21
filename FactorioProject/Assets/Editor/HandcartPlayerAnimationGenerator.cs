#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HandcartPlayerAnimationGenerator
{
    private const string RunningClipPath = "Assets/Animation/Character/Running.anim";
    private const string IdleClipPath = "Assets/Animation/Character/Idle.anim";
    private const string PlayerModelPath = "Assets/Model/Player/Player.fbx";
    private const string ControllerPath = "Assets/Animation/Character/Player.controller";
    private const string OutputFolder = "Assets/Animation/Character/Handcart";
    private const string ForwardClipPath = OutputFolder + "/Handcart_Walk.anim";
    private const string BackwardClipPath = OutputFolder + "/Handcart_Walk_Backward.anim";
    private const string DriveStateName = "Handcart Drive";
    private const string DriveBlendTreeName = "Handcart Drive Blend";
    private const string ForwardStateName = "Handcart Walk";
    private const string BackwardStateName = "Handcart Walk Backward";
    private const string IdleStateName = "Idle";
    private const string MountedParameter = "bHandcartMounted";
    private const string DirectionParameter = "fHandcartDirection";
    private const string MoveSpeedParameter = "fMoveSpeed";
    private const string RightShoulderPath =
        "mixamorig:Hips/mixamorig:Spine/mixamorig:Spine1/mixamorig:Spine2/mixamorig:RightShoulder";
    private const string RightBoneToken = "mixamorig:Right";
    private const string LeftBoneToken = "mixamorig:Left";

    [MenuItem("Tools/ProjectF/Animation/Generate Handcart Player Animations")]
    public static void Generate()
    {
        AnimationClip forwardClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ForwardClipPath);
        AnimationClip backwardClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BackwardClipPath);
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Require(idleClip != null, $"기존 대기 애니메이션을 찾을 수 없습니다: {IdleClipPath}");
        Require(controller != null, $"Player Animator Controller를 찾을 수 없습니다: {ControllerPath}");

        if (forwardClip == null || backwardClip == null)
        {
            AnimationClip runningClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunningClipPath);
            GameObject playerModel = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            Require(runningClip != null, $"기존 걷기 애니메이션을 찾을 수 없습니다: {RunningClipPath}");
            Require(playerModel != null, $"Player 모델을 찾을 수 없습니다: {PlayerModelPath}");

            EnsureFolder(OutputFolder);
            Dictionary<EditorCurveBinding, AnimationCurve> idleCurves = ReadCurves(idleClip);
            Dictionary<string, Quaternion> mirroredLeftArmPose =
                BuildMirroredLeftArmPose(playerModel, idleCurves);
            if (forwardClip == null)
            {
                forwardClip = CreateClip(
                    BuildHandcartClip(runningClip, idleCurves, mirroredLeftArmPose, false, "Handcart_Walk"),
                    ForwardClipPath);
            }

            if (backwardClip == null)
            {
                backwardClip = CreateClip(
                    BuildHandcartClip(runningClip, idleCurves, mirroredLeftArmPose, true, "Handcart_Walk_Backward"),
                    BackwardClipPath);
            }
        }

        if (NeedsControllerConfiguration(controller, idleClip, forwardClip, backwardClip))
        {
            ConfigureController(controller, idleClip, forwardClip, backwardClip);
            EditorUtility.SetDirty(controller);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate(forwardClip, backwardClip, controller);
        Selection.activeObject = forwardClip;
        Debug.Log("Handcart Animator configured: 기존 애니메이션 유지 + 단일 연속 Drive 상태");
    }

    [MenuItem("Tools/ProjectF/Validation/Handcart Player Animation")]
    public static void ValidateGeneratedAssets()
    {
        Validate(
            AssetDatabase.LoadAssetAtPath<AnimationClip>(ForwardClipPath),
            AssetDatabase.LoadAssetAtPath<AnimationClip>(BackwardClipPath),
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath));
        Debug.Log("Handcart player animation validation passed");
    }

    private static AnimationClip BuildHandcartClip(
        AnimationClip runningClip,
        Dictionary<EditorCurveBinding, AnimationCurve> idleCurves,
        Dictionary<string, Quaternion> mirroredLeftArmPose,
        bool reverse,
        string clipName)
    {
        AnimationClip result = new AnimationClip
        {
            name = clipName,
            frameRate = runningClip.frameRate,
            legacy = false,
            wrapMode = WrapMode.Loop
        };

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(runningClip);
        float clipLength = ResolveCurveLength(runningClip, bindings);
        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding binding = bindings[i];
            AnimationCurve runningCurve = AnimationUtility.GetEditorCurve(runningClip, binding);
            if (runningCurve == null)
            {
                continue;
            }

            AnimationCurve outputCurve;
            if (IsStationaryBodyBinding(binding))
            {
                float attentionValue;
                if (!TryGetMirroredRotationValue(mirroredLeftArmPose, binding, out attentionValue))
                {
                    attentionValue = idleCurves.TryGetValue(binding, out AnimationCurve idleCurve)
                        && idleCurve != null
                        ? idleCurve.Evaluate(0f)
                        : runningCurve.Evaluate(0f);
                }

                outputCurve = AnimationCurve.Constant(0f, clipLength, attentionValue);
            }
            else
            {
                outputCurve = reverse
                    ? ReverseCurve(runningCurve, clipLength)
                    : CloneCurve(runningCurve);
            }

            AnimationUtility.SetEditorCurve(result, binding, outputCurve);
        }

        EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(runningClip);
        for (int i = 0; i < objectBindings.Length; i++)
        {
            EditorCurveBinding binding = objectBindings[i];
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(runningClip, binding);
            AnimationUtility.SetObjectReferenceCurve(result, binding, keys);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(runningClip);
        settings.loopTime = true;
        settings.loopBlend = true;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(result, settings);
        result.EnsureQuaternionContinuity();
        return result;
    }

    private static float ResolveCurveLength(
        AnimationClip clip,
        EditorCurveBinding[] bindings)
    {
        float maximumKeyTime = 0f;
        for (int i = 0; i < bindings.Length; i++)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
            if (curve == null || curve.length <= 0)
            {
                continue;
            }

            Keyframe[] keys = curve.keys;
            maximumKeyTime = Mathf.Max(maximumKeyTime, keys[keys.Length - 1].time);
        }

        return Mathf.Max(1f / Mathf.Max(1f, clip.frameRate), maximumKeyTime);
    }

    private static Dictionary<EditorCurveBinding, AnimationCurve> ReadCurves(AnimationClip clip)
    {
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        Dictionary<EditorCurveBinding, AnimationCurve> curves =
            new Dictionary<EditorCurveBinding, AnimationCurve>(bindings.Length);
        for (int i = 0; i < bindings.Length; i++)
        {
            curves[bindings[i]] = AnimationUtility.GetEditorCurve(clip, bindings[i]);
        }

        return curves;
    }

    private static Dictionary<string, Quaternion> BuildMirroredLeftArmPose(
        GameObject playerModel,
        Dictionary<EditorCurveBinding, AnimationCurve> idleCurves)
    {
        Transform root = playerModel.transform;
        Transform[] transforms = playerModel.GetComponentsInChildren<Transform>(true);
        Dictionary<string, Transform> transformsByPath =
            new Dictionary<string, Transform>(transforms.Length, StringComparer.Ordinal);
        for (int i = 0; i < transforms.Length; i++)
        {
            string path = AnimationUtility.CalculateTransformPath(transforms[i], root);
            transformsByPath[path] = transforms[i];
        }

        Dictionary<string, Quaternion> mirroredPose =
            new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform rightTransform = transforms[i];
            string rightPath = AnimationUtility.CalculateTransformPath(rightTransform, root);
            if (!IsRightArmPath(rightPath))
            {
                continue;
            }

            string leftPath = rightPath.Replace(RightBoneToken, LeftBoneToken);
            if (!transformsByPath.TryGetValue(leftPath, out Transform leftTransform))
            {
                continue;
            }

            if (!HasCompleteRotationCurves(idleCurves, rightPath))
            {
                continue;
            }

            Quaternion rightPose = ReadRotation(idleCurves, rightPath, rightTransform.localRotation);
            mirroredPose[leftPath] = MirrorUsingRestPose(
                rightPose,
                rightTransform.localRotation,
                leftTransform.localRotation);
        }

        Require(mirroredPose.Count >= 4, "Player 리그에서 좌우 팔 본 쌍을 찾지 못했습니다.");
        return mirroredPose;
    }

    private static bool HasCompleteRotationCurves(
        Dictionary<EditorCurveBinding, AnimationCurve> curves,
        string path)
    {
        return HasCurve(curves, path, "m_LocalRotation.x")
               && HasCurve(curves, path, "m_LocalRotation.y")
               && HasCurve(curves, path, "m_LocalRotation.z")
               && HasCurve(curves, path, "m_LocalRotation.w");
    }

    private static bool HasCurve(
        Dictionary<EditorCurveBinding, AnimationCurve> curves,
        string path,
        string propertyName)
    {
        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
        return curves.TryGetValue(binding, out AnimationCurve curve) && curve != null;
    }

    private static bool IsRightArmPath(string path)
    {
        return string.Equals(path, RightShoulderPath, StringComparison.Ordinal)
               || path.StartsWith(RightShoulderPath + "/", StringComparison.Ordinal);
    }

    private static Quaternion ReadRotation(
        Dictionary<EditorCurveBinding, AnimationCurve> curves,
        string path,
        Quaternion fallback)
    {
        return NormalizeQuaternion(new Quaternion(
            ReadRotationComponent(curves, path, "m_LocalRotation.x", fallback.x),
            ReadRotationComponent(curves, path, "m_LocalRotation.y", fallback.y),
            ReadRotationComponent(curves, path, "m_LocalRotation.z", fallback.z),
            ReadRotationComponent(curves, path, "m_LocalRotation.w", fallback.w)));
    }

    private static float ReadRotationComponent(
        Dictionary<EditorCurveBinding, AnimationCurve> curves,
        string path,
        string propertyName,
        float fallback)
    {
        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
        return curves.TryGetValue(binding, out AnimationCurve curve) && curve != null
            ? curve.Evaluate(0f)
            : fallback;
    }

    private static Quaternion MirrorUsingRestPose(
        Quaternion rightPose,
        Quaternion rightRest,
        Quaternion leftRest)
    {
        int mirrorMode = 0;
        float bestDot = Mathf.Abs(Quaternion.Dot(MirrorQuaternion(rightRest, mirrorMode), leftRest));
        for (int candidateMode = 1; candidateMode <= 3; candidateMode++)
        {
            float candidateDot = Mathf.Abs(
                Quaternion.Dot(MirrorQuaternion(rightRest, candidateMode), leftRest));
            if (candidateDot > bestDot)
            {
                bestDot = candidateDot;
                mirrorMode = candidateMode;
            }
        }

        Quaternion mirrored = MirrorQuaternion(rightPose, mirrorMode);
        if (Quaternion.Dot(mirrored, leftRest) < 0f)
        {
            mirrored = NegateQuaternion(mirrored);
        }

        return NormalizeQuaternion(mirrored);
    }

    private static Quaternion MirrorQuaternion(Quaternion rotation, int mirrorMode)
    {
        switch (mirrorMode)
        {
            case 1:
                return new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
            case 2:
                return new Quaternion(-rotation.x, rotation.y, -rotation.z, rotation.w);
            case 3:
                return new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w);
            default:
                return rotation;
        }
    }

    private static Quaternion NegateQuaternion(Quaternion rotation)
    {
        return new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
    }

    private static Quaternion NormalizeQuaternion(Quaternion rotation)
    {
        float magnitude = Mathf.Sqrt(
            rotation.x * rotation.x
            + rotation.y * rotation.y
            + rotation.z * rotation.z
            + rotation.w * rotation.w);
        if (magnitude <= Mathf.Epsilon)
        {
            return Quaternion.identity;
        }

        float inverseMagnitude = 1f / magnitude;
        return new Quaternion(
            rotation.x * inverseMagnitude,
            rotation.y * inverseMagnitude,
            rotation.z * inverseMagnitude,
            rotation.w * inverseMagnitude);
    }

    private static bool TryGetMirroredRotationValue(
        Dictionary<string, Quaternion> mirroredPose,
        EditorCurveBinding binding,
        out float value)
    {
        value = 0f;
        if (!mirroredPose.TryGetValue(binding.path ?? string.Empty, out Quaternion rotation))
        {
            return false;
        }

        switch (binding.propertyName)
        {
            case "m_LocalRotation.x":
                value = rotation.x;
                return true;
            case "m_LocalRotation.y":
                value = rotation.y;
                return true;
            case "m_LocalRotation.z":
                value = rotation.z;
                return true;
            case "m_LocalRotation.w":
                value = rotation.w;
                return true;
            default:
                return false;
        }
    }

    private static AnimationCurve CloneCurve(AnimationCurve source)
    {
        AnimationCurve clone = new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
        return clone;
    }

    private static AnimationCurve ReverseCurve(AnimationCurve source, float clipLength)
    {
        Keyframe[] sourceKeys = source.keys;
        Keyframe[] reversedKeys = new Keyframe[sourceKeys.Length];
        for (int i = 0; i < sourceKeys.Length; i++)
        {
            Keyframe original = sourceKeys[sourceKeys.Length - 1 - i];
            Keyframe reversed = original;
            reversed.time = Mathf.Max(0f, clipLength - original.time);
            reversed.inTangent = -original.outTangent;
            reversed.outTangent = -original.inTangent;
            reversed.inWeight = original.outWeight;
            reversed.outWeight = original.inWeight;
            reversed.weightedMode = SwapWeightedMode(original.weightedMode);
            reversedKeys[i] = reversed;
        }

        AnimationCurve result = new AnimationCurve(reversedKeys)
        {
            preWrapMode = source.postWrapMode,
            postWrapMode = source.preWrapMode
        };
        return result;
    }

    private static WeightedMode SwapWeightedMode(WeightedMode mode)
    {
        switch (mode)
        {
            case WeightedMode.In:
                return WeightedMode.Out;
            case WeightedMode.Out:
                return WeightedMode.In;
            default:
                return mode;
        }
    }

    private static bool IsStationaryBodyBinding(EditorCurveBinding binding)
    {
        string path = binding.path ?? string.Empty;
        return string.Equals(path, "mixamorig:Hips", StringComparison.Ordinal)
               || path.IndexOf("mixamorig:Hips/mixamorig:Spine", StringComparison.Ordinal) >= 0;
    }

    private static AnimationClip CreateClip(AnimationClip generated, string path)
    {
        Require(AssetDatabase.LoadAssetAtPath<AnimationClip>(path) == null,
            $"기존 Handcart 애니메이션은 덮어쓸 수 없습니다: {path}");
        AssetDatabase.CreateAsset(generated, path);
        return generated;
    }

    private static void ConfigureController(
        AnimatorController controller,
        AnimationClip idleClip,
        AnimationClip forwardClip,
        AnimationClip backwardClip)
    {
        EnsureParameter(controller, MountedParameter, AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, DirectionParameter, AnimatorControllerParameterType.Float);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = FindState(stateMachine, IdleStateName);
        Require(idleState != null, $"Animator에서 {IdleStateName} 상태를 찾을 수 없습니다.");
        AnimatorState driveState = FindOrCreateState(stateMachine, DriveStateName, new Vector3(560f, 205f));
        AnimatorState legacyForwardState = FindState(stateMachine, ForwardStateName);
        AnimatorState legacyBackwardState = FindState(stateMachine, BackwardStateName);
        BlendTree driveBlendTree = GetOrCreateDriveBlendTree(controller, driveState);
        ConfigureDriveBlendTree(driveBlendTree, idleClip, forwardClip, backwardClip);
        ConfigureHandcartState(driveState, driveBlendTree);

        RemoveGeneratedAnyStateTransitions(
            stateMachine,
            driveState,
            legacyForwardState,
            legacyBackwardState);
        ClearTransitions(driveState);
        RemoveLegacyState(stateMachine, legacyForwardState);
        RemoveLegacyState(stateMachine, legacyBackwardState);

        AnimatorStateTransition entry = ConfigureTransition(stateMachine.AddAnyStateTransition(driveState));
        entry.canTransitionToSelf = false;
        entry.AddCondition(AnimatorConditionMode.If, 0f, MountedParameter);

        AnimatorStateTransition dismounted = ConfigureTransition(driveState.AddTransition(idleState));
        dismounted.canTransitionToSelf = false;
        dismounted.AddCondition(AnimatorConditionMode.IfNot, 0f, MountedParameter);
    }

    private static bool NeedsControllerConfiguration(
        AnimatorController controller,
        AnimationClip idleClip,
        AnimationClip forwardClip,
        AnimationClip backwardClip)
    {
        if (!HasParameter(controller, MountedParameter, AnimatorControllerParameterType.Bool)
            || !HasParameter(controller, DirectionParameter, AnimatorControllerParameterType.Float))
        {
            return true;
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = FindState(stateMachine, IdleStateName);
        AnimatorState driveState = FindState(stateMachine, DriveStateName);
        if (idleState == null
            || driveState == null
            || FindState(stateMachine, ForwardStateName) != null
            || FindState(stateMachine, BackwardStateName) != null
            || !(driveState.motion is BlendTree driveBlendTree)
            || !IsDriveBlendTreeConfigured(
                driveBlendTree,
                idleClip,
                forwardClip,
                backwardClip))
        {
            return true;
        }

        return !HasMountedEntryTransition(stateMachine, driveState)
               || !HasDismountTransition(driveState, idleState);
    }

    private static bool HasParameter(
        AnimatorController controller,
        string parameterName,
        AnimatorControllerParameterType parameterType)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].name, parameterName, StringComparison.Ordinal)
                && parameters[i].type == parameterType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMountedEntryTransition(
        AnimatorStateMachine stateMachine,
        AnimatorState destination)
    {
        AnimatorStateTransition[] transitions = stateMachine.anyStateTransitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition.destinationState == destination
                && !transition.canTransitionToSelf
                && transition.conditions.Length == 1
                && HasCondition(transition, MountedParameter, AnimatorConditionMode.If, 0f))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDismountTransition(AnimatorState source, AnimatorState idleState)
    {
        AnimatorStateTransition[] transitions = source.transitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition.destinationState == idleState
                && transition.conditions.Length == 1
                && HasCondition(transition, MountedParameter, AnimatorConditionMode.IfNot, 0f))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCondition(
        AnimatorStateTransition transition,
        string parameterName,
        AnimatorConditionMode conditionMode,
        float threshold)
    {
        AnimatorCondition[] conditions = transition.conditions;
        for (int i = 0; i < conditions.Length; i++)
        {
            AnimatorCondition condition = conditions[i];
            if (string.Equals(condition.parameter, parameterName, StringComparison.Ordinal)
                && condition.mode == conditionMode
                && Mathf.Abs(condition.threshold - threshold) <= 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private static BlendTree GetOrCreateDriveBlendTree(
        AnimatorController controller,
        AnimatorState driveState)
    {
        if (driveState.motion is BlendTree existingBlendTree)
        {
            return existingBlendTree;
        }

        BlendTree blendTree = new BlendTree
        {
            name = DriveBlendTreeName,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(blendTree, controller);
        driveState.motion = blendTree;
        return blendTree;
    }

    private static void ConfigureDriveBlendTree(
        BlendTree blendTree,
        AnimationClip idleClip,
        AnimationClip forwardClip,
        AnimationClip backwardClip)
    {
        blendTree.blendType = BlendTreeType.Simple1D;
        blendTree.blendParameter = DirectionParameter;
        blendTree.useAutomaticThresholds = false;
        blendTree.children = new[]
        {
            CreateBlendChild(backwardClip, -1f),
            CreateBlendChild(idleClip, 0f),
            CreateBlendChild(forwardClip, 1f)
        };
        EditorUtility.SetDirty(blendTree);
    }

    private static ChildMotion CreateBlendChild(Motion motion, float threshold)
    {
        return new ChildMotion
        {
            motion = motion,
            threshold = threshold,
            timeScale = 1f,
            cycleOffset = 0f,
            mirror = false
        };
    }

    private static bool IsDriveBlendTreeConfigured(
        BlendTree blendTree,
        AnimationClip idleClip,
        AnimationClip forwardClip,
        AnimationClip backwardClip)
    {
        if (blendTree.blendType != BlendTreeType.Simple1D
            || !string.Equals(blendTree.blendParameter, DirectionParameter, StringComparison.Ordinal))
        {
            return false;
        }

        ChildMotion[] children = blendTree.children;
        return children.Length == 3
               && HasBlendChild(children, backwardClip, -1f)
               && HasBlendChild(children, idleClip, 0f)
               && HasBlendChild(children, forwardClip, 1f);
    }

    private static bool HasBlendChild(ChildMotion[] children, Motion motion, float threshold)
    {
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].motion == motion
                && Mathf.Abs(children[i].threshold - threshold) <= 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private static void ConfigureHandcartState(AnimatorState state, Motion motion)
    {
        state.motion = motion;
        state.speed = 1f;
        state.speedParameter = MoveSpeedParameter;
        state.speedParameterActive = true;
        state.writeDefaultValues = true;
    }

    private static AnimatorStateTransition ConfigureTransition(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.12f;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.orderedInterruption = true;
        return transition;
    }

    private static void RemoveGeneratedAnyStateTransitions(
        AnimatorStateMachine stateMachine,
        AnimatorState driveState,
        AnimatorState legacyForwardState,
        AnimatorState legacyBackwardState)
    {
        AnimatorStateTransition[] transitions = stateMachine.anyStateTransitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            AnimatorState destination = transitions[i].destinationState;
            if (destination == driveState
                || destination == legacyForwardState
                || destination == legacyBackwardState)
            {
                stateMachine.RemoveAnyStateTransition(transitions[i]);
            }
        }
    }

    private static void RemoveLegacyState(
        AnimatorStateMachine stateMachine,
        AnimatorState legacyState)
    {
        if (legacyState == null)
        {
            return;
        }

        ClearTransitions(legacyState);
        stateMachine.RemoveState(legacyState);
    }

    private static void ClearTransitions(AnimatorState state)
    {
        AnimatorStateTransition[] transitions = state.transitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            state.RemoveTransition(transitions[i]);
        }
    }

    private static AnimatorState FindOrCreateState(
        AnimatorStateMachine stateMachine,
        string stateName,
        Vector3 position)
    {
        AnimatorState existing = FindState(stateMachine, stateName);
        return existing != null ? existing : stateMachine.AddState(stateName, position);
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        ChildAnimatorState[] states = stateMachine.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state != null
                && string.Equals(states[i].state.name, stateName, StringComparison.Ordinal))
            {
                return states[i].state;
            }
        }

        return null;
    }

    private static void EnsureParameter(
        AnimatorController controller,
        string parameterName,
        AnimatorControllerParameterType parameterType)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].name, parameterName, StringComparison.Ordinal))
            {
                Require(
                    parameters[i].type == parameterType,
                    $"Animator 파라미터 타입이 일치하지 않습니다: {parameterName}");
                return;
            }
        }

        controller.AddParameter(parameterName, parameterType);
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static void Validate(
        AnimationClip forwardClip,
        AnimationClip backwardClip,
        AnimatorController controller)
    {
        Require(forwardClip != null, "Handcart 전진 걷기 클립이 없습니다.");
        Require(backwardClip != null, "Handcart 후진 걷기 클립이 없습니다.");
        Require(controller != null, "Player Animator Controller가 없습니다.");
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        Require(idleClip != null, "Handcart 정지 블렌드에 필요한 Idle 클립이 없습니다.");
        Require(
            !NeedsControllerConfiguration(controller, idleClip, forwardClip, backwardClip),
            "Handcart Animator가 단일 연속 Drive 상태로 구성되지 않았습니다.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
#endif

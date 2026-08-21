using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PipeFluidCompatibilityValidation
{
    private const string ReportFileName = "PipeFluidCompatibilityValidation.txt";
    private const string TeePipePrefabPath = "Assets/MapObject/Fluid/Pipe/Pipe_T.prefab";
    private const string CrossPipePrefabPath = "Assets/MapObject/Fluid/Pipe/Pipe_Cross.prefab";
    private const string WaterPumpPrefabPath = "Assets/MapObject/Fluid/Water pump/Water pump.prefab";
    private const string OilDrillingMachinePrefabPath =
        "Assets/MapObject/InputOutputModule/Oil drilling machine/Oil drilling machine.prefab";
    private const int WaterFluidItemId = 1;
    private const int OilFluidItemId = 4;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private sealed class PipeAuditTarget
    {
        public Pipe pipe;
        public Vector2Int coordinate;
        public int quarterTurns;
    }

    private sealed class FluidConstraintResult
    {
        public readonly HashSet<int> itemIds = new HashSet<int>();
        public bool invocationSucceeded;
        public bool collectionSucceeded;
        public bool hasConstraint;
        public string errorMessage;
    }

    private sealed class EdgeDiagnostic
    {
        public Vector2Int direction;
        public FluidConstraintResult currentSide;
        public FluidConstraintResult oppositeSide;
        public bool hasNeighborPipe;
        public Pipe neighborPipe;
        public Quaternion neighborRotation;
        public bool neighborFacesBack;
    }

    private sealed class JunctionInvariantFixtureResult
    {
        public int totalCases;
        public int mixedCases;
        public int homogeneousCases;
        public int geometryChecks;
        public int identityChecks;
        public int wiringChecks;
        public int failureCount;
        public int errorCount;

        public bool Passed => failureCount <= 0 && errorCount <= 0;
    }

    private static readonly OpCode[] SingleByteOpCodes;
    private static readonly OpCode[] MultiByteOpCodes;

    static PipeFluidCompatibilityValidation()
    {
        BuildOpCodeTables(out SingleByteOpCodes, out MultiByteOpCodes);
    }

    [MenuItem("Tools/Diagnostics/Pipe Fluid Compatibility Audit %#p")]
    private static void AuditLoadedPipeJunctions()
    {
        string reportPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../Library", ReportFileName));
        StringBuilder details = new StringBuilder(4096);

        InstallationPlacementController controller =
            UnityEngine.Object.FindAnyObjectByType<InstallationPlacementController>(
                FindObjectsInactive.Include);
        MethodInfo compatibilityMethod = GetControllerMethod(
            "CanPipePlacementFluidConnectionsMatch",
            typeof(Vector2Int),
            typeof(Pipe),
            typeof(Quaternion),
            typeof(MapObject),
            typeof(int));
        MethodInfo networkMethod = GetControllerMethod(
            "TryCollectPipeNetworkFluidConstraintsExcludingConnection",
            typeof(Vector2Int),
            typeof(Pipe),
            typeof(Quaternion),
            typeof(MapObject),
            typeof(Vector2Int),
            typeof(Vector2Int),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType());
        MethodInfo rootNetworkMethod = GetControllerMethod(
            "TryCollectPipeNetworkFluidConstraints",
            typeof(Vector2Int),
            typeof(Pipe),
            typeof(Quaternion),
            typeof(MapObject),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType());
        MethodInfo fullNetworkMethod = GetControllerMethod(
            "TryCollectPipeNetworkFluidConstraints",
            typeof(Vector2Int),
            typeof(Pipe),
            typeof(Quaternion),
            typeof(MapObject),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType(),
            typeof(bool),
            typeof(Vector2Int),
            typeof(Vector2Int));
        MethodInfo adjacentMethod = GetControllerMethod(
            "TryCollectAdjacentPipeConnectionFluidConstraints",
            typeof(Vector2Int),
            typeof(Vector2Int),
            typeof(MapObject),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType());
        MethodInfo pipeLookupMethod = GetControllerMethod(
            "TryGetPipePlacementAtCoordinate",
            typeof(Vector2Int),
            typeof(MapObject),
            typeof(Pipe).MakeByRefType(),
            typeof(Quaternion).MakeByRefType());
        MethodInfo pipeAreaMergeMethod = GetControllerMethod(
            "TryMergePipeAreaFluidConstraintsAtPipeCoordinate",
            typeof(Vector2Int),
            typeof(Pipe),
            typeof(Quaternion),
            typeof(MapObject),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType());
        MethodInfo runtimeStorageMergeMethod = GetControllerMethod(
            "TryMergeRuntimeAdjacentFluidStorageConstraint",
            typeof(Vector2Int),
            typeof(Vector2Int),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType());
        Type pipeAreaBlockCandidateType = typeof(InstallationPlacementController).GetNestedType(
            "PipeAreaBlockCandidate",
            BindingFlags.NonPublic);
        MethodInfo adjacentPipeAreaCollectionMethod = GetControllerMethod(
            "TryCollectAdjacentPipeAreaFluidConstraints",
            typeof(Vector2Int),
            typeof(Vector2Int),
            typeof(MapObject),
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType());
        MethodInfo pipeAreaCandidateFluidMethod = pipeAreaBlockCandidateType != null
            ? GetControllerMethod(
                "ResolvePipeAreaCandidateFluidItemIds",
                typeof(Vector2Int),
                pipeAreaBlockCandidateType,
                typeof(ISet<int>))
            : null;
        MethodInfo candidateOutputMethod = GetControllerMethod(
            "TryGetCandidateOutputItemIds",
            typeof(Vector2Int),
            typeof(MapObject),
            typeof(int),
            typeof(ISet<int>));
        MethodInfo configuredOutputMethod = typeof(InputOutputModule).GetMethod(
            "TryAppendConfiguredOutputItemIds",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(ISet<int>) },
            null);
        MethodInfo constraintMergeMethod = GetControllerStaticMethod(
            "TryMergeFluidCompatibilityConstraint",
            typeof(HashSet<int>),
            typeof(bool).MakeByRefType(),
            typeof(ISet<int>));

        JunctionInvariantFixtureResult fixtureResult = RunDeterministicJunctionInvariantFixture(
            details,
            compatibilityMethod,
            rootNetworkMethod,
            networkMethod,
            fullNetworkMethod,
            adjacentMethod,
            pipeAreaMergeMethod,
            runtimeStorageMergeMethod,
            adjacentPipeAreaCollectionMethod,
            pipeAreaCandidateFluidMethod,
            candidateOutputMethod,
            configuredOutputMethod,
            constraintMergeMethod);

        if (controller == null
            || compatibilityMethod == null
            || networkMethod == null
            || rootNetworkMethod == null
            || fullNetworkMethod == null
            || adjacentMethod == null
            || pipeLookupMethod == null
            || pipeAreaMergeMethod == null
            || runtimeStorageMergeMethod == null
            || adjacentPipeAreaCollectionMethod == null
            || pipeAreaCandidateFluidMethod == null
            || candidateOutputMethod == null
            || configuredOutputMethod == null
            || constraintMergeMethod == null)
        {
            details.AppendLine("ERROR: one or more pipe compatibility diagnostic entry points are unavailable.");
            AppendAvailability(details, "Controller", controller != null);
            AppendAvailability(details, "CanPipePlacementFluidConnectionsMatch", compatibilityMethod != null);
            AppendAvailability(
                details,
                "TryCollectPipeNetworkFluidConstraintsExcludingConnection",
                networkMethod != null);
            AppendAvailability(details, "TryCollectPipeNetworkFluidConstraints(root)", rootNetworkMethod != null);
            AppendAvailability(details, "TryCollectPipeNetworkFluidConstraints(full)", fullNetworkMethod != null);
            AppendAvailability(details, "TryCollectAdjacentPipeConnectionFluidConstraints", adjacentMethod != null);
            AppendAvailability(details, "TryGetPipePlacementAtCoordinate", pipeLookupMethod != null);
            AppendAvailability(details, "TryMergePipeAreaFluidConstraintsAtPipeCoordinate", pipeAreaMergeMethod != null);
            AppendAvailability(details, "TryMergeRuntimeAdjacentFluidStorageConstraint", runtimeStorageMergeMethod != null);
            AppendAvailability(details, "TryCollectAdjacentPipeAreaFluidConstraints", adjacentPipeAreaCollectionMethod != null);
            AppendAvailability(details, "ResolvePipeAreaCandidateFluidItemIds", pipeAreaCandidateFluidMethod != null);
            AppendAvailability(details, "TryGetCandidateOutputItemIds", candidateOutputMethod != null);
            AppendAvailability(details, "TryAppendConfiguredOutputItemIds", configuredOutputMethod != null);
            AppendAvailability(details, "TryMergeFluidCompatibilityConstraint", constraintMergeMethod != null);
            WriteReport(reportPath, details.ToString());
            Debug.LogError($"[Pipe Fluid Audit] Validator unavailable. Report: {reportPath}");
            return;
        }

        Dictionary<int, string> itemNamesById = BuildItemNameLookup();
        List<PipeAuditTarget> targets = CollectActiveInstalledPipes(out int discoveredPipeCount);
        Dictionary<PipeVariantKind, int> variantCounts = new Dictionary<PipeVariantKind, int>();
        int invalidPipeCount = 0;
        int auditErrorCount = fixtureResult.errorCount + fixtureResult.failureCount;

        for (int index = 0; index < targets.Count; index++)
        {
            PipeAuditTarget target = targets[index];
            IncrementVariantCount(variantCounts, target.pipe.VariantKind);

            bool isCompatible;
            try
            {
                isCompatible = InvokeBoolean(
                    compatibilityMethod,
                    controller,
                    new object[]
                    {
                        target.coordinate,
                        target.pipe,
                        target.pipe.transform.rotation,
                        null,
                        target.pipe.GetConnectionMask(target.pipe.transform.rotation)
                    });
            }
            catch (Exception exception)
            {
                auditErrorCount++;
                details.AppendLine(
                    $"ERROR {FormatPipeIdentity(target)}: {SanitizeMessage(exception.GetBaseException().Message)}");
                continue;
            }

            if (isCompatible)
            {
                continue;
            }

            invalidPipeCount++;
            details.AppendLine();
            details.AppendLine($"INVALID {FormatPipeIdentity(target)}");
            details.AppendLine($"  object={GetHierarchyPath(target.pipe.transform)}");

            List<EdgeDiagnostic> edges = CollectEdgeDiagnostics(
                controller,
                target,
                networkMethod,
                adjacentMethod,
                pipeLookupMethod);
            bool exposedPairConflict = AppendEdgeDiagnostics(
                details,
                edges,
                itemNamesById,
                ref auditErrorCount);

            if (edges.Count <= 0)
            {
                details.AppendLine("  NOTE: the rejected pipe exposes no cardinal connection directions.");
            }
            else if (!exposedPairConflict)
            {
                details.AppendLine(
                    "  NOTE: rejection was reproduced, but available snapshots did not expose a complete "
                    + "disjoint ID pair. A CONFLICT/PARTIAL or ERROR side may contain the unresolved endpoint.");
            }
        }

        StringBuilder report = new StringBuilder(details.Length + 512);
        report.Append("Discovered=").Append(discoveredPipeCount)
            .Append(", ActiveInstalled=").Append(targets.Count)
            .Append(", Checked=").Append(targets.Count)
            .Append(", Invalid=").Append(invalidPipeCount)
            .Append(", AuditErrors=").Append(auditErrorCount)
            .AppendLine();
        report.Append("Fixture=").Append(fixtureResult.Passed ? "PASS" : "FAIL")
            .Append(", Cases=").Append(fixtureResult.totalCases)
            .Append(", Mixed=").Append(fixtureResult.mixedCases)
            .Append(", Homogeneous=").Append(fixtureResult.homogeneousCases)
            .Append(", GeometryChecks=").Append(fixtureResult.geometryChecks)
            .Append(", IdentityChecks=").Append(fixtureResult.identityChecks)
            .Append(", WiringChecks=").Append(fixtureResult.wiringChecks)
            .Append(", Failures=").Append(fixtureResult.failureCount)
            .Append(", Errors=").Append(fixtureResult.errorCount)
            .AppendLine();
        report.Append("Variants=").AppendLine(FormatVariantCounts(variantCounts));
        report.Append(details);
        WriteReport(reportPath, report.ToString());

        if (invalidPipeCount > 0 || auditErrorCount > 0)
        {
            Debug.LogError(
                $"[Pipe Fluid Audit] {invalidPipeCount}/{targets.Count} incompatible pipes, "
                + $"{auditErrorCount} audit errors. Report: {reportPath}");
        }
        else if (targets.Count <= 0)
        {
            Debug.LogWarning($"[Pipe Fluid Audit] No active installed pipes found. Report: {reportPath}");
        }
        else
        {
            Debug.Log(
                $"[Pipe Fluid Audit] All {targets.Count} active installed pipes are fluid-compatible. "
                + $"Report: {reportPath}");
        }
    }

    private static MethodInfo GetControllerMethod(string methodName, params Type[] parameterTypes)
    {
        return typeof(InstallationPlacementController).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            parameterTypes,
            null);
    }

    private static MethodInfo GetControllerStaticMethod(string methodName, params Type[] parameterTypes)
    {
        return typeof(InstallationPlacementController).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            parameterTypes,
            null);
    }

    private static bool InvokeBoolean(MethodInfo method, object target, object[] arguments)
    {
        object value = method.Invoke(target, arguments);
        if (value is bool result)
        {
            return result;
        }

        throw new InvalidOperationException($"{method.Name} did not return a Boolean value.");
    }

    private static JunctionInvariantFixtureResult RunDeterministicJunctionInvariantFixture(
        StringBuilder report,
        MethodInfo compatibilityMethod,
        MethodInfo rootNetworkMethod,
        MethodInfo excludingNetworkMethod,
        MethodInfo fullNetworkMethod,
        MethodInfo adjacentMethod,
        MethodInfo pipeAreaMergeMethod,
        MethodInfo runtimeStorageMergeMethod,
        MethodInfo adjacentPipeAreaCollectionMethod,
        MethodInfo pipeAreaCandidateFluidMethod,
        MethodInfo candidateOutputMethod,
        MethodInfo configuredOutputMethod,
        MethodInfo constraintMergeMethod)
    {
        JunctionInvariantFixtureResult result = new JunctionInvariantFixtureResult();
        report.AppendLine("DETERMINISTIC JUNCTION FIXTURE Water(1) <> Oil(4)");

        ValidateFixtureFluidDefinition(report, result, WaterFluidItemId, "Water");
        ValidateFixtureFluidDefinition(report, result, OilFluidItemId, "Oil");
        ValidateFixtureOutputIdentity(
            report,
            result,
            WaterPumpPrefabPath,
            WaterFluidItemId,
            OilFluidItemId,
            "Water pump");
        ValidateFixtureOutputIdentity(
            report,
            result,
            OilDrillingMachinePrefabPath,
            OilFluidItemId,
            WaterFluidItemId,
            "Oil drilling machine");
        ValidateFixtureMethodCall(
            report,
            result,
            compatibilityMethod,
            rootNetworkMethod,
            "placement gate -> root network collection");
        ValidateFixtureMethodCall(
            report,
            result,
            rootNetworkMethod,
            fullNetworkMethod,
            "root network collection -> full network walk");
        ValidateFixtureMethodCall(
            report,
            result,
            excludingNetworkMethod,
            fullNetworkMethod,
            "edge-excluding network collection -> full network walk");
        ValidateFixtureMethodCall(
            report,
            result,
            fullNetworkMethod,
            adjacentMethod,
            "full network walk -> adjacent branch collection");
        ValidateFixtureMethodCall(
            report,
            result,
            fullNetworkMethod,
            pipeAreaMergeMethod,
            "full network walk -> same-cell pipe-area collection");
        ValidateFixtureMethodCall(
            report,
            result,
            adjacentMethod,
            runtimeStorageMergeMethod,
            "adjacent branch collection -> runtime storage collection");
        ValidateFixtureMethodCall(
            report,
            result,
            adjacentMethod,
            constraintMergeMethod,
            "adjacent branch collection -> fluid-set intersection");
        ValidateFixtureMethodCall(
            report,
            result,
            pipeAreaMergeMethod,
            constraintMergeMethod,
            "same-cell pipe-area collection -> fluid-set intersection");
        ValidateFixtureMethodCall(
            report,
            result,
            runtimeStorageMergeMethod,
            constraintMergeMethod,
            "runtime storage collection -> fluid-set intersection");
        ValidateFixtureMethodCall(
            report,
            result,
            adjacentMethod,
            adjacentPipeAreaCollectionMethod,
            "adjacent branch collection -> pipe-area endpoint collection");
        ValidateFixtureMethodCall(
            report,
            result,
            adjacentPipeAreaCollectionMethod,
            pipeAreaCandidateFluidMethod,
            "pipe-area endpoint collection -> candidate fluid identity");
        ValidateFixtureMethodCall(
            report,
            result,
            pipeAreaMergeMethod,
            pipeAreaCandidateFluidMethod,
            "same-cell pipe-area collection -> candidate fluid identity");
        ValidateFixtureMethodCall(
            report,
            result,
            pipeAreaCandidateFluidMethod,
            candidateOutputMethod,
            "candidate fluid identity -> configured output identity");
        ValidateFixtureMethodCall(
            report,
            result,
            candidateOutputMethod,
            configuredOutputMethod,
            "configured output identity -> virtual module output");

        Pipe teePipe = LoadFixturePipe(TeePipePrefabPath);
        Pipe crossPipe = LoadFixturePipe(CrossPipePrefabPath);
        RunJunctionVariantFixture(
            report,
            result,
            teePipe,
            PipeVariantKind.Tee,
            3,
            constraintMergeMethod,
            TeePipePrefabPath);
        RunJunctionVariantFixture(
            report,
            result,
            crossPipe,
            PipeVariantKind.Cross,
            4,
            constraintMergeMethod,
            CrossPipePrefabPath);

        report.Append("  RESULT=").Append(result.Passed ? "PASS" : "FAIL")
            .Append(", cases=").Append(result.totalCases)
            .Append(", mixed=").Append(result.mixedCases)
            .Append(", homogeneous=").Append(result.homogeneousCases)
            .Append(", geometry=").Append(result.geometryChecks)
            .Append(", identity=").Append(result.identityChecks)
            .Append(", wiring=").Append(result.wiringChecks)
            .Append(", failures=").Append(result.failureCount)
            .Append(", errors=").Append(result.errorCount)
            .AppendLine();
        report.AppendLine();
        return result;
    }

    private static void ValidateFixtureFluidDefinition(
        StringBuilder report,
        JunctionInvariantFixtureResult result,
        int itemId,
        string expectedItemName)
    {
        ItemDefinition definition = FindItemDefinitionAsset(itemId);
        if (definition != null
            && InputOutputModule.IsFluidItemDefinition(definition)
            && string.Equals(definition.itemName, expectedItemName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        result.errorCount++;
        string actualName = definition != null ? definition.itemName : "<missing>";
        report.Append("  ERROR fluid definition id=").Append(itemId)
            .Append(", expected=").Append(expectedItemName)
            .Append(", actual=").AppendLine(actualName);
    }

    private static ItemDefinition FindItemDefinitionAsset(int itemId)
    {
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        for (int index = 0; index < guids.Length; index++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
            if (definition != null && definition.id == itemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static void ValidateFixtureOutputIdentity(
        StringBuilder report,
        JunctionInvariantFixtureResult result,
        string prefabPath,
        int requiredFluidItemId,
        int forbiddenFluidItemId,
        string label)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        InputOutputModule module = prefab != null
            ? prefab.GetComponent<InputOutputModule>() ?? prefab.GetComponentInChildren<InputOutputModule>(true)
            : null;
        if (module == null)
        {
            result.errorCount++;
            report.Append("  ERROR output identity prefab has no InputOutputModule: ")
                .AppendLine(prefabPath);
            return;
        }

        HashSet<int> outputItemIds = new HashSet<int>();
        bool foundOutput;
        try
        {
            foundOutput = module.TryAppendConfiguredOutputItemIds(outputItemIds);
        }
        catch (Exception exception)
        {
            result.errorCount++;
            report.Append("  ERROR output identity ").Append(label).Append(": ")
                .AppendLine(SanitizeMessage(exception.GetBaseException().Message));
            return;
        }

        result.identityChecks++;
        if (foundOutput
            && outputItemIds.Contains(requiredFluidItemId)
            && !outputItemIds.Contains(forbiddenFluidItemId))
        {
            return;
        }

        result.failureCount++;
        report.Append("  FAIL output identity ").Append(label)
            .Append(", required=").Append(requiredFluidItemId)
            .Append(", forbidden=").Append(forbiddenFluidItemId)
            .Append(", actual=").Append(FormatFixtureItemIds(outputItemIds))
            .AppendLine();
    }

    private static Pipe LoadFixturePipe(string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            return null;
        }

        Pipe pipe = prefab.GetComponent<Pipe>();
        return pipe != null ? pipe : prefab.GetComponentInChildren<Pipe>(true);
    }

    private static void RunJunctionVariantFixture(
        StringBuilder report,
        JunctionInvariantFixtureResult result,
        Pipe pipe,
        PipeVariantKind expectedVariant,
        int expectedBranchCount,
        MethodInfo constraintMergeMethod,
        string assetPath)
    {
        if (pipe == null)
        {
            result.errorCount++;
            report.Append("  ERROR fixture prefab has no Pipe: ").AppendLine(assetPath);
            return;
        }

        if (pipe.VariantKind != expectedVariant)
        {
            result.errorCount++;
            report.Append("  ERROR fixture prefab variant mismatch: ").Append(assetPath)
                .Append(", expected=").Append(expectedVariant)
                .Append(", actual=").AppendLine(pipe.VariantKind.ToString());
            return;
        }

        if (constraintMergeMethod == null)
        {
            result.errorCount++;
            report.Append("  ERROR constraint merger unavailable for ").AppendLine(expectedVariant.ToString());
            return;
        }

        List<Vector2Int> branchDirections = new List<Vector2Int>(4);
        for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
        {
            Quaternion rotation = Quaternion.Euler(0f, quarterTurns * 90f, 0f);
            branchDirections.Clear();
            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = CardinalDirections[directionIndex];
                if (pipe.HasConnectionTowards(rotation, direction))
                {
                    branchDirections.Add(direction);
                }
            }

            result.geometryChecks++;
            if (branchDirections.Count != expectedBranchCount)
            {
                result.failureCount++;
                report.Append("  FAIL geometry ").Append(expectedVariant)
                    .Append(" q=").Append(quarterTurns)
                    .Append(", expectedBranches=").Append(expectedBranchCount)
                    .Append(", actualBranches=").Append(branchDirections.Count)
                    .AppendLine();
                continue;
            }

            RunJunctionBranchAssignments(
                report,
                result,
                expectedVariant,
                quarterTurns,
                branchDirections,
                constraintMergeMethod);
        }
    }

    private static void RunJunctionBranchAssignments(
        StringBuilder report,
        JunctionInvariantFixtureResult result,
        PipeVariantKind variant,
        int quarterTurns,
        IReadOnlyList<Vector2Int> branchDirections,
        MethodInfo constraintMergeMethod)
    {
        int assignmentCount = 1 << branchDirections.Count;
        for (int assignmentMask = 0; assignmentMask < assignmentCount; assignmentMask++)
        {
            result.totalCases++;
            bool homogeneous = assignmentMask == 0 || assignmentMask == assignmentCount - 1;
            if (homogeneous)
            {
                result.homogeneousCases++;
            }
            else
            {
                result.mixedCases++;
            }

            HashSet<int> compatibleFluidItemIds = new HashSet<int>();
            bool hasFluidConstraint = false;
            int firstFluidItemId = ResolveFixtureAssignmentItemId(assignmentMask, 0);
            int expectedRejectionBranch = -1;
            int actualRejectionBranch = -1;

            try
            {
                for (int branchIndex = 0; branchIndex < branchDirections.Count; branchIndex++)
                {
                    int fluidItemId = ResolveFixtureAssignmentItemId(assignmentMask, branchIndex);
                    if (expectedRejectionBranch < 0 && fluidItemId != firstFluidItemId)
                    {
                        expectedRejectionBranch = branchIndex;
                    }

                    HashSet<int> candidateFluidItemIds = new HashSet<int> { fluidItemId };
                    if (!InvokeConstraintMerge(
                            constraintMergeMethod,
                            compatibleFluidItemIds,
                            ref hasFluidConstraint,
                            candidateFluidItemIds))
                    {
                        actualRejectionBranch = branchIndex;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                result.errorCount++;
                report.Append("  ERROR fixture invocation ").Append(variant)
                    .Append(" q=").Append(quarterTurns)
                    .Append(" assignment=0x").Append(assignmentMask.ToString("X"))
                    .Append(": ").AppendLine(SanitizeMessage(exception.GetBaseException().Message));
                continue;
            }

            bool casePassed = homogeneous
                ? actualRejectionBranch < 0
                  && hasFluidConstraint
                  && compatibleFluidItemIds.Count == 1
                  && compatibleFluidItemIds.Contains(firstFluidItemId)
                : actualRejectionBranch == expectedRejectionBranch;
            if (casePassed)
            {
                continue;
            }

            result.failureCount++;
            report.Append("  FAIL mixed-fluid invariant ").Append(variant)
                .Append(" q=").Append(quarterTurns)
                .Append(" assignment=0x").Append(assignmentMask.ToString("X"))
                .Append(", expectedRejectBranch=").Append(expectedRejectionBranch)
                .Append(", actualRejectBranch=").Append(actualRejectionBranch)
                .Append(", accepted=").Append(FormatFixtureItemIds(compatibleFluidItemIds))
                .AppendLine();
        }
    }

    private static int ResolveFixtureAssignmentItemId(int assignmentMask, int branchIndex)
    {
        return (assignmentMask & (1 << branchIndex)) != 0
            ? OilFluidItemId
            : WaterFluidItemId;
    }

    private static bool InvokeConstraintMerge(
        MethodInfo method,
        HashSet<int> compatibleFluidItemIds,
        ref bool hasFluidConstraint,
        ISet<int> candidateFluidItemIds)
    {
        object[] arguments =
        {
            compatibleFluidItemIds,
            hasFluidConstraint,
            candidateFluidItemIds
        };
        bool merged = InvokeBoolean(method, null, arguments);
        if (!(arguments[1] is bool updatedConstraintState))
        {
            throw new InvalidOperationException($"{method.Name} did not update its ref Boolean argument.");
        }

        hasFluidConstraint = updatedConstraintState;
        return merged;
    }

    private static string FormatFixtureItemIds(ICollection<int> itemIds)
    {
        if (itemIds == null || itemIds.Count <= 0)
        {
            return "[]";
        }

        List<int> sortedIds = new List<int>(itemIds);
        sortedIds.Sort();
        return "[" + string.Join(",", sortedIds) + "]";
    }

    private static void ValidateFixtureMethodCall(
        StringBuilder report,
        JunctionInvariantFixtureResult result,
        MethodInfo caller,
        MethodInfo target,
        string label)
    {
        if (!TryMethodCalls(caller, target, out bool callsTarget, out string errorMessage))
        {
            result.errorCount++;
            report.Append("  ERROR wiring ").Append(label).Append(": ").AppendLine(errorMessage);
            return;
        }

        result.wiringChecks++;
        if (callsTarget)
        {
            return;
        }

        result.failureCount++;
        report.Append("  FAIL wiring missing: ").AppendLine(label);
    }

    private static bool TryMethodCalls(
        MethodInfo caller,
        MethodInfo target,
        out bool callsTarget,
        out string errorMessage)
    {
        callsTarget = false;
        errorMessage = string.Empty;
        if (caller == null || target == null)
        {
            errorMessage = "caller or target method is unavailable";
            return false;
        }

        MethodBody body = caller.GetMethodBody();
        byte[] il = body?.GetILAsByteArray();
        if (il == null)
        {
            errorMessage = $"{caller.Name} has no readable IL body";
            return false;
        }

        try
        {
            int offset = 0;
            while (offset < il.Length)
            {
                OpCode opCode = ReadOpCode(il, ref offset);
                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    int metadataToken = ReadInt32(il, offset);
                    MethodBase calledMethod = caller.Module.ResolveMethod(
                        metadataToken,
                        caller.DeclaringType?.GetGenericArguments(),
                        caller.GetGenericArguments());
                    if (calledMethod != null
                        && calledMethod.Module == target.Module
                        && calledMethod.MetadataToken == target.MetadataToken)
                    {
                        callsTarget = true;
                        return true;
                    }
                }

                offset += GetOperandSize(opCode.OperandType, il, offset);
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage = SanitizeMessage(exception.GetBaseException().Message);
            return false;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        byte firstByte = il[offset++];
        if (firstByte != 0xFE)
        {
            OpCode singleByteOpCode = SingleByteOpCodes[firstByte];
            if (singleByteOpCode.Size <= 0)
            {
                throw new InvalidOperationException($"Unknown IL opcode 0x{firstByte:X2}.");
            }

            return singleByteOpCode;
        }

        if (offset >= il.Length)
        {
            throw new InvalidOperationException("Truncated multi-byte IL opcode.");
        }

        byte secondByte = il[offset++];
        OpCode multiByteOpCode = MultiByteOpCodes[secondByte];
        if (multiByteOpCode.Size <= 0)
        {
            throw new InvalidOperationException($"Unknown IL opcode 0xFE{secondByte:X2}.");
        }

        return multiByteOpCode;
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int operandOffset)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                int branchCount = ReadInt32(il, operandOffset);
                return 4 + branchCount * 4;
            default:
                throw new InvalidOperationException($"Unsupported IL operand type {operandType}.");
        }
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
        {
            throw new InvalidOperationException("Truncated IL operand.");
        }

        return bytes[offset]
               | bytes[offset + 1] << 8
               | bytes[offset + 2] << 16
               | bytes[offset + 3] << 24;
    }

    private static void BuildOpCodeTables(
        out OpCode[] singleByteOpCodes,
        out OpCode[] multiByteOpCodes)
    {
        singleByteOpCodes = new OpCode[256];
        multiByteOpCodes = new OpCode[256];
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        for (int index = 0; index < fields.Length; index++)
        {
            if (!(fields[index].GetValue(null) is OpCode opCode))
            {
                continue;
            }

            ushort value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
            {
                singleByteOpCodes[value] = opCode;
            }
            else if ((value & 0xFF00) == 0xFE00)
            {
                multiByteOpCodes[value & 0xFF] = opCode;
            }
        }
    }

    private static List<PipeAuditTarget> CollectActiveInstalledPipes(out int discoveredPipeCount)
    {
        Pipe[] pipes = UnityEngine.Object.FindObjectsByType<Pipe>(FindObjectsInactive.Include);
        discoveredPipeCount = pipes != null ? pipes.Length : 0;
        List<PipeAuditTarget> targets = new List<PipeAuditTarget>(discoveredPipeCount);
        if (pipes == null)
        {
            return targets;
        }

        for (int index = 0; index < pipes.Length; index++)
        {
            Pipe pipe = pipes[index];
            if (pipe == null
                || !pipe.gameObject.activeInHierarchy
                || !pipe.gameObject.scene.IsValid()
                || !pipe.gameObject.scene.isLoaded
                || pipe.name.IndexOf("Blueprint", StringComparison.OrdinalIgnoreCase) >= 0
                || !pipe.TryGetPlacementRuntime(out Vector2Int coordinate, out int quarterTurns))
            {
                continue;
            }

            targets.Add(new PipeAuditTarget
            {
                pipe = pipe,
                coordinate = coordinate,
                quarterTurns = quarterTurns
            });
        }

        targets.Sort(CompareTargets);
        return targets;
    }

    private static int CompareTargets(PipeAuditTarget first, PipeAuditTarget second)
    {
        int sceneComparison = string.CompareOrdinal(
            first.pipe.gameObject.scene.path,
            second.pipe.gameObject.scene.path);
        if (sceneComparison != 0)
        {
            return sceneComparison;
        }

        int xComparison = first.coordinate.x.CompareTo(second.coordinate.x);
        if (xComparison != 0)
        {
            return xComparison;
        }

        int yComparison = first.coordinate.y.CompareTo(second.coordinate.y);
        if (yComparison != 0)
        {
            return yComparison;
        }

        int sequenceComparison = first.pipe.RuntimePlacementSequence.CompareTo(
            second.pipe.RuntimePlacementSequence);
        return sequenceComparison != 0
            ? sequenceComparison
            : string.CompareOrdinal(
                GetHierarchyPath(first.pipe.transform),
                GetHierarchyPath(second.pipe.transform));
    }

    private static List<EdgeDiagnostic> CollectEdgeDiagnostics(
        InstallationPlacementController controller,
        PipeAuditTarget target,
        MethodInfo networkMethod,
        MethodInfo adjacentMethod,
        MethodInfo pipeLookupMethod)
    {
        List<EdgeDiagnostic> diagnostics = new List<EdgeDiagnostic>(4);
        Quaternion pipeRotation = target.pipe.transform.rotation;
        for (int index = 0; index < CardinalDirections.Length; index++)
        {
            Vector2Int direction = CardinalDirections[index];
            if (!target.pipe.HasConnectionTowards(pipeRotation, direction))
            {
                continue;
            }

            EdgeDiagnostic diagnostic = new EdgeDiagnostic
            {
                direction = direction,
                currentSide = CollectNetwork(
                    controller,
                    networkMethod,
                    target.coordinate,
                    target.pipe,
                    pipeRotation,
                    target.coordinate,
                    direction)
            };

            Vector2Int neighborCoordinate = target.coordinate + direction;
            if (TryResolvePipe(
                    controller,
                    pipeLookupMethod,
                    neighborCoordinate,
                    out Pipe neighborPipe,
                    out Quaternion neighborRotation,
                    out string lookupError))
            {
                diagnostic.hasNeighborPipe = true;
                diagnostic.neighborPipe = neighborPipe;
                diagnostic.neighborRotation = neighborRotation;
                diagnostic.neighborFacesBack = neighborPipe.HasConnectionTowards(neighborRotation, -direction);
                diagnostic.oppositeSide = CollectNetwork(
                    controller,
                    networkMethod,
                    neighborCoordinate,
                    neighborPipe,
                    neighborRotation,
                    neighborCoordinate,
                    -direction);
            }
            else
            {
                diagnostic.oppositeSide = string.IsNullOrEmpty(lookupError)
                    ? CollectAdjacent(controller, adjacentMethod, target.coordinate, direction)
                    : CreateInvocationError(lookupError);
            }

            diagnostics.Add(diagnostic);
        }

        return diagnostics;
    }

    private static FluidConstraintResult CollectNetwork(
        InstallationPlacementController controller,
        MethodInfo method,
        Vector2Int startCoordinate,
        Pipe startPipe,
        Quaternion startRotation,
        Vector2Int excludedCoordinate,
        Vector2Int excludedDirection)
    {
        FluidConstraintResult result = new FluidConstraintResult();
        object[] arguments =
        {
            startCoordinate,
            startPipe,
            startRotation,
            null,
            excludedCoordinate,
            excludedDirection,
            result.itemIds,
            false
        };

        try
        {
            result.collectionSucceeded = InvokeBoolean(method, controller, arguments);
            result.hasConstraint = arguments[7] is bool hasConstraint && hasConstraint;
            result.invocationSucceeded = true;
        }
        catch (Exception exception)
        {
            result.errorMessage = SanitizeMessage(exception.GetBaseException().Message);
        }

        return result;
    }

    private static FluidConstraintResult CollectAdjacent(
        InstallationPlacementController controller,
        MethodInfo method,
        Vector2Int pipeCoordinate,
        Vector2Int direction)
    {
        FluidConstraintResult result = new FluidConstraintResult();
        object[] arguments =
        {
            pipeCoordinate,
            direction,
            null,
            result.itemIds,
            false
        };

        try
        {
            result.collectionSucceeded = InvokeBoolean(method, controller, arguments);
            result.hasConstraint = arguments[4] is bool hasConstraint && hasConstraint;
            result.invocationSucceeded = true;
        }
        catch (Exception exception)
        {
            result.errorMessage = SanitizeMessage(exception.GetBaseException().Message);
        }

        return result;
    }

    private static bool TryResolvePipe(
        InstallationPlacementController controller,
        MethodInfo method,
        Vector2Int coordinate,
        out Pipe pipe,
        out Quaternion rotation,
        out string errorMessage)
    {
        pipe = null;
        rotation = Quaternion.identity;
        errorMessage = string.Empty;
        object[] arguments =
        {
            coordinate,
            null,
            null,
            Quaternion.identity
        };

        try
        {
            bool found = InvokeBoolean(method, controller, arguments);
            pipe = arguments[2] as Pipe;
            if (arguments[3] is Quaternion resolvedRotation)
            {
                rotation = resolvedRotation;
            }

            return found && pipe != null;
        }
        catch (Exception exception)
        {
            errorMessage = SanitizeMessage(exception.GetBaseException().Message);
            return false;
        }
    }

    private static FluidConstraintResult CreateInvocationError(string errorMessage)
    {
        return new FluidConstraintResult
        {
            errorMessage = SanitizeMessage(errorMessage)
        };
    }

    private static bool AppendEdgeDiagnostics(
        StringBuilder report,
        List<EdgeDiagnostic> diagnostics,
        Dictionary<int, string> itemNamesById,
        ref int auditErrorCount)
    {
        bool exposedPairConflict = false;
        for (int index = 0; index < diagnostics.Count; index++)
        {
            EdgeDiagnostic diagnostic = diagnostics[index];
            string neighbor = diagnostic.hasNeighborPipe
                ? $"pipe={diagnostic.neighborPipe.VariantKind}, facingBack={diagnostic.neighborFacesBack}, "
                  + $"rotation={FormatRotation(diagnostic.neighborRotation)}"
                : "fixed endpoint or empty cell";

            report.Append("  EDGE ").Append(FormatDirection(diagnostic.direction))
                .Append(" | current-excluding-edge=")
                .Append(FormatConstraint(diagnostic.currentSide, itemNamesById))
                .Append(" | opposite=")
                .Append(FormatConstraint(diagnostic.oppositeSide, itemNamesById))
                .Append(" | neighbor=").AppendLine(neighbor);

            if (!diagnostic.currentSide.invocationSucceeded)
            {
                auditErrorCount++;
            }

            if (!diagnostic.oppositeSide.invocationSucceeded)
            {
                auditErrorCount++;
            }

            if (HaveDisjointConstraints(diagnostic.currentSide, diagnostic.oppositeSide))
            {
                exposedPairConflict = true;
                report.Append("    CONFLICT two sides: ")
                    .Append(FormatItemIds(diagnostic.currentSide.itemIds, itemNamesById))
                    .Append(" <> ")
                    .AppendLine(FormatItemIds(diagnostic.oppositeSide.itemIds, itemNamesById));
            }
        }

        for (int firstIndex = 0; firstIndex < diagnostics.Count; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < diagnostics.Count; secondIndex++)
            {
                EdgeDiagnostic first = diagnostics[firstIndex];
                EdgeDiagnostic second = diagnostics[secondIndex];
                if (!HaveDisjointConstraints(first.oppositeSide, second.oppositeSide))
                {
                    continue;
                }

                exposedPairConflict = true;
                report.Append("    CONFLICT branches ")
                    .Append(FormatDirection(first.direction)).Append(' ')
                    .Append(FormatItemIds(first.oppositeSide.itemIds, itemNamesById))
                    .Append(" <> ")
                    .Append(FormatDirection(second.direction)).Append(' ')
                    .AppendLine(FormatItemIds(second.oppositeSide.itemIds, itemNamesById));
            }
        }

        return exposedPairConflict;
    }

    private static bool HaveDisjointConstraints(
        FluidConstraintResult first,
        FluidConstraintResult second)
    {
        if (first == null
            || second == null
            || !first.invocationSucceeded
            || !second.invocationSucceeded
            || !first.hasConstraint
            || !second.hasConstraint
            || first.itemIds.Count <= 0
            || second.itemIds.Count <= 0)
        {
            return false;
        }

        foreach (int itemId in first.itemIds)
        {
            if (second.itemIds.Contains(itemId))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatConstraint(
        FluidConstraintResult result,
        Dictionary<int, string> itemNamesById)
    {
        if (result == null)
        {
            return "ERROR(null result)";
        }

        if (!result.invocationSucceeded)
        {
            return $"ERROR({result.errorMessage ?? "unknown invocation failure"})";
        }

        if (!result.collectionSucceeded)
        {
            return $"CONFLICT/PARTIAL {FormatItemIds(result.itemIds, itemNamesById)}";
        }

        return !result.hasConstraint || result.itemIds.Count <= 0
            ? "UNCONSTRAINED []"
            : $"OK {FormatItemIds(result.itemIds, itemNamesById)}";
    }

    private static string FormatItemIds(
        ICollection<int> itemIds,
        Dictionary<int, string> itemNamesById)
    {
        if (itemIds == null || itemIds.Count <= 0)
        {
            return "[]";
        }

        List<int> sortedIds = new List<int>(itemIds);
        sortedIds.Sort();
        StringBuilder builder = new StringBuilder(sortedIds.Count * 16);
        builder.Append('[');
        for (int index = 0; index < sortedIds.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            int itemId = sortedIds[index];
            builder.Append(itemId).Append(':');
            if (itemNamesById.TryGetValue(itemId, out string itemName)
                && !string.IsNullOrWhiteSpace(itemName))
            {
                builder.Append(itemName);
            }
            else
            {
                builder.Append("<unknown>");
            }
        }

        return builder.Append(']').ToString();
    }

    private static Dictionary<int, string> BuildItemNameLookup()
    {
        Dictionary<int, string> namesById = new Dictionary<int, string>();
        if (GameManager.Instance != null && GameManager.Instance.ItemManger != null)
        {
            AddDefinitionNames(GameManager.Instance.ItemManger.ItemDefinitions, namesById);
        }

        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        for (int index = 0; index < guids.Length; index++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
            AddDefinitionName(
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath),
                namesById);
        }

        return namesById;
    }

    private static void AddDefinitionNames(
        IReadOnlyList<ItemDefinition> definitions,
        Dictionary<int, string> namesById)
    {
        if (definitions == null)
        {
            return;
        }

        for (int index = 0; index < definitions.Count; index++)
        {
            AddDefinitionName(definitions[index], namesById);
        }
    }

    private static void AddDefinitionName(
        ItemDefinition definition,
        Dictionary<int, string> namesById)
    {
        if (definition == null || definition.id < 0 || namesById.ContainsKey(definition.id))
        {
            return;
        }

        string displayName = ItemDefinitionLookup.GetDisplayName(definition);
        namesById.Add(
            definition.id,
            string.IsNullOrWhiteSpace(displayName) ? definition.name : displayName);
    }

    private static string FormatPipeIdentity(PipeAuditTarget target)
    {
        Quaternion rotation = target.pipe.transform.rotation;
        return $"scene={target.pipe.gameObject.scene.name}, coordinate={target.coordinate}, "
               + $"variant={target.pipe.VariantKind}, quarterTurns={target.quarterTurns}, "
               + $"rotation={FormatRotation(rotation)}, mask=0x{target.pipe.GetConnectionMask(rotation):X}, "
               + $"sequence={target.pipe.RuntimePlacementSequence}";
    }

    private static string FormatRotation(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        return $"euler({euler.x:F1},{euler.y:F1},{euler.z:F1})/"
               + $"q({rotation.x:F4},{rotation.y:F4},{rotation.z:F4},{rotation.w:F4})";
    }

    private static string FormatDirection(Vector2Int direction)
    {
        if (direction == Vector2Int.up)
        {
            return "Up(0,1)";
        }

        if (direction == Vector2Int.right)
        {
            return "Right(1,0)";
        }

        if (direction == Vector2Int.down)
        {
            return "Down(0,-1)";
        }

        return direction == Vector2Int.left ? "Left(-1,0)" : direction.ToString();
    }

    private static string GetHierarchyPath(Transform target)
    {
        List<string> names = new List<string>(8);
        while (target != null)
        {
            names.Add(target.name);
            target = target.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static string SanitizeMessage(string message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? "unknown error"
            : message.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static void IncrementVariantCount(
        Dictionary<PipeVariantKind, int> counts,
        PipeVariantKind variantKind)
    {
        counts.TryGetValue(variantKind, out int currentCount);
        counts[variantKind] = currentCount + 1;
    }

    private static string FormatVariantCounts(Dictionary<PipeVariantKind, int> counts)
    {
        Array variants = Enum.GetValues(typeof(PipeVariantKind));
        StringBuilder builder = new StringBuilder(64);
        for (int index = 0; index < variants.Length; index++)
        {
            PipeVariantKind variantKind = (PipeVariantKind)variants.GetValue(index);
            if (index > 0)
            {
                builder.Append(", ");
            }

            counts.TryGetValue(variantKind, out int count);
            builder.Append(variantKind).Append('=').Append(count);
        }

        return builder.ToString();
    }

    private static void AppendAvailability(StringBuilder report, string label, bool available)
    {
        report.Append("  ").Append(label).Append('=').AppendLine(available ? "OK" : "MISSING");
    }

    private static void WriteReport(string reportPath, string contents)
    {
        string directoryPath = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(reportPath, contents ?? string.Empty);
    }
}

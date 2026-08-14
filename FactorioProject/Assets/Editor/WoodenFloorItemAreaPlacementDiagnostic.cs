using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class WoodenFloorItemAreaPlacementDiagnostic
{
    private const int WoodenFloorItemId = 41;
    private const string ReportPath = "Library/WoodenFloorItemAreaPlacementDiagnostic.txt";
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Diagnostics/Wooden Floor ItemArea Placement %#F9")]
    private static void Run()
    {
        StringBuilder report = new StringBuilder(4096);
        report.AppendLine($"Wooden Floor ItemArea placement diagnostic - {DateTime.Now:O}");
        report.AppendLine($"Playing: {Application.isPlaying}");

        InstallationPlacementController placementController =
            UnityEngine.Object.FindAnyObjectByType<InstallationPlacementController>();
        ItemDefinition woodenFloorDefinition = FindWoodenFloorDefinition();
        if (placementController == null || woodenFloorDefinition == null || woodenFloorDefinition.mapObject == null)
        {
            report.AppendLine($"Missing runtime data: controller={placementController != null}, "
                              + $"definition={woodenFloorDefinition != null}, "
                              + $"mapObject={woodenFloorDefinition != null && woodenFloorDefinition.mapObject != null}");
            WriteReport(report);
            return;
        }

        InstallationObject woodenFloor = woodenFloorDefinition.mapObject as InstallationObject;
        report.AppendLine($"Definition: [{woodenFloorDefinition.id}] {woodenFloorDefinition.itemName}");
        report.AppendLine($"Prefab: {woodenFloorDefinition.mapObject.name} ({woodenFloorDefinition.mapObject.GetType().Name})");
        report.AppendLine($"MapFilter: {(woodenFloor != null ? woodenFloor.MapFilter.ToString() : "not InstallationObject")} "
                          + $"({(woodenFloor != null ? (int)woodenFloor.MapFilter : -1)})");

        MethodInfo itemAreaMethod = typeof(InstallationPlacementController).GetMethod(
            "CoordinateHasInputItemAreaBlockForPlacement",
            InstancePrivate);
        MethodInfo normalAreaMethod = typeof(InstallationPlacementController).GetMethod(
            "CoordinateHasNormalInputOutputAreaBlockForPlacement",
            InstancePrivate);
        MethodInfo blockTypeMethod = typeof(InstallationPlacementController).GetMethod(
            "CanPlacePreviewOnTargetBlockType",
            InstancePrivate);
        MethodInfo targetMethod = typeof(InstallationPlacementController).GetMethod(
            "TryResolvePlaceableInstallPreviewTarget",
            InstancePrivate);
        MethodInfo previewBlockMethod = typeof(InstallationPlacementController).GetMethod(
            "CanPlacePreviewOnBlock",
            InstancePrivate);
        MethodInfo gridBlockMethod = typeof(InstallationPlacementController).GetMethod(
            "CanPlaceActiveDefinitionFromGridCoordinate",
            InstancePrivate);
        MethodInfo previewOverlapMethod = typeof(InstallationPlacementController).GetMethod(
            "CanOverlapCompatiblePlacementItemAreas",
            InstancePrivate);
        FieldInfo activeDefinitionField = typeof(InstallationPlacementController).GetField(
            "activeInstallDefinition",
            InstancePrivate);

        AppendPreviewOverlapResult(
            report,
            placementController,
            woodenFloorDefinition.mapObject,
            previewOverlapMethod);

        Block[] blocks = UnityEngine.Object.FindObjectsByType<Block>(FindObjectsInactive.Exclude);
        Array.Sort(blocks, CompareBlockCoordinates);

        int checkedAreaCount = 0;
        int failedCoreCount = 0;
        int failedTargetCount = 0;
        int failedVisualCount = 0;
        object previousDefinition = activeDefinitionField?.GetValue(placementController);
        activeDefinitionField?.SetValue(placementController, woodenFloorDefinition);
        try
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                Block block = blocks[i];
                if (block == null)
                {
                    continue;
                }

                bool registeredItemArea = InputOutputModuleItemAreaController.CoordinateIsItemArea(block.Coordinate);
                bool runtimeItemArea = InputOutputModule.CoordinateIsRuntimeInputItemBlock(block.Coordinate);
                bool resolvedItemArea = InvokeItemAreaCheck(itemAreaMethod, placementController, block.Coordinate);
                if (!registeredItemArea && !runtimeItemArea && !resolvedItemArea)
                {
                    continue;
                }

                AppendBlockResult(
                    report,
                    placementController,
                    woodenFloorDefinition.mapObject,
                    blockTypeMethod,
                    targetMethod,
                    previewBlockMethod,
                    gridBlockMethod,
                    block,
                    registeredItemArea,
                    runtimeItemArea,
                    resolvedItemArea,
                    ref checkedAreaCount,
                    ref failedCoreCount,
                    ref failedTargetCount,
                    ref failedVisualCount);
            }

            if (checkedAreaCount == 0)
            {
                Block emptySyntheticBlock = FindSyntheticEmptyTestBlock(blocks);
                Block resourceSyntheticBlock = FindSyntheticResourceTestBlock(blocks);
                List<InputOutputModuleItemAreaBinding> syntheticBindings =
                    new List<InputOutputModuleItemAreaBinding>(2);
                if (emptySyntheticBlock != null)
                {
                    syntheticBindings.Add(
                        new InputOutputModuleItemAreaBinding(emptySyntheticBlock.Coordinate, WoodenFloorItemId));
                }

                if (resourceSyntheticBlock != null && resourceSyntheticBlock != emptySyntheticBlock)
                {
                    syntheticBindings.Add(
                        new InputOutputModuleItemAreaBinding(resourceSyntheticBlock.Coordinate, WoodenFloorItemId));
                }

                if (syntheticBindings.Count > 0)
                {
                    GameObject markerObject = new GameObject("Wooden Floor ItemArea Diagnostic Marker");
                    InputOutputModuleItemAreaController marker =
                        markerObject.AddComponent<InputOutputModuleItemAreaController>();
                    marker.Configure(syntheticBindings);
                    try
                    {
                        report.AppendLine(
                            "No live ItemArea was loaded; running synthetic empty/resource ItemArea checks.");
                        AppendSyntheticBlockResult(
                            "empty",
                            emptySyntheticBlock,
                            report,
                            placementController,
                            woodenFloorDefinition.mapObject,
                            itemAreaMethod,
                            blockTypeMethod,
                            targetMethod,
                            previewBlockMethod,
                            gridBlockMethod,
                            ref checkedAreaCount,
                            ref failedCoreCount,
                            ref failedTargetCount,
                            ref failedVisualCount);
                        AppendSyntheticBlockResult(
                            "resource",
                            resourceSyntheticBlock,
                            report,
                            placementController,
                            woodenFloorDefinition.mapObject,
                            itemAreaMethod,
                            blockTypeMethod,
                            targetMethod,
                            previewBlockMethod,
                            gridBlockMethod,
                            ref checkedAreaCount,
                            ref failedCoreCount,
                            ref failedTargetCount,
                            ref failedVisualCount);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(markerObject);
                    }
                }
            }

            AppendSyntheticFuelAndOutputAreaResults(
                blocks,
                report,
                placementController,
                woodenFloorDefinition.mapObject,
                normalAreaMethod,
                blockTypeMethod,
                targetMethod,
                previewBlockMethod,
                gridBlockMethod,
                ref checkedAreaCount,
                ref failedCoreCount,
                ref failedTargetCount,
                ref failedVisualCount);
        }
        finally
        {
            activeDefinitionField?.SetValue(placementController, previousDefinition);
        }

        report.AppendLine(
            $"Summary: checkedAreas={checkedAreaCount}, failedCore={failedCoreCount}, "
            + $"failedTarget={failedTargetCount}, failedVisual={failedVisualCount}");
        WriteReport(report);
    }

    [MenuItem("Tools/Diagnostics/Wooden Floor ItemArea Placement %#F9", true)]
    private static bool ValidateRun()
    {
        return Application.isPlaying;
    }

    private static ItemDefinition FindWoodenFloorDefinition()
    {
        ItemManager itemManager = UnityEngine.Object.FindAnyObjectByType<ItemManager>();
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null)
        {
            return null;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition definition = definitions[i];
            if (definition != null && definition.id == WoodenFloorItemId)
            {
                return definition;
            }
        }

        return null;
    }

    private static void AppendPreviewOverlapResult(
        StringBuilder report,
        InstallationPlacementController placementController,
        MapObject woodenFloorPrefab,
        MethodInfo previewOverlapMethod)
    {
        ItemManager itemManager = UnityEngine.Object.FindAnyObjectByType<ItemManager>();
        List<ItemDefinition> definitions = itemManager != null ? itemManager.ItemDefinitions : null;
        if (definitions == null || previewOverlapMethod == null)
        {
            report.AppendLine("Preview overlap check: unavailable");
            return;
        }

        HashSet<InputOutputModule.RectGridBlockType> testedBlockTypes =
            new HashSet<InputOutputModule.RectGridBlockType>();
        for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            ItemDefinition definition = definitions[definitionIndex];
            MapObject mapObject = definition != null ? definition.mapObject : null;
            InputOutputModule module = mapObject as InputOutputModule;
            if (module == null && mapObject != null)
            {
                module = mapObject.GetComponent<InputOutputModule>();
            }

            IReadOnlyList<InputOutputModule.RectGridBlockPlacement> placements =
                module != null ? module.RectGridPlacements : null;
            if (placements == null)
            {
                continue;
            }

            for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
            {
                InputOutputModule.RectGridBlockType blockType = placements[placementIndex].blockType;
                if (!InputOutputModule.AllowsDirectAreaInteraction(blockType)
                    || InputOutputModule.AllowsPipeAreaInteraction(blockType)
                    || !testedBlockTypes.Add(blockType))
                {
                    continue;
                }

                Vector2Int coordinate = Vector2Int.zero;
                object[] arguments =
                {
                    coordinate,
                    woodenFloorPrefab,
                    InputOutputModule.RectGridBlockType.None,
                    coordinate,
                    0,
                    mapObject,
                    blockType,
                    coordinate,
                    0
                };
                bool result = previewOverlapMethod.Invoke(placementController, arguments) is bool allowed && allowed;
                report.AppendLine(
                    $"Preview overlap check: floor + [{definition.id}] {definition.itemName} "
                    + $"{blockType} => {result}");
            }
        }

        if (testedBlockTypes.Count == 0)
        {
            report.AppendLine("Preview overlap check: no normal ItemArea prefab found");
        }
    }

    private static bool InvokeItemAreaCheck(
        MethodInfo method,
        InstallationPlacementController controller,
        Vector2Int coordinate)
    {
        return method != null
               && method.Invoke(controller, new object[] { coordinate, null }) is bool result
               && result;
    }

    private static void AppendBlockResult(
        StringBuilder report,
        InstallationPlacementController placementController,
        MapObject woodenFloorPrefab,
        MethodInfo blockTypeMethod,
        MethodInfo targetMethod,
        MethodInfo previewBlockMethod,
        MethodInfo gridBlockMethod,
        Block block,
        bool registeredItemArea,
        bool runtimeItemArea,
        bool resolvedItemArea,
        ref int itemAreaCount,
        ref int failedCoreCount,
        ref int failedTargetCount,
        ref int failedVisualCount)
    {
        itemAreaCount++;
        bool publicCoreResult = placementController.CanPlaceInstalledObjectAt(
            block.Coordinate,
            woodenFloorPrefab,
            0);
        bool privateCoreResult = InvokeCorePlacementCheck(
            blockTypeMethod,
            placementController,
            block,
            woodenFloorPrefab);
        bool targetResult = InvokeTargetCheck(
            targetMethod,
            placementController,
            block,
            woodenFloorPrefab,
            out Block resolvedAnchor,
            out int resolvedQuarterTurns);
        bool previewVisualResult = InvokePreviewBlockCheck(
            previewBlockMethod,
            placementController,
            block,
            woodenFloorPrefab);
        bool gridVisualResult = InvokeGridBlockCheck(
            gridBlockMethod,
            placementController,
            block);

        if (!publicCoreResult || !privateCoreResult)
        {
            failedCoreCount++;
        }

        if (!targetResult)
        {
            failedTargetCount++;
        }

        if (!previewVisualResult || !gridVisualResult)
        {
            failedVisualCount++;
        }

        report.AppendLine(
            $"[{block.Coordinate.x},{block.Coordinate.y}] "
            + $"type={block.Type}, object={DescribeObject(block.MapObject)}, "
            + $"registered={registeredItemArea}, runtime={runtimeItemArea}, resolved={resolvedItemArea}, "
            + $"stack={block.GetInputAreaCenterItemCount()}, "
            + $"corePublic={publicCoreResult}, corePrivate={privateCoreResult}, "
            + $"target={targetResult}, previewVisual={previewVisualResult}, gridVisual={gridVisualResult}, "
            + $"anchor={DescribeBlock(resolvedAnchor)}, turns={resolvedQuarterTurns}");
    }

    private static void AppendSyntheticBlockResult(
        string label,
        Block block,
        StringBuilder report,
        InstallationPlacementController placementController,
        MapObject woodenFloorPrefab,
        MethodInfo itemAreaMethod,
        MethodInfo blockTypeMethod,
        MethodInfo targetMethod,
        MethodInfo previewBlockMethod,
        MethodInfo gridBlockMethod,
        ref int itemAreaCount,
        ref int failedCoreCount,
        ref int failedTargetCount,
        ref int failedVisualCount)
    {
        if (block == null)
        {
            report.AppendLine($"Synthetic {label} ItemArea check: no matching loaded block");
            return;
        }

        report.AppendLine($"Synthetic {label} ItemArea check:");
        AppendBlockResult(
            report,
            placementController,
            woodenFloorPrefab,
            blockTypeMethod,
            targetMethod,
            previewBlockMethod,
            gridBlockMethod,
            block,
            InputOutputModuleItemAreaController.CoordinateIsItemArea(block.Coordinate),
            InputOutputModule.CoordinateIsRuntimeInputItemBlock(block.Coordinate),
            InvokeItemAreaCheck(itemAreaMethod, placementController, block.Coordinate),
            ref itemAreaCount,
            ref failedCoreCount,
            ref failedTargetCount,
            ref failedVisualCount);
    }

    private static void AppendSyntheticFuelAndOutputAreaResults(
        IReadOnlyList<Block> blocks,
        StringBuilder report,
        InstallationPlacementController placementController,
        MapObject woodenFloorPrefab,
        MethodInfo normalAreaMethod,
        MethodInfo blockTypeMethod,
        MethodInfo targetMethod,
        MethodInfo previewBlockMethod,
        MethodInfo gridBlockMethod,
        ref int checkedAreaCount,
        ref int failedCoreCount,
        ref int failedTargetCount,
        ref int failedVisualCount)
    {
        Block block = FindSyntheticEmptyTestBlock(blocks);
        if (block == null)
        {
            report.AppendLine("Synthetic fuel/output ItemArea checks: no empty loaded block");
            return;
        }

        GameObject energyMarkerObject = new GameObject("Wooden Floor EnergyArea Diagnostic Marker");
        InputOutputModuleEnergyAreaController energyMarker =
            energyMarkerObject.AddComponent<InputOutputModuleEnergyAreaController>();
        energyMarker.Configure(ItemDefinition.EnergyType.Burn, new[] { block.Coordinate });
        try
        {
            report.AppendLine("Synthetic fuel-input ItemArea check:");
            AppendNormalAreaBlockResult(
                block,
                report,
                placementController,
                woodenFloorPrefab,
                normalAreaMethod,
                blockTypeMethod,
                targetMethod,
                previewBlockMethod,
                gridBlockMethod,
                ref checkedAreaCount,
                ref failedCoreCount,
                ref failedTargetCount,
                ref failedVisualCount);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(energyMarkerObject);
        }

        GameObject outputMarkerObject = new GameObject("Wooden Floor OutputArea Diagnostic Marker");
        InputOutputModuleOutputAreaController outputMarker =
            outputMarkerObject.AddComponent<InputOutputModuleOutputAreaController>();
        outputMarker.Configure(new[] { block.Coordinate });
        try
        {
            report.AppendLine("Synthetic output ItemArea check:");
            AppendNormalAreaBlockResult(
                block,
                report,
                placementController,
                woodenFloorPrefab,
                normalAreaMethod,
                blockTypeMethod,
                targetMethod,
                previewBlockMethod,
                gridBlockMethod,
                ref checkedAreaCount,
                ref failedCoreCount,
                ref failedTargetCount,
                ref failedVisualCount);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(outputMarkerObject);
        }
    }

    private static void AppendNormalAreaBlockResult(
        Block block,
        StringBuilder report,
        InstallationPlacementController placementController,
        MapObject woodenFloorPrefab,
        MethodInfo normalAreaMethod,
        MethodInfo blockTypeMethod,
        MethodInfo targetMethod,
        MethodInfo previewBlockMethod,
        MethodInfo gridBlockMethod,
        ref int checkedAreaCount,
        ref int failedCoreCount,
        ref int failedTargetCount,
        ref int failedVisualCount)
    {
        AppendBlockResult(
            report,
            placementController,
            woodenFloorPrefab,
            blockTypeMethod,
            targetMethod,
            previewBlockMethod,
            gridBlockMethod,
            block,
            true,
            false,
            InvokeItemAreaCheck(normalAreaMethod, placementController, block.Coordinate),
            ref checkedAreaCount,
            ref failedCoreCount,
            ref failedTargetCount,
            ref failedVisualCount);
    }

    private static Block FindSyntheticEmptyTestBlock(IReadOnlyList<Block> blocks)
    {
        if (blocks == null)
        {
            return null;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null && block.Type == Block.BlockType.Ground && block.MapObject == null)
            {
                return block;
            }
        }

        return null;
    }

    private static Block FindSyntheticResourceTestBlock(IReadOnlyList<Block> blocks)
    {
        if (blocks == null)
        {
            return null;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block != null
                && block.Type == Block.BlockType.Ground
                && (block.MapObject is Resource || block.Resource != null))
            {
                return block;
            }
        }

        return null;
    }

    private static bool InvokeCorePlacementCheck(
        MethodInfo method,
        InstallationPlacementController controller,
        Block block,
        MapObject footprintSource)
    {
        return method != null
               && method.Invoke(
                   controller,
                   new object[]
                   {
                       block,
                       footprintSource,
                       InputOutputModule.RectGridBlockType.None,
                       (Vector2Int?)block.Coordinate,
                       0,
                       null,
                       false
                   }) is bool result
               && result;
    }

    private static bool InvokeTargetCheck(
        MethodInfo method,
        InstallationPlacementController controller,
        Block block,
        MapObject previewToIgnore,
        out Block anchorBlock,
        out int quarterTurns)
    {
        anchorBlock = null;
        quarterTurns = 0;
        if (method == null)
        {
            return false;
        }

        object[] arguments = { block, previewToIgnore, 0, false, null, 0 };
        bool result = method.Invoke(controller, arguments) is bool resolved && resolved;
        anchorBlock = arguments[4] as Block;
        quarterTurns = arguments[5] is int value ? value : 0;
        return result;
    }

    private static bool InvokePreviewBlockCheck(
        MethodInfo method,
        InstallationPlacementController controller,
        Block block,
        MapObject previewToIgnore)
    {
        return method != null
               && method.Invoke(
                   controller,
                   new object[] { block, previewToIgnore, (int?)0, false }) is bool result
               && result;
    }

    private static bool InvokeGridBlockCheck(
        MethodInfo method,
        InstallationPlacementController controller,
        Block block)
    {
        return method != null
               && method.Invoke(controller, new object[] { block, false, false }) is bool result
               && result;
    }

    private static int CompareBlockCoordinates(Block left, Block right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int xComparison = left.Coordinate.x.CompareTo(right.Coordinate.x);
        return xComparison != 0 ? xComparison : left.Coordinate.y.CompareTo(right.Coordinate.y);
    }

    private static string DescribeObject(MapObject mapObject)
    {
        return mapObject != null ? $"{mapObject.name}:{mapObject.GetType().Name}" : "none";
    }

    private static string DescribeBlock(Block block)
    {
        return block != null ? $"[{block.Coordinate.x},{block.Coordinate.y}]" : "none";
    }

    private static void WriteReport(StringBuilder report)
    {
        string fullPath = Path.GetFullPath(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "Library");
        File.WriteAllText(fullPath, report.ToString());
        Debug.Log($"Wooden Floor ItemArea placement diagnostic written to '{fullPath}'.\n{report}");
    }
}

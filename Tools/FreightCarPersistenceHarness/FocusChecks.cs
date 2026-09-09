using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector2 { }
}
static class Input
{
    public static bool Left, Right;
    public static Vector2 mousePosition;
    public static bool GetMouseButtonDown(int button) => button == 0 ? Left : Right;
}
class MapObject { public bool Active = true; }
class Block { }

// Only input/raycast and marker sinks are substituted. The mounted focus
// selection, clearing and refresh methods are extracted from PlayerController.
public partial class MountedFocusProbe
{
    MapObject mountedPinnedFocusTarget;
    Block mountedPinnedFocusFallbackBlock, standaloneInteractionAreaFocusBlock;
    readonly List<Block> mountedPinnedFocusBlocks = new(), mouseFocusBlocks = new();
    readonly Dictionary<Block, MapObject> interactionFocusTargetOverrides = new();
    MapObject hitTarget;
    bool overUi, mouseVisible, interactionVisible;
    bool IsPointerOverMouseFocusBlockingUi(Vector2 pointer) => overUi;
    bool TryResolveMouseFocusedMapObject(Vector2 pointer, out MapObject target, out Block block)
    { target = hitTarget; block = null; return target != null; }
    bool IsValidMouseFocusMapObject(MapObject target) => target != null && target.Active;
    bool AppendMapObjectFocusBlocks(MapObject target, Block fallback, List<Block> result)
    { if (!IsValidMouseFocusMapObject(target)) return false; result.Add(new Block()); return true; }
    void ResetInteractionButtonFocusTargets() { }
    void CacheInteractionButtonFocusTargets(List<Block> blocks, Block extra) { }
    void SetFocusedBlocks(List<Block> blocks) => interactionVisible = blocks != null && blocks.Count > 0;
    void SetMouseFocusedBlocks(List<Block> blocks, MapObject target = null) => mouseVisible = blocks != null && blocks.Count > 0;

    public static int Run()
    {
        int checks = 0;
        void Expect(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        var probe = new MountedFocusProbe();
        var train = new MapObject();
        probe.mountedPinnedFocusTarget = train;
        probe.RefreshMountedPinnedInteractionFocus();
        Expect(probe.interactionVisible, "Mounting initially focuses the vehicle.");
        foreach (bool right in new[] { false, true })
        {
            Input.Left = !right; Input.Right = right;
            probe.hitTarget = train;
            probe.RefreshMountedPinnedMouseFocus();
            Expect(probe.mouseVisible && probe.interactionVisible, "Clicking an object focuses it while mounted.");
            probe.overUi = true; probe.hitTarget = null;
            probe.RefreshMountedPinnedMouseFocus();
            Expect(probe.mountedPinnedFocusTarget == train, "UI clicks preserve the current selection.");
            probe.overUi = false;
            probe.RefreshMountedPinnedMouseFocus();
            Expect(probe.mountedPinnedFocusTarget == null && !probe.mouseVisible && !probe.interactionVisible,
                "Empty-ground click clears both mounted mouse and interaction focus.");
            Input.Left = Input.Right = false;
            probe.RefreshMountedPinnedInteractionFocus();
            probe.RefreshMountedPinnedMouseFocus();
            Expect(!probe.interactionVisible && probe.mountedPinnedFocusTarget == null,
                "Later refresh must not reselect the mounted train.");
        }
        probe.mountedPinnedFocusTarget = train; train.Active = false;
        probe.RefreshMountedPinnedInteractionFocus();
        Expect(probe.mountedPinnedFocusTarget == null && !probe.interactionVisible,
            "Removing a focused object clears stale mounted focus.");
        return checks;
    }
}

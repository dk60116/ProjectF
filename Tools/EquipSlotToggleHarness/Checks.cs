using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// Unity object lifetime and button events are ports; equipment visibility/binding uses the real HUD partial.
namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public sealed class GameObject
    {
        private readonly Dictionary<Type, object> components = new();
        public readonly RectTransform transform;
        public GameObject() { transform = new RectTransform { gameObject = this }; }
        public bool activeSelf = true;
        public void SetActive(bool active) => activeSelf = active;
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out var value) ? value as T : null;
        public T AddComponent<T>() where T : class, new()
        { var value = new T(); components.Add(typeof(T), value); return value; }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public readonly ClickEvent onClick = new();
        public readonly GameObject gameObject = new();
        public RectTransform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public sealed class ClickEvent
    {
        private readonly List<Action> listeners = new();
        public int Count => listeners.Count;
        public void AddListener(Action listener) => listeners.Add(listener);
        public void RemoveListener(Action listener) => listeners.RemoveAll(value => value == listener);
        public void Invoke() { for (int i = 0; i < listeners.Count; i++) listeners[i](); }
    }
}
public class ItemSlot
{
    public readonly GameObject gameObject = new();
    public RectTransform transform => gameObject.transform;
    public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
}
public partial class PlayerHUD
{
    private float mapEditButtonExpandDuration = .08f, mapEditButtonExpandStagger = .02f;
    internal void Configure(Button button, params ItemSlot[] slots) { eqipSlotOpenButton = button; equipSlots = slots; }
    internal void Initialize() => InitializeEquipmentSlots();
    internal void Disable() => ReleaseEquipmentSlots();
    internal DG.Tweening.Sequence Animation => equipmentSlotAnimation;
    internal void Finish() => equipmentSlotAnimation?.Complete();
    internal void Advance(float seconds) => equipmentSlotAnimation?.Advance(seconds);
}
internal static class Checks
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static int Run(string[] args)
    {
        var hud = new PlayerHUD();
        var button = new Button();
        var first = new ItemSlot();
        var second = new ItemSlot();
        second.gameObject.SetActive(false);
        hud.Configure(button, first, null, second);
        hud.Initialize();
        Check(!first.gameObject.activeSelf && !second.gameObject.activeSelf, "HUD starts with equipment slots collapsed");
        Check(button.gameObject.activeSelf && button.onClick.Count == 1, "Opener stays available and has exactly one listener");
        hud.Initialize();
        Check(button.onClick.Count == 1, "Awake and OnEnable cannot add duplicate listeners");
        button.onClick.Invoke();
        hud.Finish();
        Check(first.gameObject.activeSelf && second.gameObject.activeSelf, "One click expands all assigned slots, skipping empty entries");
        button.onClick.Invoke();
        hud.Finish();
        Check(!first.gameObject.activeSelf && !second.gameObject.activeSelf, "The next click collapses the same slots");
        button.onClick.Invoke();
        hud.Disable();
        Check(!first.gameObject.activeSelf && !second.gameObject.activeSelf && button.onClick.Count == 0,
            "Disabling HUD collapses slots and releases its listener");
        button.onClick.Invoke();
        Check(!first.gameObject.activeSelf, "Disabled HUD cannot respond to an old click listener");
        hud.Initialize();
        button.onClick.Invoke();
        hud.Finish();
        Check(first.gameObject.activeSelf && second.gameObject.activeSelf && button.onClick.Count == 1,
            "Re-enabled HUD binds once and expands on the first click");
        var replacement = new Button();
        hud.Configure(replacement, first, second);
        hud.Initialize();
        Check(button.onClick.Count == 0 && replacement.onClick.Count == 1, "Changing the serialized opener releases the old button");
        replacement.onClick.Invoke();
        hud.Finish();
        Check(first.gameObject.activeSelf && second.gameObject.activeSelf, "Replacement button operates the same slots");
        hud.Disable();
        AnimationChecks();
        InventoryBindingChecks();
        hud.Configure(null, (ItemSlot[])null);
        hud.Initialize(); hud.Disable();
        Check(replacement.onClick.Count == 0, "Missing optional button/slot references are handled safely");

        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        string source = File.ReadAllText(Path.Combine(root, "FactorioProject/Assets/Scripts/HUD/PlayerHUD.cs"));
        Check(Regex.IsMatch(source, @"protected override void Awake\(\)\s*\{[\s\S]*?InitializeEquipmentSlots\(\);"),
            "Actual PlayerHUD Awake initializes the equipment controls");
        Check(Regex.IsMatch(source, @"private void OnEnable\(\)\s*\{\s*ResolveHudReferences\(true\);\s*InitializeEquipmentSlots\(\);"),
            "Actual PlayerHUD OnEnable rebinds equipment controls");
        Check(Regex.IsMatch(source, @"private void OnDisable\(\)\s*\{\s*ReleaseEquipmentSlots\(\);"),
            "Actual PlayerHUD OnDisable releases equipment controls");
        Console.WriteLine($"PASS: {checks} production PlayerHUD equipment expansion and inventory binding checks");
        return 0;
    }

    private static void AnimationChecks()
    {
        var hud = new PlayerHUD();
        var button = new Button();
        var openerHover = button.gameObject.AddComponent<HUDButtonHoverTween>();
        var first = new ItemSlot();
        var second = new ItemSlot();
        first.transform.parent = second.transform.parent = button.transform;
        first.transform.Sibling = 0; second.transform.Sibling = 1;
        var firstPosition = new Vector3(-80f, 0f, 0f);
        var secondPosition = new Vector3(-160f, 0f, 0f);
        first.transform.localPosition = firstPosition;
        second.transform.localPosition = secondPosition;
        hud.Configure(button, second, null, first);
        hud.Initialize();
        button.onClick.Invoke();
        var opening = hud.Animation;
        Check(opening != null && opening.IndependentUpdate, "Equipment animation uses unscaled time like map-edit controls");
        Check(first.transform.localPosition == Vector3.zero && second.transform.localPosition == Vector3.zero,
            "Hidden slots start at the opener instead of appearing at their final positions");
        hud.Advance(.001f);
        Check(first.gameObject.activeSelf && !second.gameObject.activeSelf,
            "Slots appear in sibling order, not serialized array order");
        Check(!first.GetComponent<CanvasGroup>().interactable && !first.GetComponent<CanvasGroup>().blocksRaycasts,
            "Moving slots cannot intercept clicks or drags");
        Check(first.transform.localPosition != firstPosition, "Expansion actually moves the slot toward its destination");
        var firstMotion = (DG.Tweening.MoveTween)((DG.Tweening.Sequence)opening.Items[0].Child).Items[1].Child;
        Check(firstMotion.Duration == .08f && firstMotion.Ease == DG.Tweening.Ease.OutCubic,
            "Expansion shares map-edit duration and OutCubic easing");
        Check(opening.Items[1].Start == .02f, "Expansion uses the same stagger as map-edit controls");
        hud.Advance(.03f);
        Check(second.gameObject.activeSelf, "The second slot appears after its stagger delay");
        hud.Finish();
        Check(first.transform.localPosition == firstPosition && second.transform.localPosition == secondPosition,
            "Expansion ends at the originally authored layout");
        Check(first.GetComponent<CanvasGroup>().blocksRaycasts && first.GetComponent<CanvasGroup>().interactable,
            "Finished slots regain interaction");
        button.onClick.Invoke();
        var closing = hud.Animation;
        var lastMotion = (DG.Tweening.MoveTween)((DG.Tweening.Sequence)closing.Items[0].Child).Items[1].Child;
        Check(lastMotion.Duration == .08f * .85f && lastMotion.Ease == DG.Tweening.Ease.InCubic,
            "Collapse shares map-edit timing and InCubic easing");
        hud.Advance(.001f);
        Check(second.transform.localPosition != secondPosition && first.transform.localPosition == firstPosition,
            "Collapse moves the last sibling first");
        hud.Finish();
        Check(!first.gameObject.activeSelf && !second.gameObject.activeSelf
            && first.transform.localPosition == firstPosition && second.transform.localPosition == secondPosition,
            "Collapse hides slots and restores cached geometry for the next opening");
        button.onClick.Invoke(); hud.Advance(.03f);
        var interrupted = hud.Animation;
        var midpoint = first.transform.localPosition;
        button.onClick.Invoke();
        Check(interrupted.Killed && first.transform.localPosition == midpoint,
            "Rapid reversal kills the old sequence without snapping an already visible slot");
        hud.Advance(.01f);
        button.onClick.Invoke(); hud.Finish();
        Check(first.gameObject.activeSelf && second.gameObject.activeSelf
            && first.transform.localPosition == firstPosition && second.transform.localPosition == secondPosition,
            "Repeated reversal ends in the last requested state without losing original positions");
        button.onClick.Invoke(); hud.Advance(.01f);
        var interruptedClose = hud.Animation;
        hud.Disable();
        interruptedClose.Complete();
        Check(hud.Animation == null && interruptedClose.Killed && !first.gameObject.activeSelf && !second.gameObject.activeSelf,
            "Disabling HUD kills delayed callbacks so slots cannot reappear later");
        Check(openerHover.Resets > 0, "Opener hover geometry is reset before child movement");

        var authored = first.GetComponent<CanvasGroup>();
        authored.interactable = false;
        hud = new PlayerHUD();
        hud.Configure(button, first, second);
        hud.Initialize(); button.onClick.Invoke(); hud.Finish();
        Check(!authored.interactable && authored.blocksRaycasts, "Authored non-interactable slots remain non-interactable after animation");
        for (int i = 0; i < 10; i++) { button.onClick.Invoke(); hud.Finish(); }
        Check(first.transform.localPosition == firstPosition && second.transform.localPosition == secondPosition,
            "Repeated toggles do not accumulate position drift");
        hud.Disable();
    }

    private static void InventoryBindingChecks()
    {
        var player = new Player();
        GameManager.Instance = new() { Player = player };
        var first = new BagSlot(); var third = new BagSlot();
        var firstAmmo = new BagSlot(); var thirdAmmo = new BagSlot();
        var button = new Button(); var hud = new PlayerHUD();
        hud.Configure(button, first, null, third);
        hud.ConfigureAmmo(firstAmmo, null, thirdAmmo);
        hud.Initialize(); hud.BindInventory();
        Check(!firstAmmo.gameObject.activeSelf && !thirdAmmo.gameObject.activeSelf, "Ammo slots stay hidden with no selected weapon");
        Check(first.Bag == player.Equipment && third.Index == 2, "Equipment binding keeps authored array indices across null slots");
        Check(!first.gameObject.activeSelf && first.Capacity == 1, "Binding collapsed slots does not make them visible");
        hud.BindInventory();
        Check(player.Equipment.ListenerCount == 1, "Repeated binding subscribes once");
        player.Equipment.Ids[2] = 17; player.Equipment.Counts[2] = 1; player.Equipment.Notify();
        Check(third.ItemId == 17 && third.Count == 1, "Changes update icons while collapsed");
        button.onClick.Invoke(); hud.Finish();
        third.Click();
        Check(third.Selected && thirdAmmo.gameObject.activeSelf && !firstAmmo.gameObject.activeSelf,
            "Selecting an ammo weapon highlights it and reveals only its paired ammo slot");
        Check(thirdAmmo.Bag == player.Ammunition && thirdAmmo.Index == 0 && thirdAmmo.Capacity == 100,
            "Visible ammo slot binds shared ammunition storage with capacity 100");
        player.Ammunition.Ids[0] = 23; player.Ammunition.Counts[0] = 77; player.Ammunition.Notify();
        Check(thirdAmmo.ItemId == 23 && thirdAmmo.Count == 77, "Ammo changes refresh the displayed stack");
        button.onClick.Invoke(); hud.Finish();
        Check(!thirdAmmo.gameObject.activeSelf, "Collapsing equipment hides the selected ammo slot");
        button.onClick.Invoke(); hud.Finish();
        Check(thirdAmmo.gameObject.activeSelf, "Reopening equipment restores the selected ammo slot");
        third.Click();
        Check(!third.Selected && !thirdAmmo.gameObject.activeSelf, "Clicking the selected weapon deselects it and hides ammo");
        player.Equipment.Ids[0] = 19; player.Equipment.Counts[0] = 1; player.Equipment.Notify();
        first.Click();
        Check(first.Selected && !firstAmmo.gameObject.activeSelf && !thirdAmmo.gameObject.activeSelf,
            "Non-ammo equipment keeps every ammo slot hidden");
        var position = third.transform.localPosition;
        player.Equipment.Ids[2] = 18; player.Equipment.Notify();
        Check(third.gameObject.activeSelf && third.ItemId == 18 && third.transform.localPosition == position,
            "Inventory refresh preserves expanded visibility and geometry");
        var old = player.Equipment; player.Equipment = new(); hud.BindInventory();
        Check(old.ListenerCount == 0 && player.Equipment.ListenerCount == 1, "Changing player storage detaches its old listener");
        hud.UnbindInventory();
        Check(player.Ammunition.ListenerCount == 0 && !thirdAmmo.gameObject.activeSelf,
            "HUD teardown releases ammunition listeners and hides ammo UI");
        Check(player.Equipment.ListenerCount == 0 && first.Bag == null && third.Count == 0, "HUD teardown clears inventory binding without deleting player storage");
        player.Equipment.Ids[0] = 19; player.Equipment.Counts[0] = 1; hud.BindInventory();
        Check(first.ItemId == 19 && player.Equipment.ListenerCount == 1, "Reopening the HUD restores existing equipment");
        hud.UnbindInventory(); hud.Disable(); GameManager.Instance = null;
    }
}

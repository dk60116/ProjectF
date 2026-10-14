using System;
using System.Reflection;
using ProjectF.Monsters;
using ProjectF.Presentation;
using ProjectF.Simulation.Monsters;
using UnityEngine;

internal static class HealthBarChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static WorldHealthBarView Bar(object owner, string field) =>
        (WorldHealthBarView)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static RectTransform Fill(WorldHealthBarView bar) => (RectTransform)bar.Root.Children[0];
    private static void LateUpdate(MonsterWorld world)
    {
        object view = typeof(MonsterWorld).GetField("presentation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
        view.GetType().GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
    }

    internal static void Run()
    {
        MonsterWorld.Current?.Dispose();
        GameManager.Instance.Player = null;
        MapObjectTickManager.SimulationPaused = MapObjectTickManager.WaitingForWorldLoad = false;
        SaveManager.GameplayInputBlocked = false;
        Time.unscaledTime = 0f;
        var camera = Camera.main = new Camera();
        camera.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
        var terrain = new TerrainGenerator();
        var definition = new MonsterDefinition();
        var root = FixtureMonsterPrefab.Create();
        root.transform.position = new Vector3(2f, 0f, 3f);
        var renderer = root.AddComponent<SkinnedMeshRenderer>();
        renderer.localBounds = new Bounds(new Vector3(2f, 1.5f, 3f), new Vector3(1f, 3f, 1f));
        var monster = new MonsterInstance(root, definition, 1);
        MonsterWorld.Register(monster, definition, terrain);
        var world = MonsterWorld.Current;
        LateUpdate(world);
        Check(Bar(monster, "healthBar") == null, "Pristine idle monsters allocate no health-bar hierarchy");
        monster.SetCurrentHealth(monster.MaxHealth * .5f);
        LateUpdate(world);
        var bar = Bar(monster, "healthBar");
        Check(bar != null && bar.Root.gameObject.activeSelf, "Damaged monsters show the shared Animal health bar");
        Check(Math.Abs(Fill(bar).anchorMax.x - .5f) < .00001f, "Monster fill reflects current HP fraction");
        Check(bar.Root.sizeDelta == new Vector2(.9f, .1f), "Monster bars retain Animal dimensions");
        Check(bar.Root.GetType() == typeof(RectTransform) && bar.Root.parent != root.transform,
            "Monster bars are hosted outside rotating/scaled model roots");
        Check(Math.Abs(bar.Root.position.y - 3.25f) < .00001f, "Monster bar sits above the highest visible model bound");
        Check(bar.Root.rotation.YawDegrees == 30f, "Monster bar faces the presentation camera");
        Check(!bar.Root.gameObject.GetComponent<UnityEngine.UI.Image>().raycastTarget
            && !Fill(bar).gameObject.GetComponent<UnityEngine.UI.Image>().raycastTarget,
            "Overhead bars do not intercept monster selection clicks");
        Check(Fill(bar).gameObject.GetComponent<UnityEngine.UI.Image>().color == new Color(.78f, .12f, .1f, 1f),
            "Monster HP uses Animal's red fill color");
        MapObjectTickManager.SimulationPaused = true;
        camera.transform.rotation = Quaternion.Euler(0f, 80f, 0f);
        LateUpdate(world);
        Check(bar.Root.rotation.YawDegrees == 80f, "Billboarding continues while simulation is paused");
        MapObjectTickManager.SimulationPaused = false;
        monster.ApplySimulationPose(new Vector3(7f, 1f, 9f), Quaternion.Euler(0f, 180f, 0f));
        LateUpdate(world);
        Check(Math.Abs(bar.Root.position.x - 6.9f) < .0001f && Math.Abs(bar.Root.position.y - 4.25f) < .0001f,
            "Health bar follows the authoritative moving body instead of a stale spawn position");
        for (int i = 0; i < 5; i++) world.RefreshHealthBars(terrain.transform, camera);
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) world.RefreshHealthBars(terrain.transform, camera);
        Check(GC.GetAllocatedBytesForCurrentThread() == allocation, "Settled health-bar presentation allocates no per-frame GC");
        monster.SetCurrentHealth(monster.MaxHealth);
        LateUpdate(world);
        Check(bar.Root.gameObject.activeSelf, "Restored full HP keeps the shared one-second grace period");
        Time.unscaledTime = 1.1f;
        LateUpdate(world);
        Check(!bar.Root.gameObject.activeSelf, "Full-health bars hide after one second");
        monster.NotifyAttackAnimationStarted(terrain.transform, camera);
        Check(bar.Root.gameObject.activeSelf, "Healthy attackers temporarily show their bar like Animal");
        Time.unscaledTime = 2.2f;
        LateUpdate(world);
        Check(!bar.Root.gameObject.activeSelf, "Healthy attack notification expires without new attacks");
        monster.SetCurrentHealth(monster.MaxHealth * .25f);
        LateUpdate(world);
        root.SetActive(false);
        LateUpdate(world);
        Check(!bar.Root.gameObject.activeSelf, "Inactive monster views hide their overhead bar");
        root.SetActive(true);
        LateUpdate(world);
        Check(bar.Root.gameObject.activeSelf, "Reactivated damaged monsters restore their bar");
        monster.SetCurrentHealth(0f);
        Check(!bar.Root.gameObject.activeSelf, "Monster death hides the bar immediately without another frame");
        monster.SetCurrentHealth(monster.MaxHealth * .5f);
        LateUpdate(world);
        monster.Dispose();
        Check(!bar.Root.gameObject.activeSelf && Bar(monster, "healthBar") == null, "Disposal releases the monster bar");
        world.Dispose();

        Time.unscaledTime = 0f;
        var animal = new Animal();
        var animalBar = AnimalWorldHealthBar.Create(animal, null);
        var animalView = Bar(animalBar, "view");
        Check(!animalView.Root.gameObject.activeSelf, "Animal remains hidden at pristine full HP");
        animalBar.NotifyAttackAnimationStarted();
        Check(animalView.Root.gameObject.activeSelf, "Animal attack notification still shows the same shared bar");
        Time.unscaledTime = 1.1f;
        animalBar.Refresh();
        Check(!animalView.Root.gameObject.activeSelf, "Animal full-health attack bar still expires");
        animal.TakeDamage(50f, Vector3.zero);
        animalBar.Refresh();
        Check(animalView.Root.gameObject.activeSelf && Math.Abs(Fill(animalView).anchorMax.x - .5f) < .00001f,
            "Animal damage still refreshes its HP fraction after extraction");
        animal.TakeDamage(100f, Vector3.zero);
        animalBar.Refresh();
        Check(!animalView.Root.gameObject.activeSelf, "Animal death still hides its bar");
        animalView.Dispose();
        Console.WriteLine($"PASS: {checks} production Animal/monster overhead health-bar checks");
    }
}

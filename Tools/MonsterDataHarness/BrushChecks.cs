using System;
using ProjectF.Monsters;
using UnityEngine;
using UnityEngine.EventSystems;

internal static class BrushChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static void Tick(float x, float z, bool escape = false)
    {
        Time.frameCount++;
        Input.Click = !escape;
        Input.Escape = escape;
        Input.mousePosition = new Vector2(x, z);
        MonsterSpawnBrush.Tick();
    }
    public static void Run()
    {
        MonsterWorld.Current?.Dispose();
        MonsterSpawnBrush.Stop();
        var terrain = new TerrainGenerator();
        var definition = new MonsterDefinition();
        GameManager.Instance.Player = new Player();
        Camera.main = new Camera();
        AnimalAIWorld.Instance = new AnimalAIWorld();
        MapObjectTickManager.WaitingForWorldLoad = MapObjectTickManager.SimulationPaused = false;
        SaveManager.GameplayInputBlocked = GameManager.TextInputFocused = false;
        Application.isPlaying = false;
        Check(!MonsterSpawnBrush.Begin(definition, 1), "Brush cannot start outside Play Mode");
        Application.isPlaying = true;
        Check(!MonsterSpawnBrush.Begin(definition, 99), "Brush requires a saved variant");
        Check(MonsterSpawnBrush.Begin(definition, 1) && MonsterSpawnBrush.IsPainting(definition, 1)
            && MonsterSpawnBrush.BlocksGameplayInput, "Brush claims game input for the selected variant");
        terrain.Blocks[new Vector2Int(4, 5)] = new Block { WorldPosition = new Vector3(4, 2, 5) };
        Check(MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(4.2f, 99, 5.1f), out var spawned, out _)
            && spawned.transform.position.x == 4.2f && spawned.transform.position.z == 5.1f
            && spawned.transform.position.y == 2 && MonsterWorld.Current.IsOccupied(spawned.transform.position, .01f),
            "Exact spawn preserves clicked XZ, uses ground height and enters the ECS world");
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(4.2f, 0, 5.1f), out _, out _),
            "Brush does not overlap existing monsters");
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(60, 0, 60), out _, out _),
            "Unloaded clicked ground is rejected, not relocated");
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(float.NaN, 0, 5), out _, out _), "Invalid coordinates are rejected");
        terrain.Blocks[new Vector2Int(0, 0)] = new Block();
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(0, 0, 0), out _, out _), "Exact spawn avoids the player");
        terrain.Blocks[new Vector2Int(16, 16)] = new Block();
        AnimalAIWorld.Instance.Animals.Add(new AnimalAIController { SimulationPosition = new Vector3(16, 0, 16) });
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(16, 0, 16), out _, out _), "Exact spawn avoids active animals");
        terrain.Blocks[new Vector2Int(20, 20)] = new Block { MapObject = new MapObject() };
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(20, 0, 20), out _, out _), "Structure cells reject spawning");
        terrain.Blocks[new Vector2Int(24, 24)] = new Block { IsRuntimeActive = false };
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(24, 0, 24), out _, out _), "Inactive cells reject spawning");
        terrain.Blocks[new Vector2Int(28, 28)] = new Block();
        terrain.Water.Add(new Vector2Int(28, 28));
        Check(!MonsterSpawner.TrySpawnAt(definition, 1, new Vector3(28, 0, 28), out _, out _), "Water cells reject spawning");
        terrain.Blocks[new Vector2Int(8, 8)] = new Block { WorldPosition = new Vector3(8, 0, 8) };
        Tick(8.1f, 8.2f);
        Check(MonsterWorld.Current.IsOccupied(new Vector3(8.1f, 0, 8.2f), .01f) && MonsterSpawnBrush.IsActive,
            "Left click paints one monster and keeps the brush active");
        int warnings = Debug.Warnings;
        MonsterSpawnBrush.Tick();
        Check(Debug.Warnings == warnings, "Input is processed at most once per frame");
        terrain.Blocks[new Vector2Int(12, 12)] = new Block { WorldPosition = new Vector3(12, 0, 12) };
        EventSystem.current = new EventSystem { Hit = new GameObject { UiComponent = new UnityEngine.UI.Selectable() } };
        Tick(12, 12);
        Check(!MonsterWorld.Current.IsOccupied(new Vector3(12, 0, 12), .01f), "UI clicks never paint through onto terrain");
        EventSystem.current = null;
        Time.frameCount++;
        Input.Click = false;
        MonsterSpawnBrush.Tick();
        Check(!MonsterWorld.Current.IsOccupied(new Vector3(12, 0, 12), .01f), "Holding or hovering without a new left click does not paint");
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Time.frameCount++;
            MonsterSpawnBrush.Tick();
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore, "Active brush idle polling allocates no managed memory");
        MapObjectTickManager.SimulationPaused = true;
        Tick(12, 12);
        Check(!MonsterWorld.Current.IsOccupied(new Vector3(12, 0, 12), .01f), "Paused simulation rejects painting");
        Tick(12, 12, true);
        Check(!MonsterSpawnBrush.IsActive && MonsterSpawnBrush.BlocksGameplayInput, "Escape cancels even when paused and consumes the cancel frame");
        Time.frameCount++;
        Check(!MonsterSpawnBrush.BlocksGameplayInput, "Normal input resumes on the next frame");
        MapObjectTickManager.SimulationPaused = false;
        Check(MonsterSpawnBrush.Begin(definition, 1), "Brush can be enabled again");
        MonsterSpawnBrush.Stop();
        Check(!MonsterSpawnBrush.IsActive, "Button toggle can stop the brush");
        MonsterSpawnBrush.Begin(definition, 1);
        MapObjectTickManager.WaitingForWorldLoad = true;
        Tick(12, 12);
        Check(!MonsterSpawnBrush.IsActive, "World load cancels the brush");
        MapObjectTickManager.WaitingForWorldLoad = false;
        Input.Click = Input.Escape = false;
        MonsterSpawnBrush.Stop();
        MonsterWorld.Current?.Dispose();
        AnimalAIWorld.Instance = null;
        terrain = new TerrainGenerator();
        terrain.Blocks[new Vector2Int(2, 0)] = new Block { WorldPosition = new Vector3(2, 3, 0) };
        Check(MonsterSpawner.TrySpawnNearPlayer(definition, 1, out spawned, out _)
            && spawned.transform.position.x == 2 && spawned.transform.position.y == 3
            && MonsterWorld.Current.IsOccupied(spawned.transform.position, .01f),
            "The original near-player Spawn button still uses the shared creation and ECS registration path");
        MonsterWorld.Current?.Dispose();
        Console.WriteLine($"PASS: {checks} production monster brush/spawn checks");
    }
}

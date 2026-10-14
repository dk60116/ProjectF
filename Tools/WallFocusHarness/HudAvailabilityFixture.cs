using System;

namespace WallFocusHarness
{
    // Minimal boundaries; the availability method is loaded from PlayerHUD.cs.
    public interface IMapObjectTarget { bool IsTargetActive { get; } }
    public sealed class BuildingRuntimeRecord : IMapObjectTarget
    {
        public bool Active = true;
        public bool IsTargetActive => Active;
    }
    public sealed class RailwayTarget : IMapObjectTarget
    {
        public bool Active = true;
        public bool IsTargetActive => Active;
    }
    public sealed class GameObject { public bool activeInHierarchy = true; }
    public class Component { public GameObject gameObject = new GameObject(); }
    public class SceneTarget : Component, IMapObjectTarget
    {
        public bool IsTargetActive => gameObject.activeInHierarchy;
    }
    public sealed class ConveyorBelt : SceneTarget { }
    public sealed class Pipe : SceneTarget { }
    public sealed class PortableObject
    {
        public bool IsAlive = true, IsActive = true, IsOnConveyor, IsVisualRenderingSuppressed;
        public int ItemId = 1;
    }
    public sealed class Block { public int Coordinate; }
    public sealed class ConveyorWorld
    {
        public static ConveyorWorld Current;
        public IMapObjectTarget Target;
        public bool TryGetMatchingAtCoordinate(int coordinate, ConveyorBelt target, out object record)
        { record = null; return coordinate == 1 && ReferenceEquals(Target, target); }
    }
    public sealed class PipeWorld
    {
        public static PipeWorld Current;
        public IMapObjectTarget Target;
        public bool TryGetMatchingAtCoordinate(int coordinate, Pipe target, out object record)
        { record = null; return coordinate == 1 && ReferenceEquals(Target, target); }
    }
    public sealed class PlayerController
    {
        public IMapObjectTarget Target;
        public bool TryGetFocusedMapObject(out IMapObjectTarget target)
        { target = Target; return target != null; }
    }
    public sealed class HudFixture
    {
        private object clickedObjectInfoTarget;
        private Block clickedObjectInfoFallbackBlock;
        private bool currentObjectInfoOpenedByYellowFocus;

        // PRODUCTION_METHOD

        private static void Require(bool condition, string message, ref int checks)
        { checks++; if (!condition) throw new Exception(message); }

        public static int Run()
        {
            int checks = 0;
            var hud = new HudFixture();
            var controller = new PlayerController();
            var wall = new BuildingRuntimeRecord();
            Require(!hud.IsObjectInfoTargetAvailable(null, controller), "Null target clears", ref checks);
            Require(!hud.IsObjectInfoTargetAvailable(new object(), controller), "Unknown target clears", ref checks);
            hud.clickedObjectInfoTarget = wall;
            for (int frame = 0; frame < 5; frame++)
                Require(hud.IsObjectInfoTargetAvailable(wall, controller), "Clicked ECS wall survives refresh " + frame, ref checks);
            hud.currentObjectInfoOpenedByYellowFocus = true;
            hud.clickedObjectInfoTarget = null;
            Require(hud.IsObjectInfoTargetAvailable(wall, controller), "Nearby ECS wall remains available", ref checks);
            wall.Active = false;
            Require(!hud.IsObjectInfoTargetAvailable(wall, controller), "Removed ECS wall clears", ref checks);
            var railway = new RailwayTarget();
            Require(hud.IsObjectInfoTargetAvailable(railway, controller), "Active railway follows shared contract", ref checks);
            railway.Active = false;
            Require(!hud.IsObjectInfoTargetAvailable(railway, controller), "Removed railway clears", ref checks);
            var scene = new SceneTarget();
            Require(hud.IsObjectInfoTargetAvailable(scene, controller), "Active scene target stays", ref checks);
            scene.gameObject.activeInHierarchy = false;
            Require(!hud.IsObjectInfoTargetAvailable(scene, controller), "Inactive scene target clears", ref checks);
            var animal = new Component();
            Require(hud.IsObjectInfoTargetAvailable(animal, controller), "Active non-map component stays", ref checks);
            animal.gameObject.activeInHierarchy = false;
            Require(!hud.IsObjectInfoTargetAvailable(animal, controller), "Inactive non-map component clears", ref checks);
            var monster = new ProjectF.Monsters.MonsterInstance();
            Require(hud.IsObjectInfoTargetAvailable(monster, controller), "Active monster stays", ref checks);
            monster.IsActive = false;
            Require(!hud.IsObjectInfoTargetAvailable(monster, controller), "Inactive monster clears", ref checks);
            var portable = new PortableObject();
            Require(hud.IsObjectInfoTargetAvailable(portable, controller), "Portable item stays", ref checks);
            portable.IsOnConveyor = true;
            Require(!hud.IsObjectInfoTargetAvailable(portable, controller), "Belt item excluded", ref checks);
            portable.IsOnConveyor = false;
            portable.IsVisualRenderingSuppressed = true;
            Require(!hud.IsObjectInfoTargetAvailable(portable, controller), "Hidden item excluded", ref checks);
            var belt = new ConveyorBelt();
            belt.gameObject.activeInHierarchy = false;
            var pipe = new Pipe();
            pipe.gameObject.activeInHierarchy = false;
            hud.currentObjectInfoOpenedByYellowFocus = false;
            hud.clickedObjectInfoFallbackBlock = new Block { Coordinate = 1 };
            hud.clickedObjectInfoTarget = belt;
            ConveyorWorld.Current = new ConveyorWorld { Target = belt };
            Require(hud.IsObjectInfoTargetAvailable(belt, controller), "Clicked virtual belt retained", ref checks);
            hud.clickedObjectInfoTarget = pipe;
            PipeWorld.Current = new PipeWorld { Target = pipe };
            Require(hud.IsObjectInfoTargetAvailable(pipe, controller), "Clicked virtual pipe retained", ref checks);
            hud.clickedObjectInfoFallbackBlock.Coordinate = 2;
            Require(!hud.IsObjectInfoTargetAvailable(pipe, controller), "Mismatched pipe placement clears", ref checks);
            hud.currentObjectInfoOpenedByYellowFocus = true;
            hud.clickedObjectInfoTarget = null;
            controller.Target = belt;
            Require(hud.IsObjectInfoTargetAvailable(belt, controller), "Nearby virtual belt retained", ref checks);
            controller.Target = pipe;
            Require(hud.IsObjectInfoTargetAvailable(pipe, controller), "Nearby virtual pipe retained", ref checks);
            controller.Target = null;
            Require(!hud.IsObjectInfoTargetAvailable(pipe, controller), "Lost nearby virtual pipe clears", ref checks);
            Require(!hud.IsObjectInfoTargetAvailable(pipe, null), "Missing controller cannot keep nearby pipe", ref checks);
            ConveyorWorld.Current = null;
            PipeWorld.Current = null;
            return checks;
        }
    }
}
namespace ProjectF.Monsters
{
    public sealed class MonsterInstance { public bool IsActive = true; }
}

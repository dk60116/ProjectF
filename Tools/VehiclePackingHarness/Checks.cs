using System;

public class InstallationObject { }
public class Vehicle : InstallationObject { }
public class Railload : InstallationObject { public bool Occupied; }
public sealed class PlayerController
{
    public Vehicle MountedVehicle;
    public bool IsMountedOnVehicle(Vehicle vehicle) => vehicle != null && MountedVehicle == vehicle;
}
public sealed class Player
{
    public PlayerController Controller;
    public T GetComponent<T>() where T : class => Controller as T;
}
public sealed class GameManager
{
    public static GameManager Instance;
    public Player Player;
}

public partial class InstallationPlacementController
{
    bool IsRailOccupiedByTrain(Railload rail) => rail.Occupied;
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static void Main()
    {
        var controller = new InstallationPlacementController();
        Check(!controller.CanPackEditableInstallation(null), "A missing installation cannot be packed");
        Check(controller.CanPackEditableInstallation(new InstallationObject()), "A regular installation remains packable");

        var vehicle = new Vehicle();
        Check(controller.CanPackEditableInstallation(vehicle), "A vehicle is packable when no player exists");
        GameManager.Instance = new GameManager { Player = new Player() };
        Check(controller.CanPackEditableInstallation(vehicle), "A missing player controller must not block packing");
        var playerController = new PlayerController();
        GameManager.Instance.Player.Controller = playerController;
        Check(controller.CanPackEditableInstallation(vehicle), "An unoccupied vehicle remains packable");
        playerController.MountedVehicle = new Vehicle();
        Check(controller.CanPackEditableInstallation(vehicle), "Riding another vehicle must not block this one");
        playerController.MountedVehicle = vehicle;
        Check(!controller.CanPackEditableInstallation(vehicle), "The currently ridden vehicle must not be packable");

        var freeRail = new Railload();
        var occupiedRail = new Railload { Occupied = true };
        Check(controller.CanPackEditableInstallation(freeRail), "An empty rail remains packable");
        Check(!controller.CanPackEditableInstallation(occupiedRail), "The existing occupied-rail rule must remain active");
        Console.WriteLine($"PASS: {checks} vehicle packing checks");
    }
}

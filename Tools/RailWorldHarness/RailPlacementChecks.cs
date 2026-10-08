using System;

static class RailPlacementChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }

        var rail = new Railload {
            MapFilter = InstallationMapFilter.Ground | InstallationMapFilter.WaterOutline | InstallationMapFilter.Railload
        };
        MapObject[] railSources = {
            rail, new MapObject { Component = rail }, new MapObject { ChildComponent = rail }
        };
        InstallationObject[] occupants = {
            new Train(), new SteamTrain(), new FreightCar(), new RailHandcar(),
            new InstallationObject(), new Floor(), new Pipe(), new Railload(), new Trainstation()
        };
        foreach (MapObject source in railSources)
        {
            foreach (InstallationObject occupant in occupants)
            {
                Check(InstallationPlacementController.TestOccupant(occupant, source, rail.MapFilter)
                    == (occupant is Train), "Only train occupants gain rail co-occupancy: " + occupant.GetType().Name);
                Check(InstallationPlacementController.TestSavedCell(source, occupant)
                    == (occupant is Train || occupant is Railload), "Saved grid agrees for: " + occupant.GetType().Name);
            }
        }

        foreach (InstallationObject source in new InstallationObject[] {
            new Train(), new Trainstation(), new InstallationObject(), new Pipe(), new Floor()
        })
        {
            source.MapFilter = rail.MapFilter;
            Check(!InstallationPlacementController.TestOccupant(new Train(), source, source.MapFilter),
                "Rail filter alone must not allow other objects over trains");
            Check(!InstallationPlacementController.TestSavedCell(source, new Train()),
                "Saved grid must not treat trains as existing rails");
        }

        Check(InstallationPlacementController.TestOccupant(new Floor(), rail, InstallationMapFilter.Floor), "Floor filter retained");
        Check(!InstallationPlacementController.TestOccupant(new InstallationObject(), rail, InstallationMapFilter.Floor), "Floor filter excludes buildings");
        Check(InstallationPlacementController.TestOccupant(new InstallationObject(), rail, InstallationMapFilter.OtherInstallObject), "Explicit building overlap filter retained");
        Check(!InstallationPlacementController.TestOccupant(null, rail, rail.MapFilter), "Missing occupant is not an overlap");
        Check(!InstallationPlacementController.TestOccupant(new Train(), null, rail.MapFilter), "Missing rail source cannot permit overlap");
        Check(!InstallationPlacementController.TestSavedCell(rail, null), "Missing saved occupant remains invalid");
        Check(!InstallationPlacementController.TestSavedCell(null, new Train()), "Missing saved-grid source remains invalid");
        Console.WriteLine($"PASS {checks} rail/train occupancy and saved-grid checks (production methods)");
    }
}

public partial class InstallationPlacementController
{
    public static bool TestOccupant(IMapObjectTarget occupant, MapObject source, InstallationMapFilter filter)
        => IsInstallationObjectAllowedForPlacement(occupant, source, filter);
    public static bool TestSavedCell(MapObject source, MapObject occupant)
        => CanInstallGridSourceShareSavedInstallationCell(source, occupant);
}

public class Floor : InstallationObject { }
public class FreightCar : Train { }
public class Resource : MapObject { }
public class ConveyorBelt : InstallationObject
{
    public bool IsCornerVariant;
    public ConveyorBelt StraightVariantPrefab;
}
public class Wall : InstallationObject { public Wall StraightVariantPrefab; }
public class Pipe : InstallationObject { public Pipe StraightVariantPrefab; }

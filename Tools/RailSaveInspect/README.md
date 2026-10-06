# Read-only rail save inspection

`dotnet run --project Tools/RailSaveInspect/RailSaveInspect.csproj -- <save-file> ...`

Reads local `.pfsave` files with the compiled production serializer and reports rail IDs, names, path sizes, occupied coordinates and world poses. Checks each rail through an in-memory production binary round trip, including all path points and occupied cells. Never writes a save or starts Unity.

Build `FactorioProject/Assembly-CSharp.csproj` first. The project references its compiled runtime in `FactorioProject/Temp/bin/Debug` and the installed Unity CoreModule.

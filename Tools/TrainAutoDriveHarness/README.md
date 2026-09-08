# Train automatic-driving harness

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/TrainAutoDriveHarness/Run.ps1
```

The harness extracts production methods from `SteamTrain.cs` and `RailHandcar.cs`
and runs them in a temporary .NET 9 console project, using Unity's managed vector
math. It does not launch or control the Unity editor.

Coverage includes:

- Destination-side powered locomotive selection, excluding manual handcars.
- Forward-only fixed routes and graph search, including reverse-only departures,
  reversals at connections, and forward alternatives longer than the old penalty.
- Actual control and fuel-request handoff, return trips, station wait preservation,
  save state, UI ownership and duplicate ticks from mounted input.
- Movement-tape ownership transfer with the schedule, replacement of an earlier
  leg's tape, and rejection of unrelated or moved cars.
- Production tape reversal and follower sampling across oppositely authored rail
  endpoints, preserving junction progress and one-cell spacing on return departure.
- Initial tape alignment from actual car positions on curves and junctions, followed
  by normal convergence to one-cell spacing after movement begins.
- Empty leading locomotive waiting instead of using a trailing engine in reverse.
- Automatic input, reverse momentum, exact docking step limits, forward-only
  station docking, and bidirectional water-pipe docking through the common
  snap/consist docking method.
- Manual reverse input and backward docking remaining available.
- A non-driving locomotive at the opposite end deploying and receiving water,
  simultaneous filling at both ends, and consist alignment without disrupting an
  engine that is already filling.
- Station discovery from either drivable end of a consist, including aligned,
  forward automatic, reverse-blocked automatic, and manual reverse approaches.

Engine objects, station lookup, fuel storage, route discovery and physics are
substituted. Tape transfer, reversal, interpolation and pose validation use the
production methods. Actual in-game rail geometry, animations, energy deduction and
save-file I/O still require a play-mode check by the project owner.

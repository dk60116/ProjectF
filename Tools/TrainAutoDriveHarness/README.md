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
- Independent Target A/Target B fuel and freight departure conditions, including
  Full and Empty freight checks across the connected consist.
- Departure after the arrival wait with Free freight, including full/empty/no
  freight storage. Free imposes no storage or capacity requirement.
- Departure conditions remain armed while motion is blocked and are completed
  only after actual movement. Consuming fuel after a Full departure does not
  reintroduce the station wait; the next arrival rearms its own fuel/cargo filters.
- A travelling schedule saves an empty pending-departure station and retains its
  route destination, so loading that state does not reapply departure capacity.
  The binary format is unchanged; the last-arrived field now means a station
  whose departure has not yet completed, rather than historical arrival data.
- Target A/Target B station color indicators being serialized and refreshed from
  the selected stations in the Train Filter prefab.
- Train Filter controls arranged in paired Target, Fuel, and Freight rows.
- Movement-tape ownership transfer with the schedule, replacement of an earlier
  leg's tape, and rejection of unrelated or moved cars.
- Production tape reversal and follower sampling across oppositely authored rail
  endpoints, preserving junction progress and one-cell spacing on return departure.
- Initial tape alignment from actual car positions on curves and junctions, followed
  by normal convergence to one-cell spacing after movement begins.
- First-departure path construction and sampling across overlapping rails, using
  the production endpoint scan and fallback search with all four headings, both
  authored rail directions and opposing car orientations. Every car must retain
  one-cell spacing throughout the first cell of travel.
- First-departure movement from an empty tape through production vehicle ordering,
  route locking, leader advance, follower sampling and the pose-application loop.
  384 scenarios cover straight and curved linked rails, uniform and alternating
  rail directions, either driving end, forward/reverse manual travel, opposing car
  facings and first-frame route locking. All five cars must move continuously and
  keep one-cell spacing over 80 frames, including their first rail crossings.
- Fallback replacement of failed samples, including samples past the successful
  endpoint, and traversal order through several equal-distance rail transitions.
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

Engine objects, station lookup, fuel storage, endpoint connection lookup and physics
are substituted. Initial tape construction and its fallback search, movement
preparation and application, tape transfer, reversal, interpolation and pose
validation use the production methods. Departure fixtures use deterministic
straight/arc geometry and capture the final poses instead of applying Unity physics.
Actual in-game rail geometry, animations, energy deduction and
save-file I/O still require a play-mode check by the project owner.

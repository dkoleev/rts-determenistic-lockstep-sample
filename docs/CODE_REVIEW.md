# Code Review

Scope: everything under `rts-determenistic-lockstep-sample/Assets/Code/` (Core, Demo, Tests).
Paths below are relative to that folder. Review date: 2026-09-27.

Overall, the code is small, well commented and makes the right calls for a teaching sample.
Determinism is enforced by construction (fixed-point math, a Unity-free Core assembly, ordered
collections, validation inside the simulation), and the tests cover the full server + client stack.
Most findings below are about **robustness against a hostile or buggy peer**, which matters as
soon as `SimulatedNetwork` is replaced with real sockets.

Severity: **High** = crash or exploit once exposed to a real network · **Medium** = wrong
behaviour in a reachable edge case · **Low** = latent issue, docs or style.

---

## Strengths

- **Clean layering.** `Rts.Lockstep` has `noEngineReferences: true`, so `float`-based Unity APIs
  can't leak into the simulation. The networking layer depends only on `IChannel`.
- **Server authority done right.** `PlayerId` is stamped from the connection
  (`LockstepServer.cs:103`), `SpawnPlayer` is server-only (`LockstepServer.cs:100`), and ownership
  is validated *inside* `Simulation` (`Simulation.cs:102`) so invalid orders are ignored identically
  everywhere.
- **Correct late-join ordering.** Snapshot of tick N is sent before frame N on the same ordered
  channel, and the spawn itself is a command in frame N (`LockstepServer.cs:86-91`).
- **Resync storm prevention.** `ResyncTick` ignores hashes that were in flight when the snapshot was
  sent (`LockstepServer.cs:116`).
- **Good client playback logic.** Jitter buffer, stall without time debt, catch-up, and
  interpolation that never extrapolates (`LockstepClient.cs:55-93`).
- **Reproducible tests.** Time is injected (`SimulatedNetwork.Advance`), seeds are fixed, and the
  integration test covers latency, jitter, uneven frame times and a late join.

---

## Findings

### 1. [High] A malformed packet crashes the whole server

`LockstepServer.cs:43-45`, `Command.cs:43-44`, `Protocol.cs:28-33`

`Handle` has no error handling. `Command.Read` throws `InvalidDataException` for more than 64 unit
ids, and any truncated payload throws `EndOfStreamException` from `BinaryReader`. The exception
propagates out of `LockstepServer.Update`, so a single bad packet from one client stops the tick
loop for everyone.

**Suggestion:** wrap the per-message handling in `try/catch`, and on failure drop/disconnect that
peer only. Also ignore unknown `MessageType` values explicitly.

### 2. [High] Unlimited snapshot resends via `StateHash`

`LockstepServer.cs:107-109`, `LockstepServer.cs:114-128`

- `StateHash` is processed even when `peer.Joined == false` (the `Join` and `Command` cases check
  it, this one doesn't). An unjoined connection can receive a full world snapshot, and it gets
  `PlayerId = 0`.
- A joined client can send a made-up hash with any tick in `(ResyncTick, currentTick]` that is still
  in `_hashHistory`. Every such message triggers a full `Snapshot`. Snapshots grow with the number of
  units, so this multiplies traffic, and it gives the client the full world whenever it asks.

**Suggestion:** add `if (!peer.Joined) return;`; allow at most one resync in flight per peer
(e.g. ignore mismatches until the client reports a correct hash after `ResyncTick`) and/or add a
cooldown; count repeated desyncs and kick the peer.

### 3. [Medium] `Move.Target` is not validated → fixed-point overflow

`Simulation.cs:48-58`, `Simulation.cs:89-99`, `FixVec2.cs:19`

The simulation checks unit ownership but not the target. A client (or a bad ground raycast) can
send a target with huge coordinates. `SqrMagnitude` (`X * X`) then overflows `long`. C# arithmetic
is unchecked by default, so the wrap-around is **deterministic** (no desync), but the result is
nonsense: a negative square → `Sqrt` returns `0` → `distance <= speed` → the unit teleports to the
target in one tick.

**Suggestion:** clamp or reject targets outside the map bounds in `Simulation.Apply` (inside the
simulation, so every peer does it identically).

### 4. [Medium] `Command.Write` truncates the unit count silently

`Command.cs:27-29`

`(byte)UnitIds.Length` is written, but then **all** ids are written. With 65–255 ids the server
throws on read (see #1). With more than 255 the count wraps, the reader consumes the wrong number of
ints and parses the rest as `Target` — a corrupted command with no error. Not reachable in the demo
(6 units per player), but it will be as soon as armies grow.

**Suggestion:** validate `UnitIds.Length <= MaxUnitsPerCommand` in `Write` (or in the factory
methods), and let `ClientView` split large selections into several commands.

### 5. [Medium] `[Conditional("DEBUG")]` on Unity message methods

`Demo/RtsDemo.cs:95`, `Demo/RtsDemo.cs:112`, `Demo/RtsDemo.cs:144`

`ConditionalAttribute` removes **call sites** at compile time; it does not remove the method.
Unity calls `OnGUI` / `OnDrawGizmos` by message, not through a compiled call, so they still run
when `DEBUG` isn't defined. The calls to `DrawClientPanel` inside `OnGUI` *are* stripped, though.
Result in a non-development player build: the server log box is drawn, but the client panels
(including client B's **Join** button) are gone.

**Suggestion:** use `#if UNITY_EDITOR || DEVELOPMENT_BUILD` around the whole block, or simply
drop the attribute — the UI is the point of the demo.

### 6. [Low] Player id overflow crashes the simulation

`LockstepServer.cs:36`, `LockstepServer.cs:84`, `Simulation.cs:79`, `Demo/ClientView.cs:130`

`_nextPlayerId` is a `byte`. After 255 joins it wraps to `0`, and
`PlayerBases[(0 - 1) % 4]` evaluates to `PlayerBases[-1]` → `IndexOutOfRangeException` inside
`Simulation.Step`, on every peer. Theoretical for this sample (and there is no disconnect handling
yet), but a crash inside the deterministic step is the worst place for it.

**Suggestion:** reject joins once the player limit is reached, and don't allow id `0`.

### 7. [Low] `Fix64` range comment is wrong

`Fix64.cs:12`

The comment says "values up to ~2^23 are safe for multiplication". With 16 fractional bits,
`a.Raw * b.Raw = a·b·2^32` must fit in a signed `long`, so the limit is `|a·b| < 2^31`, i.e.
about **±46 340** when squaring (`SqrMagnitude`). Still plenty for a map of ±20 m, but the documented
bound is off by a lot, and it matters for finding #3. Division has a similar limit:
`a.Raw << 16` requires `|a| < 2^31`.

### 8. [Low] Hash verification has a silent blind spot

`LockstepServer.cs:21`, `LockstepServer.cs:116`

`_hashHistory` keeps 256 ticks (12.8 s). A client that falls further behind than that is never
verified, and nothing is logged. Fine for the demo; worth a log line or a "too far behind →
snapshot" rule.

### 9. [Low] Command order favours lower peer index

`LockstepServer.cs:43-45`

All messages from peer 0 are drained before peer 1 in each `Update`, so within one tick, peer 0's
commands are always applied first. It's deterministic and harmless now (players don't interact),
but once units can fight or compete for resources, this becomes a small systematic advantage.
Using arrival time (or round-robin) would be fairer.

### 10. [Low] Tests

- `SameCommands_ProduceIdenticalWorlds` runs both worlds in the same process with the same JIT, so it
  proves "no hidden shared state / order dependence", but it can't catch cross-platform
  differences. A **golden-hash test** (hard-code the expected hash after the 600 frames) would fail
  on Mono vs IL2CPP vs ARM if determinism ever broke.
- `Tests/Rts.Lockstep.Tests.asmdef` has an empty `includePlatforms`, no
  `UNITY_INCLUDE_TESTS` define constraint and no explicit `nunit.framework.dll` /
  `UnityEngine.TestRunner` / `UnityEditor.TestRunner` references. It is worth checking that a player
  build doesn't try to compile it; the usual EditMode setup is `"includePlatforms": ["Editor"]`
  plus those references.
- No test for malformed input (findings #1, #4) or out-of-range targets (#3).

### 11. [Low] Docs and style

- The README says Core can be tested "via `dotnet test`", but there is no `.csproj`. Either add a
  small test project or reword it.
- Namespaces don't follow the asmdef root namespaces: `Rts.Lockstep.Code.Core` (root
  `Rts.Lockstep`), `Rts.Lockstep.Demo.Code.Demo`, and tests in `Rts.Lockstep.Demo.Code.Tests`.
- `Simulation` has only static members; `public static class Simulation` states the intent.
- Missing features that are expected in the sample's scope but worth listing: peer disconnect,
  per-peer command rate limiting, max player count.

---

## Summary table

| # | Severity | Area | Short description |
|---|---|---|---|
| 1 | High | Server | Malformed packet throws out of `Update` |
| 2 | High | Server | `StateHash` → unlimited snapshots; not gated by `Joined` |
| 3 | Medium | Simulation | No target bounds → `Fix64` overflow, teleporting units |
| 4 | Medium | Protocol | `Command.Write` truncates unit count |
| 5 | Medium | Demo | `[Conditional]` on `OnGUI` hides client panels in release builds |
| 6 | Low | Server/Sim | `byte` player id wrap → index −1 crash |
| 7 | Low | Math | Wrong safe-range comment in `Fix64` |
| 8 | Low | Server | Hash history window is silent |
| 9 | Low | Server | Peer-order bias within a tick |
| 10 | Low | Tests | No golden hash; test asmdef platforms; no negative tests |
| 11 | Low | Docs/Style | `dotnet test` claim, namespaces, static class |

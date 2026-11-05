# Out of bounds and Lakitu rescue

What makes Mario Kart World 1.8 send Lakitu (Japanese name *Jugem*) to pick a player up, and where it puts them
back. Code addresses are from the 1.8 executable (`Product.180`); function names in IDA were given while tracing.
Anything marked **unverified** was inferred, not seen in code.

Short version for runners:
- Every rescue goes through one network event, `RaceSeq_PlayerRescue`. Several independent systems can send it.
- On the course itself, the triggers are the **Rescue** and **Valley** collision materials (boxes baked from
  `SystemObj_RescueArea_00` / `SystemObj_ValleyArea_00`, and Rescue material painted on ordinary geometry), plus
  **WorldEndArea** volumes, plus **ForbidReverse** areas (driving backwards too far).
- Off-course watchdogs also rescue you: **not moving** or **not making lap progress** for long enough (12 s of
  accumulated "stuck" time), and CPU drivers stuck for ~6.7 s.
- The respawn point comes from your **last valid lap-path point**: the nearest recover point attached to it, else the
  path point itself.
- Not found yet: the check on the player's own kart for water/lava depth, falling (kill height) and the Fall
  material. See "Open questions".

## How a rescue happens (confirmed)

1. A trigger sends `RaceSeq_PlayerRescue` (event hash `0xA66067F2`, argument: player index). Event hashes are
   MurmurHash3 x86_32, seed 0, of the event name.
2. The race sequence handles it in `sub_71009A4E10` (PlayerRescue branch). It ignores the event when the player is
   already being recovered, during some race-sequence states, and in spectator/remote cases.
3. Otherwise `Rescue_CalcRecoverPose` (`0x71023ACE40`) picks the respawn pose and `RaceSeq_SendJugemRecover`
   (`0x71023AD360`) sends `RaceSeq_JugemRecover` (hash `0xB29FA62C`: position, rotation, `path_id`, player index).
4. While recovering, the per-player flag bit `0x8000` ("JugemRecovering") is set (`0x7100326964` in
   `sub_71003255C0`); it is cleared when the recovery ends.

`RaceSeq_PlayerRescueRaw` (`0xB8394A1C`) is the same request with an explicit position and rotation.

## Triggers

| # | Trigger | Condition | Delay | Where | Status |
|---|---|---|---|---|---|
| 1 | **Rescue / Valley material** | Contact with static course collision whose material is Valley (14) or Rescue (15): the code tests `(material & ~1) == 14`. | None: immediate | `sub_710217BFF0` (`0x710217C15C`), `sub_7102186590` (`0x71021867C0`), also `0x710218137C`, `0x710218BBEC`; sends via `Rescue_SendPlayerRescue` `0x71021A0030` | Confirmed for physics objects in that cluster (carried/ridden objects). That this exact code is the **kart's** own check is **unverified**. |
| 2 | **WorldEndArea** (`SystemObj_WorldEndArea_00`) | Player within 2.0 of the area's shape (bounding box + 2.0 margin first). | Immediate, once per entry. A second path waits 59 frames and has a 5000-frame cooldown (not fully decoded). | `Rescue_WorldEndAreaUpdate` `0x7100530000` (`0x7100530480`, `0x7100530640`, send at `0x7100530670`) | Confirmed |
| 3 | **Going backwards** (`game__GameParam_ForbidReverse` area) | Inside the area, (best progress − current progress) × course scale > the area's `ThresholdDistance`. | Immediate once over the threshold | `Rescue_ForbidReverseUpdate` `0x7102274840` | Confirmed (the actor that carries this param was not identified) |
| 4 | **Stuck / no progress** (all players) | Any of: horizontal movement < 0.01 for > 3600 frames; a per-vehicle counter reaching 1801 frames; lap progress not passing the stored mark (previous best + 75, capped at +100) within 180 frames; more than 5.0 behind with no 60-frame grace left. While any holds, a counter rises 1/frame (falls 2/frame otherwise). | Rescue at **720 frames (12 s)**; 3600-frame cooldown | `Rescue_StuckWatchdogUpdate` `0x71005BB220` (`0x71005BB848`, `0x71005BB898`, `0x71005BB900`, `0x71005BB9F0`) | Confirmed. Skipped while already recovering. |
| 5 | **CPU driver stuck** | AI state 4: still within 10.0 units of where a timer started (checked every 10 frames). Auto-drive also rescues below 8.0 under a reference plane, or > 300 frames outside its band. | 400 frames (~6.7 s) | `AI_StuckRescueCheck` `0x71004487D0`, `AI_RequestRescue` `0x7101D675D0` | Confirmed; skips human drivers outside debug/auto modes |
| 6 | **Crushed from above** | An object with flag `0x400` hits a vehicle with contact normal Y < −0.707. | Immediate | `Rescue_ObjectCrushVehicleCheck` `0x7102262C80` | Confirmed in code; which objects use it is **unverified** |

Other senders exist that were not opened yet: `sub_7100998CA0`, `sub_71021A3600`, `sub_71023C2780`, and two spots in
`sub_71003255C0` (per-player flag `0x4000`; a 60-frame counter when race-seq state +224 is 1). The kart's own
out-of-bounds check is probably one of these (see "Open questions").

The same contact code as trigger 1 also breaks carried objects without a rescue: water more than 0.6 above the
object's reference height (2.5 for MuddyWater), and Lava when an owner flag is set (`0x710218683C`).

## Where you come back (confirmed)

`Rescue_CalcRecoverPose` (`0x71023ACE40`):
1. If a global override pose is active, use it.
2. Otherwise `Rescue_FindRecoverPointOnPath` (`0x7100D01670`), keyed on the player's **last valid path point**
   (path index + point index):
   - If that point has a list of recover points, take the one **nearest the player** in 3D.
   - Otherwise use the path point's own position and rotation (then a longer search, not fully read).
   - The facing can be corrected from nearby data within 300 units.
3. If that fails, search outward from the player in radius steps of 100 from 200 to 1900, and flip the result if it
   faces against the player's direction.

The `path_id` sent with `JugemRecover` is the chosen point's path. How `SystemObj_PrioRecoverPoint` (144 placed in 30
scenes), `SystemObj_RespawnArea_00` and the respawn points fill the per-point recover lists is **unverified**.

Course data that looks related (from `mkwsat scan`, meanings **unverified**):
- `LapPathGraph` points: `Dynamic.IsJugemLeft` (342 points): which side Lakitu comes from.
- `RoutePathGraph` points: `Dynamic.RecoverType` (1–5, 79,762 points) and `Dynamic.IsRecoverDash` (100).

Lakitu's own parameters (`CmnObj_Jugem001_A_00`, `game__GameParam_Jugem`) have move sets for **Lift** (rescue),
**Reverse** (wrong way), **Lap**, **Start**, **Goal**, **Dropout** and Default. Only the names come from data.

## In the course data

Rescue and Valley boxes in a race scene's collision are the `SystemObj_RescueArea_00` / `SystemObj_ValleyArea_00`
actors baked in (see collision.md, "Volumes vs area actors"). `mkwsat collision <course>` over all 57 race scenes:

- 423 Rescue boxes, 174 Valley boxes; 5 boxes have no matching actor (Crown City A/B, Boo Cinema B).
- 675 pieces of **Rescue material on ordinary geometry** in 19 scenes (no actor): `Rainbow_A` (488: clouds, station
  undersides), `WestCity_A–D` (Crown City; concrete, iron, grass), `Central_A–C` (34 concrete pieces each), `BigTree_A`
  (17, wood), `SnowMountain_A` (12), `SFCMarioCircuit_*` (grass), `Cinema_A/B`.
- **Fall** material only on `DinoBones_A` (408 triangles) and `Lava_A` (2,016).
- No Lava-material triangles in any race scene's static collision.
- **No OOB volumes at all**: `ChocoMountain_B/C`, `DonkeyKong_A`, `Cave_A` (Dandelion Depths, which has 4 area actors
  but no baked boxes). Lakitu there must come from the other triggers.

Actor counts across all Banc scenes: `SystemObj_RescueArea_00` 3,309 (282 scenes), `SystemObj_RescueAreaMovable_00`
127, `SystemObj_ValleyArea_00` 1,369 (248 scenes), `SystemObj_ValleyAreaMovable_00` 44. `SystemObj_NoJugemAppearArea_00`
exists in the code (registered as `AreaInfoNoJugemAppearArea`, `0x7100D0D064`) but is **not placed in any scene**.

The viewer's Collision layer highlights Rescue (default) or Valley volumes; the Areas layers show the actors.

## Open questions

- The kart's own check for water depth (`VehicleMoveCollision::mWaterDepth`, registered at `0x7100A49DD0`; state
  flags `mStateFlag`), the Fall material, lava and a kill height. Leads: who sets per-player flag bit `0x4000` (read
  at `0x7100326E1C`), and race-seq fields +224 / +472.
- Whether touching Rescue material on ordinary ground respawns instantly for the kart (trigger 1 is confirmed only for
  carried objects).
- Unknown event hashes in rescue paths: `0xC7A2C2BD`, `0xD2FE0B42` (distance compared to a threshold before rescuing),
  `0x48740F93`, `0xB0BCE3F5`.
- `IsRecoverEnabled` (`sub_7100E59FB0`) and `EnableStartInForceEndRecover` (`sub_71008EEEB0`) not looked at.

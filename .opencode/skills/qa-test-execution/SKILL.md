---
name: qa-test-execution
description: Use when the user asks to run (прогнать) QA tests through gdUnit4, execute test scenarios, verify a REQ feature, or generate a test report with screenshots. Triggers on phrases like "прогони тесты", "запусти gdUnit", "проверь REQ-NNNN", "сделай отчёт по тестам", "run QA for REQ-NNNN".
---

# QA Test Execution (gdUnit4)

## Overview

Run gdUnit4 data-driven test scenarios for a REQ feature, produce an HTML report, and capture screenshots for every FAIL. This skill uses the existing `tests/ScenarioRunner.cs` infrastructure. Video recording is OFF by default.

**Core principle:** NEVER modify source code — only run tests and report.

## When to Use

- User says "прогони тесты для REQ-NNNN", "запусти QA", "проверь фичу"
- Running all or filtered test scenarios through gdUnit4
- Generating HTML report with screenshots for failures
- Re-running a single failed TC for debugging
- When NOT to use: creating new test scenarios (→ `qa-scenario-generation`), modifying game code, or running godot directly for gameplay

## Input

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `QA_REQ` env var | `REQ-0021-tennis-ball` | Target REQ folder under `qa/` |
| `TC_FILTER` env var | `.*` | Regex to filter TC-IDs (e.g., `TC-F47-03`) |
| `DISPLAY` | `:0` | X11 display for viewport rendering |
| `GODOT_BIN` | from AGENTS.md | Path to Godot 4.7 mono |
| Video | OFF | Use `--video` flag or set `record_video: true` per TC JSON |

## Quick Start

```bash
# Run ALL scenarios for a feature
QA_REQ='REQ-0021-tennis-ball' DISPLAY=:0 \
  GODOT_BIN="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64" \
  DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet test --filter 'FullyQualifiedName~ScenarioRunner'

# Run ONE specific scenario
TC_FILTER='TC-F47-03' QA_REQ='REQ-0021-tennis-ball' DISPLAY=:0 \
  GODOT_BIN="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64" \
  DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet test --filter 'FullyQualifiedName~ScenarioRunner'
```

## Process

### Phase 1: Build & Import

```bash
dotnet build
"$GODOT" --headless --import
```

Check for build errors. If `dotnet build` fails, report the error and stop — do NOT proceed.

### Phase 2: Discover Available Scenarios

List what exists before running:

```bash
ls qa/$QA_REQ/TC-*.json
```

Report the count: "Найдено N JSON-сценариев в qa/$QA_REQ/".

If zero, report: "Нет JSON-сценариев — только markdown-документация. Ручной прогон по 00-environment.md."

### Phase 3: Run Tests

Execute `dotnet test` with the proper environment. The run ID is auto-generated (`yyyyMMdd-HHmmss`).

```bash
GODOT_BIN="$GODOT" \
  DOTNET_ROLL_FORWARD=LatestMajor \
  QA_REQ='REQ-0021-tennis-ball' \
  TC_FILTER='.*' \
  DISPLAY=:0 \
  dotnet test --filter 'FullyQualifiedName~ScenarioRunner' 2>&1
```

**Timeout:** The full suite (~31 scenarios) takes 3–8 minutes. Single scenario ~15–30 seconds. Use a generous timeout (600s for full suite, 120s for single).

### Phase 4: Locate Report

After run, the report is at:

```
qa/$QA_REQ/_runs/<run-id>/index.html
```

The run ID is printed in console output as `[QA] HTML-отчёт: /path/to/report`.

Also check console summary:
```
[QA] N PASS / M FAIL / K SKIP из TOTAL
```

### Phase 5: Parse & Present Results

Read the HTML report. Present a summary:

```
REQ-0021-tennis-ball — Результаты прогона
==========================================
PASS  17
FAIL   3
SKIP  11
Всего 31

Проваленные:
  TC-F50-04  RMB во время заряда → отмена
    → [log_not_contains] найдено запрещённое: \[Throw\] launch
    → скриншот: qa/REQ-0021-tennis-ball/_runs/<run-id>/screenshots/TC-F50-04-FAIL.png
  TC-REG-08  ...
```

### Phase 6: Screenshots for FAILs

For each FAIL:
1. The runner auto-captures a FAIL screenshot at `_runs/<run-id>/screenshots/<TC-ID>-FAIL.png`
2. These are embedded in the HTML report
3. If the user needs a specific screenshot mid-run, add a `screenshot` step to the JSON scenario and re-run

**The HTML report IS the deliverable.** Point the user to the report path.

## Interpreting Results

### PASS
The scenario ran all steps and all assertions passed. No action needed.

### FAIL
One or more assertions failed. Read the failure message:
- `[log_contains] не найдено` → expected log line never appeared (timing issue or regression)
- `[log_not_contains] найдено запрещённое` → unwanted log line appeared (regression)
- `[log_order] нарушена последовательность` → events happened in wrong order
- `[node] exists=false, ожидалось exists=true` → node missing from scene tree
- `[node_property] X, ожидалось ≈Y` → property value wrong

For each FAIL:
1. Check the log dump: `qa/$QA_REQ/_runs/<run-id>/logs/<TC-ID>.txt` — frame-tagged log
2. Check the screenshot: `<TC-ID>-FAIL.png` — visual state at failure
3. Cross-reference with markdown TC doc for expected behavior

### SKIP
Scenarios requiring navigation harness (`find_monster`/`move_player`/`aim_camera.at_monster`). These need manual/agent-driven execution per the markdown TC docs.

### Video (if enabled)
When a TC has `"record_video": true` (or `--video` flag), the runner produces:
- `_runs/<run-id>/video/<TC-ID>.mp4` — encoded from frame captures at video_fps
- `_runs/<run-id>/video/<TC-ID>.srt` — subtitle timeline
- `_runs/<run-id>/video/<TC-ID>.timeline.json` — machine-readable timeline

Video is embedded in the HTML report as `<video controls>`. Video is OFF by default — enable only for debugging complex multi-step scenarios.

## Single Scenario Debugging

When one TC fails and needs re-running:

```bash
# Run with video to see what happened
TC_FILTER='TC-F50-04' QA_REQ='REQ-0021-tennis-ball' DISPLAY=:0 \
  GODOT_BIN="$GODOT" DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet test --filter 'FullyQualifiedName~ScenarioRunner' 2>&1
```

Then check:
1. Console output for `[QA] FAIL детали:` lines
2. Log dump: `qa/$QA_REQ/_runs/<run-id>/logs/TC-F50-04.txt`
3. Screenshot: `qa/$QA_REQ/_runs/<run-id>/screenshots/TC-F50-04-FAIL.png`
4. Re-run with `record_video: true` in the JSON if visual debugging needed

## Report Format (HTML)

The report at `_runs/<run-id>/index.html` contains:
- Summary bar: PASS N / FAIL M / SKIP K
- Per TC: ID, title, status badge, failure message (if FAIL), manual visual notes, embedded screenshots, embedded video (if recorded)

## Environment Requirements

- **Linux/X11** with `DISPLAY=:0` (viewport rendering)
- **Godot 4.7 mono** at `$GODOT` path
- **.NET 8.0+** with `DOTNET_ROLL_FORWARD=LatestMajor`
- **GdUnit4** must be installed (should be via NuGet in the project)
- **ffmpeg** (optional, for video encoding only)
- **No GUI interaction needed** — fully headless with `--headless` on Godot scene run
- **AGENTS.md** — read for `GODOT` path definition

## Common Mistakes

- **Forgetting `dotnet build` before test run** — changed `.cs` won't be picked up.
- **Forgetting `--headless --import`** — `.glb` models won't render, visual assertions fail.
- **Missing `DISPLAY`** — headless mode still needs X11 for viewport.GetTexture().
- **Wrong `QA_REQ`** — if not set, defaults to `REQ-0021-tennis-ball`, which may not be what you want.
- **Running without reading environment doc first** — some TCs need specific game state (seeded items, monster spawns). The 00-environment.md explains this.
- **Interpreting SKIP as failure** — SKIP means the TC needs navigation harness, not that the feature is broken.
- **Not checking log dumps for FAILs** — the failure message is a summary; the log dump has frame-precise event order.
- **Running the full suite for a minor change** — use `TC_FILTER` to run only affected scenarios.

## Architecture Note

The test runner (`tests/ScenarioRunner.cs`) uses GdUnit4's `ISceneRunner` which:
- Loads `res://main.tscn` as a real Godot scene
- Simulates input via `SimulateActionPress/Release`, `SimulateKeyPressed`, `SimulateMouseMoveRelative`
- Advances physics frames via `SimulateFrames(N, 16)` (60Hz physics)
- Captures `GD.Print` output via `GameLog` buffer (instrumented in `src/GameLog.cs`)
- Evaluates assertions against captured log + scene tree + node properties

Scenarios run in isolated scene instances (one per TC), so test pollution is impossible.

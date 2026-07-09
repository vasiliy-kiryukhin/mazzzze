---
name: qa-scenario-generation
description: Use when the user asks to create test scenarios (тест-сценарии) for a REQ requirement, generate QA documentation and gdUnit4 JSON scenario files, or build the qa/ test suite for a feature. Triggers on phrases like "создай тест-сценарии для REQ-NNNN", "напиши тест-кейсы", "сгенерируй QA для фичи".
---

# QA Scenario Generation

## Overview

Generate a complete test suite for a REQ feature: markdown test-case documentation + gdUnit4 JSON scenario files. Output goes to `qa/REQ-NNNN-slug/`, following the structure established by `qa/REQ-0021-tennis-ball/` (the reference implementation).

## When to Use

- User says "создай тест-сценарии для REQ-NNNN"
- Building QA coverage for a newly implemented feature
- Adding missing TC documentation or JSON scenarios to an existing qa folder
- When NOT to use: the user asked only to run tests (→ `qa-test-execution` skill) or only to modify source code

## The Golden Rule

**NEVER modify source code.** This skill reads REQ files and writes to `qa/`. No `.cs`, `.tscn`, `.tres`, or `.glb` files are touched.

## Input / Output Map

| Input | Location |
|-------|----------|
| REQ README.md | `requirements/REQ-NNNN-slug/README.md` |
| Facet files (logic/ui/visual/input/data/animation) | `requirements/REQ-NNNN-slug/NN-*.md` |
| design.md | `requirements/REQ-NNNN-slug/design.md` |
| Reference QA suite | `qa/REQ-0021-tennis-ball/` |

| Output | Location |
|--------|----------|
| QA README.md | `qa/REQ-NNNN-slug/README.md` |
| Environment doc | `qa/REQ-NNNN-slug/00-environment.md` |
| Schema (copy) | `qa/REQ-NNNN-slug/_scenario-schema.json` |
| Markdown TC docs | `qa/REQ-NNNN-slug/NN-category-name.md` |
| JSON scenarios | `qa/REQ-NNNN-slug/TC-{FID}-NN.description.json` |
| Scratch dirs | `qa/REQ-NNNN-slug/_report/`, `_screenshots/`, `_video/` |

## Process

### Phase 1: Read & Understand REQ

Read ALL files in `requirements/REQ-NNNN-slug/`:

1. **README.md** → extract: feature name, US-NN, F-IDs, acceptance criteria, sub-feature links, related features
2. **Each facet file** (`NN-logic.md`, `NN-input.md`, `NN-visual.md`, `NN-data.md`, etc.) → extract functional requirements, parameter tables, transition diagrams, input maps, visual specs
3. **design.md** → extract: C# classes, log markers (crucial for assertions!), key mechanics, scope/limits ("Что НЕ входит")

**CRITICAL:** Log markers from `design.md` are the foundation for `log_contains`/`log_order`/`log_not_contains` assertions. Search for patterns like `[ClassName] Action 'item'`, `GD.Print` / `GameLog.Print`, and console output examples.

### Phase 2: Map F-IDs → Test Categories

Group F-IDs by concern (one category = one markdown file):

| F-ID type | Category file | Example |
|-----------|---------------|---------|
| Lifecycle/state transitions | `10-lifecycle-*.md` | F-47 |
| Input/controls | `20-input-*.md` | F-50 |
| Core mechanics/logic | `30-*-logic.md` | F-48 |
| Effects on other systems | `40-*-effect.md` | F-49 |
| Visual/model | `50-visual.md` | — |
| Edge case regression | `60-edge-regression.md` | — |

Number prefix controls ordering (10, 20, 30, ...). Assign each F-ID to exactly one primary category.

### Phase 2.5: Analyze Physical & Spatial Constraints (MANDATORY)

**BEFORE writing any TC setup, extract EVERY physical/spatial constraint from REQ docs
and map them to required positioning.** A test that doesn't position the player correctly
will FAIL not from a code bug but from environment conditions (wall too close, corridor too
short, item usage radius violated). The maze is randomly generated per run — the start cell
and initial camera direction are NOT guaranteed to be favourable.

#### Step A: Collect constraints from REQ files

Read ALL facet files and `design.md`. Look for:

| Constraint type | Where to find it | Example |
|-----------------|------------------|---------|
| Min/max distance | `[Export] public float FocusMinDistance` | Vintage camera: 1.8u clearance needed |
| Duration requiring space | `EnterDuration = 1.3333f` | Photo entry: ~7-10u clear corridor needed |
| Collision rules | `MoveAndSlide`, `IsFloor` | Player can't walk through walls |
| Item pickup radius | `PickupRadius`, proximity checks | Auto-pickup range |
| Raycast/collision mask | `CollisionMask = 1` | What counts as an obstacle |

#### Step B: Classify each TC by positioning needs

| Test intent | Positioning requirement | Setup actions |
|-------------|----------------------|---------------|
| **Success path** (e.g. walk forward, take photo, enter photo) | Player MUST have clear space ahead ≥ required distance/duration | `aim_camera` toward open corridor, optionally `move_forward hold` to step away from start wall |
| **Failure/blocked path** (e.g. wall stops progress, focus blocked) | Player MUST face an obstacle within the constraint range | `aim_camera yaw_deg: 180` or `mouse_motion dx: -500` to face the wall behind start |
| **Indifferent** (e.g. activate/deactivate in place, drop at feet) | No spatial requirement | No positioning needed (default is fine) |

#### Step C: Build the positioning prefix for setup

Insert positioning steps AFTER `wait_frames 90` (world init) and BEFORE the first
gameplay action. Use these primitives:

| Primitive | Use when | Example |
|-----------|----------|---------|
| `aim_camera` with `yaw_deg` | You know the absolute yaw needed | `{ "type": "aim_camera", "yaw_deg": 0.0 }` — face +Z (corridor forward) |
| `mouse_motion` with `dx`, `dy` | Relative turn from current view | `{ "type": "mouse_motion", "dx": -300, "dy": 0 }` — turn ~180° left |
| `action move_forward hold` | Need to step away from wall | `{ "type": "action", "name": "move_forward", "mode": "hold", "frames": 15 }` — ~0.25s walk |

**Default defensive positioning** (append to every success-path setup unless overridden):

```json
{ "type": "mouse_motion", "dx": 0, "dy": -20, "reason": "слегка опустить камеру — гарантировать взгляд вдоль коридора, а не в стену" },
{ "type": "wait_frames", "frames": 5 }
```

This costs almost nothing and reduces seed-dependent failures.

**For vintage-camera tests specifically:** after activating the camera, before LMB, the focus
ray must hit open space ≥ 1.8u. The start cell is an entrance — the camera's initial forward
is usually the corridor. But on some seeds the entrance cell borders a wall. Defensive fix:

```json
{ "type": "action", "name": "move_forward", "mode": "hold", "frames": 8, "reason": "шаг вперёд от стены — гарантировать FocusMinDistance" },
{ "type": "wait_frames", "frames": 10 }
```

**For photo-enter tests specifically:** the player needs ~1.33s of uninterrupted forward
movement. The start corridor is usually long enough, but confirm the camera faces the
corridor (not a wall). If in doubt, rotate 180° first:

```json
{ "type": "aim_camera", "yaw_deg": 180.0, "reason": "разворот — гарантировать взгляд вдоль длинного коридора" },
{ "type": "wait_frames", "frames": 10 }
```

#### Step D: Document constraints in 00-environment.md

Add a "Пространственные ограничения" subsection listing every constraint that affects
positioning, so the test executor knows what to look for in FAIL dumps:

```markdown
### Пространственные ограничения (важно для setup'а)

| Параметр | Значение | Влияние на тесты |
|----------|----------|-----------------|
| `FocusMinDistance` (камера) | 1.8 юнита | Перед ЛКМ игрок должен быть ≥ 1.8u от стены по лучу взгляда |
| `EnterDuration` (фото) | 1.3333 с | Нужен коридор ≥ ~10u для непрерывного хода вперёд |
```

### Phase 3: Generate Markdown TC Documentation

For each category file (`NN-category.md`), following the pattern from `qa/REQ-0021-tennis-ball/10-lifecycle-reusability.md`:

```markdown
# NN — Category Name (F-XX, F-YY)

> **Покрытие:** [F-XX](../../requirements/REQ-NNNN-slug/facet-file.md)
> **Что проверяем:** one-sentence summary.
>
> Окружение/запуск/ввод — см. [00-environment.md](./00-environment.md).

---

### TC-FXX-01: Title in Russian

**Покрытие:** F-XX (aspect) · **Тип:** logic|input|visual|model|animation

**Предусловия:** state before test.

**Шаги:**
1. Concrete action with xdotool command or game interaction.
2. Next step.

**Ожидаемый результат:**
- What the player sees.
- What the game state should be.

**Верификация:**
- Консоль: `grep pattern` — expected log line.
- Визуал: screenshot description.
```

Rules for TCs:
- Each TC covers exactly one functional aspect
- Title is in Russian, short
- Steps include exact xdotool/inventory commands where applicable
- Verification specifies console markers AND visual checks
- TC-ID format: `TC-{FID}-NN` (e.g., `TC-F47-01`) or `TC-{VIS|REG}-NN` for non-F categories
- Mark TCs that require `find_monster`/`move_player`/`aim_camera.at_monster` as needing navigation harness (won't get JSON)

### Phase 4: Write 00-environment.md

Adapt from `qa/REQ-0021-tennis-ball/00-environment.md`:

1. **Системные предусловия** — keep standard (Linux/X11, dotnet build, Godot --import, DISPLAY=:0)
2. **Путь к движку** — keep the GODOT= line
3. **Стартовая конфигурация игры** — describe the feature-specific starting state, seeded items, spawned entities, log markers
4. **Запуск тестовой сессии** — keep the standard launch commands, adapt log path
5. **Инжекция ввода** — keep xdotool table, add feature-specific timing notes
6. **Способы верификации** — list ALL relevant log markers from design.md, with meaning
7. **Таблица результатов** — empty template for results

### Phase 5: Generate JSON Scenarios

For every automatable TC (no `find_monster`/`move_player`/`aim_camera.at_monster` steps), create a `TC-{ID}.description.json` file. Follow `_scenario-schema.json` exactly.

**BEFORE writing setup steps:** review Phase 2.5 constraints for this feature. Every success-path
TC that involves walking, using a camera, or entering a photo MUST include a positioning prefix
in the setup block (after `wait_frames 90` init). Without it, the TC will fail on random maze
seeds where the start cell borders a wall.

**Setup structure for success-path TCs:**
```
wait_frames 90 (init)
→ POSITIONING PREFIX (see Phase 2.5 step C)
→ item activation / camera usage / etc.
→ steps (the actual test actions)
```

**Setup structure for failure/blocked TCs:**
```
wait_frames 90 (init)
→ aim_camera yaw_deg:180 (face the wall)
→ item activation
→ steps (attempt action that should fail)
```

**JSON template:**
```json
{
  "$schema": "_scenario-schema.json",
  "format_version": "1.0",
  "_comment": "One-line Russian description.",
  "id": "TC-FXX-NN",
  "title": "Russian title from markdown TC",
  "requirement": {
    "feature": "REQ-NNNN-slug",
    "functional": ["F-XX"],
    "acceptance": "Acceptance criterion from REQ README",
    "markdown": "NN-category.md",
    "markdown_anchor": "tc-fxx-nn"
  },
  "categories": ["logic"],
  "scene": "res://test_scene.tscn",
  "run": { "headless": true, "display": ":0" },
  "max_duration_frames": 480,
  "harness_requirements": ["capture_gd_print", "input_simulation", "frame_advance"],
  "preconditions": ["..."],
  "setup": [],
  "steps": [],
  "assertions": [],
  "manual_verification_ref": "NN-category.md → TC-FXX-NN",
  "expected_result_summary": "..."
}
```

**Step types** (from schema):

| Type | Example |
|------|---------|
| `wait_frames` | `{ "type": "wait_frames", "frames": 90, "reason": "..." }` |
| `action` | `{ "type": "action", "name": "inventory_toggle", "mode": "tap" }` |
| `action` hold | `{ "type": "action", "name": "use_activated", "mode": "hold", "frames": 60 }` |
| `key` | `{ "type": "key", "physical_keycode": "Key1", "mode": "tap" }` |
| `mouse_button` | `{ "type": "mouse_button", "button": "left", "mode": "hold", "frames": 40 }` |
| `mouse_motion` | `{ "type": "mouse_motion", "dx": 120, "dy": 0 }` |
| `aim_camera` | `{ "type": "aim_camera", "yaw_deg": 45.0 }` |
| `screenshot` | `{ "type": "screenshot", "path": "/tmp/tc-xx-01.png" }` |
| `node_property` | `{ "type": "node_property", "path": "...", "property": "...", "capture_as": "..." }` |

**Assertion kinds** (from schema):

| Kind | Example |
|------|---------|
| `log_contains` | `{ "kind": "log_contains", "pattern": "\\[ClassName\\] Action", "after_step": 1 }` |
| `log_not_contains` | `{ "kind": "log_not_contains", "pattern": "\\[Throw\\] launch" }` |
| `log_order` | `{ "kind": "log_order", "patterns": ["A", "B", "C"] }` |
| `node` | `{ "kind": "node", "path": "/root/Main/Player/...", "exists": true, "at": "end" }` |
| `node_by_type` | `{ "kind": "node_by_type", "parent": "/root/Main", "type": "WorldItem", "count_min": 1 }` |
| `node_property` | `{ "kind": "node_property", "path": "...", "property": "Speed", "equals": 1.0 }` |
| `compare` | `{ "kind": "compare", "a": "captured_val", "op": "gt", "b": 0 }` |
| `manual_visual_check` | `{ "kind": "manual_visual_check", "description": "..." }` |

**Key details:**
- `scene`: **По умолчанию `"res://test_scene.tscn"`** — тестовая комната 50×50 без лабиринта, с полом и стенами. Если тесту нужен реальный лабиринт (чанки, MazeData, монстры), укажи `"scene": "res://main.tscn"` явно. Свойство опционально — при отсутствии раннер сам подставит `test_scene.tscn`.
- `after_step`: `"setup"` | integer (1-based step index) | `"end"` (default). Log assertions without `after_step` run at end.
- `at`: `"after_setup"` | `"after_step:N"` | `"end"` — for node assertions.
- `max_duration_frames`: estimate generously (90 init + steps + post-flight wait).
- `record_video`: `false` by default. Set `true` only for complex multi-step TCs.
- `random_seed_note`: mention if random positions affect the test.
- Regex patterns: escape backslashes for JSON (`\\[` → `[`).

### Phase 6: Write README.md

Create `qa/REQ-NNNN-slug/README.md` following the pattern from the reference:

```markdown
# QA — REQ-NNNN-slug (Feature Name)

> **Фича:** [REQ-NNNN-slug](../../requirements/REQ-NNNN-slug/README.md) · **User Story:** US-NN
> **Покрытие:** list of F-IDs + visuals + regression
> **Тип документа:** ручные/полуавтоматические тест-кейсы

One-paragraph summary.

## Как пользоваться этим набором

1. Прочитай **[00-environment.md](./00-environment.md)**
2. Исполняй тест-кейсы ...
3. Результат веди по форме: ...

## Файлы набора

### Документация тест-кейсов (markdown)

| Файл | Грань | F-ID | Что проверяет |
|------|-------|------|---------------|
| ... | ... | ... | ... |

### Сценарии для GdUnit4 (JSON)

| Группа | Файлы | Кол-во |
|--------|-------|--------|
| ... | ... | N |

## Матрица трассировки

| Критерий приёмки (README REQ) | Тест-кейсы |
|-------------------------------|------------|
| ... | TC-XX-NN, TC-YY-MM |

## Статус

🟡 Набор составлен, не прогонан.
```

### Phase 7: Create Required Directories

```bash
mkdir -p qa/REQ-NNNN-slug/_report qa/REQ-NNNN-slug/_screenshots qa/REQ-NNNN-slug/_video
```

Copy `_scenario-schema.json` from the reference if the target folder doesn't have one.

### Phase 8: Update Registry

Update `requirements/README.md` if the feature's status changed (e.g., "✅ реализовано" with QA).

## Log Marker Mapping

When reading `design.md`, extract these patterns for assertions:

| Pattern in code | Log marker (console) | Use in assertion |
|-----------------|---------------------|------------------|
| `GD.Print($"[ClassName] action")` / `GameLog.Print($"[ClassName] action")` | `[ClassName] action` | `log_contains` |
| `GD.Print($"[ClassName] verb 'item' → state")` / `GameLog.Print(...)` | Capture with regex group | `log_contains` + `capture_group` |
| Multiple lines in sequence | Capture order | `log_order` |
| Absence proves correctness | Must NOT appear | `log_not_contains` |

## Quick Reference: Test Scene (default)

По умолчанию все JSON-сценарии запускаются в `res://test_scene.tscn` — комната 50×50:
пол и стены с текстурами из основной игры, без MazeData/ChunkManager/MonsterSpawner.
Нет лабиринта — игрок всегда в открытом пространстве, нет случайности сидов.

**Когда переопределять на `main.tscn`:**
- Тесту нужны чанки (`ChunkManager`) — проверка стриминга после телепорта
- Тесту нужен MazeData — проверка `IsFloor`, навигация по клеткам
- Тесту нужны монстры (`Monster.All`) — проверка стана/отвлечения, LoS
- Тесту нужно случайное окружение — проверка поведения у стены, в тупике

Во всех остальных случаях (активация, деактивация, drop, pickup, жизненный цикл предмета,
ввод, визуал) `test_scene.tscn` предпочтительнее — нет зависимости от maze-сида.

В сценарии JSON свойство `"scene"` опционально. Если оно отсутствует, раннер использует
`res://test_scene.tscn`. Для тестов, требующих лабиринт, добавь `"scene": "res://main.tscn"`.

## Quick Reference: Common Setup Sequence

Most TCs share the same "activate tennis ball" setup. Adapt for the feature:

```json
"setup": [
  { "type": "wait_frames", "frames": 90, "reason": "инициализация мира и инвентаря" },
  { "type": "action", "name": "inventory_toggle", "mode": "tap" },
  { "type": "wait_frames", "frames": 5 },
  { "type": "key", "physical_keycode": "Key1", "mode": "tap" },
  { "type": "wait_frames", "frames": 5 },
  { "type": "key", "physical_keycode": "Key2", "mode": "tap" },
  { "type": "wait_frames", "frames": 10 },
  { "type": "action", "name": "inventory_toggle", "mode": "tap" },
  { "type": "wait_frames", "frames": 10 }
]
```

### Positioning prefix (insert after init, before gameplay actions)

```json
// --- SUCCESS PATH: гарантировать свободное пространство впереди ---
{ "type": "mouse_motion", "dx": 0, "dy": -20, "reason": "опустить камеру — смотреть вдоль пола, не в стену" },
{ "type": "wait_frames", "frames": 5 }

// --- FAILURE/BLOCKED PATH: гарантировать стену впереди ---
{ "type": "aim_camera", "yaw_deg": 180.0, "reason": "разворот на 180° — смотреть в стену позади старта" },
{ "type": "wait_frames", "frames": 10 }
```

## Common Mistakes

- **Missing `after_step` on log assertions** — without it, assertions run only at `end`, missing mid-step verifications.
- **Forgetting to escape backslashes in JSON regex** — `\\[ClassName\\]` not `\[ClassName\]`.
- **Setting `max_duration_frames` too low** — include init (90), setup, steps, and post-flight wait.
- **Creating JSON for navigation-requiring TCs** — mark them SKIP in the runner. If a TC has `find_monster`/`move_player`/`aim_camera.at_monster` steps, document it in markdown only (no JSON).
- **Not reading design.md for log markers** — assertions depend on exact console output strings.
- **Duplicate TC-IDs** — assign IDs sequentially per F-group (TC-F47-01, TC-F47-02, ...).
- **Not creating _report/ _screenshots/ _video/ dirs** — the runner needs them to exist.

### Positioning mistakes (seed-dependent failures)

These are the most common cause of spurious FAIL in CI-less runs. The maze is
**randomly generated per run** — the start cell and initial camera direction are
non-deterministic.

| Mistake | Symptom in log | Fix |
|---------|---------------|-----|
| **Assuming start has open space ahead** — no positioning prefix before distance-sensitive action | `[Camera] Focus blocked — too close` (vintage camera) or `[Photo] Entered → teleported` never appears (photo entry) | Add `mouse_motion dy:-20` or `aim_camera yaw_deg:180` in setup before the action |
| **Not checking focus distance for camera tests** — LMB opens viewfinder but focus check fails silently (now visible as `[Camera] Focus blocked — too close` in GameLog after the GD.Print→GameLog.Print fix) | Photo never created, subsequent steps cascade-fail | Add a small `move_forward hold 8 frames` before LMB to step away from the start wall, OR `aim_camera` toward the open corridor |
| **Not checking EnterDuration needs a long corridor** — `move_forward hold 90` won't reach `EnterDuration` if the player starts facing a wall | `[Photo] Entered → teleported` never appears | Ensure camera faces an open corridor (the start cell entrance usually opens one way — rotate if needed) |
| **Confusing success-path and failure-path positioning** — using the same setup for both "walk forward succeeds" and "wall blocks" TCs | Opposite results: success TC fails, failure TC passes for wrong reason | Use `aim_camera yaw_deg:0` for success (open ahead), `aim_camera yaw_deg:180` for failure (wall ahead) |
| **Forgetting that `move_forward hold` needs a non-zero velocity projection** — `PlanarCamForward` dot `Velocity` must exceed `Speed * 0.4` for photo-enter progress to accumulate | `[Photo] Entered → teleported` never appears even with clear space | Check that the action's `frames` parameter is long enough for the full `EnterDuration` plus acceleration ramp-up |

## Reference

The canonical QA suite: `qa/REQ-0021-tennis-ball/`. Study it before generating.
Scenario schema: `qa/REQ-0021-tennis-ball/_scenario-schema.json`.
Test runner: `tests/ScenarioRunner.cs`.

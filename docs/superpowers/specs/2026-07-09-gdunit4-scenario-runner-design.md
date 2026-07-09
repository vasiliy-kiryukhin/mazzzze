# Design — GdUnit4 scenario runner (REQ-0021-tennis-ball QA)

**Дата:** 2026-07-09 · **Статус:** approved (MVP) · **Связано:** [`qa/REQ-0021-tennis-ball/`](../../../qa/REQ-0021-tennis-ball/)

Data-driven раннер, который грузит JSON-сценарии из `qa/REQ-0021-tennis-ball/TC-*.json` и
исполняет их через GdUnit4 (`ISceneRunner`), формируя HTML-отчёт со скриншотами для FAIL.

## API-факты (gdUnit4.api 5.1.0-rc1, изучено из GdUnit4Api.xml)

- `ISceneRunner.Load(string resPath, bool autoFree, bool verbose) → ISceneRunner` — статическая фабрика.
- `SimulateFrames(uint frames) → Task` — **await**; крутит `_PhysicsProcess` (60 Гц по умолчанию).
- `SimulateActionPress/Release/Pressed(string action) → ISceneRunner` — синхронно, InputMap-action.
- `SimulateKeyPress/Pressed/Release(Key k, bool shift, bool ctrl) → ISceneRunner` — физические клавиши + модификаторы.
- `SimulateMouseMoveRelative(Vector2, double time, Tween.TransitionType)`, `SimulateMouseButtonPress/Release/Pressed(MouseButton, bool double) → ISceneRunner`.
- `Scene`, `FindChild`, `GetProperty/GetProperty<T>/SetProperty`, `AwaitIdleFrame`, `AwaitMillis`.
- `GdUnit4.Core.Hooks.IStdOutHook` (StartCapture/StopCapture/GetCapturedOutput) — контракт перехвата stdout.
- `[TestSuite]` + `[Test]` (async Task поддерживается); `Assertions.AssertThat(...)`.

## Архитектура (`tests/`, одна сборка `maze-prototype-1.csproj`)

| Файл | Ответственность |
|------|-----------------|
| `ScenarioModel.cs` | `Scenario`, `Step`, `Assertion`, `TcResult` — чтение через `JsonNode` DOM |
| `ScenarioLoader.cs` | `Discover()` + `Load(path)` (парсинг, валидация, SKIP-детект) |
| `LogCapture.cs` | Перехват `GD.Print`: `dup2(fd1→pipe)` + reader-поток; `Drain()` — строки с накопления |
| `ScreenshotUtil.cs` | `Capture(viewport, path)`; `OnFail(runner, tcId)` |
| `StepExecutor.cs` | `Execute(steps, runner, frameClock, logCapture)` — шаг → ISceneRunner-вызовы |
| `AssertionEvaluator.cs` | Оценка `log_contains/not_contains/order`, `node`, `node_by_type`, `node_property`, `compare`; `manual_visual_check` → INFO |
| `HtmlReport.cs` | `_report/index.html`: таблица TC (PASS/FAIL/SKIP) + `<img>` скриншоты |
| `ScenarioRunner.cs` | `[TestSuite]`, `[Test] async Task RunAllScenarios()` (фильтр env `TC_FILTER`) |

## Поток выполнения

1. `Discover()` — `Directory.GetFiles(qaDir, "TC-*.json")`, сортировка.
2. Для каждого: `Load` → если содержит шаг `find_monster`/`move_player` или `aim_camera.at_monster` → `TcResult(SKIP, reason)`.
3. Иначе: `LogCapture.Start()`; `runner = ISceneRunner.Load("res://main.tscn", true, false)`; execute `setup`+`steps`
   (между шагами `await runner.SimulateFrames(...)`; после каждого `SimulateFrames` — `Drain()` + тег кадра);
   `Screenshot`-шаги сохраняют PNG; на любом FAIL проверки — `ScreenshotUtil.OnFail`.
4. `LogCapture.Stop()`; оценить `assertions`; собрать `TcResult`.
5. Освободить сцену (`runner.Scene.QueueFree()` + `await SimulateFrames(2)`).
6. После всех: `HtmlReport.Write(results)`; тест валится, если есть FAIL (с суммарным сообщением + путём к отчёту).

## Согласования решений

- **Перехват лога — `dup2` fd 1 → pipe + reader-thread** (Linux-only; проект Linux-only). Альтернатива `Console.SetOut` не ловит `GD.Print`. Строки тегируются кадром через `Drain()` после каждого `SimulateFrames` → корректны `after_step`/`within_frames`.
- **Шаги MVP:** `wait_frames/wait_msec`, `action` (press/release/tap/hold), `key` (+modifiers shift/ctrl), `mouse_motion`, `mouse_button`, `aim_camera yaw_deg/pitch_deg` (через `SimulateMouseMoveRelative` с `dx=Δyaw/MouseSensitivity`), `screenshot`.
- **node_property — через reflection** (раннер в той же сборке): `node.GetType().GetField/GetProperty`. Покрывает и plain-поля (`Player.ExternalSpeedFactor`), и computed-свойства (`CameraYawForward`, `CameraYawDeg`).
- **node_by_type — `parent.FindChildren("*", type, true, false)`** + `Count` (по дереву, не по статике).
- **SKIP (≈14 TC):** сценарии с навигацией/монстрами (`find_monster`, `move_player`, `aim_camera.at_monster`) — весь F-49, F47-04/05, REG-03/04/06/07. **≈31 TC исполняется.**
- **Скриншоты багов:** на FAIL — автоскрин `_screenshots/<ID>-FAIL-<n>.png` + плановые `screenshot`-шаги; всё встраивается `<img>` (base64) в `_report/index.html`.
- **Изоляция сцен:** fresh `Load` на сценарий + `QueueFree` предыдущей сцены; статические реестры (`Monster.All`, `WorldItem.All`) могут содержать stale-записи, но они везде защищены `IsInstanceValid`, а tree-based assertions их не видят.

## Границы (явно не в MVP)
Навигация игрока (`move_player` к монстру/WorldItem/стене), поиск монстров по фильтру, прицеливание на монстра — следующая итерация. Pixel-diff скриншотов — будущее (сейчас скриншоты как доказательство в HTML, без авто-сравнения).

## Запуск
```bash
dotnet build
DISPLAY=:0 "$GODOT" --headless --import   # ассеты для рендера
dotnet test --filter "FullyQualifiedName~ScenarioRunner"
# отладка одного TC:
TC_FILTER='TC-F47-03' dotnet test --filter 'FullyQualifiedName~ScenarioRunner'
```
Отчёт: `qa/REQ-0021-tennis-ball/_report/index.html`. Скриншоты: `qa/REQ-0021-tennis-ball/_screenshots/`.

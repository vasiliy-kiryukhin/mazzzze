# tests/ — GdUnit4-раннер сценариев REQ-0021-tennis-ball

Data-driven раннер, который читает JSON-сценарии из `qa/REQ-0021-tennis-ball/TC-*.json`,
исполняет их через GdUnit4 (`ISceneRunner`) и строит HTML-отчёт со скриншотами.

> Архитектура и решения: [`docs/superpowers/specs/2026-07-09-gdunit4-scenario-runner-design.md`](../docs/superpowers/specs/2026-07-09-gdunit4-scenario-runner-design.md)

## Состав

| Файл | Ответственность |
|------|-----------------|
| `ScenarioRunner.cs` | `[TestSuite]`, `[TestCase] RunAllScenarios()` — оркестрация, фильтр `TC_FILTER`, итоговый assert |
| `ScenarioModel.cs` | `Scenario`, `TcResult`, расширения `JsonNode` |
| `ScenarioLoader.cs` | обнаружение `TC-*.json`, парсинг, детектор SKIP (навигация/монстры) |
| `StepExecutor.cs` | шаг JSON → вызовы `ISceneRunner`; фрейм-точная привязка лога |
| `AssertionEvaluator.cs` | `log_contains/not_contains/order`, `node`, `node_by_type`, `node_property`, `compare`; чекпоинты `at` |
| `LogCapture.cs` | обёртка над `GameLog` (перехват `GD.Print` в буфер раннера) |
| `ScreenshotUtil.cs` | viewport → PNG с даунскейлом до 512px |
| `HtmlReport.cs` | `_report/index.html` (таблица TC + скриншоты по относительным путям) |

`src/GameLog.cs` — инструментальный тап в игре: `GameLog.Print` = `GD.Print` + буфер при
`Recording=true`. В обычной игре вывод идентичен `GD.Print`; раннер включает запись на время сценария.

## Запуск

```bash
# 1. Сборка
dotnet build

# 2. Импорт ассетов (нужен .godot/imported для рендера скриншотов)
GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
"$GODOT" --headless --import

# 3. Прогон всех сценариев
DISPLAY=:0 GODOT_BIN="$GODOT" DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet test --filter 'FullyQualifiedName~ScenarioRunner'

# 3a. Один сценарий (отладка) — фильтр по ID (regex)
TC_FILTER='TC-F47-03' DISPLAY=:0 GODOT_BIN="$GODOT" DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet test --filter 'FullyQualifiedName~ScenarioRunner'
```

Переменные окружения:
- `GODOT_BIN` — путь к Godot 4.7 mono (обязателен для GdUnit4 `[RequireGodotRuntime]`).
- `DOTNET_ROLL_FORWARD=LatestMajor` — среда имеет .NET 9/10; тесты target'ят net8.0.
- `DISPLAY` — нужен для рендера viewport (скриншоты); `:0`.
- `TC_FILTER` — regex по `id` сценария; по умолчанию `.*` (все).

## Результаты

- **GdUnit4-отчёт** (CLI): тест `RunAllScenarios` PASS, если нет FAIL, иначе FAIL с числом.
- **HTML-отчёт**: `qa/REQ-0021-tennis-ball/_report/index.html` — таблица всех TC (PASS/FAIL/SKIP),
  сообщения об ошибках и встроенные скриншоты (плановые + авто-скрин при FAIL).
- **Лог-дампы**: `qa/REQ-0021-tennis-ball/_report/logs/<TC-ID>.txt` — перехваченные строки `GD.Print`
  с тегом кадра (для разбора FAIL).
- **Скриншоты**: `qa/REQ-0021-tennis-ball/_screenshots/<TC-ID>-FAIL.png` + плановые из шагов.

## Видеозапись прогона (опционально)

В сценарии JSON задай `"record_video": true` (+ опц. `"video_fps"`, playback FPS, по умолч. 15).
Тогда раннер:
- захватывает viewport каждый N-й физкадр средствами Godot (`Viewport.GetTexture().GetImage()`, JPEG 640px), где N = `60/video_fps` (stride) — компромисс плавность/скорость из-за оверхеда `SimulateFrames` на вызов;
- кодирует последовательность в **`_video/<ID>.mp4`** через ffmpeg (должен быть в PATH);
- пишет companion-файлы: **`<ID>.srt`** (субтитры — на каждый setup/main-step тайм-код + описание; открыть рядом с видео в плеере) и **`<ID>.timeline.json`** (то же машиночитаемо: `start_frame/end_frame/start_sec/end_sec`).

Видео и SRT синхронны: время = физкадр / 60 (реальное время игры). В HTML-отчёте для такого TC
встроен `<video controls>` + ссылка на SRT. По умолчанию запись выкл; ниже FPS — быстрее и рывковее.

## SKIP

Сценарии с шагами `find_monster`/`move_player`/`aim_camera.at_monster` помечаются SKIP —
навигация игрока и поиск монстров за рамками MVP раннера (весь F-49 и часть REG/F47).

## Границы (MVP)

Не реализовано: перемещение игрока к точке/монстру, поиск монстров по фильтру, прицеливание на
монстра, pixel-diff скриншотов. Детали — в spec-документе.

# Design — Видеозапись прогона сценария (REQ-0021 QA runner)

**Дата:** 2026-07-09 · **Статус:** approved · **Связано:** [`tests/`](../../tests/), [`2026-07-09-gdUnit4-scenario-runner-design.md`](./2026-07-09-gdunit4-scenario-runner-design.md)

Опциональная запись полного видео прогона сценария + companion-файл, размечающий шаги→тайминги.

## Решение (выбрано из вариантов)

Захват кадров внутри GdUnit4-раннера средствами Godot (`Viewport.GetTexture().GetImage()`),
кодирование в видео через ffmpeg (есть в системе). Альтернатива `--write-movie` отвергнута:
пишет весь процесс от старта, несовместима с подпроцессом GdUnit4.

## Механика

- **Опция сценария:** `record_video: true` (+ опц. `video_fps`, по умолч. 60) в JSON. По умолчанию выкл.
- **Захват:** весь прогон кадров в `StepExecutor` идёт через хелпер `Simulate(n)`. При записи он
  симулирует по 1 кадру (`SimulateFrames(1,16)`) и после каждого захватывает viewport → PNG
  `_video/<ID>/frame_NNNNN.png`. Без записи — одним куском (как раньше). Каждый PNG = 1 физкадр;
  видео в 60 fps → время детерминировано: `t = frame_index / 60`.
- **Кодирование:** `VideoRecorder.Finish()` → `ffmpeg -framerate 60 -i frame_%05d.png -c:v libx264
  -pix_fmt yuv420p _video/<ID>.mp4`, затем удаление PNG-последовательности.
- **Companion «инструкция»:**
  - `_video/<ID>.srt` — субтитры (setup + каждый main-step): `HH:MM:SS,mmm --> …` + описание шага.
  - `_video/<ID>.timeline.json` — то же машиночитаемо: `{step, type, reason, start_frame, end_frame, start_sec, end_sec}`.
- **Отчёт:** в HTML для записанных TC — `<video controls>` + ссылка на SRT.

## Компоненты

- `VideoRecorder.cs` (новый): `Start/Checkpoint/CaptureFrame/AddSpan/Finish/WriteTimeline`.
- `StepExecutor.cs`: `Simulate(n)`-хелпер, поле `Recorder`.
- `ScenarioRunner.cs`: создание рекордера, разметка спанов setup/main-step, финиш + таймлайн.
- `_scenario-schema.json`: свойства `record_video`, `video_fps`.
- `HtmlReport.cs`: блок `<video>` + SRT.

## Границы

Запись медленнее (пооборотный захват + ffmpeg). Действует только для opted-in сценариев.
Если ffmpeg недоступен — остаётся PNG-последовательность с пометкой в отчёте. Не заменяет плановые скриншоты.

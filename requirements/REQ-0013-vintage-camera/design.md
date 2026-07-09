# REQ-0013-vintage-camera — Реализация (Design / HOW)

> **Фича:** REQ-0013-vintage-camera · **Тип документа:** реализация (HOW) · **Статус:** ✅ реализовано
> **Глобальный контекст:** [TECH_SPEC.md](../TECH_SPEC.md).

---

## Структура кода

- **`src/Item.cs`** — фотоаппарат — обычный `Item` с `Usage = ItemUsage.ActivatedB`,
  `TypeId = "vintage_camera"`, `ModelPath = res://art/old_kodak_camera.glb`. Засев в
  `InventoryHud._Ready` (слот 0).
- **`src/ViewfinderHud.cs`** (`Control`, узел `HUD/Viewfinder`) — видоискатель + таймер +
  фокус (F-21..F-23). Конечный автомат `Phase { Inactive, Blocked, Counting }`.
- **`src/InventoryHud.cs`** — активация (F-18/B), бронь слота (F-19a), обработка ЛКМ и
  срабатывание (создание фотографии). См. [REQ-0012 design.md](../REQ-0012-base-item/design.md).
- **`src/Player.cs`** — предоставляет `EyePosition`, `CameraYawForward`, `CameraYawDeg`,
  `CameraPitchDeg`, `HeadAnchor` + `UnprojectToScreen`/`IsInFrontOfCamera` (для окна над
  головой) и `PlayPickupGesture()` (анимация взятия в руку).

## Активация и ввод

- Активация из инвентаря: выбор ячейки с предметом `ActivatedB` → `InventoryHud.ActivateSlot`
  (в руку, слот бронируется). Повторный выбор — `Deactivate` (toggle, F-19). При активации
  проигрывается жест взятия в руку `Player.PlayPickupGesture()` (клип `Interact`).
- Использование — действие `use_activated` (ЛКМ, кнопка мыши 1) в `InventoryHud._Input`:
  если активирован фотоаппарат и видоискатель неактивен → `ViewfinderHud.Begin(player, OnCameraFired)`.
  Повторная ЛКМ во время отсчёта игнорируется; открытие инвентаря блокируется (F-21).

## Видоискатель (F-21)

- **Рендер вида от первого лица:** `SubViewport` с `World3D = player.GetWorld3D()` (тот же мир,
  что у основной камеры) + собственная `Camera3D` на уровне глаз (`EyePosition`, Y≈1.5) с
  суженным FOV (`ViewfinderFov`=50). **Решение (top-down игра):** видоискатель смотрит
  **горизонтально** вдоль yaw (`CameraYawForward`, pitch=0) — в кадре коридор впереди.
  Камера обновляется каждый физ.кадр (`UpdateViewfinderCamera`).
- **Окно над головой (`_Draw`):** окно шириной `WindowWidthFraction`≈0.15 экрана (уменьшено ×2)
  позиционируется **над головой игрока** — проекция `Player.HeadAnchor` через
  `Player.UnprojectToScreen` (`WindowRect`), с «хвостиком»-указателем к голове.
  **Основной вид от третьего лица сохраняется** (сцену не затемняем, `_camera` не трогаем).
  Деревянная рамка, `ViewportTexture` вида, сепия, латунный кант, виньетка, крупная цифра.
- **Обзор не ограничивается:** pitch НЕ блокируется — игрок свободно осматривается от третьего
  лица; горизонтальный вид объектива обеспечивает отдельная камера видоискателя.

## Таймер (F-22)

- `[Export] float TickSeconds = 0.6667f`, `[Export] int StartCount = 3`. В `_PhysicsProcess`
  (фикс. такт) копится `_elapsed`; цифра = `StartCount - floor(_elapsed/TickSeconds)`
  (3→2→1). Срабатывание при `_elapsed ≥ TickSeconds*StartCount`; **0 не показывается**.

## Фокусное расстояние (F-23)

- `FocusDistanceOk()`: луч `DirectSpaceState.IntersectRay` из `EyePosition` вдоль
  горизонтального `CameraYawForward` длиной `FocusMinDistance` (1.8 = 3×0.6), маска 1,
  исключая Rid игрока. Нет попадания → фокус в норме.
- Проверка **перед** запуском (иначе `Phase.Blocked` ~`BlockedDuration`=1с, затем закрытие,
  камера цела) и **каждый кадр** во время `Counting` — при нарушении `Close()` без съёмки.

## Срабатывание (интеграция)

- По завершении отсчёта `ViewfinderHud` вызывает колбэк `_onFire` (= `InventoryHud.OnCameraFired`)
  **после** `Close()`. `OnCameraFired` создаёт `PhotoItem` с `CapturedWorldPos` = XZ игрока,
  `CapturedYawDeg` = `Player.CameraYawDeg`, `CapturedPitchDeg` = `Player.CameraPitchDeg`
  (наклон основной, top-down камеры — чтобы после переноса вид сверху сохранился), и
  `ConsumeActivated(photo)`: уничтожает камеру, кладёт фотографию в **тот же** забронированный
  слот (F-19a → REQ-0017). Затем `Player.PlayPickupGesture()` + подсветка ячейки (`_flashSlot`) —
  чтобы игрок заметил появление нового предмета.
- Прерывания: `Deactivate`/`DropActivated` в `InventoryHud` вызывают `ViewfinderHud.Cancel()`.

## Параметры (экспорт `ViewfinderHud`)

| Имя | Умолчание | Смысл |
|-----|-----------|-------|
| `TickSeconds` | 0.6667 | длительность тика отсчёта |
| `StartCount` | 3 | стартовое число (3→2→1) |
| `FocusMinDistance` | 1.8 | мин. фокусное (3 корпуса × 0.6) |
| `ViewfinderFov` | 50 | FOV объектива |
| `BlockedDuration` | 1.0 | показ «съёмка невозможна», сек |
| `WindowWidthFraction` | 0.15 | ширина окна как доля экрана (уменьшено ×2) |

## Границы (НЕ реализовано)

- Прерывание таймера через drop/deactivate **во время открытого видоискателя** недоступно из
  UI (инвентарь во время видоискателя заблокирован, F-21). Основной штатный сброс —
  **нарушение фокуса**. Явные drop/deactivate работают вне видоискателя.
- Ассеты рамки/виньетки/плёночного шума — процедурные (первый проход, F-21 TODO).
- Миниатюра запечатлённого вида в иконке фотографии не захватывается (см. REQ-0017 `design.md`);
  фотография использует процедурный плейсхолдер-полароид.
- 3D-модель фотоаппарата «в руке» не отображается (базовый F-16 её не требует).
- Звук тика/затвора — отложен.

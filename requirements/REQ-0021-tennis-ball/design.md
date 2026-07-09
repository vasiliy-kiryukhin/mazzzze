# REQ-0021-tennis-ball — Реализация (Design / HOW)

> **Фича:** REQ-0021-tennis-ball · **Тип документа:** реализация (HOW) · **Статус:** ✅ реализовано
> **Глобальный контекст:** [TECH_SPEC.md](../TECH_SPEC.md).

Реализовано. Ниже — как устроено на текущей архитектуре.

---

## Что принципиально нового

1. **Первый многоразовый предмет.** В отличие от фотоаппарата/фотографии (`ConsumeActivated`), бросок не уничтожает мяч: переход `Activated → InWorld`, слот освобождается (как `DropActivated`), мяч возвращается через авто-подбор (REQ-0016).
2. **Первый реальный физический снаряд.** Существующий `src/DropProjectile.cs` — детерминированная дуга A→B **без коллизий** (`_start.Lerp(_land,_t)` + парабола; на финише `new WorldItem`). Бросок — **кинематический** снаряд на `MoveAndCollide` (как весь код проекта — Player/Monster двигаются вручную).

> **Почему не `RigidBody3D`:** первая версия была на `RigidBody3D` и давала кучу артефактов в узком коридоре — мяч проваливался сквозь пол/стены, «плавал» (движковая гравитация 9.8 вместо игровых 15), бесконечно скакал между стен и отскакивал неестественно. Кинематический снаряд с ручной гравитацией и отражением от нормали удара всё это чинит и полностью контролируем.

## Структура кода

- **`src/Item.cs`** — мяч как обычный `Item` с `Usage = ActivatedB`, `TypeId = "tennis_ball"`, `ModelPath = res://art/base_ball.glb`. `BuildModel()` по умолчанию (инстанцирует glb) — без подкласса (как фотоаппарат, не как `PhotoItem`). Засев в слот 1 в `InventoryHud._Ready`.
- **`src/ThrowProjectile.cs`** (`CharacterBody3D`, кинематический) — снаряд броска:
  - `Setup(spawn, dir, force, upwardBias, item, targetHeight, aware)`: масштабирует и центрирует модель мяча по AABB (как `WorldItem`), ставит `SphereShape3D`, задаёт `_vel = dir*force + up*upwardBias`, несёт `Item`, `targetHeight` и множество «aware-at-throw» монстров. `CollisionLayer = 0` (никого не толкает), `CollisionMask = 1`.
  - Физика в `_PhysicsProcess`: `_vel.Y -= Gravity*dt` (`Gravity` = 15, **в темпе игры**, не движковые 9.8), мягкое гашение `_vel *= 1 - LinearDamp*dt`, затем `MoveAndCollide(_vel*dt)` (**заметаемая** коллизия — не протыкает тонкий пол/стены). На коллизии — **отражение от нормали**: нормальная составляющая × `Restitution`, касательная × `(1 - ImpactFriction·|vn|)` (сильный удар — больше потеря), плюс докат остатка `Slide(normal)`. На полу (нормаль вверх) — **трение качения** `GroundFriction`: гасит горизонт, чтобы мяч уверенно останавливался, а не бесконечно «полз» на пороге (иначе рывок при передаче в `WorldItem`).
  - **Попадание в монстра — не по слою, а по близости** (монстры делят слой 1 со стенами): `NearestMonsterHit()` ищет монстра в пределах `_hitRadius` планарно и по высоте тела (`BodyHeight`). При попадании: если монстр в `aware` → `monster.Stun()` (ветвь А, [F-49](./03-monster-effect-logic.md)); иначе — без стана. Затем `Land()` — снаряд гасится (одна цель).
  - Остановка: при `_vel.Length() < StopThreshold` накапливать `StopTime` → `Land()`. Предохранитель по `MaxAirborneTime`.
  - `Land()` (как `DropProjectile.Land`): создать `WorldItem` в точке остановки, `Setup(item, targetHeight)`, `QueueFree`. Позиция — `(pos.X, pos.Y − _radius, pos.Z)`: низ мяча остаётся на текущей высоте опоры, поэтому передача снаряд→предмет **бесшовна** (без проседания в пол и без рывка в сторону).
- **`src/InventoryHud.cs`** — заряд и бросок (диспетч по типу в `_Input`, перед веткой камеры):
  - Зажать ЛКМ (`use_activated` `IsActionPressed`) при активированном мяче → `_charging=true`, `_chargeT=0`.
  - Отпустить ЛКМ (`IsActionReleased`) → `ThrowActivated()`: `force = lerp(Min,Max, clamp(_chargeT/MaxChargeTime))`; `_inv.RemoveAt(slot)` (слот свободен, бронь снята), `ClearActivated()`, `SpawnThrow(item, force)`. `ConsumeActivated` **не** зовётся — мяч не расходуется, `item` живёт в снаряде.
  - `throw_cancel` (RMB) во время заряда → `CancelCharge()`: сброс `_chargeT`, восстановление `ExternalSpeedFactor`, мяч остаётся в руке.
  - `SpawnThrow`: точка выпуска — `player.HandThrowOrigin` (центр мяча в кисти) + `dir*0.35`, чтобы вылет шёл **из руки**, а не из-за спины; `dir = player.CameraYawForward` (горизонт); собирает `aware` = монстры из `Monster.All`, для которых `SeesPlayerNow()`; создаёт `ThrowProjectile` под `Main`.
  - **Единый размер мяча:** `WorldHeightFor(item)` возвращает `BallSize` для `tennis_ball` (иначе `WorldItemSizeFraction*PlayerHeight`), поэтому мяч в руке, в полёте и лежащий — **одного размера** (раньше рука была 0.18, а снаряд/мир — 0.45 = ×2.5, и мяч «раздувался» при приземлении). `ShowHandItem` тоже использует `BallSize`.
  - `UpdateThrowCharge` в `_PhysicsProcess`: копит `_chargeT` (плато на `MaxChargeTime`), занижает `player.ExternalSpeedFactor = MoveSpeedFactorWhileCharging`, пульсирует модель в руке.
- **`src/Monster.cs`** — добавлен `public bool SeesPlayerNow()` (обёртка над `CanSee(Player.EyePosition)`) для фиксации «видел бросок». `Stun()` уже был публичным; `BodyHeight` — публичное поле (для проверки высоты попадания).
- **`src/Player.cs`** — `CameraYawForward` (был) для направления броска; `ExternalSpeedFactor` — множитель скорости (инвентарь занижает при заряде, читается в `_PhysicsProcess`). Модель в руке через **`BoneAttachment3D` к кости `RightHand`** скелета `ModelPivot/Character/Rig/GeneralSkeleton`: `ShowHandItem/HideHandItem/SetHandItemCharge` + `HandThrowOrigin` (мировая точка мяча в кисти для выпуска броска). Крепление к кости → мяч **следует за анимацией** и выглядит в руке при любом ракурсе камеры (первая версия крепила к `ModelPivot` фикс-смещением — мяч «висел» рядом и уезжал при повороте камеры; заменено).

## Интеграция с монстром ([F-49](./03-monster-effect-logic.md))

**REQ-0019 реализован** (`Monster.cs`/`Ifrit.cs`), поэтому обе ветви работают:

- **Стан (ветвь А):** на выпуске мяча собираем `aware` по `Monster.All` (`SeesPlayerNow()`). При попадании снаряда в монстра из `aware` → `monster.Stun()` (состояние `Stun`, `StunDuration` живёт в монстре).
- **Отвлечение (ветвь Б):** уже обслуживается самим монстром без спец-кода мяча. `Monster.FindVisibleLure()` в состоянии `Cycle` сканирует `WorldItem.All` (любой предмет в конусе зрения) → `Distract` → идёт к предмету, при достижении `DistractReachRadius` возвращается в `Cycle`. Мяч, ставший `WorldItem` после остановки, автоматически попадает в этот скан. Мяч остаётся в мире (не уносится) — можно подобрать.
- **Мяч в полёте** для отвлечения **не** учитывается: `WorldItem` создаётся только после остановки, а `FindVisibleLure` сканирует `WorldItem.All`. Раннее отвлечение в полёте — будущее (потребует регистрации летящего снаряда как lure).

## Что НЕ входит (границы)

- **Убийство маленького монстра** — будущая специализация (маленьких монстров нет).
- **Пробивание (piercing)** — нет, одна цель на бросок.
- **Прицельная дуга** — отсутствует (возможна через будущие артефакты).
- **Трейл снаряда / звук отскока** — отложены.
- **Тонкая калибровка позы мяча в ладони** (`HandPalmOffset`) — смещение подобрано на глаз; в покое кисть висит у бедра, поэтому мяч там же (следует за костью).
- **Отдельный `InspectDuration` мяча** — не вводится: длительность «осмотра» = штатное поведение `Distract` монстра (доходит и возвращается), отдельного «стоит N секунд у мяча» в базовом монстре нет.
- **Раннее отвлечение мячом в полёте** — нет (см. выше).

## Связи

[REQ-0012-base-item](../REQ-0012-base-item/README.md); [REQ-0014 design.md](../REQ-0012-base-item/REQ-0014-base-item-item-in-world/design.md) (`WorldItem.Setup`/`ComputeSceneAabb` — образец для посадки мяча на пол и масштаба); [REQ-0015 DropProjectile.cs](../REQ-0012-base-item/REQ-0015-base-item-drop/design.md) (образец `Land()` и contrasts); [REQ-0016](../REQ-0012-base-item/REQ-0016-base-item-pickup/README.md) (возврат); [REQ-0019-base-monster](../REQ-0019-base-monster/README.md) (стан/отвлечение); [IDEA-0025](../../ideas/items/IDEA-0025-tennis-ball.md).

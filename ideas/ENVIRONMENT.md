# Разнообразие окружения — сводный индекс

> **Область:** world · atmosphere · mechanics · **Статус:** 🟡 индекс идей (эпик)

Индекс всех идей по тому, как сделать мир игры **разнообразнее, интереснее и вовлекательнее**.
Отдельные идеи живут файлами `IDEA-NNNN` в своих областях (`world/`, `atmosphere/`, `mechanics/`) —
здесь собран **список со всеми обсуждёнными деталями**: что даёт, как реализовать (с привязкой к коду),
где взять ассеты, зависимость от библиотеки `maze-gen` и оценка сложности в story points.

Язык: идеи — по-русски; идентификаторы кода, имена файлов, классы/методы библиотеки, названия
ассет-источников и Godot-узлов — по-английски (как во всех файлах `ideas/`).

---

## Легенда сложности (story points)

Относительная оценка по Фибоначчи: код игры + ассеты + доработка библиотеки. Библиотечная работа
(доработка фасада `PlayersWorlds.Maps.World` или форк) — главный множитель.

| SP | Смысл |
|----|-------|
| **1–2** | точечная правка (материал/параметр/один узел) |
| **3** | небольшая самодостаточная фича, ассеты почти не нужны |
| **5** | фича на 1 подсистему игры, немного данных/ассетов |
| **8** | новая подсистема (генератор данных + рендер + стриминг) |
| **13** | крупная фича, затрагивает генерацию/патфайндинг **или** требует доработки фасада библиотеки |
| **21** | архитектурный сдвиг: форк/расширение `maze-gen` **или** новое измерение мира |

---

## Что уже умеет `maze-gen` (границы возможностей)

Фасад `PlayersWorlds.Maps.World`, который использует [MazeData](../requirements/TECH_SPEC.md#51-mazedata---world-data-and-procedural-generation).
Это определяет, «дёшево» или «дорого» обходится каждая идея.

**Доступно прямо сейчас через фасад:**
- Пресеты рецептов `RegionRecipe`: `Maze` / `Corridors` / `Dungeon` / `Caverns`.
- 6 алгоритмов `RegionAlgorithm` (RecursiveBacktracker, AldousBroder, HuntAndKill, Wilsons, Sidewinder, BinaryTree) + `Custom<T>()`.
- `WithFill(double)` — частичное заполнение (неквадратные/коридорные мазы), `WithCells` — неквадратные клетки.
- `WithRooms(count, min, max, RoomKind, tags…)` — комнаты `Hall` / `Cave` / `Blocked` + произвольные строковые теги.
- PoI: `RegionView.Pois` → `Entrance` / `Exit` / `DeadEnd`.
- Пограничные **`Gate`** (`RegionView.Gates`) — будущие «швы» для соседних регионов.
- **Per-cell `Tags`** через `RegionView.CellAt(...).Tags` — включая `MAZE2D_WALL` / `MAZE2D_TRAIL` / `MAZE2D_CORNER`. **Это главный канал per-cell разнообразия сегодня.**
- N-мерная адресация `RegionAddress` + `ToWorld`/`FromWorld`, детерминированный сид, подключаемый `IRegionStore`.

**Есть слоем ниже, требует доработки фасада (façade work):**
- Per-cell **тип комнаты** (`RegionCell.Type` сейчас всегда `Environment` — не различает Hall/Cave).
- Классификация junction / прямой коридор / петля / зал (выводится из `Cell.Links()` на Border-мазе до её отбрасывания).
- Вероятностные тег-таблицы `RandomAreaGenerator` (ruins/lake/swamp/den/cave/loot) — сейчас фасад их **не использует**.
- Вложенные мазы (маз в комнате) через `GeneratedWorld.OfMaze` рекурсию; `FullWidth`/`FullHeight` частичные заливки.

**В дорожной карте, но НЕ реализовано (нужен форк/расширение библиотеки):**
- **Сшивка соседних регионов** по `Gate` (адресация и Gates есть, стыковки нет) — см. [IDEA-0027](world/IDEA-0027-adjacent-regions.md).
- **Elevation / 3D** — `WithElevation` бросает `NotImplementedException`; `AddEnvironmentAreas` — заглушка. Основа для [IDEA-0031](world/IDEA-0031-verticality.md).
- **Генерация петель/braid** (все алгоритмы дают perfect-мазы) — основа для [IDEA-0030](world/IDEA-0030-cyclic-generation.md).
- Новые типы PoI (`PoiKind` — закрытый enum).

---

## Фундаментные идеи (делать первыми — от них зависят остальные)

| ID | Идея | Почему фундамент | SP |
|----|------|------------------|----|
| [0014](world/IDEA-0014-biomes.md) | **Биомы** (тег региона/квадранта) | один тег биома управляет палитрой, туманом, светом, тайлсетом, таблицами пропов/мобов | 5 |
| [0037](mechanics/IDEA-0037-cell-feature-layer.md) | **Слой фич клетки + валидация** | общий канал для воды/ям/дверей/ловушек/вертикальности + гарантия проходимости | 8 |
| [0035](world/IDEA-0035-material-variety.md) | **Разнообразие материалов** (per-chunk material) | снимает главный «затык» однообразия: 2 глобальных материала на весь мир | 8 |

---

## Каталог идей по темам

Колонки: **Что даёт** · **Как реализовать** (привязка к коду/узлам) · **Ассеты** · **Библиотека** · **SP**.

### Тема 1 — Структура мира и генерация (`world/`)

| ID | Идея | Что даёт · Как · Ассеты · Библиотека | SP |
|----|------|--------------------------------------|----|
| [0028](world/IDEA-0028-algorithm-variety.md) | Разные алгоритмы/рецепты по зонам | **Что:** структурное разнообразие (петляющие коридоры vs каверны vs арены) само читается как разнообразие. **Как:** `MazeData` выбирает `RegionRecipe`/`RegionAlgorithm` по биому. **Библиотека:** доступно сейчас. | 5 |
| [0029](world/IDEA-0029-rooms-with-purpose.md) | Комнаты и залы с назначением | **Что:** боевые/сокровищные/безопасные/видовые комнаты вместо «просто ширины». **Как:** `WithRooms(Hall/Cave)` + теги; декор по типу. **Библиотека:** `WithRooms` есть; per-cell `Type` — façade work. | 8 |
| [0030](world/IDEA-0030-cyclic-generation.md) | Циклическая генерация / петли | **Что:** убирает скучный backtracking из тупиков, «рукотворное» ощущение (Unexplored). **Как:** инъекция `HardLinks` game-side или форк алгоритма. **Библиотека:** braid НЕ реализован. | 13 |
| [0027](world/IDEA-0027-adjacent-regions.md) | Смежные регионы («достраивание мира») | **Что:** бесконечный мир, соседние регионы стыкуются по проходам. **Как:** `World.GetOrCreate(RegionAddress)` + сшивка по `Gate`. **Библиотека:** адресация+Gates есть, **стыковка не реализована**. | 21 |
| [0031](world/IDEA-0031-verticality.md) | Вертикальность (`floorLevel`, лестницы, шахты, пандусы) | **Что:** самый крупный структурный выигрыш от 30-юнитовых стен: этажи, балконы, спуски. **Как:** `floorLevel` per cell, стек 2D-уровней, переработка `Chunk`/патфайндинга. **Библиотека:** elevation `NotImplemented`. | 21 |
| [0032](world/IDEA-0032-landmarks.md) | Ориентиры-«вини» над стенами | **Что:** башня/статуя/кристалл над `WallHeight` виден издалека → навигация без HUD. **Как:** спавн сцен над стенами по PoI/биому; лучи/дым как маяки. **Ассеты:** Kenney, Quaternius, Poly Haven, Sketchfab CC0. | 8 |
| [0033](world/IDEA-0033-open-space-content.md) | Наполнение открытых пространств | **Что:** большая комната «зарабатывает» размер: фокус-объект, арена-энкаунтер, награда, тablo-история, фото-ракурс. **Как:** от типа комнаты ([0029](world/IDEA-0029-rooms-with-purpose.md)) + `MonsterSpawner` + предметы. | 8 |
| [0034](world/IDEA-0034-poi-driven-population.md) | PoI-управляемое наполнение | **Что:** лут/мобы/декор/ориентиры по PoI (Entrance/Exit/DeadEnd + новые: длинный коридор, junction). **Как:** читать `RegionView.Pois`; новые PoI выводить из `IsFloor`-соседства game-side. **Библиотека:** базовые PoI есть; новые — façade work. | 5 |
| [0035](world/IDEA-0035-material-variety.md) | Разнообразие текстур/материалов | **Что:** снимает 2-материальное однообразие. **Как:** per-chunk `ShaderMaterial` (tint/biome), triplanar-breakup, Detail-overlay, `Decal`, MeshLibrary-варианты, `Texture2DArray`. **Ассеты:** ambientCG, Poly Haven, cgbookcase (CC0). | 8 |
| [0036](world/IDEA-0036-structural-walls.md) | Разные стены на углах/тупиках/торцах | **Что:** углы/тупики/торцы стен выглядят иначе → детализация «даром». **Как:** выбор меша/декора по per-cell тегам `MAZE2D_CORNER`/`WALL`/`TRAIL` + PoI dead-end. **Библиотека:** теги доступны сейчас. | 5 |

### Тема 2 — Фичи и препятствия (`mechanics/`)

Все зависят от [IDEA-0037](mechanics/IDEA-0037-cell-feature-layer.md) (слой фич + валидация проходимости).

| ID | Идея | Что даёт · Как · Ассеты | SP |
|----|------|--------------------------|----|
| [0037](mechanics/IDEA-0037-cell-feature-layer.md) | Слой фич клетки + валидация | **Что:** общий per-cell enum/flags (Water/Pit/Rubble/Door/Hazard/Stair/Prop) + двухфазная генерация generate-then-validate (flood-fill по `IsFloor`). **Как:** генератор пишет слой, `Chunk` читает при постройке GridMap, не-тайловые фичи — сцены в центрах клеток. | 8 |
| [0038](mechanics/IDEA-0038-water.md) | Вода | **Что:** пруды, затопленные коридоры (замедление/глушение звука), ручьи, водопады по стенам, reskin в лаву/кислоту. **Как:** water-shader + флаг глубины. **Ассеты:** Boujie Water (MIT), GameIdea Stylized Water. | 8 |
| [0039](mechanics/IDEA-0039-chasms-bridges.md) | Пропасти и мосты | **Что:** ямы/уступы + каменный/верёвочный/сломанный/подъёмный мост, «стёпки». **Как:** `Pit`-флаг блокирует патфайндинг кроме переходов; `AnimatableBody3D` для мостов. | 8 |
| [0040](mechanics/IDEA-0040-rubble-destructibles.md) | Завалы и разрушаемые объекты | **Что:** непроходимые/расчищаемые завалы, ломкие стены/бочки, толкаемые блоки, обвал-ловушка. **Как:** `RigidBody3D`/здоровье/snap-to-grid; ломкая стена открывает секрет. **Ассеты:** Quaternius Dungeon, Kenney (CC0). | 8 |
| [0041](mechanics/IDEA-0041-doors-locks.md) | Двери, ворота, замки, ключи | **Что:** запертая дверь+ключ (переиспользует Key-предметы), портокуллис-ловушка, one-way, секретные, цветные. **Как:** door-state per edge; lock-key граф — ключ на маршруте **до** двери. | 8 |
| [0042](mechanics/IDEA-0042-traps-hazards.md) | Ловушки и опасности | **Что:** плиты/растяжки, шипы, дротики, маятники, огонь/газ, обваливающийся пол, тьма, лёд, тревога-спавн. **Как:** state-machine (armed→trigger→active→cooldown) в `_PhysicsProcess`; у каждой — видимый «tell». | 13 |
| [0043](mechanics/IDEA-0043-interactive-objects.md) | Интерактивные объекты | **Что:** рычаги/кнопки/факелы-зажечь/сундуки/вентили/алтари/колокола. **Как:** реестр (аналог `WorldItem.All`) + сигнал на связанную фичу-id. | 8 |

### Тема 3 — Атмосфера (`atmosphere/`)

| ID | Идея | Что даёт · Как · Ассеты | SP |
|----|------|--------------------------|----|
| [0044](atmosphere/IDEA-0044-zone-lighting.md) | Освещение по зонам | **Что:** `LightingProfile` на биом (цвет/энергия солнца, ambient, glow), мерцающие факелы, цвет-света для навигации, вертикальные god-ray-шахты в каньоне. **Как:** лерп профиля при смене региона; `SpotLight3D` вниз + volumetric. | 8 |
| [0045](atmosphere/IDEA-0045-volumetric-fog.md) | Объёмный туман и световые шахты | **Что:** глубина и настроение; туман-как-хоррор при близости монстра. **Как:** `WorldEnvironment` volumetric fog + height-fog fallback + локальные `FogVolume`. **Ассеты:** SimplestGodRay3D addon. | 5 |
| [0046](atmosphere/IDEA-0046-ambient-particles.md) | Атмосферные частицы | **Что:** пылинки в god-ray, угли, светлячки, капель, споры (осадки-погода — в [0022](atmosphere/IDEA-0022-weather-fog.md)). **Как:** `GPUParticles3D` за игроком, `local_coords=false`, `GPUParticlesCollision3D` HeightField. | 5 |
| [0047](atmosphere/IDEA-0047-spatial-audio.md) | Пространственный звук и реверберация | **Что:** реверб по типу помещения, слои ambient, позиционные звуки, дальний рёв монстра, оклюзия low-pass, duck напряжения. **Как:** `Area3D`+reverb-bus, `AudioStreamPlayer3D`. **Ассеты:** Freesound, OpenGameArt (CC0). | 8 |
| [0048](atmosphere/IDEA-0048-sky-grading.md) | Небо и цветокоррекция по биому | **Что:** HDRI/PhysicalSky, time-of-day, per-biome LUT/adjustments, AgX-tonemap, SSAO/SSIL, vignette. **Как:** `PanoramaSkyMaterial` + `Environment.adjustment_*`. **Ассеты:** Poly Haven HDRI (CC0). | 5 |
| [0021](atmosphere/IDEA-0021-dynamic-music.md) | Динамическая музыка *(существует)* | напряжение у монстра / спокойствие у алтарей; подмножество [0047](atmosphere/IDEA-0047-spatial-audio.md). | 5 |
| [0022](atmosphere/IDEA-0022-weather-fog.md) | Погода (туман/дождь) *(существует)* | локальная регионозависимая погода; связана с [0045](atmosphere/IDEA-0045-volumetric-fog.md)/[0046](atmosphere/IDEA-0046-ambient-particles.md). | 8 |
| [0023](atmosphere/IDEA-0023-footstep-audio.md) | Шаги по поверхностям *(существует)* | звук шага от материала пола; требует [0035](world/IDEA-0035-material-variety.md). | 3 |

---

## Дорожная карта (предлагаемый порядок)

**1. Быстрые победы** (низкий SP, высокий эффект, без работы с библиотекой) — сразу «оживляют» мир:
[0045](atmosphere/IDEA-0045-volumetric-fog.md) туман+шахты (5) · [0046](atmosphere/IDEA-0046-ambient-particles.md) частицы (5) ·
[0044](atmosphere/IDEA-0044-zone-lighting.md) свет по зонам (8) · [0048](atmosphere/IDEA-0048-sky-grading.md) небо/грейд (5) ·
[0036](world/IDEA-0036-structural-walls.md) стены углов/торцов (5) · [0028](world/IDEA-0028-algorithm-variety.md) алгоритмы по зонам (5).

**2. Фундамент** — открывает всё остальное:
[0014](world/IDEA-0014-biomes.md) тег биома (5) · [0035](world/IDEA-0035-material-variety.md) per-chunk материалы (8) ·
[0037](mechanics/IDEA-0037-cell-feature-layer.md) слой фич + валидация (8).

**3. Средние** — содержательное разнообразие:
[0029](world/IDEA-0029-rooms-with-purpose.md) комнаты-назначения (8) · [0034](world/IDEA-0034-poi-driven-population.md) PoI-наполнение (5) ·
[0032](world/IDEA-0032-landmarks.md) ориентиры (8) · [0033](world/IDEA-0033-open-space-content.md) открытые пространства (8) ·
[0038](mechanics/IDEA-0038-water.md)–[0041](mechanics/IDEA-0041-doors-locks.md) вода/пропасти/завалы/двери (8 каждая) ·
[0043](mechanics/IDEA-0043-interactive-objects.md) интерактив (8) · [0047](atmosphere/IDEA-0047-spatial-audio.md) звук (8).

**4. Крупные ставки** (форк/расширение библиотеки или новое измерение):
[0030](world/IDEA-0030-cyclic-generation.md) петли (13) · [0042](mechanics/IDEA-0042-traps-hazards.md) ловушки (13) ·
[0027](world/IDEA-0027-adjacent-regions.md) смежные регионы (21) · [0031](world/IDEA-0031-verticality.md) вертикальность (21).

---

## Каталог бесплатных ассет-источников

| Источник | Лицензия | Для чего |
|----------|----------|----------|
| [ambientCG](https://ambientcg.com/) | CC0 | PBR-материалы (rock/gravel/dirt/moss, ORM-ready), HDRI |
| [Poly Haven](https://polyhaven.com/) | CC0 | HDRI-небо, PBR-текстуры, сканы камней/пропов |
| [cgbookcase](https://www.cgbookcase.com/) · [3DTextures.me](https://3dtextures.me/) · [ShareTextures](https://www.sharetextures.com/) | CC0 | доп. PBR-текстуры |
| [Kenney.nl](https://kenney.nl/assets) | CC0 | dungeon/nature/graveyard киты пропов, SFX |
| [Quaternius](https://quaternius.com/) | CC0 | low-poly модульный dungeon (бочки, факелы, двери, мосты), природа, монстры |
| [Sketchfab](https://sketchfab.com) | CC/CC0 (по модели) | статуи, руины, уникальные ориентиры — **проверять лицензию** |
| [OpenGameArt](https://opengameart.org/) | смешанная | пропы, листва, CC0 SFX-паки, dungeon-ambience |
| [Freesound](https://freesound.org/) | CC/CC0 (фильтр) | ветер, капель, треск факелов, скрип дверей, рёв монстров |
| [Godot AssetLib](https://godotengine.org/asset-library/) | смешанная | fog/particle/sky/scatter/WFC аддоны |
| [godotshaders.com](https://godotshaders.com) | в осн. CC0/MIT | triplanar, вода, туман, splatmap, tint, tile-breakup шейдеры |

> Осторожно: free-tier textures.com и FreePBR **не** CC0 — проверять условия. На OpenGameArt/Sketchfab лицензия — по каждому ассету.

---

## Ссылки и референсы

**Теория PCG:** Kate Compton [«So you want to build a generator…»](https://galaxykate0.tumblr.com/post/139774965871/) и
[«10 000 Bowls of Oatmeal»](https://emshort.blog/2016/09/21/bowls-of-oatmeal-and-text-generation/);
[Red Blob Games](https://www.redblobgames.com/); Bob Nystrom [«Rooms and Mazes»](https://journal.stuffwithstuff.com/2014/12/21/rooms-and-mazes/).
**WFC (C#):** Boris the Brave [DeBroglie](https://boristhebrave.github.io/DeBroglie/).
**Циклическая генерация:** [«Unexplored's Secret: Cyclic Dungeon Generation»](https://www.gamedeveloper.com/design/unexplored-s-secret-cyclic-dungeon-generation-).
**GDC:** Bucklew & Grinblat [«End-to-End Procedural Generation in Caves of Qud»](https://www.youtube.com/watch?v=jV-DZqdKlnE).
**Level design:** [Level Design Book — Wayfinding](https://book.leveldesignbook.com/process/blockout/wayfinding); «weenies» (Disney).
**Игры-референсы:** Spelunky, Enter the Gungeon, Dead Cells, Hades, Unexplored, Caves of Qud (структура);
Amnesia, Silent Hill, Lethal Company, Metro, Blair Witch/Devour (атмосфера, релевантно фото-механике).

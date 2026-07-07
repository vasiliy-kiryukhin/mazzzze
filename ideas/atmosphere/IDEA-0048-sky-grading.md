# IDEA-0048 — Небо и цветокоррекция по биому

> **Область:** atmosphere · **Статус:** 🟡 идея

Небо и грейд как дешёвый сигнал зоны. `PhysicalSkyMaterial` + медленный time-of-day (поворот `DirectionalLight3D` — dawn/dusk/overcast; фикс по сиду меняет настроение даром); высокие стены обрамляют небо полосой — стоит сделать красивой. `PanoramaSkyMaterial` + CC0 HDRI (штормовое/звёздное/кроваво-закатное; свап по биому; ambient/reflection source = Sky, чтобы красное небо красило стены). Per-biome LUT/adjustments — лерп `adjustment_brightness/contrast/saturation` (десатурация+краш чёрных = жуть, тепло+сочность = безопасный хаб; расширяет sepia-конвейер фото на весь биом). AgX-tonemap (4.3+) мягко катает хайлайты факелов в темноте; SSAO углубляет швы стен/пола; SSIL отражает тёплый свет факела; vignette (усиливать при близости монстра).

**Сложность:** 5 SP. **Ассеты:** Poly Haven HDRI (CC0); godot-color-lut-shader.

**Связи:** [IDEA-0014](../world/IDEA-0014-biomes.md), [IDEA-0044](IDEA-0044-zone-lighting.md), REQ-0013/REQ-0017 (sepia фото-конвейер), [ENVIRONMENT](../ENVIRONMENT.md).

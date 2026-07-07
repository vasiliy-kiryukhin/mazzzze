# IDEA-0035 — Разнообразие текстур и материалов

> **Область:** world · **Статус:** 🟡 идея

Снимает главный «затык» однообразия: сейчас весь мир — 2 глобальных материала в `MazeTiles.tres`. Приёмы дешёвого разнообразия поверх текущего world-space triplanar:

- **Per-chunk `ShaderMaterial`**: каждый `Chunk` получает свой инстанс материала с uniform-ами биома (tint/HSV) — GridMap не даёт per-cell shader-параметры, поэтому вариация **на чанк**.
- **Tile-breakup** (noise-offset) + macro-noise, чтобы 30-юнитовая стена не «тикала» повтором.
- **Detail-overlay** (вторичный albedo+normal по маске) и `Decal`-ноды (мох/трещины/лужи/грязь) точечно.
- **MeshLibrary-варианты** (Wall_A/B/C, та же коллизия) + `Texture2DArray`; выбор по стабильному хешу мировых координат клетки.

**Сложность:** 8 SP. **Ассеты:** ambientCG, Poly Haven, cgbookcase (CC0 PBR, ORM-ready); шейдеры на godotshaders.com.

**Связи:** [IDEA-0014](IDEA-0014-biomes.md), [IDEA-0036](IDEA-0036-structural-walls.md), [IDEA-0023](../atmosphere/IDEA-0023-footstep-audio.md), [ENVIRONMENT](../ENVIRONMENT.md).

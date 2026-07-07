# IDEA-0045 — Объёмный туман и световые шахты

> **Область:** atmosphere · **Статус:** 🟡 идея

Самый сильный per-area рычаг настроения. Глобальный volumetric fog на существующем `WorldEnvironment` (Forward+): `density` ~0.01–0.05, `anisotropy` ~0.3 под forward-scatter шахты. Дешёвый fallback — height-fog (`fog_height`/`fog_height_density`), стелющийся по дну каньона почти даром; volumetric — только в hero-зонах. Локальные `FogVolume` per room (положительная плотность — жуть в коридорах, **отрицательная** — чистые карманы в безопасных комнатах; `NoiseTexture3D` для дрейфа). Туман-как-хоррор: поднимать плотность при падении дистанции до ближайшего `Monster.All` (Silent Hill). True god rays: очень низкая глобальная `density` + `light_volumetric_fog_energy` высокий.

**Сложность:** 5 SP. **Ассеты:** SimplestGodRay3D addon (стилизованные лучи без volumetric).

**Связи:** [IDEA-0022](IDEA-0022-weather-fog.md) (динамическая погода), [IDEA-0044](IDEA-0044-zone-lighting.md), [ENVIRONMENT](../ENVIRONMENT.md).

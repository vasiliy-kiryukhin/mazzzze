# IDEA-0047 — Пространственный звук и реверберация

> **Область:** atmosphere · **Статус:** 🟡 идея

Per-zone реверб через `Area3D` + reverb-bus (`AudioEffectReverb`): в зал — глубокое эхо, в открытый каньон — сухо/широко (по одному на чанк/тип PoI). Слои ambient — кросс-фейд 2–3 непозиционных лупов (ветер каньона, капель, дальний гул) той же сменой зоны, что свет. Позиционные env-звуки (капель, треск факела, ветер в щелях) через `AudioStreamPlayer3D`. Дальний звук монстра (рык/шаги от позиций `Monster`, большой `max_distance` + low-pass при оклюзии — переиспользуя LoS-рейкаст: слышишь ифрита раньше, чем видишь). Duck напряжения при `CanSee(player)` монстра. Оклюзия low-pass (`AudioEffectLowPassFilter`, cutoff растёт с LoS — глухо сквозь стены).

**Сложность:** 8 SP. **Ассеты:** Freesound, OpenGameArt (CC0 SFX, dungeon-ambience).

**Связи:** [IDEA-0021](IDEA-0021-dynamic-music.md), [IDEA-0023](IDEA-0023-footstep-audio.md), REQ-0019/REQ-0020 (монстры, LoS), [ENVIRONMENT](../ENVIRONMENT.md).

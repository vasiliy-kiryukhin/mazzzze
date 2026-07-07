# IDEA-0042 — Ловушки и опасности

> **Область:** mechanics · **Статус:** 🟡 идея

Опасность per-cell со state-machine armed→trigger→active→cooldown в `_PhysicsProcess`; у каждой ловушки — триггер + эффект + **видимый «tell»** (честность). Плиты/растяжки (универсальный триггер: игрок, толкаемый блок или монстр); шипы (по триггеру/ритму); дротики из стен (дырки = подсказка); маятники/качающиеся лезвия/катящиеся валуны (цикл по `Path3D`/tween на `AnimatableBody3D`); огненные струи/газ (пульс/DoT-облако, обойти или найти вентиль, `Area3D` + volumetric); обваливающийся пол (floor→pit через beat после шага); зоны тьмы (гасят ambient — виден только `HeadLight`); лёд/грязь (модификатор трения/скорости, без урона); плита-тревога (спавнит/агрит монстров через `MonsterSpawner`/`Monster.All` — ловушка как инструмент темпа).

**Сложность:** 13 SP (много типов; зависит от [IDEA-0037](IDEA-0037-cell-feature-layer.md)).

**Связи:** [IDEA-0037](IDEA-0037-cell-feature-layer.md), [IDEA-0043](IDEA-0043-interactive-objects.md), [IDEA-0034](../world/IDEA-0034-poi-driven-population.md), REQ-0019/REQ-0020 (монстры), [ENVIRONMENT](../ENVIRONMENT.md).

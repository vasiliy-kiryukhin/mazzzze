# IDEA-0038 — Вода

> **Область:** mechanics · **Статус:** 🟡 идея

Вода per-cell (флаг + глубина): мелкие лужи/пруды (косметика + всплеск/рябь), затопленные коридоры по пояс (замедление ~от глубины, глушение звука), ручьи через коридор (ломают длинные пролёты, сносят предметы к игроку), водопады по 30-юнитовым стенам (scrolling-UV плоскость + `GPUParticles3D`-туман — драматичный ориентир), комната с прибывающей/убывающей водой. Reskin той же системы в лаву/кислоту (emissive + флаг урона; глубокая лава = провал, нужен мост).

**Сложность:** 8 SP (зависит от [IDEA-0037](IDEA-0037-cell-feature-layer.md)). **Ассеты:** Boujie Water Shader (MIT), GameIdea Stylized Water (Godot 4.6), bramreth Godot4WaterShader.

**Связи:** [IDEA-0037](IDEA-0037-cell-feature-layer.md), [IDEA-0039](IDEA-0039-chasms-bridges.md), [IDEA-0043](IDEA-0043-interactive-objects.md) (вентиль осушает), [ENVIRONMENT](../ENVIRONMENT.md).

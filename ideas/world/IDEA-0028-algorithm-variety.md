# IDEA-0028 — Разные алгоритмы генерации по зонам

> **Область:** world · **Статус:** 🟡 идея

Структура сама читается как разнообразие: петляющие коридоры (RecursiveBacktracker), органичные каверны (`Caverns`-пресет с AldousBroder+Cave), почти открытые арены (BinaryTree/`WithFill`). Пусть `MazeData` выбирает `RegionRecipe` / `RegionAlgorithm` по биому/квадранту. Всё доступно через фасад уже сейчас — дешёвый источник вариативности.

**Сложность:** 5 SP. **Библиотека:** доступно сейчас (6 алгоритмов + `Custom<T>()`; учесть, что Sidewinder/BinaryTree требуют `FillFactor.Full`).

**Связи:** [IDEA-0014](IDEA-0014-biomes.md), [IDEA-0029](IDEA-0029-rooms-with-purpose.md), [ENVIRONMENT](../ENVIRONMENT.md).

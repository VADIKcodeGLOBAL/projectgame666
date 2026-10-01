# projectgame666 — 3D shooter

Корень папки — проект Unity 6.3 LTS (6000.3.2f1), Built-in Render Pipeline.

```
Assets/           всё, что импортируется в движок (Art, Audio, Animations, Scenes, Prefabs, Scripts, Shaders, Data)
Packages/         манифест пакетов Unity
ProjectSettings/  настройки проекта Unity
Source/           рабочие файлы (.blend, скрипты Blender, Substance, сырой звук) — в движок не попадают
Docs/             дизайн-документы, референсы, техдокументация
Builds/           сборки игры
Tools/ Tests/ ThirdParty/
Library/ Temp/ Logs/ UserSettings/   служебные папки Unity, в .gitignore
```

## Игра: аркада «Удержи холм»

Сцена: `Assets/Scenes/Levels/Level_KingOfTheHill.unity`. Игрок держит зону на вершине холма, 5 волн ботов по 1:30,
смена дня и ночи. Описание режима, карты и алгоритма генератора: `Docs/Design/KingOfTheHill_LevelDesign.md`.

| Что | Где |
|---|---|
| Генератор карты | `Assets/Scripts/Editor/KothMapGenerator.cs` — меню **Tools > Level > Hill Map Generator** |
| Параметры карты и волн | `Assets/Data/Levels/KothMapSettings.asset` |
| Сгенерированные ассеты (террейн, слои, камни, фон) | `Assets/Art/Environment/KingOfTheHill/` |
| Игра волн, боты, зона | `Assets/Scripts/Core/WaveSurvivalGame.cs`, `Assets/Scripts/AI/EnemyBot.cs`, `Assets/Scripts/Level/HillZone.cs` |
| День и ночь | `Assets/Scripts/Level/DayNightCycle.cs`, шейдер `Assets/Shaders/SkyDayNight.shader` |
| Игрок, оружие, HUD, эффект камеры | `Assets/Scripts/Player/`, `Assets/Scripts/Weapons/PlayerGun.cs`, `Assets/Scripts/UI/SurvivalHud.cs`, `Assets/Scripts/Core/EdgeBlurEffect.cs` |
| Префаб бота | `Assets/Prefabs/Enemies/EnemyBot_Box.prefab` |
| Шейдеры | `Assets/Shaders/` — небо, боты, зона, вода, листва, эффект камеры, двусторонний PBR, вершинный цвет |
## Модели

- Деревья: `Source/Blender/Scripts/trees_leafcards.py`, LOD — `tree_lods.py` (исходник `Source/Blender/Environment/trees.blend`).
- Персонаж RavenWolf: `Assets/Art/Characters/NPC/RavenWolf/`, префаб `Assets/Prefabs/NPC/RavenWolf.prefab`.
- Конвертация `.glb` в FBX + материалы: `Source/Blender/Scripts/glb_to_unity.py`, затем **Tools > Models > Setup Converted Model**.
- Первый low-poly ландшафт с большой горой: `Assets/Art/Environment/Landscape/Models/Landscape_LowPoly.fbx`
  (генератор `Source/Blender/Scripts/landscape_lowpoly.py`) — в текущем уровне не используется.

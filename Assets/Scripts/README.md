# Sportland - Code Organization

This document explains the folder structure and namespace organization for the Sportland multi-sport management game.

## Namespace Convention

All namespaces follow the folder structure pattern: `Sportland.<FolderPath>`

Example:
- `Assets/Scripts/Core/GameManagement/` → `namespace Sportland.Core.GameManagement`
- `Assets/Scripts/Sports/Basketball/Gameplay/` → `namespace Sportland.Sports.Basketball.Gameplay`

## Folder Structure

```
Assets/Scripts/
├── Core/                          # Central systems (persist across all sports)
│   ├── GameManagement/            # Game lifecycle & sport loading
│   │   ├── CoreGameManager.cs     # Main singleton managing everything
│   │   └── ISportModule.cs        # Interface for all sports
│   ├── Athlete/                   # Player/athlete data
│   │   └── Athlete.cs             # ScriptableObject for athletes
│   ├── Flags/                     # Flag system (TODO)
│   ├── Player/                    # Player character & classes (TODO)
│   ├── Calendar/                  # Time/season management (TODO)
│   └── Utilities/                 # Shared utilities
│
├── Sports/                        # All sport implementations
│   ├── _Shared/                   # Shared sport utilities
│   ├── Basketball/                # Basketball sport module
│   │   ├── Gameplay/              # Core gameplay scripts
│   │   │   ├── BasketballPlayer.cs
│   │   │   ├── Ball.cs
│   │   │   ├── Hoop.cs
│   │   │   └── BasketballGameController.cs
│   │   ├── Stats/                 # Basketball-specific calculations
│   │   │   ├── ShotOutcomeCalculator.cs
│   │   │   └── ShotMissCalculator.cs
│   │   └── Flags/                 # Basketball-specific flags (TODO)
│   └── Baseball/                  # Future: Baseball implementation
│
├── HubWorld/                      # Hub world systems
│   ├── Buildings/                 # Arena selector, management buildings (TODO)
│   ├── Navigation/                # Hub navigation (TODO)
│   └── UI/                        # Hub-specific UI (TODO)
│
├── Management/                    # Team management layer
│   ├── TeamManagement/            # Roster & lineup management (TODO)
│   ├── Events/                    # Team building events (TODO)
│   ├── Training/                  # Practice & development (TODO)
│   └── Scouting/                  # Player evaluation (TODO)
│
└── UI/                            # User interface
    ├── Shared/                    # Reusable UI components (TODO)
    ├── Hub/                       # Hub world UI
    │   └── HubMenuController.cs
    ├── Management/                # Management screens (TODO)
    └── Sports/                    # Sport-specific UI (TODO)
```

## Key Design Patterns

### 1. Sport Module Pattern
All sports implement `ISportModule` interface allowing CoreGameManager to load/unload them dynamically.

### 2. Singleton Pattern
`CoreGameManager` persists across all scenes using DontDestroyOnLoad.

### 3. ScriptableObject Pattern
Athletes are data assets (ScriptableObjects) that persist independently of scenes.

### 4. Additive Scene Loading
Hub World stays loaded while sport scenes load additively on top.

## Adding a New Sport

To add a new sport (e.g., Baseball):

1. Create folder structure:
   ```
   Sports/Baseball/
   ├── Gameplay/
   ├── Stats/
   └── Flags/
   ```

2. Create `BaseballModule.cs` implementing `ISportModule`:
   ```csharp
   namespace Sportland.Sports.Baseball
   {
       public class BaseballModule : MonoBehaviour, ISportModule
       {
           public SportType GetSportType() => SportType.Baseball;
           // ... implement other interface methods
       }
   }
   ```

3. Create Baseball.unity scene

4. Add `Baseball` to `SportType` enum in `ISportModule.cs`

5. CoreGameManager will automatically handle loading via ISportModule

## TODO Systems

The following systems have folder placeholders but need implementation:

- **Flag System** (`Core/Flags/`) - Player trait/personality system
- **Player Character** (`Core/Player/`) - User's character & class system
- **Calendar System** (`Core/Calendar/`) - Multi-sport season management
- **Hub Buildings** (`HubWorld/Buildings/`) - Arena selection & management
- **Management Systems** (`Management/`) - Team building, training, scouting
- **Additional Sports** (`Sports/Baseball/`, etc.) - Baseball, Football, etc.

## Current Namespace Usage

### Core
- `Sportland.Core.GameManagement` - Game lifecycle
- `Sportland.Core.Athlete` - Athlete data

### Sports
- `Sportland.Sports.Basketball.Gameplay` - Basketball gameplay
- `Sportland.Sports.Basketball.Stats` - Basketball calculations

### UI
- `Sportland.UI.Hub` - Hub world UI

## Migration Notes

**Changes from previous structure:**
- `Sportland.Core` → Split into `GameManagement`, `Athlete`, etc.
- `Sportland.Basketball` → `Sportland.Sports.Basketball.Gameplay`
- `Sportland.UI` → `Sportland.UI.Hub`

**Unity references:** MonoBehaviours and ScriptableObjects remain intact - Unity tracks by class name, not namespace.

**Compilation:** All using statements updated to reflect new namespaces.

---

Last updated: 2026-01-02

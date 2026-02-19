# Getting Started

> By the end of this guide you'll have CrowSave running in a new Unity project with a working **save / load / transition** loop.

**Requirements:** Unity project with scenes configured in Build Settings, and CrowSave imported (`Persistence/` folder present).

## Documentation
| Doc | Contents |
|---|---|
| [Overview](overview.md) | Architecture, core concepts, modules |
| [Getting Started](getting-started.md) | Bootstrap, SaveConfig, first entity, operations |
| [Persistence](persistence.md) | Capture/Apply authoring, versioning, DirtyOnly, policies |
| [Architecture](architecture.md) | End-to-end pipeline maps for all operations |
---

## 1) Add the bootstrap

CrowSave's runtime services must exist **once** and persist across scene loads. There are two ways to set this up:

**Option A — Bootstrap scene (recommended)**

<img width="479" height="416" alt="image" src="https://github.com/user-attachments/assets/e188dc12-ff86-4e9a-9f11-3ed4815e59ca" />

1. Create a dedicated first scene (e.g. `Bootstrap` or `MainMenu`).
2. Add a GameObject named `CrowSave`.
3. Add `PersistenceBootstrap` and `SaveOrchestrator` components to it.
4. Make it persist: use `DdolRoot` if your project has it, otherwise call `DontDestroyOnLoad` in your bootstrap code.

**Option B — Auto-spawn (dev convenience)**

Spawn the bootstrap prefab in every scene if it isn't already present. Useful during development when you don't want a dedicated bootstrap scene yet.

---

## 2) Create a `SaveConfig`

1. In the Project window: **Create → CrowSave → Save Config**
2. Select your `SaveOrchestrator` component and assign the new asset.

<img width="501" height="763" alt="image" src="https://github.com/user-attachments/assets/273a45b3-271f-4980-be21-fbc074485202" />


| Setting | Recommended value |
|---|---|
| Scene Identity Mode | `SceneGuid` |
| Capture Mode | `DirtyOnly` |
| Freeze Time During Ops | Enabled |

---

## 3) Run the Scene GUID Manager

> Skip this step if you chose a different Scene Identity Mode.

Open **Tools → CrowSave → Scene GUID Manager**, then:

<img width="1371" height="280" alt="image" src="https://github.com/user-attachments/assets/6135ec9b-466b-459d-8105-d8111c42f837" />


1. Select your scenes and press **Add to Build**.
2. Press **Select Problems** → **Add/Fix Selected** to add a `SceneGuid` component to each scene.
3. Press **Generate Registry** to produce the Scene GUID Registry.

This keeps scene identity stable if you ever rename or move a scene file.

---

## 4) Make your first persistent entity

Every persistent entity needs two things on its GameObject:

- A **`PersistentId`** component — holds the stable identity and scope.
- A component implementing persistence — use `PersistentMonoBehaviour` to get registration and dirty tracking for free.

**Example — a `Health` component:**

```csharp
using CrowSave.Persistence.Core;
using CrowSave.Persistence.Runtime;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PersistentId))]
public sealed class Health : PersistentMonoBehaviour
{
    [SerializeField] private int value = 100;

    public void Damage(int amount)
    {
        if (amount <= 0) return;
        value = Mathf.Max(0, value - amount);
        MarkDirty();
    }

    public override void Capture(IStateWriter w)
    {
        w.WriteVersion(1);
        w.WriteInt(value);
    }

    public override void Apply(IStateReader r)
    {
        r.ReadVersion(min: 1, max: 1);
        value = r.ReadInt();
    }
}
```

**Setup steps:**

1. Create a GameObject (e.g. `Player`) and add `PersistentId`.
2. Generate an ID using the editor button on the component.
3. Enable **Global scope** if this entity should survive scene transitions (player, inventory, etc.).
4. Add your `PersistentMonoBehaviour` script.

> **`MarkDirty()` is required in `DirtyOnly` mode.** Call it whenever persisted state changes. Never call it every frame.

---

## 5) Trigger operations

```csharp
// Save to a slot
SaveOrchestrator.Instance.SaveSlot(1);

// Load a slot (travel behavior follows SaveConfig.loadScenePolicy)
SaveOrchestrator.Instance.LoadSlot(1);

// Load with explicit travel control
SaveOrchestrator.Instance.LoadSlot(1, travelToSavedScene: true);
SaveOrchestrator.Instance.LoadSlot(1, travelToSavedScene: false);

// Checkpoint (ring size must be > 0 in SaveConfig)
SaveOrchestrator.Instance.Checkpoint();
SaveOrchestrator.Instance.Checkpoint("boss_defeated");

// Scene transition (capture → load → barrier → apply)
SaveOrchestrator.Instance.TransitionToScene("Level02");
```

> Only one operation runs at a time. Calls are refused if `SaveOrchestrator.IsBusy == true`. Operations may freeze time scale during execution (`SaveConfig.freezeTimeScaleDuringOps`).

---

## Troubleshooting checklist

If persistence appears to do nothing, work through this list:

- [ ] `PersistenceBootstrap` and `SaveOrchestrator` exist exactly once in the scene
- [ ] `SaveConfig` is assigned on the `SaveOrchestrator`
- [ ] The GameObject has a `PersistentId` with a valid, generated ID
- [ ] The entity is **enabled and alive** when the barrier runs
- [ ] *(DirtyOnly)* `MarkDirty()` was called after state changed (if using DirtyOnly)
- [ ] *(SceneGuid)* Scene GUID Manager has been run and the registry generated (if using Scene GUID)

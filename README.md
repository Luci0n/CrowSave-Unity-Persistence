# CrowSave

A Unity persistence framework for **predictable save/load and deterministic scene travel**.

CrowSave is built around explicit capture/apply, scoped identity, and stable orchestration — designed to eliminate the class of bugs caused by implicit serialization, unscoped IDs, and non-deterministic apply order.

---

## Features

- **Explicit Capture/Apply** — entities own their blob format; no reflection, no magic
- **Scoped identity** — state keyed by `(ScopeKey, EntityId)` prevents cross-scene and cross-instance collisions
- **Two storage layers** — RAM layer for in-session travel; disk layer for saves, checkpoints, and load-game
- **Deterministic orchestration** — stable apply order with a barrier step before every Apply phase
- **Tombstones** — destroyed entities stay destroyed across loads and transitions
- **DirtyOnly capture** — skip unchanged entities for efficient saves
- **Checkpoint ring** — rolling in-memory snapshots for rewind flows
- **Scene GUID identity** — stable scope keys that survive scene renames and moves

---

## Installation

**Option A — Unity Package (.unitypackage)**

1. Download the latest `.unitypackage` from the releases page.
2. Double click on the file and import all assets.

**Option B — Unity Package Manager (Git URL)**

1. Open **Window → Package Manager**.
2. Click **+** → **Add package from git URL**.
3. Enter - https://github.com/Luci0n/CrowSave-Unity-Persistence.git - and confirm.

---

## Documentation

| Doc | Contents |
|---|---|
| [Overview](docs/overview.md) | Architecture, core concepts, modules |
| [Getting Started](docs/getting-started.md) | Bootstrap, SaveConfig, first entity, operations |
| [Persistence](docs/persistence.md) | Capture/Apply authoring, versioning, DirtyOnly, policies |
| [Architecture](docs/architecture.md) | End-to-end pipeline maps for all operations |

---

## Quick example

```csharp
[RequireComponent(typeof(PersistentId))]
public sealed class Health : PersistentMonoBehaviour
{
    [SerializeField] private int value = 100;

    public void Damage(int amount)
    {
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

```csharp
SaveOrchestrator.Instance.SaveSlot(1);
SaveOrchestrator.Instance.LoadSlot(1);
SaveOrchestrator.Instance.TransitionToScene("Level02");
SaveOrchestrator.Instance.Checkpoint("boss_defeated");
```

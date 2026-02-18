# Persistence

This document covers everything you need to **author persistent entities**: writing `Capture()`/`Apply()`, versioning blobs, using `DirtyOnly`, and configuring policies and reset behavior.

## Documentation
| Doc | Contents |
|---|---|
| [Overview](overview.md) | Architecture, core concepts, modules |
| [Getting Started](getting-started.md) | Bootstrap, SaveConfig, first entity, operations |
| [Persistence](persistence.md) | Capture/Apply authoring, versioning, DirtyOnly, policies |
| [Architecture](architecture.md) | End-to-end pipeline maps for all operations |
---

## Implementing a persistent component

The recommended path is to derive from `PersistentMonoBehaviour`. This gives you registration, unregistration, and `MarkDirty()` wiring for free.

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

If you implement the persistence interfaces directly instead of deriving from `PersistentMonoBehaviour`, you are responsible for correct registration/unregistration and dirty tracking yourself.

---

## Capture and Apply

`Capture()` writes state into a binary **blob**. `Apply()` reads it back.

Two rules are non-negotiable:

**Rule 1 — Version first.** The first thing written and read must always be the version.

**Rule 2 — Order must match exactly.** If you write `Int` then `Bool`, you must read `Int` then `Bool`. A mismatch produces silent data corruption or exceptions.

```csharp
public override void Capture(IStateWriter w)
{
    w.WriteVersion(1);
    w.WriteInt(hp);
    w.WriteBool(isAlive);
}

public override void Apply(IStateReader r)
{
    r.ReadVersion(min: 1, max: 1);
    hp = r.ReadInt();
    isAlive = r.ReadBool();
}
```

### What to persist

Persist **game state**: inventory counts, flags, door open/closed, current HP.

Do not persist:

- Derived or computed fields — recalculate them after Apply.
- Temporary runtime state — timers, cached references, coroutine handles.
- Unity object references — unless you have a stable ID mapping to resolve them.

---

## Versioning

Blobs must be versioned so you can evolve the format without breaking existing saves.

```csharp
public override void Capture(IStateWriter w)
{
    w.WriteVersion(2);
    w.WriteInt(hp);
    w.WriteInt(mana); // added in v2
}

public override void Apply(IStateReader r)
{
    int v = r.ReadVersion(min: 1, max: 2);
    hp = r.ReadInt();

    if (v >= 2) mana = r.ReadInt();
    else mana = 100; // default for saves that predate v2
}
```

Guidelines:

- **Increment the version** whenever you add, remove, or reorder fields.
- **Branch in `Apply()`** on the version to handle old saves gracefully.
- **Always provide a sensible default** for fields that didn't exist in older versions.
- Never change what an existing version wrote — old saves still exist.

---

## DirtyOnly capture

When `SaveConfig` uses `DirtyOnly` capture mode, CrowSave skips an entity's `Capture()` entirely unless the entity has been marked dirty since the last capture.

Call `MarkDirty()` whenever persisted state changes:

```csharp
public void SetLevel(int newLevel)
{
    level = newLevel;
    MarkDirty();
}
```

**What not to do:**

```csharp
// Bad — marks dirty every frame, defeats the purpose of DirtyOnly
private void Update()
{
    MarkDirty();
}
```

If you forget `MarkDirty()`, saves and transitions will silently capture stale state. If you call it too eagerly, you lose the performance benefit of `DirtyOnly`.

> In `CaptureAll` mode, `MarkDirty()` calls are harmless but unnecessary. Writing them anyway is good practice — it makes switching modes safe.

---

## PersistencePolicy

`PersistencePolicy` controls **where** an entity's state is stored.

```csharp
namespace CrowSave.Persistence.Core
{
    public enum PersistencePolicy
    {
        Never        = 0,
        SessionOnly  = 1,
        SaveGame     = 2,
        CheckpointOnly = 3,
        Respawnable  = 4
    }
}
```

| Value | Behavior |
|---|---|
| `Never` | Not captured or stored. Entity participates in runtime only. |
| `SessionOnly` | Captured to RAM for in-session travel and backtracking. Lost on quit — never written to disk. |
| `SaveGame` | Included in manual slot saves and loads. |
| `CheckpointOnly` | Included only in checkpoint snapshots. |
| `Respawnable` | Reserved for explicit reset/respawn flows. |

**Choosing a policy:**

- Most scene objects → `SaveGame`
- Player, inventory, global progression → `SaveGame` with global scope
- Cosmetic or easily re-derived state that doesn't need to survive a quit → `SessionOnly`
- Objects that should never outlive a reset → `Respawnable`

---

## ResetPolicy

`ResetPolicy` controls what happens during a **disk load** when an entity's blob is missing from the save, or when you want to force defaults.

```csharp
namespace CrowSave.Persistence.Core
{
    public enum ResetPolicy
    {
        Keep                    = 0,
        ResetOnMissingOnDiskLoad = 1,
        ResetAlwaysOnDiskLoad   = 2
    }
}
```

| Value | Behavior |
|---|---|
| `Keep` | Do nothing if the blob is absent. The entity stays in whatever state it was initialized to. |
| `ResetOnMissingOnDiskLoad` | Call `ResetState()` only if the save contains no blob for this entity. |
| `ResetAlwaysOnDiskLoad` | Always call `ResetState()` before applying, regardless of whether a blob exists. |

To support reset, implement `IResettablePersistent.ResetState(ApplyReason reason)`. This method should restore the component to its inspector defaults or a known baseline state — not to the last saved state.

```csharp
public void ResetState(ApplyReason reason)
{
    hp = 100;   // inspector default
    mana = 100;
}
```

---

## ApplyReason

`ApplyReason` is passed into `Apply()` and tells the entity **why** the restore is happening. Use it to branch behavior where the right thing to do differs by context.

```csharp
namespace CrowSave.Persistence.Core
{
    public enum ApplyReason
    {
        Transition  = 0,
        DiskLoad    = 1,
        Checkpoint  = 2,
        Respawn     = 3
    }
}
```

| Value | When |
|---|---|
| `Transition` | Scene travel within a session (RAM layer). |
| `DiskLoad` | Loading a saved slot from disk. |
| `Checkpoint` | Restoring a checkpoint snapshot. |
| `Respawn` | Explicit respawn flow. |

**Example — only restore transform position on a full disk load:**

```csharp
public override void Apply(IStateReader r)
{
    r.ReadVersion(min: 1, max: 1);
    var pos = r.ReadVector3();

    if (r.Reason == ApplyReason.DiskLoad)
        transform.position = pos;
    // On Transition, position is handled by the scene itself
}
```

---

## Quick reference

| Question | Answer |
|---|---|
| What must come first in every blob? | `WriteVersion` / `ReadVersion` |
| Does write order need to match read order? | Yes, exactly |
| When do I call `MarkDirty()`? | Whenever persisted state changes |
| What does `SessionOnly` mean? | RAM only — lost when the application quits |
| When is `ResetState()` called? | Only when `ResetPolicy` is not `Keep` and conditions are met during disk load |
| How do I handle a new field in an old save? | Branch on version in `Apply()` and provide a default |

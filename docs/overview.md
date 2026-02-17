# CrowSave — Overview

> **CrowSave** is a Unity persistence framework focused on **predictable save/load** and **deterministic scene travel**.

At its core, CrowSave is the **Persistence** system: capture/apply, orchestration, disk saves, checkpoints, and scene transitions. On top of that core, CrowSave can ship with **optional modules** that reuse the same scopes and state pipeline.

---

## What's in CrowSave

### Core: Persistence

The Persistence system is the reason CrowSave exists. It is designed to avoid three common failure modes:

| Failure mode | How CrowSave addresses it |
|---|---|
| Implicit serialization that breaks after refactors | Explicit `Capture()` / `Apply()` — entities own their blob format |
| Global unscoped IDs that collide across scenes or instances | All state is addressed by **(ScopeKey, EntityId)** |
| Non-deterministic apply order causing intermittent bugs | Stable ordering + a **barrier** before any Apply call |

**What it provides:**

- **Deterministic capture/apply** — entities write a binary **blob** in `Capture()` and restore it in `Apply()`. Write/read order must match, and blobs should be versioned by the entity.
- **Scoped identity** — state is addressed by **(ScopeKey, EntityId)** so different scenes or prefab instances never collide.
- **Stable orchestration** — operations run in a predictable sequence: capture → package → travel/wait → barrier → apply.
- **Two storage layers** — a **RAM layer** for fast in-session state (transitions, backtracking) and a **disk layer** for manual saves, checkpoints, and load-game.
- **Tombstones** — records of destroyed entities that prevent "resurrected pickups" after load or transition.

---

### Optional module: Flags

Flags is a lightweight, scoped **key/value state layer** designed for world logic.

**Ideal for:**
- Triggers, switches, doors, and gates
- Quest or progression bits
- "Fire once" latches that survive scene reloads and disk loads

**Key ideas:**
- Flags are stored in a **FlagsStore** and accessed through a **FlagsService**.
- Writes call `MarkDirty()` via `FlagsService`, so **DirtyOnly** capture works naturally.
- A **FlagsHub** can reapply flag state to the scene after store rebuilds and scene loads.
- Targets are referenced by stable keys — prefer **PersistentId**, then **FlagsLocalId**, then name as a fallback.

---

### Optional authoring layer: Reflect

Reflect is an authoring convenience layer built on top of the same persistence contracts. It reduces boilerplate for simple components by letting you mark fields with `[Persist]`, which auto-writes and reads those fields into a blob.

> **Reflect is optional.** For maximum explicitness and no magic, use plain `PersistentMonoBehaviour` and implement `Capture()`/`Apply()` manually. This is the recommended approach for any component where read/write order, versioning, or branching logic matters.

---

## Core concepts

### Blob

A **blob** is the binary payload an entity produces in `Capture()` and consumes in `Apply()`.

- CrowSave does not interpret blob contents — the entity owns the format entirely.
- Blobs should be **versioned** by the entity (write version first, read with a min/max range).
- Blobs are opaque to the orchestrator and stored/retrieved as raw bytes.

```csharp
public override void Capture(IStateWriter w)
{
    w.WriteVersion(2);
    w.WriteInt(hp);
    w.WriteInt(mana); // added in v2
}

public override void Apply(IStateReader r)
{
    int v = r.ReadVersion(1, 2);
    hp = r.ReadInt();
    if (v >= 2) mana = r.ReadInt();
    else mana = 0;
}
```

---

### Identity: `(ScopeKey, EntityId)`

CrowSave identifies a persisted thing using two parts:

| Part | Source | Notes |
|---|---|---|
| **EntityId** | `PersistentId` component | Generated once, never changed |
| **ScopeKey** | Scene identity mode or global/custom override | Computed at runtime from `SaveConfig` |

This pair prevents collisions. Two copies of the same prefab in different scenes can safely share the same `EntityId` because their `ScopeKey` will differ.

---

### Scopes

A **scope** is a namespace boundary for state. CrowSave partitions all state into independent buckets by scope, so operations on one scope never touch another.

| Scope type | Typical use |
|---|---|
| **Scene scope** | Most scene-local objects. Key derived from scene identity (name, path, or GUID). |
| **Global scope** | Player stats, inventory, progression, and cross-scene managers. |
| **Custom scope** | Named instances like `dungeon_1`, `boss_room`, or procedural session keys. |

---

### Scene identity and travel

CrowSave separates two concerns:

- **Scene identity** determines the scope key for a scene's objects. It can be based on scene name, asset path, or asset GUID. **GUID mode is recommended** — it is stable across renames and moves, and requires a `SceneGuid` component on each scene managed via **Tools → CrowSave → Scene GUID Manager**.
- **Scene travel** is how the pipeline actually loads a scene, using a travel key (typically scene name or build index) stored in the save header alongside the identity key.

---

### Deterministic apply order

CrowSave eliminates timing bugs by applying entities in a stable order (by priority, then EntityId) and using a **barrier** step that waits until all entities have registered before any Apply call is made. During disk loads, **global scope is applied before scene scope**.

---

### Tombstones

Tombstones record *"this entity was destroyed and must not reappear after load or transition."* They are stored in both the RAM layer and disk saves, and are the mechanism that prevents classic bugs like collectibles respawning when a player returns to a scene.

---

### Policies

Two enums control per-entity storage and reset behavior:

#### `PersistencePolicy` — where state is stored

| Value | Behavior |
|---|---|
| `Never` | No capture or storage |
| `SessionOnly` | RAM only — lost on quit |
| `SaveGame` | Included in manual slot saves and loads |
| `CheckpointOnly` | Included only in checkpoint snapshots |
| `Respawnable` | Reserved for explicit reset/respawn flows |

#### `ResetPolicy` — what happens on disk load when a blob is missing

| Value | Behavior |
|---|---|
| `Keep` | Do not auto-reset |
| `ResetOnMissingOnDiskLoad` | Reset only if no blob exists in the save |
| `ResetAlwaysOnDiskLoad` | Always reset to defaults, ignoring any saved blob |

#### `ApplyReason` — why Apply is happening

`ApplyReason` is passed into `Apply()` so components can branch behavior — for example, only restoring a transform on `DiskLoad`, not on `Transition`.

| Value | When |
|---|---|
| `Transition` | Scene travel within a session |
| `DiskLoad` | Loading a saved slot |
| `Checkpoint` | Restoring a checkpoint snapshot |
| `Respawn` | Explicit respawn flow |

---

## Folder layout

```
Persistence/
├── Core/                # Public contracts and primitives: policies, reasons, reset rules, StateIO.
├── Runtime/             # Runtime services: registry, world state, capture/apply engine, bootstrap, IDs.
├── Save/                # Save package model, serializer, disk backend, orchestrator, pipeline, loading UI.
└── Persistence.Editor/  # Editor tooling: Scene GUID manager, validators, inspectors, utilities.
```
# Runtime Behavior Recipes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a curated runtime behavior layer so users can command objects to spin, orbit, throw, follow a hand, attach to a hand, and stop behaviors through voice or typed UI without exposing arbitrary Unity APIs.

**Architecture:** Preserve the existing `VoiceIntentCommand -> WorldActionDispatcher` pipeline and add a behavior lane behind it. `WorldActionDispatcher` routes behavior intents to `BehaviorCommandController`, which uses `SceneEntityResolver`, a small `BehaviorPolicy`, and `RuntimeBehaviorHost` components to execute safe built-in behaviors.

**Tech Stack:** Unity 6.2 C#, XR Interaction context already captured by `SpatialContextProvider`, current `SpeechIntent` runtime scripts, batch-mode editor tests via `SpeechIntentBatchTests.RunDistanceAndMovementTests`.

---

## File Structure

- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`
  - Add `AttachBehavior` and `StopBehavior` enum values.
  - Add behavior fields to `VoiceIntentCommand`.
- Modify: `Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs`
  - Parse local behavior commands for tests and offline typed input.
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs`
  - Add developer instructions for behavior commands.
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorCommandResult.cs`
  - Small success/failure result model for behavior execution.
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/MissingCapabilityReport.cs`
  - Structured unsupported-behavior report.
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorPolicy.cs`
  - Protected-target checks and parameter clamps.
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/RuntimeBehaviorHost.cs`
  - Per-target component that ticks spin, orbit, follow-hand, and attach-to-hand behaviors.
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorCommandController.cs`
  - Resolves targets and attaches/stops runtime behaviors.
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
  - Add a `BehaviorCommandController` reference, auto-wire it, and route behavior intents.
- Modify: `Assets/App/Editor/SpeechIntentSceneSetup.cs`
  - Ensure scene setup adds/wires `BehaviorCommandController`.
- Modify: `Assets/App/Editor/SpeechIntentBatchTests.cs`
  - Add behavior parser/controller/host tests to the existing batch test entry point.

---

### Task 1: Add Behavior Intent Schema

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs`
- Test: `Assets/App/Editor/SpeechIntentBatchTests.cs`

- [ ] **Step 1: Add failing parser assertions**

In `Assets/App/Editor/SpeechIntentBatchTests.cs`, add `TestBehaviorCommandsParseLocally();` after `TestCachedObjectChoiceRepliesParseLocally();` in `RunDistanceAndMovementTests`.

Add this method before the assertion helpers:

```csharp
static void TestBehaviorCommandsParseLocally()
{
    VoiceIntentCommand spin = LocalTypedIntentParser.Parse("make cube spin");
    AssertEqual(VoiceIntentType.AttachBehavior, spin.intent, "Expected AttachBehavior for spin.");
    AssertEqual("spin", spin.behavior_name, "Expected spin behavior.");
    AssertEqual("cube", spin.target_name, "Expected cube target.");
    AssertEqual(TargetReferenceMode.NamedObject, spin.target_reference, "Expected named target for cube spin.");
    AssertTrue(spin.should_execute, "Expected spin command to execute.");

    VoiceIntentCommand stopAll = LocalTypedIntentParser.Parse("stop all behaviors");
    AssertEqual(VoiceIntentType.StopBehavior, stopAll.intent, "Expected StopBehavior for stop all behaviors.");
    AssertTrue(stopAll.behavior_stop_all, "Expected stop-all flag.");
    AssertTrue(stopAll.should_execute, "Expected stop-all command to execute.");
}
```

- [ ] **Step 2: Run test to verify schema compile failure**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-speechintent-behavior-task1-fail.log
```

Expected: compile failure mentioning missing `AttachBehavior`, `StopBehavior`, or behavior fields.

- [ ] **Step 3: Add enum values and fields**

In `VoiceIntentType`, append:

```csharp
AttachBehavior = 41,  // attach a curated runtime behavior such as spin, orbit, throw, or follow hand
StopBehavior = 42,  // stop one named behavior, behaviors on a target, or all runtime behaviors
```

In `VoiceIntentCommand`, add after the runtime proxy section:

```csharp
[Header("Runtime Behaviors")]
[Tooltip("Curated behavior name such as spin, orbit, throw, follow_hand, or attach_to_hand.")]
public string behavior_name = "";
[Tooltip("start, stop, toggle, or a behavior-specific action.")]
public string behavior_action = "";
[Tooltip("Optional secondary target for behaviors such as orbit or throw.")]
public string behavior_secondary_target_name = "";
[Tooltip("Speed parameter for behavior commands. Units depend on behavior: degrees/second for spin/orbit, meters/second for throw.")]
public float behavior_speed = 0f;
[Tooltip("Radius in meters for orbit-style behaviors.")]
public float behavior_radius = 0f;
[Tooltip("Axis hint such as x, y, z, up, right, forward, local_up, or world_up.")]
public string behavior_axis = "";
[Tooltip("When true, stop every runtime behavior in the scene.")]
public bool behavior_stop_all = false;
```

- [ ] **Step 4: Run test to verify parser fails semantically**

Run the same Unity command.

Expected: test fails with `Expected AttachBehavior for spin` because parser is not implemented yet.

- [ ] **Step 5: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/VoiceIntentSchemas.cs Assets/App/Editor/SpeechIntentBatchTests.cs
git commit -m "Add behavior intent schema"
```

---

### Task 2: Parse Local Behavior Commands

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs`
- Test: `Assets/App/Editor/SpeechIntentBatchTests.cs`

- [ ] **Step 1: Add richer failing parser assertions**

Extend `TestBehaviorCommandsParseLocally` with:

```csharp
VoiceIntentCommand pointedSpin = LocalTypedIntentParser.Parse("make this spin");
AssertEqual(VoiceIntentType.AttachBehavior, pointedSpin.intent, "Expected AttachBehavior for this spin.");
AssertEqual("spin", pointedSpin.behavior_name, "Expected spin behavior for this spin.");
AssertEqual(TargetReferenceMode.PointedObject, pointedSpin.target_reference, "Expected pointed target for this spin.");

VoiceIntentCommand orbit = LocalTypedIntentParser.Parse("make ball orbit table");
AssertEqual(VoiceIntentType.AttachBehavior, orbit.intent, "Expected AttachBehavior for orbit.");
AssertEqual("orbit", orbit.behavior_name, "Expected orbit behavior.");
AssertEqual("ball", orbit.target_name, "Expected ball subject.");
AssertEqual("table", orbit.behavior_secondary_target_name, "Expected table orbit center.");

VoiceIntentCommand follow = LocalTypedIntentParser.Parse("make this follow my left hand");
AssertEqual(VoiceIntentType.AttachBehavior, follow.intent, "Expected AttachBehavior for follow hand.");
AssertEqual("follow_hand", follow.behavior_name, "Expected follow_hand behavior.");
AssertEqual(TargetReferenceMode.PointedObject, follow.target_reference, "Expected pointed object for follow hand.");
AssertEqual(HandSelection.Left, follow.target_hand, "Expected left hand selection.");

VoiceIntentCommand give = LocalTypedIntentParser.Parse("give me the blaster");
AssertEqual(VoiceIntentType.AttachBehavior, give.intent, "Expected AttachBehavior for give me.");
AssertEqual("attach_to_hand", give.behavior_name, "Expected attach_to_hand behavior.");
AssertEqual("blaster", give.target_name, "Expected blaster target.");
AssertEqual(HandSelection.Either, give.target_hand, "Expected either hand for give me.");
```

- [ ] **Step 2: Run test to verify failure**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-speechintent-behavior-task2-fail.log
```

Expected: parser assertions fail.

- [ ] **Step 3: Add parser entry point**

In `LocalTypedIntentParser.Parse`, call behavior parsing before transform parsing:

```csharp
if (TryParseBehaviorCommand(original, lower, command))
    return command;
```

Place it after cached-object/generation-control parsing and before broad material/relative transform parsing.

- [ ] **Step 4: Add parser helpers**

Add these methods near the other `TryParse...` helpers:

```csharp
static bool TryParseBehaviorCommand(string original, string lower, VoiceIntentCommand command)
{
    if (TryParseStopBehavior(original, lower, command))
        return true;
    if (TryParseFollowHandBehavior(original, lower, command))
        return true;
    if (TryParseAttachToHandBehavior(original, lower, command))
        return true;
    if (TryParseOrbitBehavior(original, lower, command))
        return true;
    if (TryParseThrowBehavior(original, lower, command))
        return true;
    if (TryParseSpinBehavior(original, lower, command))
        return true;
    return false;
}

static bool TryParseStopBehavior(string original, string lower, VoiceIntentCommand command)
{
    if (!lower.Contains("stop"))
        return false;

    bool mentionsBehavior = lower.Contains("behavior") || lower.Contains("spin") || lower.Contains("orbit") || lower.Contains("follow");
    if (!mentionsBehavior)
        return false;

    command.intent = VoiceIntentType.StopBehavior;
    command.should_execute = true;
    command.transcript = original;
    command.behavior_action = "stop";
    command.behavior_stop_all = lower.Contains("all");
    if (lower.Contains("spin")) command.behavior_name = "spin";
    else if (lower.Contains("orbit")) command.behavior_name = "orbit";
    else if (lower.Contains("follow")) command.behavior_name = "follow_hand";
    command.target_reference = command.behavior_stop_all ? TargetReferenceMode.All : TargetReferenceMode.LastCreatedOrInteracted;
    command.spoken_response = command.behavior_stop_all ? "Stopping all behaviors." : "Stopping behavior.";
    return true;
}

static bool TryParseSpinBehavior(string original, string lower, VoiceIntentCommand command)
{
    if (!lower.Contains("spin") && !lower.Contains("rotate continuously"))
        return false;

    string target = ExtractBehaviorTarget(original, lower, "spin");
    command.intent = VoiceIntentType.AttachBehavior;
    command.should_execute = true;
    command.transcript = original;
    command.behavior_name = "spin";
    command.behavior_action = "start";
    ApplyBehaviorTarget(command, target, lower);
    command.spoken_response = "Starting spin behavior.";
    return true;
}

static bool TryParseOrbitBehavior(string original, string lower, VoiceIntentCommand command)
{
    int orbitIndex = lower.IndexOf(" orbit ", StringComparison.Ordinal);
    if (orbitIndex < 0 && !lower.Contains("go around") && !lower.Contains("circle"))
        return false;

    string subject = "";
    string center = "";
    if (orbitIndex >= 0)
    {
        subject = CleanBehaviorPhrase(original.Substring(0, orbitIndex));
        center = CleanBehaviorPhrase(original.Substring(orbitIndex + " orbit ".Length));
    }

    command.intent = VoiceIntentType.AttachBehavior;
    command.should_execute = true;
    command.transcript = original;
    command.behavior_name = "orbit";
    command.behavior_action = "start";
    ApplyBehaviorTarget(command, subject, lower);
    command.behavior_secondary_target_name = center;
    command.spoken_response = "Starting orbit behavior.";
    return true;
}

static bool TryParseThrowBehavior(string original, string lower, VoiceIntentCommand command)
{
    if (!lower.Contains("throw") && !lower.Contains("toss") && !lower.Contains("fling") && !lower.Contains("launch"))
        return false;

    command.intent = VoiceIntentType.AttachBehavior;
    command.should_execute = true;
    command.transcript = original;
    command.behavior_name = "throw";
    command.behavior_action = "start";
    command.target_reference = lower.Contains("this") || lower.Contains("that") ? TargetReferenceMode.PointedObject : TargetReferenceMode.LastCreatedOrInteracted;
    command.behavior_secondary_target_name = ExtractAfterAny(original, lower, " at ", " toward ", " to ");
    command.spoken_response = "Throwing object.";
    return true;
}

static bool TryParseFollowHandBehavior(string original, string lower, VoiceIntentCommand command)
{
    if (!lower.Contains("follow") || !lower.Contains("hand"))
        return false;

    command.intent = VoiceIntentType.AttachBehavior;
    command.should_execute = true;
    command.transcript = original;
    command.behavior_name = "follow_hand";
    command.behavior_action = "start";
    command.target_hand = lower.Contains("left") ? HandSelection.Left : lower.Contains("right") ? HandSelection.Right : HandSelection.Either;
    ApplyBehaviorTarget(command, ExtractBeforeAny(original, lower, " follow "), lower);
    command.spoken_response = "Starting hand follow.";
    return true;
}

static bool TryParseAttachToHandBehavior(string original, string lower, VoiceIntentCommand command)
{
    if (!lower.StartsWith("give me", StringComparison.Ordinal) &&
        !lower.Contains(" in my hand") &&
        !lower.Contains(" in my left hand") &&
        !lower.Contains(" in my right hand") &&
        !lower.Contains("attach") &&
        !lower.Contains("hold"))
    {
        return false;
    }

    command.intent = VoiceIntentType.AttachBehavior;
    command.should_execute = true;
    command.transcript = original;
    command.behavior_name = "attach_to_hand";
    command.behavior_action = "start";
    command.target_hand = lower.Contains("left") ? HandSelection.Left : lower.Contains("right") ? HandSelection.Right : HandSelection.Either;
    string target = lower.StartsWith("give me", StringComparison.Ordinal)
        ? CleanBehaviorPhrase(original.Substring(Math.Min(original.Length, "give me".Length)))
        : ExtractBeforeAny(original, lower, " in my ");
    ApplyBehaviorTarget(command, target, lower);
    command.spoken_response = "Attaching object to hand.";
    return true;
}

static void ApplyBehaviorTarget(VoiceIntentCommand command, string target, string lower)
{
    target = CleanBehaviorPhrase(target);
    if (lower.Contains("this") || lower.Contains("that"))
    {
        command.target_reference = TargetReferenceMode.PointedObject;
        if (!string.Equals(target, "this", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, "that", StringComparison.OrdinalIgnoreCase))
            command.target_name = target;
        return;
    }

    if (string.IsNullOrWhiteSpace(target))
    {
        command.target_reference = TargetReferenceMode.LastCreatedOrInteracted;
        return;
    }

    command.target_reference = TargetReferenceMode.NamedObject;
    command.target_name = target;
}

static string ExtractBehaviorTarget(string original, string lower, string behaviorToken)
{
    int index = lower.IndexOf(behaviorToken, StringComparison.Ordinal);
    if (index <= 0)
        return "";
    return CleanBehaviorPhrase(original.Substring(0, index));
}

static string ExtractBeforeAny(string original, string lower, params string[] separators)
{
    foreach (string separator in separators)
    {
        int index = lower.IndexOf(separator, StringComparison.Ordinal);
        if (index > 0)
            return CleanBehaviorPhrase(original.Substring(0, index));
    }
    return "";
}

static string ExtractAfterAny(string original, string lower, params string[] separators)
{
    foreach (string separator in separators)
    {
        int index = lower.IndexOf(separator, StringComparison.Ordinal);
        if (index >= 0)
            return CleanBehaviorPhrase(original.Substring(index + separator.Length));
    }
    return "";
}

static string CleanBehaviorPhrase(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return "";

    string cleaned = value.Trim();
    string[] prefixes = { "make ", "the ", "a ", "an ", "object ", "it ", "this ", "that ", "me " };
    bool changed;
    do
    {
        changed = false;
        foreach (string prefix in prefixes)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(prefix.Length).Trim();
                changed = true;
            }
        }
    } while (changed);

    string[] suffixes = { ".", "?", "!", "please" };
    foreach (string suffix in suffixes)
    {
        if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(0, cleaned.Length - suffix.Length).Trim();
    }

    return cleaned;
}
```

- [ ] **Step 5: Run parser tests**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-speechintent-behavior-task2-pass.log
```

Expected: `[SpeechIntentBatchTests] All distance and movement tests passed.`

- [ ] **Step 6: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/LocalTypedIntentParser.cs Assets/App/Editor/SpeechIntentBatchTests.cs
git commit -m "Parse runtime behavior commands"
```

---

### Task 3: Add RuntimeBehaviorHost

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorCommandResult.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/MissingCapabilityReport.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/RuntimeBehaviorHost.cs`
- Test: `Assets/App/Editor/SpeechIntentBatchTests.cs`

- [ ] **Step 1: Add failing host test**

Add `TestRuntimeBehaviorHostSpinTicks();` after `TestBehaviorCommandsParseLocally();`.

Add this method before assertion helpers:

```csharp
static void TestRuntimeBehaviorHostSpinTicks()
{
    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
    try
    {
        RuntimeBehaviorHost host = cube.AddComponent<RuntimeBehaviorHost>();
        bool started = host.StartSpin("spin-test", Vector3.up, 90f, Space.Self);
        AssertTrue(started, "Expected spin behavior to start.");

        Quaternion before = cube.transform.rotation;
        host.TickForTests(1f);
        Quaternion after = cube.transform.rotation;

        AssertTrue(Quaternion.Angle(before, after) > 80f, "Expected one second of spin to rotate the cube.");
        AssertTrue(host.StopBehavior("spin"), "Expected spin behavior to stop.");
        AssertTrue(!host.HasBehavior("spin"), "Expected spin behavior to be removed.");
    }
    finally
    {
        UnityEngine.Object.DestroyImmediate(cube);
    }
}
```

- [ ] **Step 2: Run test to verify compile failure**

Run the Unity batch command from Task 2.

Expected: compile failure because `RuntimeBehaviorHost` does not exist.

- [ ] **Step 3: Create behavior result model**

Create `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorCommandResult.cs`:

```csharp
namespace SpeechIntent.Behaviors
{
    public sealed class BehaviorCommandResult
    {
        public bool success;
        public string message = "";
        public MissingCapabilityReport missingCapability;

        public static BehaviorCommandResult Success(string message)
        {
            return new BehaviorCommandResult { success = true, message = message ?? "" };
        }

        public static BehaviorCommandResult Failure(string message)
        {
            return new BehaviorCommandResult { success = false, message = message ?? "" };
        }

        public static BehaviorCommandResult Missing(MissingCapabilityReport report)
        {
            return new BehaviorCommandResult
            {
                success = false,
                message = report != null ? report.ToUserMessage() : "That behavior is not available.",
                missingCapability = report
            };
        }
    }
}
```

- [ ] **Step 4: Create missing capability report**

Create `Assets/App/Command/SpeechIntent/Runtime/Behaviors/MissingCapabilityReport.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    [System.Serializable]
    public sealed class MissingCapabilityReport
    {
        public string status = "missing_capability";
        public string user_request = "";
        public string requested_behavior = "";
        public List<string> available_behaviors = new List<string>();
        public List<string> needed_capabilities = new List<string>();
        public string possible_approximation = "";

        public string ToUserMessage()
        {
            if (available_behaviors == null || available_behaviors.Count == 0)
                return $"I do not have a {requested_behavior} behavior yet.";
            return $"I do not have a {requested_behavior} behavior yet. I can currently {string.Join(", ", available_behaviors)}.";
        }

        public void Log()
        {
            Debug.LogWarning("[MissingCapabilityReport] " + ToJson());
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            AppendJson(sb, "status", status); sb.Append(",");
            AppendJson(sb, "user_request", user_request); sb.Append(",");
            AppendJson(sb, "requested_behavior", requested_behavior); sb.Append(",");
            sb.Append("\"available_behaviors\":[");
            for (int i = 0; i < available_behaviors.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(Escape(available_behaviors[i])).Append("\"");
            }
            sb.Append("],");
            AppendJson(sb, "possible_approximation", possible_approximation);
            sb.Append("}");
            return sb.ToString();
        }

        static void AppendJson(StringBuilder sb, string key, string value)
        {
            sb.Append("\"").Append(Escape(key)).Append("\":\"").Append(Escape(value)).Append("\"");
        }

        static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
```

- [ ] **Step 5: Create RuntimeBehaviorHost**

Create `Assets/App/Command/SpeechIntent/Runtime/Behaviors/RuntimeBehaviorHost.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    public sealed class RuntimeBehaviorHost : MonoBehaviour
    {
        enum BehaviorKind
        {
            Spin,
            Orbit,
            FollowHand,
            AttachToHand
        }

        sealed class BehaviorState
        {
            public string id;
            public string name;
            public BehaviorKind kind;
            public Vector3 axis = Vector3.up;
            public float speed;
            public Space space = Space.Self;
            public Transform center;
            public float radius;
            public float angleDegrees;
            public BodyAnchor bodyAnchor;
            public SpatialSnapshot spatial;
            public Vector3 offset;
            public float missingAnchorSeconds;
        }

        static readonly HashSet<RuntimeBehaviorHost> Hosts = new HashSet<RuntimeBehaviorHost>();
        readonly List<BehaviorState> _behaviors = new List<BehaviorState>();

        public int BehaviorCount => _behaviors.Count;

        void OnEnable()
        {
            Hosts.Add(this);
        }

        void OnDisable()
        {
            Hosts.Remove(this);
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        public bool StartSpin(string id, Vector3 axis, float speedDegreesPerSecond, Space space)
        {
            axis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
            Upsert(new BehaviorState
            {
                id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                name = "spin",
                kind = BehaviorKind.Spin,
                axis = axis,
                speed = speedDegreesPerSecond,
                space = space
            });
            return true;
        }

        public bool StartOrbit(string id, Transform center, float radius, float speedDegreesPerSecond)
        {
            if (center == null)
                return false;

            Vector3 toTarget = transform.position - center.position;
            if (toTarget.sqrMagnitude <= 0.0001f)
                toTarget = Vector3.forward * Mathf.Max(0.1f, radius);

            Upsert(new BehaviorState
            {
                id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                name = "orbit",
                kind = BehaviorKind.Orbit,
                center = center,
                radius = Mathf.Max(0.05f, radius > 0f ? radius : toTarget.magnitude),
                angleDegrees = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg,
                speed = speedDegreesPerSecond
            });
            return true;
        }

        public bool StartHandFollow(string id, BodyAnchor anchor, SpatialSnapshot spatial, Vector3 offset, bool attachStyle)
        {
            if (anchor != BodyAnchor.LeftHand && anchor != BodyAnchor.RightHand)
                return false;

            Upsert(new BehaviorState
            {
                id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                name = attachStyle ? "attach_to_hand" : "follow_hand",
                kind = attachStyle ? BehaviorKind.AttachToHand : BehaviorKind.FollowHand,
                bodyAnchor = anchor,
                spatial = spatial,
                offset = offset
            });
            return true;
        }

        public bool StopBehavior(string behaviorName)
        {
            int removed = _behaviors.RemoveAll(b => string.Equals(b.name, behaviorName, StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }

        public bool StopAll()
        {
            bool hadAny = _behaviors.Count > 0;
            _behaviors.Clear();
            return hadAny;
        }

        public bool HasBehavior(string behaviorName)
        {
            return _behaviors.Exists(b => string.Equals(b.name, behaviorName, StringComparison.OrdinalIgnoreCase));
        }

        public static int StopAllHosts()
        {
            int count = 0;
            foreach (RuntimeBehaviorHost host in new List<RuntimeBehaviorHost>(Hosts))
            {
                if (host != null && host.StopAll())
                    count++;
            }
            return count;
        }

        public void TickForTests(float deltaTime)
        {
            Tick(deltaTime);
        }

        void Tick(float deltaTime)
        {
            for (int i = _behaviors.Count - 1; i >= 0; i--)
            {
                BehaviorState behavior = _behaviors[i];
                if (behavior == null)
                {
                    _behaviors.RemoveAt(i);
                    continue;
                }

                switch (behavior.kind)
                {
                    case BehaviorKind.Spin:
                        transform.Rotate(behavior.axis, behavior.speed * deltaTime, behavior.space);
                        break;
                    case BehaviorKind.Orbit:
                        TickOrbit(behavior, deltaTime);
                        break;
                    case BehaviorKind.FollowHand:
                    case BehaviorKind.AttachToHand:
                        TickHandBehavior(behavior, deltaTime);
                        break;
                }
            }
        }

        void TickOrbit(BehaviorState behavior, float deltaTime)
        {
            if (behavior.center == null)
                return;

            behavior.angleDegrees += behavior.speed * deltaTime;
            float radians = behavior.angleDegrees * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * behavior.radius;
            transform.position = behavior.center.position + offset;
        }

        void TickHandBehavior(BehaviorState behavior, float deltaTime)
        {
            if (behavior.spatial == null)
                return;

            if (!BodyAnchorResolver.TryResolve(behavior.spatial, behavior.bodyAnchor, HandSelection.None, out Vector3 position, out Quaternion rotation))
            {
                behavior.missingAnchorSeconds += deltaTime;
                return;
            }

            behavior.missingAnchorSeconds = 0f;
            transform.SetPositionAndRotation(position + rotation * behavior.offset, rotation);
        }

        void Upsert(BehaviorState state)
        {
            _behaviors.RemoveAll(b => string.Equals(b.name, state.name, StringComparison.OrdinalIgnoreCase));
            _behaviors.Add(state);
        }
    }
}
```

- [ ] **Step 6: Add using to tests**

Add to `SpeechIntentBatchTests.cs`:

```csharp
using SpeechIntent.Behaviors;
```

- [ ] **Step 7: Run tests**

Run the Unity batch command.

Expected: all distance/movement tests pass.

- [ ] **Step 8: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/Behaviors Assets/App/Editor/SpeechIntentBatchTests.cs
git commit -m "Add runtime behavior host"
```

---

### Task 4: Add BehaviorCommandController

**Files:**
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorPolicy.cs`
- Create: `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorCommandController.cs`
- Test: `Assets/App/Editor/SpeechIntentBatchTests.cs`

- [ ] **Step 1: Add failing controller tests**

Add these calls after `TestRuntimeBehaviorHostSpinTicks();`:

```csharp
TestBehaviorCommandControllerAttachesSpin();
TestBehaviorCommandControllerRejectsProtectedTargets();
TestBehaviorCommandControllerReportsMissingCapability();
```

Add methods:

```csharp
static void TestBehaviorCommandControllerAttachesSpin()
{
    GameObject resolverObject = new GameObject("Resolver");
    GameObject controllerObject = new GameObject("BehaviorCommandController");
    GameObject cube = new GameObject("cube");
    try
    {
        cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
        SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
        BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
        controller.entityResolver = resolver;

        VoiceIntentCommand command = new VoiceIntentCommand
        {
            transcript = "make cube spin",
            intent = VoiceIntentType.AttachBehavior,
            should_execute = true,
            behavior_name = "spin",
            target_reference = TargetReferenceMode.NamedObject,
            target_name = "cube"
        };

        BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
        AssertTrue(result.success, "Expected behavior command to succeed.");
        RuntimeBehaviorHost host = cube.GetComponent<RuntimeBehaviorHost>();
        AssertTrue(host != null, "Expected RuntimeBehaviorHost on cube.");
        AssertTrue(host.HasBehavior("spin"), "Expected cube to have spin behavior.");
    }
    finally
    {
        UnityEngine.Object.DestroyImmediate(cube);
        UnityEngine.Object.DestroyImmediate(controllerObject);
        UnityEngine.Object.DestroyImmediate(resolverObject);
    }
}

static void TestBehaviorCommandControllerRejectsProtectedTargets()
{
    GameObject resolverObject = new GameObject("Resolver");
    GameObject controllerObject = new GameObject("BehaviorCommandController");
    GameObject arch = new GameObject("Arch");
    try
    {
        arch.AddComponent<SpeechIntentTrackable>().canonicalName = "Arch";
        SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
        BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
        controller.entityResolver = resolver;

        VoiceIntentCommand command = new VoiceIntentCommand
        {
            transcript = "make arch spin",
            intent = VoiceIntentType.AttachBehavior,
            should_execute = true,
            behavior_name = "spin",
            target_reference = TargetReferenceMode.NamedObject,
            target_name = "Arch"
        };

        BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
        AssertTrue(!result.success, "Expected protected target to be rejected.");
        AssertTrue(arch.GetComponent<RuntimeBehaviorHost>() == null, "Expected no host on protected target.");
    }
    finally
    {
        UnityEngine.Object.DestroyImmediate(arch);
        UnityEngine.Object.DestroyImmediate(controllerObject);
        UnityEngine.Object.DestroyImmediate(resolverObject);
    }
}

static void TestBehaviorCommandControllerReportsMissingCapability()
{
    GameObject resolverObject = new GameObject("Resolver");
    GameObject controllerObject = new GameObject("BehaviorCommandController");
    GameObject cube = new GameObject("cube");
    try
    {
        cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
        SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
        BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
        controller.entityResolver = resolver;

        VoiceIntentCommand command = new VoiceIntentCommand
        {
            transcript = "make cube melt",
            intent = VoiceIntentType.AttachBehavior,
            should_execute = true,
            behavior_name = "melt",
            target_reference = TargetReferenceMode.NamedObject,
            target_name = "cube"
        };

        BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
        AssertTrue(!result.success, "Expected missing behavior to fail.");
        AssertTrue(result.missingCapability != null, "Expected missing capability report.");
        AssertEqual("melt", result.missingCapability.requested_behavior, "Expected missing behavior name.");
    }
    finally
    {
        UnityEngine.Object.DestroyImmediate(cube);
        UnityEngine.Object.DestroyImmediate(controllerObject);
        UnityEngine.Object.DestroyImmediate(resolverObject);
    }
}
```

- [ ] **Step 2: Run test to verify compile failure**

Run the Unity batch command.

Expected: compile failure because `BehaviorCommandController` and `BehaviorPolicy` do not exist.

- [ ] **Step 3: Create BehaviorPolicy**

Create `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorPolicy.cs`:

```csharp
using System;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    public sealed class BehaviorPolicy
    {
        public float maxSpinDegreesPerSecond = 720f;
        public float maxOrbitDegreesPerSecond = 360f;
        public float minOrbitRadius = 0.05f;
        public float maxOrbitRadius = 25f;
        public float maxThrowSpeedMetersPerSecond = 8f;
        public int maxTargets = 1;

        public bool CanModify(GameObject target, out string reason)
        {
            reason = "";
            if (target == null)
            {
                reason = "No target.";
                return false;
            }

            string name = target.name ?? "";
            if (IsProtectedName(name) || HasProtectedParent(target.transform))
            {
                reason = "I cannot attach that behavior to a protected Holodeck object.";
                return false;
            }

            if (target.GetComponent<SpeechIntentTrackable>() == null)
            {
                reason = "That object is not available for runtime behaviors.";
                return false;
            }

            return true;
        }

        public float ClampSpinSpeed(float value)
        {
            if (Mathf.Approximately(value, 0f))
                value = 90f;
            return Mathf.Clamp(value, -maxSpinDegreesPerSecond, maxSpinDegreesPerSecond);
        }

        public float ClampOrbitSpeed(float value)
        {
            if (Mathf.Approximately(value, 0f))
                value = 30f;
            return Mathf.Clamp(value, -maxOrbitDegreesPerSecond, maxOrbitDegreesPerSecond);
        }

        public float ClampOrbitRadius(float value)
        {
            return Mathf.Clamp(value, minOrbitRadius, maxOrbitRadius);
        }

        public float ClampThrowSpeed(float value)
        {
            if (Mathf.Approximately(value, 0f))
                value = 5f;
            return Mathf.Clamp(value, 0.1f, maxThrowSpeedMetersPerSecond);
        }

        static bool HasProtectedParent(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (IsProtectedName(current.name))
                    return true;
                current = current.parent;
            }
            return false;
        }

        static bool IsProtectedName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "me" ||
                   normalized == "main camera" ||
                   normalized == "arch" ||
                   normalized == "archlcars" ||
                   normalized == "systems" ||
                   normalized.Contains("lcars") ||
                   normalized.Contains("worldmanager") ||
                   normalized.Contains("speechintent");
        }
    }
}
```

- [ ] **Step 4: Create BehaviorCommandController**

Create `Assets/App/Command/SpeechIntent/Runtime/Behaviors/BehaviorCommandController.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    public sealed class BehaviorCommandController : MonoBehaviour
    {
        public SceneEntityResolver entityResolver;
        public InteractionMemory interactionMemory;
        public Transform defaultThrowTarget;
        public BehaviorPolicy policy = new BehaviorPolicy();

        public string LastFailureMessage { get; private set; } = "";

        public BehaviorCommandResult Execute(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            LastFailureMessage = "";
            if (command == null)
                return Fail("No behavior command.");

            if (command.intent == VoiceIntentType.StopBehavior)
                return Stop(command, spatial);

            string behavior = NormalizeBehaviorName(command.behavior_name);
            if (!IsKnownBehavior(behavior))
                return Missing(command, behavior);

            SceneTargetResolution resolution = ResolveTargets(command, spatial);
            if (resolution.status == SceneTargetResolutionStatus.None || resolution.status == SceneTargetResolutionStatus.Ambiguous)
                return Fail(string.IsNullOrWhiteSpace(resolution.message) ? "Which object?" : resolution.message);

            if (resolution.targets.Count != 1)
                return Fail("Please choose one object for that behavior.");

            GameObject target = resolution.Target;
            if (!policy.CanModify(target, out string reason))
                return Fail(reason);

            switch (behavior)
            {
                case "spin":
                    return StartSpin(target, command);
                case "orbit":
                    return StartOrbit(target, command, spatial);
                case "throw":
                    return Throw(target, command, spatial);
                case "follow_hand":
                    return StartHand(target, command, spatial, attachStyle: false);
                case "attach_to_hand":
                    return StartHand(target, command, spatial, attachStyle: true);
                default:
                    return Missing(command, behavior);
            }
        }

        BehaviorCommandResult Stop(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command.behavior_stop_all || command.target_reference == TargetReferenceMode.All)
            {
                int stopped = RuntimeBehaviorHost.StopAllHosts();
                return BehaviorCommandResult.Success(stopped > 0 ? $"Stopped behaviors on {stopped} object(s)." : "No active behaviors were found.");
            }

            SceneTargetResolution resolution = ResolveTargets(command, spatial);
            if (resolution.status == SceneTargetResolutionStatus.None || resolution.status == SceneTargetResolutionStatus.Ambiguous)
                return Fail(string.IsNullOrWhiteSpace(resolution.message) ? "Which object?" : resolution.message);

            int count = 0;
            string behavior = NormalizeBehaviorName(command.behavior_name);
            foreach (GameObject target in resolution.targets)
            {
                RuntimeBehaviorHost host = target != null ? target.GetComponent<RuntimeBehaviorHost>() : null;
                if (host == null)
                    continue;
                bool changed = string.IsNullOrWhiteSpace(behavior) ? host.StopAll() : host.StopBehavior(behavior);
                if (changed)
                    count++;
            }
            return BehaviorCommandResult.Success(count > 0 ? "Stopped behavior." : "No matching behavior was active.");
        }

        BehaviorCommandResult StartSpin(GameObject target, VoiceIntentCommand command)
        {
            RuntimeBehaviorHost host = EnsureHost(target);
            Vector3 axis = ParseAxis(command.behavior_axis);
            host.StartSpin("spin", axis, policy.ClampSpinSpeed(command.behavior_speed), Space.Self);
            Register(target);
            return BehaviorCommandResult.Success($"Started spinning {target.name}.");
        }

        BehaviorCommandResult StartOrbit(GameObject target, VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            GameObject center = ResolveSecondaryTarget(command, spatial);
            if (center == null)
                return Fail("Orbit around what?");

            float radius = command.behavior_radius > 0f
                ? policy.ClampOrbitRadius(command.behavior_radius)
                : Vector3.Distance(target.transform.position, center.transform.position);

            RuntimeBehaviorHost host = EnsureHost(target);
            host.StartOrbit("orbit", center.transform, policy.ClampOrbitRadius(radius), policy.ClampOrbitSpeed(command.behavior_speed));
            Register(target);
            return BehaviorCommandResult.Success($"Started orbiting {target.name} around {center.name}.");
        }

        BehaviorCommandResult Throw(GameObject target, VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            Vector3 direction = ResolveThrowDirection(target, command, spatial);
            if (direction.sqrMagnitude <= 0.0001f)
                return Fail("Throw toward what?");

            Rigidbody body = target.GetComponent<Rigidbody>() ?? target.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = false;
            body.AddForce(direction.normalized * policy.ClampThrowSpeed(command.behavior_speed), ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.onUnitSphere * 0.5f, ForceMode.VelocityChange);
            Register(target);
            return BehaviorCommandResult.Success($"Threw {target.name}.");
        }

        BehaviorCommandResult StartHand(GameObject target, VoiceIntentCommand command, SpatialSnapshot spatial, bool attachStyle)
        {
            BodyAnchor anchor = ResolveHandAnchor(command, spatial);
            if (anchor == BodyAnchor.None)
                return Fail("Which hand?");

            RuntimeBehaviorHost host = EnsureHost(target);
            Vector3 offset = attachStyle ? Vector3.zero : new Vector3(0f, 0f, 0.15f);
            host.StartHandFollow(attachStyle ? "attach_to_hand" : "follow_hand", anchor, spatial, offset, attachStyle);
            Register(target);
            return BehaviorCommandResult.Success(attachStyle ? $"Attached {target.name} to hand." : $"{target.name} is following your hand.");
        }

        SceneTargetResolution ResolveTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (entityResolver != null)
                return entityResolver.ResolveTargets(command, spatial);

            GameObject remembered = interactionMemory != null ? interactionMemory.GetLastCreatedOrInteracted() : null;
            return SceneTargetResolution.FromTargets(
                remembered != null ? new List<GameObject> { remembered } : new List<GameObject>(),
                "object",
                false);
        }

        GameObject ResolveSecondaryTarget(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            string name = command.behavior_secondary_target_name;
            if (!string.IsNullOrWhiteSpace(name) && entityResolver != null)
            {
                return entityResolver.ResolveTarget(TargetReferenceMode.NamedObject, name, spatial, HandSelection.None);
            }
            return null;
        }

        Vector3 ResolveThrowDirection(GameObject target, VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            GameObject secondary = ResolveSecondaryTarget(command, spatial);
            if (secondary != null)
                return secondary.transform.position - target.transform.position;

            if (spatial != null)
            {
                if (spatial.right_hand != null && spatial.right_hand.has_hit)
                    return spatial.right_hand.hit_point - target.transform.position;
                if (spatial.left_hand != null && spatial.left_hand.has_hit)
                    return spatial.left_hand.hit_point - target.transform.position;
                if (spatial.head_forward.sqrMagnitude > 0.0001f)
                    return spatial.head_forward.normalized;
            }
            return defaultThrowTarget != null ? defaultThrowTarget.position - target.transform.position : Vector3.zero;
        }

        BodyAnchor ResolveHandAnchor(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command.target_hand == HandSelection.Left)
                return BodyAnchor.LeftHand;
            if (command.target_hand == HandSelection.Right)
                return BodyAnchor.RightHand;
            if (spatial?.right_hand != null && spatial.right_hand.is_available && spatial.right_hand.is_pointing)
                return BodyAnchor.RightHand;
            if (spatial?.left_hand != null && spatial.left_hand.is_available && spatial.left_hand.is_pointing)
                return BodyAnchor.LeftHand;
            return BodyAnchor.None;
        }

        RuntimeBehaviorHost EnsureHost(GameObject target)
        {
            return target.GetComponent<RuntimeBehaviorHost>() ?? target.AddComponent<RuntimeBehaviorHost>();
        }

        void Register(GameObject target)
        {
            if (interactionMemory != null && target != null)
                interactionMemory.RegisterInteraction(target);
        }

        BehaviorCommandResult Fail(string message)
        {
            LastFailureMessage = message;
            return BehaviorCommandResult.Failure(message);
        }

        BehaviorCommandResult Missing(VoiceIntentCommand command, string behavior)
        {
            var report = new MissingCapabilityReport
            {
                user_request = command?.transcript ?? "",
                requested_behavior = string.IsNullOrWhiteSpace(behavior) ? "unknown" : behavior,
                available_behaviors = new List<string> { "spin", "orbit", "throw", "follow hand", "attach to hand" },
                possible_approximation = ""
            };
            report.Log();
            LastFailureMessage = report.ToUserMessage();
            return BehaviorCommandResult.Missing(report);
        }

        static bool IsKnownBehavior(string behavior)
        {
            return behavior == "spin" || behavior == "orbit" || behavior == "throw" || behavior == "follow_hand" || behavior == "attach_to_hand";
        }

        static string NormalizeBehaviorName(string value)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
            if (normalized == "follow") return "follow_hand";
            if (normalized == "attach") return "attach_to_hand";
            return normalized;
        }

        static Vector3 ParseAxis(string axis)
        {
            string normalized = (axis ?? "").Trim().ToLowerInvariant();
            return normalized switch
            {
                "x" or "right" => Vector3.right,
                "z" or "forward" => Vector3.forward,
                _ => Vector3.up
            };
        }
    }
}
```

- [ ] **Step 5: Add using to tests**

Ensure `SpeechIntentBatchTests.cs` has:

```csharp
using SpeechIntent.Behaviors;
```

- [ ] **Step 6: Run tests**

Run the Unity batch command.

Expected: all distance/movement tests pass.

- [ ] **Step 7: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/Behaviors Assets/App/Editor/SpeechIntentBatchTests.cs
git commit -m "Add behavior command controller"
```

---

### Task 5: Route Behavior Commands Through Dispatcher

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs`
- Modify: `Assets/App/Editor/SpeechIntentSceneSetup.cs`
- Test: existing Unity batch tests

- [ ] **Step 1: Add dispatcher reference**

In `WorldActionDispatcher`, add with the other controller references:

```csharp
public SpeechIntent.Behaviors.BehaviorCommandController behaviorCommandController;
```

- [ ] **Step 2: Auto-wire in Awake**

In `Awake`, after other controller discovery:

```csharp
if (behaviorCommandController == null)
    behaviorCommandController = GetComponent<SpeechIntent.Behaviors.BehaviorCommandController>()
                                ?? FindFirstObjectByType<SpeechIntent.Behaviors.BehaviorCommandController>(FindObjectsInactive.Include);
if (behaviorCommandController == null)
    behaviorCommandController = gameObject.AddComponent<SpeechIntent.Behaviors.BehaviorCommandController>();
if (behaviorCommandController.entityResolver == null && targetTransformController != null)
    behaviorCommandController.entityResolver = targetTransformController.entityResolver;
if (behaviorCommandController.interactionMemory == null)
    behaviorCommandController.interactionMemory = interactionMemory;
```

- [ ] **Step 3: Add switch cases**

In `Execute`, add:

```csharp
case VoiceIntentType.AttachBehavior:
case VoiceIntentType.StopBehavior:
    HandleBehaviorCommand(command, spatial);
    break;
```

- [ ] **Step 4: Add handler**

Add near other handler methods:

```csharp
private void HandleBehaviorCommand(VoiceIntentCommand command, SpatialSnapshot spatial)
{
    if (behaviorCommandController == null)
    {
        Debug.LogWarning("[WorldActionDispatcher] BehaviorCommandController not assigned.");
        command.spoken_response = "Behavior controller not assigned.";
        ArchStatusBus.Warning(command.spoken_response, "BEHAVIOR");
        return;
    }

    SpeechIntent.Behaviors.BehaviorCommandResult result = behaviorCommandController.Execute(command, spatial);
    if (result == null)
    {
        command.spoken_response = "Behavior command returned no result.";
        ArchStatusBus.Warning(command.spoken_response, "BEHAVIOR");
        return;
    }

    command.spoken_response = result.message;
    if (result.success)
        ArchStatusBus.Info(result.message, "BEHAVIOR");
    else
        ArchStatusBus.Warning(result.message, "BEHAVIOR");
}
```

- [ ] **Step 5: Update scene setup**

In `SpeechIntentSceneSetup.cs`, where controllers are added to `speechRoot`, add:

```csharp
SpeechIntent.Behaviors.BehaviorCommandController behaviorController =
    GetOrAdd<SpeechIntent.Behaviors.BehaviorCommandController>(speechRoot);
```

After `targetTransform.entityResolver` and `interactionMemory` are wired, add:

```csharp
dispatcher.behaviorCommandController = behaviorController;
behaviorController.entityResolver = resolver;
behaviorController.interactionMemory = memory;
```

Use the existing local variable names in that file. If they differ, use the matching `SceneEntityResolver` and `InteractionMemory` variables already created by the setup script.

- [ ] **Step 6: Run tests**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-speechintent-behavior-task5.log
```

Expected: all distance/movement tests pass.

- [ ] **Step 7: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/WorldActionDispatcher.cs Assets/App/Editor/SpeechIntentSceneSetup.cs
git commit -m "Route runtime behavior commands"
```

---

### Task 6: Add OpenAI Behavior Instructions

**Files:**
- Modify: `Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs`
- Test: existing Unity batch tests and asset parse through Unity compile

- [ ] **Step 1: Update developer instructions**

In `additionalDeveloperInstructions`, add this block after material/transform guidance and before audio guidance:

```text
Runtime behavior commands:
- For "make this spin", "make the cube spin", "rotate continuously", or "turn around forever", use intent=AttachBehavior, behavior_name='spin'. Use target_reference=PointedObject for "this/that" when indicated, NamedObject for named objects, and LastCreatedOrInteracted for pronouns like "it".
- For "make the ball orbit the table", "make this circle that", or "make the moon go around the planet", use intent=AttachBehavior, behavior_name='orbit'. Put the moving object in target_name/object_name and the orbit center in behavior_secondary_target_name.
- For "throw this at the wall", "toss the ball toward the table", "fling it there", or "launch the cube", use intent=AttachBehavior, behavior_name='throw'. Put the thrown object in target_name/object_name or use PointedObject, and put the destination object in behavior_secondary_target_name when named.
- For "make this follow my left hand" or "make the cube follow my right hand", use intent=AttachBehavior, behavior_name='follow_hand', and set target_hand=Left or Right. If no hand is specified, use target_hand=Either.
- For "give me the blaster", "put the cube in my hand", or "attach this to my right hand", use intent=AttachBehavior, behavior_name='attach_to_hand'. Set target_hand from the utterance or Either if unspecified.
- For "stop spinning", "stop it orbiting", "stop this following my hand", use intent=StopBehavior. Set behavior_name when specified and target_reference based on the referenced object.
- For "stop all behaviors", use intent=StopBehavior, behavior_stop_all=true, target_reference=All.
- Do not invent behavior names. If the requested behavior is not spin, orbit, throw, follow_hand, or attach_to_hand, set behavior_name to the requested behavior and let Unity report missing capability.
```

- [ ] **Step 2: Run Unity compile/tests**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-speechintent-behavior-task6.log
```

Expected: all distance/movement tests pass and no asset parse errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/App/Command/SpeechIntent/Runtime/OpenAiSpeechIntentConfig.cs
git commit -m "Teach intent parser behavior commands"
```

---

### Task 7: Final Verification

**Files:**
- No code changes expected.

- [ ] **Step 1: Run behavior/distance tests**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.SpeechIntentBatchTests.RunDistanceAndMovementTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-speechintent-behavior-final.log
```

Expected log contains:

```text
[SpeechIntentBatchTests] All distance and movement tests passed.
```

- [ ] **Step 2: Run cached object tests**

Run:

```bash
/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev -executeMethod HeadsetHolodeck.EditorTests.CachedObjectBatchTests.RunCachedObjectStoreTests -logFile /Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeckDev/unity-cached-object-behavior-final.log
```

Expected log contains:

```text
[CachedObjectBatchTests] Cached object store tests passed.
```

- [ ] **Step 3: Check staged scope**

Run:

```bash
git status --short
```

Expected: `.env` may remain modified and must not be staged. The source brief `docs/superpowers/specs/codex_ai_unity_scene_control_architecture_brief.md` may remain untracked unless the user explicitly wants it committed.

- [ ] **Step 4: Final commit if needed**

If Task 6 was the last code commit and there are no remaining code changes, skip this step. If verification required small fixes, commit them:

```bash
git add Assets/App/Command/SpeechIntent/Runtime Assets/App/Editor
git commit -m "Finalize runtime behavior recipes"
```

Do not add `.env`.

---

## Self-Review Notes

- Spec coverage: the plan covers schema, parser, behavior host, behavior controller, policy, dispatcher integration, OpenAI instruction updates, tests, and final verification.
- Scope intentionally excludes general JSON recipe interpretation and full primitive manifests.
- Type consistency: behavior fields use `behavior_name`, `behavior_action`, `behavior_secondary_target_name`, `behavior_speed`, `behavior_radius`, `behavior_axis`, and `behavior_stop_all` throughout.
- Migration path remains additive: existing object, transform, material, audio, light, and world-generation commands continue through their current controllers.

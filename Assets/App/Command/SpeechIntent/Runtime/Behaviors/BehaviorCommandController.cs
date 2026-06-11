using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    public sealed class BehaviorCommandController : MonoBehaviour
    {
        static readonly string[] AvailableBehaviors =
        {
            "spin",
            "orbit",
            "throw",
            "follow_hand",
            "attach_to_hand"
        };

        public SceneEntityResolver entityResolver;
        public InteractionMemory interactionMemory;
        public SpatialContextProvider spatialContextProvider;
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
                string message = stopped > 0
                    ? $"Stopped behaviors on {stopped} object(s)."
                    : "No active behaviors were found.";
                return BehaviorCommandResult.Success(message);
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
            if (!host.StartSpin("spin", axis, policy.ClampSpinSpeed(command.behavior_speed), Space.Self))
                return Fail("Could not start spin behavior.");

            Register(target);
            return BehaviorCommandResult.Success($"Started spinning {target.name}.");
        }

        BehaviorCommandResult StartOrbit(GameObject target, VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (!TryResolveSecondaryTarget(command, spatial, "Orbit around what?", out GameObject center, out string failureMessage))
                return Fail(failureMessage);

            float radius = command.behavior_radius > 0f
                ? command.behavior_radius
                : Vector3.Distance(target.transform.position, center.transform.position);

            RuntimeBehaviorHost host = EnsureHost(target);
            if (!host.StartOrbit("orbit", center.transform, policy.ClampOrbitRadius(radius), policy.ClampOrbitSpeed(command.behavior_speed)))
                return Fail("Could not start orbit behavior.");

            Register(target);
            return BehaviorCommandResult.Success($"Started orbiting {target.name} around {center.name}.");
        }

        BehaviorCommandResult Throw(GameObject target, VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (!TryResolveThrowDirection(target, command, spatial, out Vector3 direction, out string failureMessage))
                return Fail(failureMessage);

            if (direction.sqrMagnitude <= 0.0001f)
                return Fail("Throw toward what?");

            Rigidbody body = target.GetComponent<Rigidbody>();
            bool createdBody = false;
            if (body == null)
            {
                body = target.AddComponent<Rigidbody>();
                body.useGravity = true;
                body.isKinematic = false;
                createdBody = true;
            }
            else if (body.isKinematic)
            {
                return Fail("That object is kinematic and cannot be thrown yet.");
            }

            body.AddForce(direction.normalized * policy.ClampThrowSpeed(command.behavior_speed), ForceMode.VelocityChange);
            if (createdBody || !body.isKinematic)
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
            string id = attachStyle ? "attach_to_hand" : "follow_hand";
            if (!host.StartHandFollow(id, anchor, spatial, offset, attachStyle, spatialContextProvider))
                return Fail("Could not start hand behavior.");

            Register(target);
            return BehaviorCommandResult.Success(attachStyle ? $"Attached {target.name} to hand." : $"{target.name} is following your hand.");
        }

        SceneTargetResolution ResolveTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (entityResolver != null)
                return entityResolver.ResolveTargets(command, spatial);

            GameObject remembered = interactionMemory != null ? interactionMemory.GetLastCreatedOrInteracted() : null;
            List<GameObject> targets = remembered != null
                ? new List<GameObject> { remembered }
                : new List<GameObject>();
            return SceneTargetResolution.FromTargets(targets, "object", false);
        }

        bool TryResolveSecondaryTarget(
            VoiceIntentCommand command,
            SpatialSnapshot spatial,
            string missingMessage,
            out GameObject target,
            out string failureMessage)
        {
            target = null;
            failureMessage = "";
            if (command == null || string.IsNullOrWhiteSpace(command.behavior_secondary_target_name))
            {
                failureMessage = missingMessage;
                return false;
            }

            if (entityResolver == null)
            {
                failureMessage = missingMessage;
                return false;
            }

            var secondaryCommand = new VoiceIntentCommand
            {
                target_reference = TargetReferenceMode.NamedObject,
                target_name = command.behavior_secondary_target_name,
                object_name = command.behavior_secondary_target_name,
                target_entity = command.behavior_secondary_target_name
            };

            SceneTargetResolution resolution = entityResolver.ResolveTargets(secondaryCommand, spatial);
            if (resolution.status == SceneTargetResolutionStatus.Single && resolution.Target != null)
            {
                target = resolution.Target;
                return true;
            }

            failureMessage = string.IsNullOrWhiteSpace(resolution.message)
                ? missingMessage
                : resolution.message;
            return false;
        }

        bool TryResolveThrowDirection(
            GameObject target,
            VoiceIntentCommand command,
            SpatialSnapshot spatial,
            out Vector3 direction,
            out string failureMessage)
        {
            direction = Vector3.zero;
            failureMessage = "";

            if (command != null && !string.IsNullOrWhiteSpace(command.behavior_secondary_target_name))
            {
                if (!TryResolveSecondaryTarget(command, spatial, "Throw toward what?", out GameObject secondary, out failureMessage))
                    return false;

                direction = secondary.transform.position - target.transform.position;
                return direction.sqrMagnitude > 0.0001f;
            }

            if (spatial != null)
            {
                if (spatial.right_hand != null && spatial.right_hand.has_hit)
                {
                    direction = spatial.right_hand.hit_point - target.transform.position;
                    return true;
                }
                if (spatial.left_hand != null && spatial.left_hand.has_hit)
                {
                    direction = spatial.left_hand.hit_point - target.transform.position;
                    return true;
                }
                if (spatial.head_forward.sqrMagnitude > 0.0001f)
                {
                    direction = spatial.head_forward.normalized;
                    return true;
                }
            }

            if (defaultThrowTarget != null)
            {
                direction = defaultThrowTarget.position - target.transform.position;
                return direction.sqrMagnitude > 0.0001f;
            }

            failureMessage = "Throw toward what?";
            return false;
        }

        BodyAnchor ResolveHandAnchor(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            if (command.body_anchor == BodyAnchor.LeftHand || command.body_anchor == BodyAnchor.RightHand)
                return command.body_anchor;

            if (command.target_hand == HandSelection.Left)
                return BodyAnchor.LeftHand;
            if (command.target_hand == HandSelection.Right)
                return BodyAnchor.RightHand;

            if (spatial?.right_hand != null && spatial.right_hand.is_available)
                return BodyAnchor.RightHand;
            if (spatial?.left_hand != null && spatial.left_hand.is_available)
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
            LastFailureMessage = message ?? "";
            return BehaviorCommandResult.Failure(message);
        }

        BehaviorCommandResult Missing(VoiceIntentCommand command, string behavior)
        {
            var report = new MissingCapabilityReport(
                command?.transcript ?? "",
                string.IsNullOrWhiteSpace(behavior) ? "unknown" : behavior,
                AvailableBehaviors,
                Array.Empty<string>(),
                "");
            report.Log();
            LastFailureMessage = report.ToUserMessage();
            return BehaviorCommandResult.Missing(report);
        }

        static bool IsKnownBehavior(string behavior)
        {
            return behavior == "spin" ||
                   behavior == "orbit" ||
                   behavior == "throw" ||
                   behavior == "follow_hand" ||
                   behavior == "attach_to_hand";
        }

        static string NormalizeBehaviorName(string value)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
            if (normalized == "follow")
                return "follow_hand";
            if (normalized == "attach")
                return "attach_to_hand";

            return normalized;
        }

        static Vector3 ParseAxis(string axis)
        {
            string normalized = (axis ?? "").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "x":
                case "right":
                    return Vector3.right;
                case "z":
                case "forward":
                    return Vector3.forward;
                default:
                    return Vector3.up;
            }
        }
    }
}

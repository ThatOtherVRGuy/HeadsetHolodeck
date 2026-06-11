using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SpeechIntent
{
    public enum CommandClarificationSlot
    {
        None = 0,
        Placement = 1,
        RotationDegrees = 2,
        RotationAxis = 3,
        Target = 4,
        Confirmation = 5
    }

    public sealed class CommandDialogStateManager
    {
        VoiceIntentCommand _pendingCommand;
        CommandClarificationSlot _pendingSlot;
        string _question = "";

        public bool HasPendingClarification => _pendingCommand != null && _pendingSlot != CommandClarificationSlot.None;
        public CommandClarificationSlot PendingSlot => _pendingSlot;
        public string Question => _question;

        public void BeginClarification(VoiceIntentCommand command, CommandClarificationSlot slot, string question)
        {
            _pendingCommand = CloneCommand(command);
            _pendingSlot = slot;
            _question = question ?? "";
        }

        public void Clear()
        {
            _pendingCommand = null;
            _pendingSlot = CommandClarificationSlot.None;
            _question = "";
        }

        public bool TryComplete(SpeechIntentResult result, out VoiceIntentCommand completedCommand)
        {
            completedCommand = null;
            if (!HasPendingClarification || result == null)
                return false;

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            VoiceIntentCommand command = CloneCommand(_pendingCommand);
            command.transcript = text;

            switch (_pendingSlot)
            {
                case CommandClarificationSlot.RotationDegrees:
                    if (!TryParseDegrees(text, out float degrees))
                        return false;
                    command.rotation_degrees = degrees;
                    command.should_execute = true;
                    command.intent = VoiceIntentType.RotateTarget;
                    command.spoken_response = BuildRotateResponse(command);
                    completedCommand = command;
                    Clear();
                    return true;

                case CommandClarificationSlot.RotationAxis:
                    if (!TryParseRotationAxis(text, out RotationAxis axis))
                        return false;
                    command.rotation_axis = axis;
                    command.should_execute = true;
                    command.intent = VoiceIntentType.RotateTarget;
                    command.spoken_response = BuildRotateResponse(command);
                    completedCommand = command;
                    Clear();
                    return true;

                case CommandClarificationSlot.Target:
                    if (!TryApplyTargetClarification(text, command))
                        return false;
                    command.should_execute = true;
                    command.spoken_response = BuildTargetResponse(command);
                    completedCommand = command;
                    Clear();
                    return true;

                default:
                    return false;
            }
        }

        public bool TryCompletePlacement(
            SpeechIntentResult result,
            Func<string, VoiceIntentCommand, bool> applyPlacement,
            out VoiceIntentCommand completedCommand)
        {
            completedCommand = null;
            if (!HasPendingClarification || _pendingSlot != CommandClarificationSlot.Placement || result == null)
                return false;

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (string.IsNullOrWhiteSpace(text) || applyPlacement == null)
                return false;

            VoiceIntentCommand command = CloneCommand(_pendingCommand);
            command.transcript = text;
            if (!applyPlacement(text, command))
                return false;

            command.should_execute = true;
            command.spoken_response = string.IsNullOrWhiteSpace(command.object_name)
                ? "Creating object."
                : $"Creating {command.object_name}.";
            completedCommand = command;
            Clear();
            return true;
        }

        public static bool TryParseDegrees(string text, out float degrees)
        {
            degrees = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            Match match = Regex.Match(text, @"[-+]?\d+(\.\d+)?");
            if (!match.Success)
                return false;

            return float.TryParse(
                match.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out degrees);
        }

        public static bool TryParseRotationAxis(string text, out RotationAxis axis)
        {
            axis = RotationAxis.None;
            string lower = (text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            if (lower.Contains("x axis", StringComparison.Ordinal) ||
                lower == "x" ||
                lower.Contains("pitch", StringComparison.Ordinal))
            {
                axis = RotationAxis.X;
                return true;
            }

            if (lower.Contains("y axis", StringComparison.Ordinal) ||
                lower == "y" ||
                lower.Contains("yaw", StringComparison.Ordinal))
            {
                axis = RotationAxis.Y;
                return true;
            }

            if (lower.Contains("z axis", StringComparison.Ordinal) ||
                lower == "z" ||
                lower.Contains("roll", StringComparison.Ordinal))
            {
                axis = RotationAxis.Z;
                return true;
            }

            return false;
        }

        static string BuildRotateResponse(VoiceIntentCommand command)
        {
            string target = FirstNonEmpty(command.target_name, command.target_entity, command.object_name);
            if (command.target_reference == TargetReferenceMode.CurrentWorld || string.Equals(target, "world", StringComparison.OrdinalIgnoreCase))
                target = "world";
            if (string.IsNullOrWhiteSpace(target))
                target = "target";

            return $"Rotating {target}.";
        }

        static bool TryApplyTargetClarification(string text, VoiceIntentCommand command)
        {
            string lower = (text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            if (lower == "this" || lower == "this one" || lower == "that" || lower == "that one")
            {
                command.target_reference = TargetReferenceMode.PointedObject;
                return true;
            }

            string qualifier = StripTargetReplyFiller(lower);
            if (TryParseSpatialQualifier(qualifier, out string spatialQualifier))
            {
                command.target_spatial_qualifier = spatialQualifier;
                return true;
            }

            if (RuntimeMaterialCatalog.TryParseDescriptor(qualifier, default, out RuntimeMaterialDescriptor descriptor))
            {
                command.target_material_prompt = descriptor.name;
                return true;
            }

            return false;
        }

        static string StripTargetReplyFiller(string lower)
        {
            string value = lower.Trim();
            value = Regex.Replace(value, @"[^\w\s]", " ");
            value = Regex.Replace(value, @"\b(the|that|this|one|object|thing)\b", " ");
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value;
        }

        static bool TryParseSpatialQualifier(string value, out string spatialQualifier)
        {
            spatialQualifier = "";
            string lower = (value ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            if (lower == "top" ||
                lower == "on top" ||
                lower == "topmost" ||
                lower == "upper" ||
                lower == "uppermost" ||
                lower == "highest")
            {
                spatialQualifier = "topmost";
                return true;
            }

            if (lower == "bottom" ||
                lower == "on bottom" ||
                lower == "bottommost" ||
                lower == "lower" ||
                lower == "lowermost" ||
                lower == "lowest")
            {
                spatialQualifier = "bottommost";
                return true;
            }

            return false;
        }

        static string BuildTargetResponse(VoiceIntentCommand command)
        {
            string material = command.target_material_prompt;
            string spatial = command.target_spatial_qualifier;
            string name = FirstNonEmpty(command.target_name, command.target_entity, command.object_name);
            string target = FirstNonEmpty(JoinNonEmpty(" ", spatial, material, name), material, name);
            return string.IsNullOrWhiteSpace(target)
                ? "Using that target."
                : $"Using {target}.";
        }

        static string JoinNonEmpty(string separator, params string[] values)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    parts.Add(value.Trim());
            }
            return string.Join(separator, parts);
        }

        public static VoiceIntentCommand CloneCommand(VoiceIntentCommand source)
        {
            if (source == null)
                return null;

            return new VoiceIntentCommand
            {
                transcript = source.transcript,
                intent = source.intent,
                confidence = source.confidence,
                should_execute = source.should_execute,
                spoken_response = source.spoken_response,
                world_prompt = source.world_prompt,
                image_search_query = source.image_search_query,
                ui_panel = source.ui_panel,
                target_entity = source.target_entity,
                lighting_preset = source.lighting_preset,
                light_type = source.light_type,
                light_color_prompt = source.light_color_prompt,
                light_action = source.light_action,
                light_intensity = source.light_intensity,
                light_range = source.light_range,
                light_spot_angle = source.light_spot_angle,
                target_hand = source.target_hand,
                spatial_reference = source.spatial_reference,
                body_anchor = source.body_anchor,
                object_name = source.object_name,
                placement_mode = source.placement_mode,
                object_width_meters = source.object_width_meters,
                object_weightless = source.object_weightless,
                object_choice_action = source.object_choice_action,
                target_reference = source.target_reference,
                target_name = source.target_name,
                target_material_prompt = source.target_material_prompt,
                target_spatial_qualifier = source.target_spatial_qualifier,
                scale_multiplier = source.scale_multiplier,
                reset_to_default_scale = source.reset_to_default_scale,
                rotation_axis = source.rotation_axis,
                rotation_degrees = source.rotation_degrees,
                relative_direction = source.relative_direction,
                relative_distance_meters = source.relative_distance_meters,
                material_prompt = source.material_prompt,
                physics_action = source.physics_action,
                physics_mass = source.physics_mass,
                proxy_category = source.proxy_category,
                proxy_visible = source.proxy_visible,
                behavior_name = source.behavior_name,
                behavior_secondary_target_name = source.behavior_secondary_target_name,
                behavior_speed = source.behavior_speed,
                behavior_radius = source.behavior_radius,
                behavior_axis = source.behavior_axis,
                behavior_stop_all = source.behavior_stop_all,
                generation_model = source.generation_model,
                content_path = source.content_path,
                config_name = source.config_name,
                sound_prompt = source.sound_prompt,
                sound_prompts = source.sound_prompts != null ? new System.Collections.Generic.List<string>(source.sound_prompts) : new System.Collections.Generic.List<string>(),
                sound_category = source.sound_category,
                sound_species = source.sound_species,
                sound_provider = source.sound_provider,
                sound_count = source.sound_count,
                sound_max_duration_seconds = source.sound_max_duration_seconds,
                audio_loop = source.audio_loop,
                audio_volume = source.audio_volume,
                audio_volume_delta = source.audio_volume_delta,
                audio_playback_mode = source.audio_playback_mode,
                audio_interval_seconds = source.audio_interval_seconds,
                audio_interval_variance_seconds = source.audio_interval_variance_seconds,
                audio_control = source.audio_control,
                audio_muted = source.audio_muted,
                audio_play_now = source.audio_play_now,
                audio_spatial_blend = source.audio_spatial_blend,
                reason = source.reason
            };
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }
    }
}

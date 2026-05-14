using System;
using System.Text.RegularExpressions;

namespace SpeechIntent
{
    public static class LocalTypedIntentParser
    {
        public static VoiceIntentCommand Parse(string text)
        {
            string transcript = (text ?? string.Empty).Trim();
            string lower = transcript.ToLowerInvariant();

            VoiceIntentCommand command = new VoiceIntentCommand
            {
                transcript = transcript,
                confidence = 0.62f,
                should_execute = true,
                spoken_response = ""
            };

            if (string.IsNullOrWhiteSpace(transcript))
                return Unknown(transcript);

            if (TryParseContentPath(transcript, command))
                return command;

            if (ContainsAny(lower, "capture thumbnail", "take thumbnail", "save thumbnail", "update thumbnail", "make thumbnail", "create thumbnail"))
            {
                command.intent = VoiceIntentType.CaptureWorldThumbnail;
                command.spoken_response = "Capturing thumbnail.";
                return command;
            }

            if (ContainsAny(lower, "capture panorama", "capture pano", "take panorama", "take pano", "save panorama", "save pano", "update panorama", "update pano", "create panorama", "create pano"))
            {
                command.intent = VoiceIntentType.CaptureWorldPanorama;
                command.spoken_response = "Capturing panorama.";
                return command;
            }

            if (ContainsAny(lower, "capture image", "capture camera", "take picture", "take photo", "open camera", "camera preview"))
            {
                command.intent = VoiceIntentType.CaptureHeadsetCamera;
                command.spoken_response = "Opening camera.";
                return command;
            }

            if (IsApproval(lower))
            {
                command.intent = VoiceIntentType.ConfirmHeadsetCameraCapture;
                command.spoken_response = "Capturing image.";
                return command;
            }

            if (ContainsAny(lower, "next image", "next result"))
            {
                command.intent = VoiceIntentType.NextImageSearchResult;
                return command;
            }

            if (ContainsAny(lower, "previous image", "prev image", "previous result", "prev result"))
            {
                command.intent = VoiceIntentType.PreviousImageSearchResult;
                return command;
            }

            if (ContainsAny(lower, "use this image", "select this image", "choose this image"))
            {
                command.intent = VoiceIntentType.SelectImageSearchResult;
                command.spoken_response = "Using this image.";
                return command;
            }

            if (TryStripPrefix(transcript, lower, out string imageQuery,
                    "search image", "search images", "image search", "find image", "find images", "search pixabay", "pixabay"))
            {
                command.intent = VoiceIntentType.SearchImages;
                command.image_search_query = imageQuery;
                command.world_prompt = imageQuery;
                command.spoken_response = "Searching images.";
                return command;
            }

            if (TryStripPrefix(transcript, lower, out string captureWorldPrompt,
                    "create world from image", "make world from image", "create world from capture", "make world from capture"))
            {
                command.intent = VoiceIntentType.GenerateWorldFromCapture;
                command.world_prompt = string.IsNullOrWhiteSpace(captureWorldPrompt)
                    ? "Create a world inspired by this image."
                    : captureWorldPrompt;
                command.spoken_response = "Creating world from image.";
                return command;
            }

            if (TryStripPrefix(transcript, lower, out string captureObjectPrompt,
                    "create object from image", "make object from image", "create object from capture", "make object from capture"))
            {
                command.intent = VoiceIntentType.GenerateObjectFromCapture;
                command.object_name = captureObjectPrompt;
                command.world_prompt = captureObjectPrompt;
                command.spoken_response = "Creating object from image.";
                return command;
            }

            if (TryStripPrefix(transcript, lower, out string soundPrompt,
                    "add sound", "add sounds", "create sound", "create sounds", "add audio", "create audio"))
            {
                command.intent = VoiceIntentType.CreateAudioSource;
                if (TryInferBodyAnchor(lower, out BodyAnchor anchor, out HandSelection hand))
                {
                    soundPrompt = StripBodyAnchorPhrase(soundPrompt);
                    command.spatial_reference = SpatialReferenceMode.BodyAnchor;
                    command.body_anchor = anchor;
                    command.target_hand = hand;
                }
                command.sound_prompt = soundPrompt;
                command.sound_count = 1;
                command.sound_provider = "auto";
                command.audio_playback_mode = "auto";
                command.spoken_response = "Adding sound.";
                return command;
            }

            if (TryParseAudioControl(transcript, lower, command))
                return command;

            if (TryParseDelete(transcript, lower, command))
                return command;

            if (TryParseMaterialCommand(transcript, lower, command))
                return command;

            if (TryParseWorldOriginPlacement(transcript, lower, command))
                return command;

            if (TryParseBodyAnchorPlacement(transcript, lower, command))
                return command;

            if (TryParseBodyAnchorMove(transcript, lower, command))
                return command;

            if (TryParseRelativeMove(transcript, lower, command))
                return command;

            if (TryStripPrefix(transcript, lower, out string worldPrompt,
                    "create world", "make world", "generate world", "build world"))
            {
                command.intent = VoiceIntentType.GenerateWorld;
                command.world_prompt = worldPrompt;
                command.spoken_response = "Creating world.";
                return command;
            }

            if (ContainsAny(lower, "exit holodeck", "quit application", "quit app"))
            {
                command.intent = VoiceIntentType.QuitApplication;
                command.spoken_response = "Exiting holodeck.";
                return command;
            }

            return Unknown(transcript);
        }

        static bool TryParseWorldOriginPlacement(string original, string lower, VoiceIntentCommand command)
        {
            if (!ContainsAny(lower, "world origin", "the origin", "origin"))
                return false;

            if (!TryStripPrefix(original, lower, out string objectText,
                    "create a", "create an", "create", "make a", "make an", "make",
                    "put a", "put an", "put", "place a", "place an", "place",
                    "spawn a", "spawn an", "spawn"))
            {
                return false;
            }

            objectText = Regex.Replace(objectText, @"\b(at|on|in|to)\s+(the\s+)?(world\s+)?origin\b", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(objectText))
                return false;

            command.intent = VoiceIntentType.PlaceObject;
            command.object_name = objectText;
            command.spatial_reference = SpatialReferenceMode.WorldOrigin;
            command.spoken_response = $"Creating {objectText}.";
            return true;
        }

        static bool TryParseMaterialCommand(string original, string lower, VoiceIntentCommand command)
        {
            if (!StartsWithAny(lower, "make ", "turn ", "set ", "color "))
                return false;

            if (!RuntimeMaterialCatalog.TryParseDescriptor(original, RuntimeMaterialDescriptor.Default, out _))
                return false;

            if (!TrySplitMaterialCommand(original, out string target, out string materialPrompt))
                return false;

            TargetReferenceMode targetReference = ResolveMaterialTargetReference(target);
            string targetMaterialPrompt = "";
            ExtractMaterialQualifierFromTarget(ref target, ref targetMaterialPrompt);

            command.intent = VoiceIntentType.SetTargetMaterial;
            command.material_prompt = materialPrompt;
            command.target_material_prompt = targetMaterialPrompt;
            command.target_name = target;
            command.object_name = target;
            command.target_reference = targetReference;
            command.spoken_response = "Updating material.";
            return true;
        }

        static bool TrySplitMaterialCommand(string original, out string target, out string materialPrompt)
        {
            target = "";
            materialPrompt = "";

            Match match = Regex.Match(
                original,
                @"^(make|turn|set|color)\s+(?<target>.+?)\s+(?<material>red|blue|green|yellow|orange|purple|pink|black|white|gray|grey|silver|gold|chrome|metallic|metal|matte|glossy|shiny|polished)(?<finish>\s+(metallic|metal|matte|glossy|shiny|polished))?\s*$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            target = CleanMaterialTarget(match.Groups["target"].Value);
            materialPrompt = (match.Groups["material"].Value + match.Groups["finish"].Value).Trim();
            return RuntimeMaterialCatalog.TryParseDescriptor(materialPrompt, RuntimeMaterialDescriptor.Default, out _);
        }

        static string CleanMaterialTarget(string target)
        {
            string cleaned = (target ?? "").Trim();
            cleaned = Regex.Replace(cleaned, @"^(it|this|that)$", "", RegexOptions.IgnoreCase).Trim();
            cleaned = Regex.Replace(cleaned, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase).Trim();
            return cleaned;
        }

        static void ExtractMaterialQualifierFromTarget(ref string target, ref string targetMaterialPrompt)
        {
            if (string.IsNullOrWhiteSpace(target) || !string.IsNullOrWhiteSpace(targetMaterialPrompt))
                return;

            string qualifierTarget = target;
            string qualifier = "";
            SceneEntityResolver.ExtractMaterialQualifierForCommand(ref qualifierTarget, ref qualifier);
            if (string.IsNullOrWhiteSpace(qualifier))
                return;

            target = qualifierTarget;
            targetMaterialPrompt = qualifier;
        }

        static TargetReferenceMode ResolveMaterialTargetReference(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return TargetReferenceMode.LastCreatedOrInteracted;

            string lower = target.ToLowerInvariant();
            if (ContainsAny(lower, "this", "that"))
                return TargetReferenceMode.PointedObject;
            if (ContainsAny(lower, "all", "every"))
                return TargetReferenceMode.All;
            return TargetReferenceMode.NamedObject;
        }

        static bool StartsWithAny(string value, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static bool TryParseDelete(string original, string lower, VoiceIntentCommand command)
        {
            string target = "";
            if (TryStripPrefix(original, lower, out target, "delete", "remove", "destroy"))
            {
                target = CleanDeleteTarget(target);
            }
            else
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(target))
                return false;

            bool all = ContainsAny(target.ToLowerInvariant(), "all ", "every ") ||
                       string.Equals(target, "all", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(target, "everything", StringComparison.OrdinalIgnoreCase);
            bool indicated = ContainsAny(target.ToLowerInvariant(), "this", "that");
            string cleanedTarget = StripAllQualifier(target);
            cleanedTarget = StripIndicatedQualifier(cleanedTarget);

            command.intent = VoiceIntentType.DeleteTarget;
            command.target_reference = indicated
                ? TargetReferenceMode.PointedObject
                : (all ? TargetReferenceMode.All : TargetReferenceMode.NamedObject);
            command.target_name = cleanedTarget;
            command.object_name = cleanedTarget;
            command.sound_prompt = IsAudioDeleteTarget(cleanedTarget) ? cleanedTarget : "";
            command.spoken_response = all
                ? $"Deleting all {cleanedTarget}."
                : $"Deleting {cleanedTarget}.";
            return true;
        }

        static bool TryParseBodyAnchorPlacement(string original, string lower, VoiceIntentCommand command)
        {
            if (!TryInferBodyAnchor(lower, out BodyAnchor anchor, out HandSelection hand))
                return false;

            if (!TryStripPrefix(original, lower, out string objectText,
                    "create a", "create an", "create", "make a", "make an", "make",
                    "put a", "put an", "put", "place a", "place an", "place",
                    "spawn a", "spawn an", "spawn"))
            {
                return false;
            }

            objectText = StripBodyAnchorPhrase(objectText);
            if (string.IsNullOrWhiteSpace(objectText))
                return false;

            command.intent = VoiceIntentType.PlaceObject;
            command.object_name = objectText;
            command.spatial_reference = SpatialReferenceMode.BodyAnchor;
            command.body_anchor = anchor;
            command.target_hand = hand;
            command.spoken_response = $"Creating {objectText}.";
            return true;
        }

        static bool TryParseBodyAnchorMove(string original, string lower, VoiceIntentCommand command)
        {
            if (!lower.StartsWith("move ", StringComparison.Ordinal))
                return false;

            if (!TryInferBodyAnchor(lower, out BodyAnchor anchor, out HandSelection hand))
                return false;

            string targetText = original.Substring("move ".Length).Trim();
            targetText = StripBodyAnchorPhrase(targetText);
            if (targetText.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                targetText = targetText.Substring("the ".Length).Trim();

            if (string.IsNullOrWhiteSpace(targetText))
                return false;

            command.intent = VoiceIntentType.MoveTarget;
            command.target_reference = TargetReferenceMode.NamedObject;
            command.target_name = targetText;
            command.object_name = targetText;
            command.spatial_reference = SpatialReferenceMode.BodyAnchor;
            command.body_anchor = anchor;
            command.target_hand = hand;
            command.spoken_response = "Moving.";
            return true;
        }

        static bool TryParseRelativeMove(string original, string lower, VoiceIntentCommand command)
        {
            if (!lower.StartsWith("move ", StringComparison.Ordinal))
                return false;

            bool relativeToMe = ContainsAny(lower,
                "in front of me", "behind me", "to my left", "to my right",
                "my left", "my right", "forward", "backward", "backwards",
                "above me", "below me", " up", " down");
            if (!relativeToMe)
                return false;

            string targetText = ExtractMoveTarget(original);
            RelativeDirection direction = InferRelativeDirection(lower);
            float distance = ExtractDistanceMeters(lower);

            command.intent = VoiceIntentType.MoveTarget;
            command.spatial_reference = SpatialReferenceMode.RelativeToMe;
            command.relative_direction = direction;
            command.relative_distance_meters = distance;
            command.spoken_response = "Moving.";

            if (IsMeTarget(targetText))
            {
                command.target_entity = "Me";
                command.target_name = "Me";
                command.target_reference = TargetReferenceMode.NamedObject;
            }
            else
            {
                command.target_name = targetText;
                command.object_name = targetText;
                command.target_reference = TargetReferenceMode.NamedObject;
            }

            return true;
        }

        static string ExtractMoveTarget(string original)
        {
            string value = original.Trim();
            if (value.StartsWith("move ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("move ".Length).Trim();

            string[] markers =
            {
                " in front of me",
                " behind me",
                " to my left",
                " to my right",
                " forward",
                " backward",
                " backwards",
                " above me",
                " below me",
                " up",
                " down",
                " left",
                " right"
            };

            foreach (string marker in markers)
            {
                int index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    value = value.Substring(0, index).Trim();
                    break;
                }
            }

            value = Regex.Replace(value, @"\b\d+(\.\d+)?\s*(meters?|metres?|m|feet|foot|ft)\b", "", RegexOptions.IgnoreCase).Trim();
            if (value.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("the ".Length).Trim();
            return string.IsNullOrWhiteSpace(value) ? "Me" : value;
        }

        static RelativeDirection InferRelativeDirection(string lower)
        {
            if (lower.Contains("in front of me", StringComparison.Ordinal) || lower.Contains("forward", StringComparison.Ordinal))
                return RelativeDirection.Forward;
            if (lower.Contains("behind me", StringComparison.Ordinal) ||
                lower.Contains("backward", StringComparison.Ordinal) ||
                lower.Contains("backwards", StringComparison.Ordinal))
                return RelativeDirection.Back;
            if (lower.Contains("left", StringComparison.Ordinal))
                return RelativeDirection.Left;
            if (lower.Contains("right", StringComparison.Ordinal))
                return RelativeDirection.Right;
            if (lower.Contains("above me", StringComparison.Ordinal) || lower.Contains(" up", StringComparison.Ordinal))
                return RelativeDirection.Up;
            if (lower.Contains("below me", StringComparison.Ordinal) || lower.Contains(" down", StringComparison.Ordinal))
                return RelativeDirection.Down;
            return RelativeDirection.Forward;
        }

        static float ExtractDistanceMeters(string lower)
        {
            Match match = Regex.Match(lower, @"(?<value>\d+(\.\d+)?)\s*(?<unit>meters?|metres?|m|feet|foot|ft)\b", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0f;

            if (!float.TryParse(match.Groups["value"].Value, out float value))
                return 0f;

            string unit = match.Groups["unit"].Value.ToLowerInvariant();
            if (unit == "feet" || unit == "foot" || unit == "ft")
                return value * 0.3048f;
            return value;
        }

        static bool IsMeTarget(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "me" || normalized == "myself" || normalized == "player";
        }

        static string CleanDeleteTarget(string target)
        {
            string result = (target ?? string.Empty).Trim();
            if (result.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                result = result.Substring("the ".Length).Trim();
            return result;
        }

        static string StripAllQualifier(string target)
        {
            string result = CleanDeleteTarget(target);
            string[] prefixes = { "all of the ", "all the ", "all ", "every " };
            foreach (string prefix in prefixes)
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return result.Substring(prefix.Length).Trim();
            }
            return result;
        }

        static string StripIndicatedQualifier(string target)
        {
            string result = CleanDeleteTarget(target);
            string[] prefixes = { "this ", "that " };
            foreach (string prefix in prefixes)
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return result.Substring(prefix.Length).Trim();
            }
            return result;
        }

        static bool IsAudioDeleteTarget(string target)
        {
            string normalized = (target ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "audio" ||
                   normalized == "audios" ||
                   normalized == "sound" ||
                   normalized == "sounds";
        }

        static bool TryInferBodyAnchor(string lower, out BodyAnchor anchor, out HandSelection hand)
        {
            anchor = BodyAnchor.None;
            hand = HandSelection.None;

            if (ContainsAny(lower, "my right hand", "right hand", "right controller"))
            {
                anchor = BodyAnchor.RightHand;
                hand = HandSelection.Right;
                return true;
            }

            if (ContainsAny(lower, "my left hand", "left hand", "left controller"))
            {
                anchor = BodyAnchor.LeftHand;
                hand = HandSelection.Left;
                return true;
            }

            if (ContainsAny(lower, "my head", "on my head", "at my head"))
            {
                anchor = BodyAnchor.Head;
                return true;
            }

            return false;
        }

        static string StripBodyAnchorPhrase(string value)
        {
            string result = value ?? string.Empty;
            string[] markers =
            {
                " in my right hand",
                " in the right hand",
                " in right hand",
                " in my right controller",
                " in the right controller",
                " in right controller",
                " to my right hand",
                " to the right hand",
                " to right hand",
                " to my right controller",
                " to the right controller",
                " to right controller",
                " in my left hand",
                " in the left hand",
                " in left hand",
                " in my left controller",
                " in the left controller",
                " in left controller",
                " to my left hand",
                " to the left hand",
                " to left hand",
                " to my left controller",
                " to the left controller",
                " to left controller",
                " at my right hand",
                " at my left hand",
                " on my right hand",
                " on my left hand",
                " at my head",
                " on my head",
                " near my head"
            };

            foreach (string marker in markers)
            {
                int index = result.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    result = result.Remove(index, marker.Length).Trim();
            }

            if (result.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                result = result.Substring("the ".Length).Trim();

            return result;
        }

        static bool TryParseAudioControl(string original, string lower, VoiceIntentCommand command)
        {
            string working = lower;
            if (working.StartsWith("please ", StringComparison.Ordinal))
                working = working.Substring("please ".Length);

            bool allSounds = ContainsAny(working,
                "all sound", "all sounds", "every sound", "every sounds", "all audio", "every audio");

            string control = "";
            if (working.StartsWith("stop ", StringComparison.Ordinal))
                control = "stop";
            else if (working.StartsWith("mute ", StringComparison.Ordinal))
                control = "mute";
            else if (working.StartsWith("unmute ", StringComparison.Ordinal))
                control = "unmute";
            else if (working.StartsWith("play ", StringComparison.Ordinal))
                control = "play_now";
            else if (ContainsAny(working, "make louder", "make it louder", "turn up", "increase volume") ||
                     (allSounds && working.Contains("louder", StringComparison.Ordinal)))
                control = "louder";
            else if (ContainsAny(working, "make softer", "make quieter", "make it quieter", "turn down", "decrease volume") ||
                     (allSounds && (working.Contains("softer", StringComparison.Ordinal) ||
                                    working.Contains("quieter", StringComparison.Ordinal))))
                control = "softer";

            if (string.IsNullOrWhiteSpace(control))
                return false;

            if (!allSounds && !ContainsAny(working, "sound", "sounds", "audio"))
                return false;

            command.intent = VoiceIntentType.ControlAudioSource;
            command.audio_control = control;
            command.audio_play_now = control == "play_now";
            command.audio_muted = control == "mute";

            if (allSounds)
            {
                command.target_reference = TargetReferenceMode.All;
                command.target_name = "all";
                command.sound_prompt = "";
                command.spoken_response = control == "stop"
                    ? "Stopping all sounds."
                    : "Updating all sounds.";
                return true;
            }

            string target = ExtractAudioControlTarget(original, control);
            command.target_reference = string.IsNullOrWhiteSpace(target)
                ? TargetReferenceMode.LastCreatedOrInteracted
                : TargetReferenceMode.NamedObject;
            command.target_name = target;
            command.sound_prompt = target;
            return true;
        }

        static string ExtractAudioControlTarget(string original, string control)
        {
            string value = original.Trim();
            string lower = value.ToLowerInvariant();
            string[] prefixes = control switch
            {
                "stop" => new[] { "stop" },
                "mute" => new[] { "mute" },
                "unmute" => new[] { "unmute" },
                "play_now" => new[] { "play" },
                _ => Array.Empty<string>()
            };

            foreach (string prefix in prefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                    value = value.Substring(prefix.Length).TrimStart(' ', ':', '-', ',', '.');
            }

            value = value.Replace("sounds", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("sound", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("audio", "", StringComparison.OrdinalIgnoreCase)
                         .Trim();
            return value;
        }

        static bool TryParseContentPath(string text, VoiceIntentCommand command)
        {
            string lower = text.ToLowerInvariant();
            bool looksLikePath = lower.StartsWith("http://", StringComparison.Ordinal) ||
                                 lower.StartsWith("https://", StringComparison.Ordinal) ||
                                 lower.StartsWith("file://", StringComparison.Ordinal) ||
                                 lower.StartsWith("/", StringComparison.Ordinal);
            if (!looksLikePath)
                return false;

            command.content_path = text;
            if (lower.EndsWith(".spz", StringComparison.Ordinal) || lower.EndsWith(".ply", StringComparison.Ordinal))
            {
                command.intent = VoiceIntentType.LoadSplat;
                command.spoken_response = "Loading splat.";
                return true;
            }

            if (lower.EndsWith(".jpg", StringComparison.Ordinal) ||
                lower.EndsWith(".jpeg", StringComparison.Ordinal) ||
                lower.EndsWith(".png", StringComparison.Ordinal))
            {
                command.intent = VoiceIntentType.LoadPanorama;
                command.spoken_response = "Loading panorama.";
                return true;
            }

            return false;
        }

        static VoiceIntentCommand Unknown(string transcript)
        {
            return new VoiceIntentCommand
            {
                transcript = transcript,
                intent = VoiceIntentType.Unknown,
                confidence = 0f,
                should_execute = false
            };
        }

        static bool IsApproval(string lower)
        {
            return lower == "ok" ||
                   lower == "okay" ||
                   lower == "shoot" ||
                   lower == "capture" ||
                   lower == "capture now" ||
                   lower == "take it";
        }

        static bool TryStripPrefix(string original, string lower, out string remainder, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (!lower.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                remainder = original.Substring(prefix.Length).TrimStart(' ', ':', '-', ',', '.');
                return true;
            }

            remainder = string.Empty;
            return false;
        }

        static bool ContainsAny(string lower, params string[] values)
        {
            foreach (string value in values)
                if (lower.Contains(value, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}

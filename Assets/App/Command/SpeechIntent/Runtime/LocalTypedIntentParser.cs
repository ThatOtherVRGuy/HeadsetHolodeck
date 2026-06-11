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

            if (ContainsAny(lower, "save spawn point", "save this spawn point", "save current spawn point", "remember spawn point", "remember this spawn point"))
            {
                command.intent = VoiceIntentType.SaveSpawnPoint;
                command.spoken_response = "Saving spawn point.";
                return command;
            }

            if (ContainsAny(lower, "next spawn point", "go to next spawn point", "use next spawn point", "next saved spawn"))
            {
                command.intent = VoiceIntentType.NextSpawnPoint;
                command.spoken_response = "Going to next spawn point.";
                return command;
            }

            if (ContainsAny(lower, "previous spawn point", "prev spawn point", "go to previous spawn point", "use previous spawn point", "previous saved spawn", "last spawn point"))
            {
                command.intent = VoiceIntentType.PreviousSpawnPoint;
                command.spoken_response = "Going to previous spawn point.";
                return command;
            }

            if (ContainsAny(lower, "suggest spawn point", "suggest a spawn point", "find spawn point", "find a spawn point", "estimate spawn point", "estimate a spawn point"))
            {
                command.intent = VoiceIntentType.SuggestSpawnPoint;
                command.spoken_response = "Suggesting a spawn point.";
                return command;
            }

            if (ContainsAny(lower, "delete all spawn points", "remove all spawn points", "clear all spawn points", "forget all spawn points", "clear spawn points"))
            {
                command.intent = VoiceIntentType.RemoveAllSpawnPoints;
                command.spoken_response = "Removing all spawn points.";
                return command;
            }

            if (ContainsAny(lower, "remove spawn point", "delete spawn point", "forget spawn point", "remove this spawn point", "delete this spawn point", "forget this spawn point", "remove current spawn point", "delete current spawn point"))
            {
                command.intent = VoiceIntentType.RemoveSpawnPoint;
                command.spoken_response = "Removing spawn point.";
                return command;
            }

            if (TryParseGenerationControl(transcript, lower, command))
                return command;

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

            if (TryParseCreateLight(transcript, lower, command))
                return command;

            if (TryParseCreateTeleportPad(transcript, lower, command))
                return command;

            if (TryParseCreateSound(transcript, lower, command))
                return command;

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

            if (TryParseProxyVisibility(transcript, lower, command))
                return command;

            if (TryParseBehaviorCommand(transcript, lower, command))
                return command;

            if (TryParseRotateTarget(transcript, lower, command))
                return command;

            if (TryParseModifyLight(transcript, lower, command))
                return command;

            if (TryParseDelete(transcript, lower, command))
                return command;

            if (TryParseMaterialCommand(transcript, lower, command))
                return command;

            if (TryParsePhysicsCommand(transcript, lower, command))
                return command;

            if (TryParseRelativePlacement(transcript, lower, command))
                return command;

            if (TryParseWorldOriginPlacement(transcript, lower, command))
                return command;

            if (TryParseBodyAnchorPlacement(transcript, lower, command))
                return command;

            if (TryParseGenericObjectCreation(transcript, lower, command))
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

        public static bool TryParseCachedObjectChoiceReply(string text, out VoiceIntentCommand command)
        {
            command = null;
            string lower = NormalizeShortReply(text);
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            var parsed = new VoiceIntentCommand
            {
                transcript = text ?? "",
                confidence = 0.75f,
                should_execute = true,
                spoken_response = ""
            };

            if (EqualsAny(lower, "use saved", "use the saved one", "use that one", "use it", "use saved one", "saved one"))
            {
                parsed.intent = VoiceIntentType.SelectCachedObject;
                parsed.object_choice_action = "use_saved";
                parsed.spoken_response = "Using saved object.";
                command = parsed;
                return true;
            }

            if (EqualsAny(lower, "create new", "create a new one", "make new", "make a new one", "generate new", "generate a new one", "new one"))
            {
                parsed.intent = VoiceIntentType.SelectCachedObject;
                parsed.object_choice_action = "create_new";
                parsed.spoken_response = "Creating new object.";
                command = parsed;
                return true;
            }

            if (EqualsAny(lower, "cancel", "never mind", "nevermind"))
            {
                parsed.intent = VoiceIntentType.SelectCachedObject;
                parsed.object_choice_action = "cancel";
                parsed.spoken_response = "Cancelled.";
                command = parsed;
                return true;
            }

            return false;
        }

        public static bool TryParseGenerationControlReply(string text, out VoiceIntentCommand command)
        {
            command = null;
            string transcript = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(transcript))
                return false;

            var parsed = new VoiceIntentCommand
            {
                transcript = transcript,
                confidence = 0.82f,
                should_execute = true,
                spoken_response = ""
            };

            if (!TryParseGenerationControl(transcript, transcript.ToLowerInvariant(), parsed))
                return false;

            command = parsed;
            return true;
        }

        static string NormalizeShortReply(string text)
        {
            string normalized = (text ?? "").Trim().ToLowerInvariant();
            while (normalized.Length > 0 && (char.IsPunctuation(normalized[normalized.Length - 1]) || char.IsWhiteSpace(normalized[normalized.Length - 1])))
                normalized = normalized.Substring(0, normalized.Length - 1).TrimEnd();
            return normalized;
        }

        static bool EqualsAny(string value, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(value, candidate, StringComparison.Ordinal))
                    return true;
            }

            return false;
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

        static bool TryParseGenerationControl(string original, string lower, VoiceIntentCommand command)
        {
            if (ContainsAny(lower,
                    "cancel object generation", "cancel model generation", "cancel 3d generation",
                    "cancel object creation", "cancel model creation", "stop object generation",
                    "stop model generation", "stop 3d generation", "stop object creation"))
            {
                command.intent = VoiceIntentType.CancelGeneration;
                command.target_entity = "object";
                command.spoken_response = "Cancelling object generation.";
                return true;
            }

            if (ContainsAny(lower,
                    "cancel world generation", "cancel world creation", "cancel worldlabs",
                    "cancel world labs", "stop world generation", "stop world creation",
                    "stop worldlabs", "stop world labs"))
            {
                command.intent = VoiceIntentType.CancelGeneration;
                command.target_entity = "world";
                command.spoken_response = "Cancelling world generation.";
                return true;
            }

            if (ContainsAny(lower,
                    "cancel generation", "cancel request", "cancel the request",
                    "stop generation", "stop generating", "stop the request"))
            {
                command.intent = VoiceIntentType.CancelGeneration;
                command.target_entity = "all";
                command.spoken_response = "Cancelling generation.";
                return true;
            }

            if (ContainsAny(lower,
                    "continue waiting for object", "keep waiting for object",
                    "continue object generation", "keep object generation going",
                    "continue model generation", "keep waiting for model"))
            {
                command.intent = VoiceIntentType.ContinueGeneration;
                command.target_entity = "object";
                command.spoken_response = "Continuing object generation.";
                return true;
            }

            if (ContainsAny(lower,
                    "continue waiting for world", "keep waiting for world",
                    "continue world generation", "keep world generation going",
                    "continue worldlabs", "continue world labs"))
            {
                command.intent = VoiceIntentType.ContinueGeneration;
                command.target_entity = "world";
                command.spoken_response = "Continuing world generation.";
                return true;
            }

            if (ContainsAny(lower,
                    "continue waiting", "keep waiting", "wait longer",
                    "keep going", "continue generation", "continue generating"))
            {
                command.intent = VoiceIntentType.ContinueGeneration;
                command.target_entity = "all";
                command.spoken_response = "Continuing generation.";
                return true;
            }

            return false;
        }

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
            if (!StartsWithAny(lower, "stop ", "remove ", "clear ", "disable "))
                return false;

            bool mentionsBehavior = ContainsAny(lower, "behavior", "behaviour", "spin", "spinning", "orbit", "orbiting", "follow", "following", "throw", "attach");
            if (!mentionsBehavior)
                return false;

            string target = "";
            if (ContainsAny(lower, "all behavior", "all behaviors", "all behaviour", "all behaviours", "every behavior", "every behaviour"))
            {
                command.behavior_stop_all = true;
                command.target_reference = TargetReferenceMode.All;
                target = "all";
            }
            else
            {
                target = original.Trim();
                target = Regex.Replace(target, @"^(stop|remove|clear|disable)\s+", "", RegexOptions.IgnoreCase).Trim();
                Match fromTarget = Regex.Match(target, @"\bfrom\s+(?<target>.+?)\s*$", RegexOptions.IgnoreCase);
                if (fromTarget.Success)
                    target = fromTarget.Groups["target"].Value.Trim();
                else
                    target = Regex.Replace(target, @"\b(follow|following)\s+(my\s+|the\s+)?(left\s+|right\s+)?(hand|controller)\b", "", RegexOptions.IgnoreCase).Trim();
                target = Regex.Replace(target, @"\b(spin|spinning|orbit|orbiting|follow|following|throw|attach|behavior|behaviors|behaviour|behaviours)\b", "", RegexOptions.IgnoreCase).Trim();
                target = CleanBehaviorTarget(target);
                command.target_reference = ResolveBehaviorTargetReference(target);
            }

            command.intent = VoiceIntentType.StopBehavior;
            command.behavior_action = "stop";
            command.behavior_name = InferBehaviorName(lower);
            command.target_name = target == "all" ? "" : target;
            command.object_name = command.target_name;
            command.spoken_response = command.behavior_stop_all ? "Stopping behaviors." : "Stopping behavior.";
            return true;
        }

        static bool TryParseSpinBehavior(string original, string lower, VoiceIntentCommand command)
        {
            Match match = Regex.Match(
                original,
                @"^(make\s+)?(?<target>.+?)\s+(spin|spinning|rotate|rotating)\s*$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string target = CleanBehaviorTarget(match.Groups["target"].Value);
            if (string.IsNullOrWhiteSpace(target))
                return false;

            SetAttachBehavior(command, "spin", target);
            command.spoken_response = "Adding spin.";
            return true;
        }

        static bool TryParseOrbitBehavior(string original, string lower, VoiceIntentCommand command)
        {
            Match match = Regex.Match(
                original,
                @"^(make\s+)?(?<target>.+?)\s+orbit\s+(around\s+)?(?<secondary>.+?)\s*$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string target = CleanBehaviorTarget(match.Groups["target"].Value);
            string secondary = CleanBehaviorTarget(match.Groups["secondary"].Value);
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(secondary))
                return false;

            SetAttachBehavior(command, "orbit", target);
            command.behavior_secondary_target_name = secondary;
            command.spoken_response = "Adding orbit.";
            return true;
        }

        static bool TryParseThrowBehavior(string original, string lower, VoiceIntentCommand command)
        {
            Match match = Regex.Match(
                original,
                @"^(throw|toss|launch)\s+(?<target>.+?)(\s+(at|to|toward|towards)\s+(?<secondary>.+?))?\s*$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string target = CleanBehaviorTarget(match.Groups["target"].Value);
            if (string.IsNullOrWhiteSpace(target))
                return false;

            SetAttachBehavior(command, "throw", target);
            if (match.Groups["secondary"].Success)
                command.behavior_secondary_target_name = CleanBehaviorTarget(match.Groups["secondary"].Value);
            command.spoken_response = "Throwing.";
            return true;
        }

        static bool TryParseFollowHandBehavior(string original, string lower, VoiceIntentCommand command)
        {
            if (!ContainsAny(lower, "follow my left hand", "follow the left hand", "follow left hand",
                    "follow my right hand", "follow the right hand", "follow right hand",
                    "follow my left controller", "follow the left controller", "follow left controller",
                    "follow my right controller", "follow the right controller", "follow right controller"))
            {
                return false;
            }

            Match match = Regex.Match(
                original,
                @"^(make\s+)?(?<target>.+?)\s+follow\s+",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            if (!TryInferBodyAnchor(lower, out BodyAnchor anchor, out HandSelection hand) || hand == HandSelection.None)
                return false;

            string target = CleanBehaviorTarget(match.Groups["target"].Value);
            if (string.IsNullOrWhiteSpace(target))
                return false;

            SetAttachBehavior(command, "follow_hand", target);
            command.spatial_reference = SpatialReferenceMode.BodyAnchor;
            command.body_anchor = anchor;
            command.target_hand = hand;
            command.spoken_response = "Adding follow behavior.";
            return true;
        }

        static bool TryParseAttachToHandBehavior(string original, string lower, VoiceIntentCommand command)
        {
            string target = "";
            HandSelection hand = HandSelection.Either;

            Match give = Regex.Match(
                original,
                @"^(give|hand|pass)\s+(me\s+)?(the\s+|a\s+|an\s+)?(?<target>.+?)\s*$",
                RegexOptions.IgnoreCase);
            if (give.Success)
            {
                target = CleanBehaviorTarget(give.Groups["target"].Value);
            }
            else
            {
                Match attach = Regex.Match(
                    original,
                    @"^(attach|stick|snap)\s+(the\s+|a\s+|an\s+)?(?<target>.+?)\s+(to|onto)\s+(my\s+)?(?<hand>left|right)?\s*(hand|controller)\s*$",
                    RegexOptions.IgnoreCase);
                if (!attach.Success)
                    return false;

                target = CleanBehaviorTarget(attach.Groups["target"].Value);
                hand = ParseOptionalBehaviorHand(attach.Groups["hand"].Value);
            }

            if (string.IsNullOrWhiteSpace(target))
                return false;

            SetAttachBehavior(command, "attach_to_hand", target);
            command.target_hand = hand;
            command.spatial_reference = SpatialReferenceMode.BodyAnchor;
            command.body_anchor = BodyAnchorResolver.FromHandSelection(hand);
            command.spoken_response = "Attaching to hand.";
            return true;
        }

        static void SetAttachBehavior(VoiceIntentCommand command, string behaviorName, string target)
        {
            command.intent = VoiceIntentType.AttachBehavior;
            command.behavior_name = behaviorName;
            command.behavior_action = "start";
            command.target_reference = ResolveBehaviorTargetReference(target);
            command.target_name = IsPointedBehaviorTarget(target) ? "" : target;
            command.object_name = command.target_name;
        }

        static string InferBehaviorName(string lower)
        {
            if (ContainsAny(lower, "spin", "spinning"))
                return "spin";
            if (ContainsAny(lower, "orbit", "orbiting"))
                return "orbit";
            if (ContainsAny(lower, "follow", "following"))
                return "follow_hand";
            if (ContainsAny(lower, "throw"))
                return "throw";
            if (ContainsAny(lower, "attach"))
                return "attach_to_hand";
            return "";
        }

        static HandSelection ParseOptionalBehaviorHand(string value)
        {
            string lower = (value ?? "").Trim().ToLowerInvariant();
            if (lower == "left")
                return HandSelection.Left;
            if (lower == "right")
                return HandSelection.Right;
            return HandSelection.Either;
        }

        static TargetReferenceMode ResolveBehaviorTargetReference(string target)
        {
            string lower = (target ?? "").Trim().ToLowerInvariant();
            if (IsPointedBehaviorTarget(lower))
                return TargetReferenceMode.PointedObject;
            if (lower == "all" || lower == "everything")
                return TargetReferenceMode.All;
            if (string.IsNullOrWhiteSpace(lower) || lower == "it")
                return TargetReferenceMode.LastCreatedOrInteracted;
            return TargetReferenceMode.NamedObject;
        }

        static bool IsPointedBehaviorTarget(string target)
        {
            string lower = (target ?? "").Trim().ToLowerInvariant();
            return lower == "this" || lower == "that" || lower == "this one" || lower == "that one";
        }

        static string CleanBehaviorTarget(string target)
        {
            string result = (target ?? "").Trim();
            result = Regex.Replace(result, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase).Trim();
            return result;
        }

        static bool TryParseRelativePlacement(string original, string lower, VoiceIntentCommand command)
        {
            if (!ContainsAny(lower,
                    "in front of me",
                    "behind me",
                    "to my left",
                    "to my right",
                    "my left",
                    "my right",
                    "above me",
                    "below me"))
            {
                return false;
            }

            if (!TryStripPrefix(original, lower, out string objectText,
                    "create a", "create an", "create", "make a", "make an", "make",
                    "put a", "put an", "put", "place a", "place an", "place",
                    "spawn a", "spawn an", "spawn"))
            {
                return false;
            }

            objectText = StripRelativePlacementPhrase(objectText);
            objectText = DistanceUnitParser.RemoveDistances(objectText).Trim();
            if (string.IsNullOrWhiteSpace(objectText))
                return false;

            command.intent = VoiceIntentType.PlaceObject;
            command.object_name = objectText;
            command.spatial_reference = SpatialReferenceMode.RelativeToMe;
            command.relative_direction = InferRelativeDirection(lower);
            command.relative_distance_meters = ExtractDistanceMeters(lower);
            command.spoken_response = $"Creating {objectText}.";
            return true;
        }

        static bool TryParseCreateSound(string original, string lower, VoiceIntentCommand command)
        {
            if (!StartsWithAny(lower, "make ", "create ", "add ", "place ", "put ", "spawn "))
                return false;

            if (!ContainsAny(lower, "sound", "sounds", "audio", "ambience", "ambiance"))
                return false;

            if (TryParseAudioControl(original, lower, command))
                return true;

            string prompt = original.Trim();
            prompt = Regex.Replace(prompt, @"^(make|create|add|place|put|spawn)\s+", "", RegexOptions.IgnoreCase).Trim();
            bool quiet = Regex.IsMatch(prompt, @"\b(quiet|soft|softly)\b", RegexOptions.IgnoreCase);
            bool loud = Regex.IsMatch(prompt, @"\b(loud|louder)\b", RegexOptions.IgnoreCase);
            prompt = Regex.Replace(prompt, @"\b(quiet|soft|softly|loud|louder)\b", "", RegexOptions.IgnoreCase).Trim();
            prompt = Regex.Replace(prompt, @"\b(sound|sounds|audio|ambience|ambiance)\b", "", RegexOptions.IgnoreCase).Trim();
            prompt = Regex.Replace(prompt, @"\s+", " ", RegexOptions.IgnoreCase).Trim();
            prompt = Regex.Replace(prompt, @"^(a|an|the)\s+", "", RegexOptions.IgnoreCase).Trim();
            prompt = Regex.Replace(prompt, @"^(of|for|with)\s+", "", RegexOptions.IgnoreCase).Trim();
            prompt = Regex.Replace(prompt, @"^(a|an|the)\s+", "", RegexOptions.IgnoreCase).Trim();

            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            command.intent = VoiceIntentType.CreateAudioSource;
            command.sound_prompt = prompt;
            command.sound_count = 1;
            command.sound_provider = "auto";
            command.audio_playback_mode = "auto";
            command.audio_volume = quiet ? 0.25f : (loud ? 1f : command.audio_volume);
            command.spoken_response = "Adding sound.";
            return true;
        }

        static bool TryParseGenericObjectCreation(string original, string lower, VoiceIntentCommand command)
        {
            if (!TryStripPrefix(original, lower, out string objectText,
                    "create a", "create an", "create", "make a", "make an", "make",
                    "put a", "put an", "put", "place a", "place an", "place",
                    "spawn a", "spawn an", "spawn"))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(objectText))
                return false;

            string objectLower = objectText.ToLowerInvariant();
            if (ContainsAny(objectLower, "world", "image", "camera", "thumbnail", "panorama", "pano"))
                return false;

            if (StartsWithAny(objectLower, "it ", "this ", "that ") &&
                ContainsAny(objectLower, "bigger", "larger", "smaller", "scale", "rotate", "move"))
            {
                return false;
            }

            ConfigureCreatedObjectAttributes(objectText, command, out string cleanedObjectText);
            if (TryExtractTargetRelativePlacement(cleanedObjectText, command, out string objectName))
            {
                command.intent = VoiceIntentType.PlaceObject;
                command.object_name = objectName;
                command.spoken_response = $"Creating {objectName}.";
                return !string.IsNullOrWhiteSpace(command.object_name);
            }

            cleanedObjectText = DistanceUnitParser.RemoveDistances(cleanedObjectText).Trim();
            cleanedObjectText = Regex.Replace(cleanedObjectText, @"\b(wide|width|tall|high|height)\b", "", RegexOptions.IgnoreCase).Trim();
            cleanedObjectText = CleanCreatedObjectName(cleanedObjectText);
            if (string.IsNullOrWhiteSpace(cleanedObjectText))
                return false;

            command.intent = VoiceIntentType.PlaceObject;
            command.object_name = cleanedObjectText;
            command.spatial_reference = SpatialReferenceMode.RelativeToMe;
            command.relative_direction = RelativeDirection.InFront;
            command.relative_distance_meters = 1f;
            command.placement_mode = "me_frame";
            command.spoken_response = $"Creating {cleanedObjectText}.";
            return true;
        }

        static void ConfigureCreatedObjectAttributes(string objectText, VoiceIntentCommand command, out string cleanedObjectText)
        {
            cleanedObjectText = objectText ?? "";

            if (DistanceUnitParser.TryExtractMeters(cleanedObjectText, out float meters) &&
                Regex.IsMatch(cleanedObjectText, @"\b(wide|width)\b", RegexOptions.IgnoreCase))
            {
                command.object_width_meters = meters;
            }

            if (Regex.IsMatch(cleanedObjectText, @"\b(weightless|zero\s+gravity|no\s+gravity|gravity\s+off)\b", RegexOptions.IgnoreCase))
            {
                command.object_weightless = true;
                cleanedObjectText = Regex.Replace(cleanedObjectText, @"\b(weightless|zero\s+gravity|no\s+gravity|gravity\s+off)\b", "", RegexOptions.IgnoreCase).Trim();
            }

            if (TryExtractLeadingMaterialPrompt(ref cleanedObjectText, out string materialPrompt))
            {
                command.material_prompt = materialPrompt;
            }
        }

        static bool TryExtractTargetRelativePlacement(string text, VoiceIntentCommand command, out string objectName)
        {
            objectName = "";
            Match possessive = Regex.Match(
                text,
                @"^(?<object>.+?)\s+(to|at|on)?\s*(the\s+)?(?<target>.+?)'s\s+(?<direction>left|right|front|back|behind|above|below|top|bottom)\s*$",
                RegexOptions.IgnoreCase);

            if (possessive.Success)
            {
                objectName = CleanCreatedObjectName(possessive.Groups["object"].Value);
                command.spatial_reference = SpatialReferenceMode.RelativeToTarget;
                command.target_reference = TargetReferenceMode.NamedObject;
                command.target_name = CleanCreatedObjectName(possessive.Groups["target"].Value);
                command.relative_direction = ParseTargetRelativeDirection(possessive.Groups["direction"].Value);
                command.relative_distance_meters = ExtractDistanceMeters(text);
                command.placement_mode = "target_local";
                return true;
            }

            Match meFrame = Regex.Match(
                text,
                @"^(?<object>.+?)\s+(?<relation>to\s+the\s+left\s+of|to\s+the\s+right\s+of|left\s+of|right\s+of|in\s+front\s+of|behind|above|below|on\s+top\s+of|under|beneath)\s+(the\s+)?(?<target>.+?)\s*$",
                RegexOptions.IgnoreCase);

            if (!meFrame.Success)
                return false;

            objectName = CleanCreatedObjectName(meFrame.Groups["object"].Value);
            command.spatial_reference = SpatialReferenceMode.RelativeToTarget;
            command.target_reference = TargetReferenceMode.NamedObject;
            command.target_name = CleanCreatedObjectName(meFrame.Groups["target"].Value);
            command.relative_direction = ParseTargetRelativeDirection(meFrame.Groups["relation"].Value);
            command.relative_distance_meters = ExtractDistanceMeters(text);
            command.placement_mode = "me_frame";
            return true;
        }

        static RelativeDirection ParseTargetRelativeDirection(string value)
        {
            string lower = (value ?? "").ToLowerInvariant();
            if (lower.Contains("left", StringComparison.Ordinal))
                return RelativeDirection.Left;
            if (lower.Contains("right", StringComparison.Ordinal))
                return RelativeDirection.Right;
            if (lower.Contains("behind", StringComparison.Ordinal) || lower.Contains("back", StringComparison.Ordinal))
                return RelativeDirection.Behind;
            if (lower.Contains("above", StringComparison.Ordinal) || lower.Contains("top", StringComparison.Ordinal))
                return RelativeDirection.Up;
            if (lower.Contains("below", StringComparison.Ordinal) || lower.Contains("under", StringComparison.Ordinal) || lower.Contains("beneath", StringComparison.Ordinal) || lower.Contains("bottom", StringComparison.Ordinal))
                return RelativeDirection.Down;
            return RelativeDirection.InFront;
        }

        static bool TryExtractLeadingMaterialPrompt(ref string value, out string materialPrompt)
        {
            materialPrompt = "";
            string working = (value ?? "").Trim();
            string[] words = working.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int maxWords = Math.Min(3, words.Length);
            for (int count = maxWords; count >= 1; count--)
            {
                string candidate = string.Join(" ", words, 0, count);
                string remainder = string.Join(" ", words, count, words.Length - count).Trim();
                if (string.IsNullOrWhiteSpace(remainder))
                    continue;

                if (!RuntimeMaterialCatalog.TryParseDescriptor(candidate, RuntimeMaterialDescriptor.Default, out _))
                    continue;

                materialPrompt = candidate;
                value = remainder;
                return true;
            }

            return false;
        }

        static string CleanCreatedObjectName(string value)
        {
            string result = (value ?? "").Trim();
            result = DistanceUnitParser.RemoveDistances(result);
            result = Regex.Replace(result, @"\b(wide|width|tall|high|height)\b", "", RegexOptions.IgnoreCase).Trim();
            result = Regex.Replace(result, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase).Trim();
            while (result.Contains("  ", StringComparison.Ordinal))
                result = result.Replace("  ", " ");
            return result.Trim();
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

        static bool TryParsePhysicsCommand(string original, string lower, VoiceIntentCommand command)
        {
            if (!StartsWithAny(lower, "make ", "set ", "turn ", "disable ", "enable "))
                return false;

            bool weightless = ContainsAny(lower, "weightless", "zero gravity", "no gravity");
            bool disableGravity = weightless || ContainsAny(lower, "disable gravity", "turn off gravity", "gravity off");
            bool enableGravity = ContainsAny(lower, "enable gravity", "turn on gravity", "gravity on", "have weight", "has weight", "give it weight", "give weight");
            if (!disableGravity && !enableGravity)
                return false;

            if (Regex.IsMatch(original, @"^(make|create|add|place|put|spawn)\s+(a|an)\s+.*\b(weightless|zero\s+gravity|no\s+gravity)\b", RegexOptions.IgnoreCase))
                return false;

            string target = original.Trim();
            target = Regex.Replace(target, @"^(make|set|turn|disable|enable)\s+", "", RegexOptions.IgnoreCase).Trim();
            target = Regex.Replace(target, @"\b(weightless|zero\s+gravity|no\s+gravity|disable\s+gravity|enable\s+gravity|turn\s+off\s+gravity|turn\s+on\s+gravity|gravity\s+off|gravity\s+on|have\s+weight|has\s+weight|give\s+it\s+weight|give\s+weight)\b", "", RegexOptions.IgnoreCase).Trim();
            target = CleanPhysicsTarget(target);

            command.intent = VoiceIntentType.ModifyPhysics;
            command.physics_action = weightless ? "set_weightless" : (enableGravity ? "enable_gravity" : "disable_gravity");
            command.object_weightless = weightless || disableGravity;
            command.target_reference = ResolvePhysicsTargetReference(target);
            command.target_name = command.target_reference == TargetReferenceMode.NamedObject ? target : "";
            command.object_name = command.target_name;
            command.spoken_response = weightless ? "Making it weightless." : (enableGravity ? "Enabling gravity." : "Disabling gravity.");
            return true;
        }

        static TargetReferenceMode ResolvePhysicsTargetReference(string target)
        {
            string lower = (target ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower) || lower == "it")
                return TargetReferenceMode.LastCreatedOrInteracted;
            if (lower == "this" || lower == "that" || lower == "this one" || lower == "that one")
                return TargetReferenceMode.PointedObject;
            if (lower == "all" || lower == "everything")
                return TargetReferenceMode.All;
            return TargetReferenceMode.NamedObject;
        }

        static string CleanPhysicsTarget(string target)
        {
            string cleaned = (target ?? "").Trim();
            while (cleaned.Length > 0 && (char.IsPunctuation(cleaned[cleaned.Length - 1]) || char.IsWhiteSpace(cleaned[cleaned.Length - 1])))
                cleaned = cleaned.Substring(0, cleaned.Length - 1).TrimEnd();
            cleaned = Regex.Replace(cleaned, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase).Trim();
            return cleaned;
        }

        static bool TryParseCreateLight(string original, string lower, VoiceIntentCommand command)
        {
            if (!ContainsAny(lower, "light", "flashlight", "torch", "sun"))
                return false;

            if (Regex.IsMatch(original, @"^(make|set)\s+.+?\s+(the\s+)?sun\s*$", RegexOptions.IgnoreCase) ||
                ContainsAny(lower, " redder", " greener", " bluer", " warmer", " cooler", " brighter", " dimmer", " softer"))
            {
                return false;
            }

            if (!StartsWithAny(lower,
                    "add ", "create ", "make ", "place ", "put ", "spawn "))
            {
                return false;
            }

            if (ContainsAny(lower, "sound", "audio"))
                return false;

            string remainder = original;
            TryStripPrefix(original, lower, out remainder,
                "add a", "add an", "add", "create a", "create an", "create", "make a", "make an", "make",
                "place a", "place an", "place", "put a", "put an", "put", "spawn a", "spawn an", "spawn");

            string working = remainder.ToLowerInvariant();
            bool flashlight = ContainsAny(working, "flashlight", "torch");
            bool ambient = ContainsAny(working, "ambient light", "ambient");
            bool directional = ContainsAny(working, "directional light", "sun");
            bool spot = ContainsAny(working, "spotlight", "spot light");

            if (!flashlight && !ambient && !directional && !spot && !ContainsAny(working, "light"))
                return false;

            command.intent = VoiceIntentType.CreateLight;
            command.light_type = flashlight ? "flashlight" :
                                 ambient ? "ambient" :
                                 directional ? "directional" :
                                 spot ? "spot" :
                                 "point";
            command.target_entity = command.light_type == "directional" ? "directional light" : command.light_type;
            command.object_name = flashlight ? "flashlight" : command.target_entity;
            command.light_color_prompt = ExtractLightColorPrompt(working);
            if (flashlight)
                command.light_spot_angle = 22f;

            if (TryInferBodyAnchor(lower, out BodyAnchor anchor, out HandSelection hand))
            {
                command.spatial_reference = SpatialReferenceMode.BodyAnchor;
                command.body_anchor = anchor;
                command.target_hand = hand;
            }
            else if (ContainsAny(lower, " here", " there", " where i am pointing", "where i'm pointing"))
            {
                command.spatial_reference = spot || directional || flashlight
                    ? SpatialReferenceMode.PointingRay
                    : SpatialReferenceMode.PointingHit;
                command.target_hand = HandSelection.Either;
            }
            else if (flashlight)
            {
                command.spatial_reference = SpatialReferenceMode.PointingRay;
                command.target_hand = HandSelection.Either;
            }

            command.spoken_response = ambient ? "Updating ambient light." : "Adding light.";
            return true;
        }

        static bool TryParseModifyLight(string original, string lower, VoiceIntentCommand command)
        {
            if (!StartsWithAny(lower, "make ", "change ", "modify ", "turn ", "set "))
                return false;

            if (!ContainsAny(lower, "light", "flashlight", "sun"))
                return false;

            if (TryParseMakeSun(original, lower, command))
                return true;

            Match match = Regex.Match(
                original,
                @"^(make|change|modify|turn|set)\s+(?<target>.+?)\s+(?<action>redder|greener|bluer|warmer|cooler|brighter|dimmer|softer|red|blue|green|yellow|orange|purple|pink|black|white|gray|grey|warm\s+white|cool\s+white)\s*$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string target = match.Groups["target"].Value.Trim();
            string action = match.Groups["action"].Value.Trim().ToLowerInvariant();
            command.intent = VoiceIntentType.ModifyLight;
            command.target_reference = ResolveLightTargetReference(target);
            command.target_name = CleanLightTarget(target);
            command.object_name = command.target_name;

            if (IsRelativeLightAction(action))
            {
                command.light_action = action;
            }
            else if (action == "brighter" || action == "dimmer" || action == "softer")
            {
                command.light_action = action;
            }
            else
            {
                command.light_action = "set_color";
                command.light_color_prompt = action;
            }

            command.spoken_response = "Updating light.";
            return true;
        }

        static bool TryParseMakeSun(string original, string lower, VoiceIntentCommand command)
        {
            if (!ContainsAny(lower, " the sun", " sun"))
                return false;

            Match match = Regex.Match(
                original,
                @"^(make|set)\s+(?<target>.+?)\s+(the\s+)?sun\s*$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string target = match.Groups["target"].Value.Trim();
            command.intent = VoiceIntentType.ModifyLight;
            command.light_action = "set_sun";
            command.target_reference = ResolveLightTargetReference(target);
            command.target_name = CleanLightTarget(target);
            command.object_name = command.target_name;
            command.spoken_response = "Setting sun.";
            return true;
        }

        static string ExtractLightColorPrompt(string lower)
        {
            string[] colors =
            {
                "warm white",
                "cool white",
                "yellow",
                "orange",
                "purple",
                "pink",
                "blue",
                "green",
                "red",
                "black",
                "white",
                "gray",
                "grey"
            };

            foreach (string color in colors)
                if (lower.Contains(color, StringComparison.Ordinal))
                    return color;
            return "";
        }

        static bool IsRelativeLightAction(string action)
        {
            return action == "redder" ||
                   action == "greener" ||
                   action == "bluer" ||
                   action == "warmer" ||
                   action == "cooler";
        }

        static TargetReferenceMode ResolveLightTargetReference(string target)
        {
            string lower = (target ?? "").ToLowerInvariant();
            if (ContainsAny(lower, "this", "that"))
                return TargetReferenceMode.PointedObject;
            if (ContainsAny(lower, "all", "every"))
                return TargetReferenceMode.All;
            if (string.IsNullOrWhiteSpace(target) || lower == "it")
                return TargetReferenceMode.LastCreatedOrInteracted;
            return TargetReferenceMode.NamedObject;
        }

        static string CleanLightTarget(string target)
        {
            string cleaned = (target ?? "").Trim();
            cleaned = Regex.Replace(cleaned, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase).Trim();
            cleaned = Regex.Replace(cleaned, @"^(this|that)\s+", "", RegexOptions.IgnoreCase).Trim();
            cleaned = Regex.Replace(cleaned, @"^(all|every)\s+", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(cleaned) || string.Equals(cleaned, "it", StringComparison.OrdinalIgnoreCase))
                return "light";
            return cleaned;
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
            ExtractSpatialQualifier(ref cleanedTarget, out string spatialQualifier);
            string materialPrompt = "";
            SceneEntityResolver.ExtractMaterialQualifierForCommand(ref cleanedTarget, ref materialPrompt);

            command.intent = VoiceIntentType.DeleteTarget;
            command.target_reference = indicated
                ? TargetReferenceMode.PointedObject
                : (all ? TargetReferenceMode.All : TargetReferenceMode.NamedObject);
            command.target_name = cleanedTarget;
            command.object_name = cleanedTarget;
            command.target_material_prompt = materialPrompt;
            command.target_spatial_qualifier = spatialQualifier;
            command.sound_prompt = IsAudioDeleteTarget(cleanedTarget) ? cleanedTarget : "";
            command.spoken_response = all
                ? $"Deleting all {cleanedTarget}."
                : $"Deleting {cleanedTarget}.";
            return true;
        }

        static bool TryParseRotateTarget(string original, string lower, VoiceIntentCommand command)
        {
            if (!StartsWithAny(lower, "rotate ", "turn "))
                return false;

            string targetText = Regex.Replace(original.Trim(), @"^(rotate|turn)\s+", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(targetText))
                return false;

            bool hasDegrees = CommandDialogStateManager.TryParseDegrees(targetText, out float degrees);
            bool hasAxis = CommandDialogStateManager.TryParseRotationAxis(targetText, out RotationAxis axis);

            targetText = Regex.Replace(targetText, @"\b(by\s+)?[-+]?\d+(\.\d+)?\s*(degrees?|deg)?\b", "", RegexOptions.IgnoreCase).Trim();
            targetText = Regex.Replace(targetText, @"\b(on|around|about)?\s*(the\s+)?[xyz]\s+axis\b", "", RegexOptions.IgnoreCase).Trim();
            targetText = Regex.Replace(targetText, @"\b(by|on|around|about)\b", "", RegexOptions.IgnoreCase).Trim();
            targetText = Regex.Replace(targetText, @"\s+", " ").Trim();
            targetText = CleanArticle(targetText);

            if (string.IsNullOrWhiteSpace(targetText))
                return false;

            command.intent = VoiceIntentType.RotateTarget;
            command.rotation_degrees = hasDegrees ? degrees : 0f;
            command.rotation_axis = hasAxis ? axis : RotationAxis.Y;
            command.should_execute = hasDegrees;
            command.spoken_response = hasDegrees ? "Rotating." : $"How many degrees should I rotate {FriendlyTargetName(targetText)}?";

            if (IsWorldTarget(targetText))
            {
                command.target_reference = TargetReferenceMode.CurrentWorld;
                command.target_entity = "world";
            }
            else if (IsMeTarget(targetText))
            {
                command.target_reference = TargetReferenceMode.NamedObject;
                command.target_entity = "Me";
                command.target_name = "Me";
            }
            else if (ContainsAny(targetText.ToLowerInvariant(), "this", "that"))
            {
                command.target_reference = TargetReferenceMode.PointedObject;
            }
            else
            {
                command.target_reference = TargetReferenceMode.NamedObject;
                command.target_name = targetText;
                command.object_name = targetText;
            }

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

        static bool TryParseCreateTeleportPad(string original, string lower, VoiceIntentCommand command)
        {
            if (!StartsWithAny(lower, "make ", "create ", "add ", "place ", "put ", "spawn "))
                return false;

            if (!ContainsTeleportNoun(lower))
                return false;

            command.intent = VoiceIntentType.PlaceObject;
            command.object_name = "teleport pad";
            command.spatial_reference = SpatialReferenceMode.RelativeToMe;
            command.relative_direction = RelativeDirection.Down;
            command.placement_mode = "under_foot";
            command.spoken_response = "Adding teleport pad.";
            return true;
        }

        static bool ContainsTeleportNoun(string lower)
        {
            return Regex.IsMatch(lower ?? "", @"\b(teleport|teleporter|teleporters|teleport\s+pad|teleport\s+pads|teleport\s+anchor|teleport\s+anchors)\b", RegexOptions.IgnoreCase);
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
                "above me", "below me", " up", " down", " left", " right");
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

            value = DistanceUnitParser.RemoveDistances(value);
            if (value.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("the ".Length).Trim();
            return string.IsNullOrWhiteSpace(value) ? "Me" : value;
        }

        static string StripRelativePlacementPhrase(string value)
        {
            string result = value ?? string.Empty;
            string[] markers =
            {
                " in front of me",
                " behind me",
                " to my left",
                " to my right",
                " my left",
                " my right",
                " above me",
                " below me"
            };

            foreach (string marker in markers)
            {
                int index = result.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    result = result.Substring(0, index).Trim();
                    break;
                }
            }

            return result;
        }

        static RelativeDirection InferRelativeDirection(string lower)
        {
            if (lower.Contains("in front of me", StringComparison.Ordinal))
                return RelativeDirection.InFront;
            if (lower.Contains("forward", StringComparison.Ordinal))
                return RelativeDirection.Forward;
            if (lower.Contains("behind me", StringComparison.Ordinal))
                return RelativeDirection.Behind;
            if (lower.Contains("backward", StringComparison.Ordinal) ||
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
            return DistanceUnitParser.TryExtractMeters(lower, out float meters) ? meters : 0f;
        }

        static bool IsMeTarget(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "me" || normalized == "myself" || normalized == "player";
        }

        static bool IsWorldTarget(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "world" ||
                   normalized == "current world" ||
                   normalized == "splat" ||
                   normalized == "scene";
        }

        static string FriendlyTargetName(string targetText)
        {
            return IsWorldTarget(targetText) ? "the world" : CleanArticle(targetText);
        }

        static string CleanArticle(string value)
        {
            string result = (value ?? string.Empty).Trim();
            if (result.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                result = result.Substring("the ".Length).Trim();
            if (result.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
                result = result.Substring("a ".Length).Trim();
            if (result.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
                result = result.Substring("an ".Length).Trim();
            return result;
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

        static void ExtractSpatialQualifier(ref string target, out string spatialQualifier)
        {
            spatialQualifier = "";
            string value = (target ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (Regex.IsMatch(value, @"\b(on top|topmost|uppermost|highest|upper one|top one)$", RegexOptions.IgnoreCase))
            {
                spatialQualifier = "topmost";
                target = Regex.Replace(value, @"\b(on top|topmost|uppermost|highest|upper one|top one)$", "", RegexOptions.IgnoreCase).Trim();
                return;
            }

            if (Regex.IsMatch(value, @"\b(on bottom|bottommost|lowermost|lowest|lower one|bottom one)$", RegexOptions.IgnoreCase))
            {
                spatialQualifier = "bottommost";
                target = Regex.Replace(value, @"\b(on bottom|bottommost|lowermost|lowest|lower one|bottom one)$", "", RegexOptions.IgnoreCase).Trim();
            }
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

        static bool TryParseProxyVisibility(string original, string lower, VoiceIntentCommand command)
        {
            bool show = StartsWithAny(lower, "show ", "display ", "reveal ");
            bool hide = StartsWithAny(lower, "hide ", "conceal ");
            if (!show && !hide)
                return false;

            if (!ContainsAny(lower, "proxy", "proxies"))
                return false;

            string category = "all";
            if (ContainsAny(lower, "light proxy", "light proxies", "sun proxy", "flashlight proxy"))
                category = "light";
            else if (ContainsAny(lower, "sound proxy", "sound proxies", "audio proxy", "audio proxies"))
                category = "audio";

            command.intent = VoiceIntentType.SetProxyVisibility;
            command.proxy_category = category;
            command.proxy_visible = show;
            command.spoken_response = show ? "Showing proxies." : "Hiding proxies.";
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

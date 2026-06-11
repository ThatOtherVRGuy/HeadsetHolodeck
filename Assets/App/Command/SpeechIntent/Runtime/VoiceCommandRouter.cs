using System.Collections;
using Holodeck.Direct;
using UnityEngine;

namespace SpeechIntent
{
    public class VoiceCommandRouter : MonoBehaviour
    {
        [Header("Core References")]
        public MicrophoneWavRecorder recorder;
        public SpatialContextProvider spatialContextProvider;
        public SceneSemanticContextProvider sceneContextProvider;
        public OpenAiSpeechIntentService speechIntentService;
        public WorldActionDispatcher dispatcher;
        public CachedObjectChoiceController cachedObjectChoiceController;

        [Header("Events")]
        public StringEvent onTranscriptReady;
        public StringEvent onAssistantResponse;
        public VoiceIntentCommandEvent onCommandReady;
        public SpeechIntentResultEvent onResult;
        public StringEvent onError;

        private bool _isRecording;
        private readonly CommandDialogStateManager _dialogState = new CommandDialogStateManager();

        private void OnEnable()
        {
            ObjectGenerationService.UserFacingFailure += HandleObjectGenerationFailure;
            VoiceToWorldLabsPluginCoordinator.UserFacingStatus += HandleWorldGenerationStatus;
        }

        private void OnDisable()
        {
            ObjectGenerationService.UserFacingFailure -= HandleObjectGenerationFailure;
            VoiceToWorldLabsPluginCoordinator.UserFacingStatus -= HandleWorldGenerationStatus;
        }

        private void HandleObjectGenerationFailure(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            Debug.LogWarning("[VoiceCommandRouter] Object generation failure: " + message, this);
            onAssistantResponse?.Invoke(message);
        }

        private void HandleWorldGenerationStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            Debug.Log("[VoiceCommandRouter] World generation status: " + message, this);
            onAssistantResponse?.Invoke(message);
        }

        public void BeginRecording()
        {
            if (_isRecording) return;
            _isRecording = true;
            if (recorder == null)
            {
                _isRecording = false;
                EmitError("Recorder reference is missing.");
                return;
            }
            recorder.BeginRecording();
        }

        public void EndRecordingAndProcess()
        {
            if (!_isRecording) return;
            _isRecording = false;
            if (recorder == null)
            {
                EmitError("Recorder reference is missing.");
                return;
            }

            byte[] wavBytes = recorder.EndRecordingToWavBytes();
            if (wavBytes == null || wavBytes.Length == 0)
            {
                EmitError("No audio was recorded.");
                return;
            }

            SpatialSnapshot spatial = spatialContextProvider != null
                ? spatialContextProvider.CaptureSnapshot()
                : new SpatialSnapshot();

            SceneSemanticSnapshot scene = sceneContextProvider != null
                ? sceneContextProvider.CaptureSnapshot()
                : new SceneSemanticSnapshot();

            StartCoroutine(ProcessUtterance(wavBytes, spatial, scene));
        }

        public void SubmitTypedCommand(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                EmitError("No typed command was entered.");
                return;
            }

            SpatialSnapshot spatial = spatialContextProvider != null
                ? spatialContextProvider.CaptureSnapshot()
                : new SpatialSnapshot();

            SceneSemanticSnapshot scene = sceneContextProvider != null
                ? sceneContextProvider.CaptureSnapshot()
                : new SceneSemanticSnapshot();

            StartCoroutine(ProcessTypedCommand(text, spatial, scene));
        }

        private IEnumerator ProcessUtterance(byte[] wavBytes, SpatialSnapshot spatial, SceneSemanticSnapshot scene)
        {
            if (speechIntentService == null)
            {
                EmitError("Speech intent service reference is missing.");
                yield break;
            }

            SpeechIntentResult finalResult = null;
            yield return speechIntentService.Interpret(
                wavBytes, spatial, scene,
                result     => finalResult = result,
                transcript =>
                {
                    Debug.Log($"[VoiceCommandRouter] Transcript received: \"{transcript}\"");
                    ArchStatusBus.Info("VOICE: " + transcript, "VOICE");
                    onTranscriptReady?.Invoke(transcript);
                });

            if (finalResult == null)
            {
                EmitError("Speech intent service returned no result.");
                yield break;
            }

            HandleResult(finalResult, spatial);
        }

        private IEnumerator ProcessTypedCommand(string text, SpatialSnapshot spatial, SceneSemanticSnapshot scene)
        {
            ArchStatusBus.Info("TYPE: " + text, "TYPE");
            onTranscriptReady?.Invoke(text);

            SpeechIntentResult finalResult = null;
            if (speechIntentService != null && speechIntentService.IsConfigured)
            {
                yield return speechIntentService.InterpretText(text, spatial, scene, result => finalResult = result);
            }
            else
            {
                finalResult = BuildLocalTypedIntent(text);
            }

            if (finalResult == null)
            {
                EmitError("Typed command produced no result.");
                yield break;
            }

            HandleResult(finalResult, spatial);
        }

        private void HandleResult(SpeechIntentResult finalResult, SpatialSnapshot spatial)
        {
            Debug.Log(
                $"[VoiceCommandRouter] HandleResult router={GetInstanceID()} pending={_dialogState.HasPendingClarification}/{_dialogState.PendingSlot} " +
                $"transcript='{FirstNonEmpty(finalResult?.transcript, finalResult?.command?.transcript)}' " +
                $"intent={finalResult?.command?.intent} execute={finalResult?.command?.should_execute} response='{finalResult?.command?.spoken_response}'");

            if (TryParsePendingCachedObjectChoice(finalResult, out VoiceIntentCommand cachedChoiceCommand))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = cachedChoiceCommand;
            }

            if (TryParseGenerationControl(finalResult, out VoiceIntentCommand generationControlCommand))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = generationControlCommand;
            }

            if (TryCompletePendingClarification(finalResult, out VoiceIntentCommand completedCommand))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = completedCommand;
            }

            if (TryBuildLocalExecutableOverride(finalResult, out VoiceIntentCommand localOverride))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = localOverride;
            }

            if (TryBuildLocalPendingClarificationOverride(finalResult, out VoiceIntentCommand pendingOverride))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = pendingOverride;
            }

            if (!finalResult.success)
            {
                EmitError(finalResult.error);
                onResult?.Invoke(finalResult);
                return;
            }

            if (finalResult.command != null)
            {
                onCommandReady?.Invoke(finalResult.command);

                if (finalResult.command.intent != VoiceIntentType.SelectCachedObject)
                    ClearPendingCachedObjectChoice();

                // Execute BEFORE speaking — the dispatcher may clear spoken_response
                // when an action isn't available (e.g. no pano for a splat-only world),
                // so the user only hears the error, not a false confirmation followed by it.
                if (dispatcher != null)
                {
                    dispatcher.Execute(finalResult.command, spatial);
                }

                UpdatePendingClarification(finalResult.command);

                if (!string.IsNullOrWhiteSpace(finalResult.command.spoken_response))
                {
                    onAssistantResponse?.Invoke(finalResult.command.spoken_response);
                }
            }

            onResult?.Invoke(finalResult);
        }

        private bool TryCompletePendingClarification(SpeechIntentResult result, out VoiceIntentCommand completedCommand)
        {
            completedCommand = null;
            if (!_dialogState.HasPendingClarification || result == null)
            {
                if (result != null)
                {
                    Debug.Log(
                        $"[VoiceCommandRouter] No pending clarification on router={GetInstanceID()} for " +
                        $"'{FirstNonEmpty(result.transcript, result.command?.transcript)}'.");
                }
                return false;
            }

            CommandClarificationSlot pendingSlot = _dialogState.PendingSlot;
            if (_dialogState.TryComplete(result, out completedCommand))
            {
                Debug.Log(
                    $"[VoiceCommandRouter] Completed pending {pendingSlot} clarification on router={GetInstanceID()} " +
                    $"as intent={completedCommand.intent} target='{completedCommand.target_name}' " +
                    $"material='{completedCommand.target_material_prompt}' spatial='{completedCommand.target_spatial_qualifier}'.");
                return true;
            }

            if (_dialogState.TryCompletePlacement(result, ApplyPlacementClarification, out completedCommand))
            {
                Debug.Log($"[VoiceCommandRouter] Completed pending placement with '{completedCommand.transcript}'.");
                return true;
            }

            Debug.Log(
                $"[VoiceCommandRouter] Pending {_dialogState.PendingSlot} clarification did not accept " +
                $"'{FirstNonEmpty(result.transcript, result.command?.transcript)}' on router={GetInstanceID()}.");
            return false;
        }

        private static bool ApplyPlacementClarification(string text, VoiceIntentCommand command)
        {
            if (!TryParsePlacementLocationOnly(text,
                    out SpatialReferenceMode spatialReference,
                    out BodyAnchor bodyAnchor,
                    out HandSelection handSelection,
                    out RelativeDirection relativeDirection,
                    out float relativeDistanceMeters))
            {
                return false;
            }

            command.spatial_reference = spatialReference;
            command.body_anchor = bodyAnchor;
            command.target_hand = handSelection;
            command.relative_direction = relativeDirection;
            command.relative_distance_meters = relativeDistanceMeters;
            return true;
        }

        public static bool TryBuildLocalExecutableOverrideForTests(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            return TryBuildLocalExecutableOverride(result, out command);
        }

        private static bool TryBuildLocalExecutableOverride(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            command = null;
            if (result == null)
                return false;

            VoiceIntentType modelIntent = result.command != null ? result.command.intent : VoiceIntentType.Unknown;
            if (result.success &&
                modelIntent != VoiceIntentType.Unknown &&
                modelIntent != VoiceIntentType.AskClarification)
            {
                return false;
            }

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            VoiceIntentCommand local = LocalTypedIntentParser.Parse(text);
            if (local == null ||
                local.intent == VoiceIntentType.Unknown ||
                local.intent == VoiceIntentType.AskClarification ||
                !local.should_execute)
            {
                return false;
            }

            command = local;
            Debug.Log($"[VoiceCommandRouter] Using local executable override for '{text}' as {local.intent}.");
            return true;
        }

        private static bool TryBuildLocalPendingClarificationOverride(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            command = null;
            if (result == null)
                return false;

            VoiceIntentType modelIntent = result.command != null ? result.command.intent : VoiceIntentType.Unknown;
            if (result.success &&
                modelIntent != VoiceIntentType.Unknown &&
                modelIntent != VoiceIntentType.AskClarification)
            {
                return false;
            }

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            VoiceIntentCommand local = LocalTypedIntentParser.Parse(text);
            if (local == null ||
                local.intent == VoiceIntentType.Unknown ||
                local.intent == VoiceIntentType.AskClarification ||
                local.should_execute)
            {
                return false;
            }

            if (TryGetClarificationSlot(local, out _))
            {
                command = local;
                Debug.Log($"[VoiceCommandRouter] Using local pending clarification override for '{text}' as {local.intent}.");
                return true;
            }

            return false;
        }

        bool TryParseGenerationControl(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            command = null;
            if (result == null)
                return false;

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (!LocalTypedIntentParser.TryParseGenerationControlReply(text, out VoiceIntentCommand local))
                return false;

            command = local;
            Debug.Log($"[VoiceCommandRouter] Parsed generation control '{local.intent}' target='{local.target_entity}' from '{text}'.");
            return true;
        }

        private void UpdatePendingClarification(VoiceIntentCommand command)
        {
            if (command == null)
                return;

            if (command.intent == VoiceIntentType.PlaceObject &&
                string.Equals(command.spoken_response, "Where?", System.StringComparison.OrdinalIgnoreCase))
            {
                _dialogState.BeginClarification(command, CommandClarificationSlot.Placement, command.spoken_response);
                Debug.Log($"[VoiceCommandRouter] Stored pending placement clarification for '{command.object_name}'.");
                return;
            }

            if (TryGetClarificationSlot(command, out CommandClarificationSlot slot))
            {
                _dialogState.BeginClarification(command, slot, command.spoken_response);
                Debug.Log(
                    $"[VoiceCommandRouter] Stored pending {slot} clarification on router={GetInstanceID()} " +
                    $"for intent='{command.intent}' target='{command.target_name}' material='{command.target_material_prompt}' " +
                    $"spatial='{command.target_spatial_qualifier}' question='{command.spoken_response}'.");
                return;
            }

            if (command.should_execute && command.intent != VoiceIntentType.AskClarification)
            {
                if (_dialogState.HasPendingClarification)
                    Debug.Log($"[VoiceCommandRouter] Clearing pending {_dialogState.PendingSlot} clarification on router={GetInstanceID()} after executable {command.intent}.");
                _dialogState.Clear();
            }
        }

        private static bool TryGetClarificationSlot(VoiceIntentCommand command, out CommandClarificationSlot slot)
        {
            slot = CommandClarificationSlot.None;
            if (command == null || command.should_execute)
                return false;

            if (command.intent == VoiceIntentType.RotateTarget && Mathf.Approximately(command.rotation_degrees, 0f))
            {
                slot = CommandClarificationSlot.RotationDegrees;
                return true;
            }

            if (command.intent == VoiceIntentType.RotateTarget && command.rotation_axis == RotationAxis.None)
            {
                slot = CommandClarificationSlot.RotationAxis;
                return true;
            }

            if (IsTargetClarification(command))
            {
                slot = CommandClarificationSlot.Target;
                return true;
            }

            return false;
        }

        public static bool TryGetClarificationSlotForTests(VoiceIntentCommand command, out CommandClarificationSlot slot)
        {
            return TryGetClarificationSlot(command, out slot);
        }

        static bool IsTargetClarification(VoiceIntentCommand command)
        {
            if (command == null || !IsTargetTakingIntent(command.intent))
                return false;

            string response = (command.spoken_response ?? "").Trim();
            return response.StartsWith("Which ", System.StringComparison.OrdinalIgnoreCase);
        }

        static bool IsTargetTakingIntent(VoiceIntentType intent)
        {
            switch (intent)
            {
                case VoiceIntentType.MoveTarget:
                case VoiceIntentType.ScaleTarget:
                case VoiceIntentType.RotateTarget:
                case VoiceIntentType.DeleteTarget:
                case VoiceIntentType.SetTargetMaterial:
                case VoiceIntentType.AttachBehavior:
                case VoiceIntentType.StopBehavior:
                case VoiceIntentType.ModifyPhysics:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryParsePlacementLocationOnlyForTests(
            string text,
            out SpatialReferenceMode spatialReference,
            out BodyAnchor bodyAnchor,
            out HandSelection handSelection,
            out RelativeDirection relativeDirection,
            out float relativeDistanceMeters)
        {
            return TryParsePlacementLocationOnly(text, out spatialReference, out bodyAnchor, out handSelection, out relativeDirection, out relativeDistanceMeters);
        }

        private static bool TryParsePlacementLocationOnly(
            string text,
            out SpatialReferenceMode spatialReference,
            out BodyAnchor bodyAnchor,
            out HandSelection handSelection,
            out RelativeDirection relativeDirection,
            out float relativeDistanceMeters)
        {
            spatialReference = SpatialReferenceMode.None;
            bodyAnchor = BodyAnchor.None;
            handSelection = HandSelection.None;
            relativeDirection = RelativeDirection.None;
            relativeDistanceMeters = 0f;
            string lower = (text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return false;

            if (lower == "world origin" ||
                lower == "origin" ||
                lower == "the origin" ||
                lower == "at world origin" ||
                lower == "at the world origin" ||
                lower == "at the origin")
            {
                spatialReference = SpatialReferenceMode.WorldOrigin;
                return true;
            }

            if (lower == "here" || lower == "right here" || lower == "there" || lower == "over there")
            {
                spatialReference = SpatialReferenceMode.PointingRay;
                return true;
            }

            if (lower.Contains("in front of me", System.StringComparison.Ordinal) ||
                lower.Contains("behind me", System.StringComparison.Ordinal) ||
                lower.Contains("to my left", System.StringComparison.Ordinal) ||
                lower.Contains("to my right", System.StringComparison.Ordinal) ||
                lower.Contains("my left", System.StringComparison.Ordinal) ||
                lower.Contains("my right", System.StringComparison.Ordinal) ||
                lower.Contains("above me", System.StringComparison.Ordinal) ||
                lower.Contains("below me", System.StringComparison.Ordinal))
            {
                spatialReference = SpatialReferenceMode.RelativeToMe;
                relativeDirection = InferPlacementRelativeDirection(lower);
                relativeDistanceMeters = DistanceUnitParser.TryExtractMeters(lower, out float meters) ? meters : 0f;
                return true;
            }

            if (lower == "in my hand" || lower == "my hand" || lower == "left hand" || lower == "right hand")
            {
                spatialReference = SpatialReferenceMode.BodyAnchor;
                if (lower.Contains("left"))
                {
                    bodyAnchor = BodyAnchor.LeftHand;
                    handSelection = HandSelection.Left;
                }
                else if (lower.Contains("right"))
                {
                    bodyAnchor = BodyAnchor.RightHand;
                    handSelection = HandSelection.Right;
                }
                else
                {
                    bodyAnchor = BodyAnchorResolver.FromHandSelection(HandSelection.Either);
                    handSelection = HandSelection.Either;
                }
                return true;
            }

            return false;
        }

        bool TryParsePendingCachedObjectChoice(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            command = null;
            CachedObjectChoiceController choiceController = cachedObjectChoiceController;
            if (choiceController == null && dispatcher != null)
                choiceController = dispatcher.cachedObjectChoiceController;
            if (choiceController == null || !choiceController.HasPendingChoice || result == null)
                return false;

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (!LocalTypedIntentParser.TryParseCachedObjectChoiceReply(text, out VoiceIntentCommand local))
                return false;

            command = local;
            Debug.Log($"[VoiceCommandRouter] Parsed cached object choice reply '{local.object_choice_action}' from '{text}'.");
            return true;
        }

        void ClearPendingCachedObjectChoice()
        {
            CachedObjectChoiceController choiceController = cachedObjectChoiceController;
            if (choiceController == null && dispatcher != null)
                choiceController = dispatcher.cachedObjectChoiceController;
            if (choiceController == null || !choiceController.HasPendingChoice)
                return;

            choiceController.Cancel();
            if (dispatcher != null && dispatcher.cachedObjectChoicePanel != null)
                dispatcher.cachedObjectChoicePanel.Hide();
            Debug.Log("[VoiceCommandRouter] Cleared stale cached object choice for unrelated command.");
        }

        private static RelativeDirection InferPlacementRelativeDirection(string lower)
        {
            if (lower.Contains("in front of me", System.StringComparison.Ordinal))
                return RelativeDirection.InFront;
            if (lower.Contains("behind me", System.StringComparison.Ordinal))
                return RelativeDirection.Behind;
            if (lower.Contains("left", System.StringComparison.Ordinal))
                return RelativeDirection.Left;
            if (lower.Contains("right", System.StringComparison.Ordinal))
                return RelativeDirection.Right;
            if (lower.Contains("above me", System.StringComparison.Ordinal))
                return RelativeDirection.Up;
            if (lower.Contains("below me", System.StringComparison.Ordinal))
                return RelativeDirection.Down;
            return RelativeDirection.InFront;
        }

        private static VoiceIntentCommand CloneCommand(VoiceIntentCommand source)
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

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }

        private SpeechIntentResult BuildLocalTypedIntent(string text)
        {
            VoiceIntentCommand command = LocalTypedIntentParser.Parse(text);
            return new SpeechIntentResult
            {
                success = command != null && command.intent != VoiceIntentType.Unknown,
                transcript = text,
                command = command,
                error = command == null || command.intent == VoiceIntentType.Unknown
                    ? "Typed command needs OpenAI intent parsing. Enter an API key or use a more direct command."
                    : ""
            };
        }

        private void EmitError(string message)
        {
            Debug.LogError(message);
            ArchStatusBus.Error(message);
            onError?.Invoke(message);
        }
    }
}

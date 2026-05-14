using System.Collections;
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

        [Header("Events")]
        public StringEvent onTranscriptReady;
        public StringEvent onAssistantResponse;
        public VoiceIntentCommandEvent onCommandReady;
        public SpeechIntentResultEvent onResult;
        public StringEvent onError;

        private bool _isRecording;
        private VoiceIntentCommand _pendingPlacementClarification;

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
            if (TryCompletePendingPlacement(finalResult, out VoiceIntentCommand completedCommand))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = completedCommand;
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

        private bool TryCompletePendingPlacement(SpeechIntentResult result, out VoiceIntentCommand completedCommand)
        {
            completedCommand = null;
            if (_pendingPlacementClarification == null || result == null)
                return false;

            string text = FirstNonEmpty(result.transcript, result.command?.transcript);
            if (!TryParsePlacementLocationOnly(text, out SpatialReferenceMode spatialReference, out BodyAnchor bodyAnchor, out HandSelection handSelection))
                return false;

            completedCommand = CloneCommand(_pendingPlacementClarification);
            completedCommand.transcript = text;
            completedCommand.spatial_reference = spatialReference;
            completedCommand.body_anchor = bodyAnchor;
            completedCommand.target_hand = handSelection;
            completedCommand.should_execute = true;
            completedCommand.spoken_response = string.IsNullOrWhiteSpace(completedCommand.object_name)
                ? "Creating object."
                : $"Creating {completedCommand.object_name}.";
            _pendingPlacementClarification = null;
            Debug.Log($"[VoiceCommandRouter] Completed pending placement with location '{text}'.");
            return true;
        }

        private void UpdatePendingClarification(VoiceIntentCommand command)
        {
            if (command == null)
                return;

            if (command.intent == VoiceIntentType.PlaceObject &&
                string.Equals(command.spoken_response, "Where?", System.StringComparison.OrdinalIgnoreCase))
            {
                _pendingPlacementClarification = CloneCommand(command);
                Debug.Log($"[VoiceCommandRouter] Stored pending placement clarification for '{command.object_name}'.");
                return;
            }

            if (command.should_execute && command.intent != VoiceIntentType.AskClarification)
                _pendingPlacementClarification = null;
        }

        private static bool TryParsePlacementLocationOnly(string text, out SpatialReferenceMode spatialReference, out BodyAnchor bodyAnchor, out HandSelection handSelection)
        {
            spatialReference = SpatialReferenceMode.None;
            bodyAnchor = BodyAnchor.None;
            handSelection = HandSelection.None;
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
                target_reference = source.target_reference,
                target_name = source.target_name,
                target_material_prompt = source.target_material_prompt,
                scale_multiplier = source.scale_multiplier,
                reset_to_default_scale = source.reset_to_default_scale,
                rotation_axis = source.rotation_axis,
                rotation_degrees = source.rotation_degrees,
                relative_direction = source.relative_direction,
                relative_distance_meters = source.relative_distance_meters,
                material_prompt = source.material_prompt,
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

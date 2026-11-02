using System;
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

        [Header("Diagnostics")]
        [Tooltip("Allows exact typed commands 'simulate openai quota error' and 'simulate openai rate limit' to exercise status/TTS error handling without calling OpenAI.")]
        public bool enableOpenAiErrorSimulationCommands = true;

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

            if (TryHandleDebugSimulationCommand(text))
                return;

            SpatialSnapshot spatial = spatialContextProvider != null
                ? spatialContextProvider.CaptureSnapshot()
                : new SpatialSnapshot();

            SceneSemanticSnapshot scene = sceneContextProvider != null
                ? sceneContextProvider.CaptureSnapshot()
                : new SceneSemanticSnapshot();

            StartCoroutine(ProcessTypedCommand(text, spatial, scene));
        }

        public void SubmitAudioCommand(byte[] wavBytes, string fallbackText = "")
        {
            fallbackText = (fallbackText ?? string.Empty).Trim();
            if (wavBytes == null || wavBytes.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(fallbackText))
                {
                    Debug.LogWarning("[VoiceCommandRouter] Audio command had no bytes; falling back to text.", this);
                    SubmitTypedCommand(fallbackText);
                    return;
                }

                EmitError("No command audio was captured.");
                return;
            }

            SpatialSnapshot spatial = spatialContextProvider != null
                ? spatialContextProvider.CaptureSnapshot()
                : new SpatialSnapshot();

            SceneSemanticSnapshot scene = sceneContextProvider != null
                ? sceneContextProvider.CaptureSnapshot()
                : new SceneSemanticSnapshot();

            // Local-first: this utterance was already transcribed on the device to detect the wake word, and
            // it arrives here as fallbackText. When the reserved grammar understands it, answer immediately —
            // no network round trip, no cloud cost, and it works offline.
            LocalTranscriptDecision decision = LocalFirstTranscriptionPolicy.Decide(fallbackText, out string transcriptReason);
            Debug.Log(
                $"[Transcribe] local='{fallbackText}' bytes={wavBytes.Length} decision={decision} reason='{transcriptReason}'",
                this);

            if (decision == LocalTranscriptDecision.AcceptLocal)
            {
                StartCoroutine(ProcessTypedCommand(fallbackText, spatial, scene));
                return;
            }

            if (speechIntentService == null || !speechIntentService.IsConfigured)
            {
                if (LocalFirstTranscriptionPolicy.IsUsableLocalFallback(fallbackText))
                {
                    Debug.LogWarning(
                        "[VoiceCommandRouter] Cloud transcription is not configured; using the local transcript instead.",
                        this);
                    StartCoroutine(ProcessTypedCommand(fallbackText, spatial, scene));
                    return;
                }

                Apologise("Cloud transcription is not configured and the local transcript is not usable.", this);
                return;
            }

            Debug.Log($"[VoiceCommandRouter] SubmitAudioCommand bytes={wavBytes.Length}, fallback='{fallbackText}'", this);
            StartCoroutine(ProcessUtterance(wavBytes, spatial, scene, fallbackText));
        }

        /// <summary>
        /// Says the apology, once, without inventing an intent.
        ///
        /// Silence is the worst possible answer to speech: the user cannot tell "not understood" from "app
        /// is stuck". This is the last step of the cascade, reached only when the local grammar declined the
        /// utterance and the cloud transcription also failed.
        /// </summary>
        private void Apologise(string reason, UnityEngine.Object context)
        {
            const string apology = "Sorry, I didn't understand that.";
            Debug.LogWarning($"[VoiceCommandRouter] {reason} Apologising: \"{apology}\"", context);
            ArchStatusBus.Info("VOICE: " + apology, "VOICE");
            onTranscriptReady?.Invoke(string.Empty);
            onAssistantResponse?.Invoke(apology);
        }

        private IEnumerator ProcessUtterance(byte[] wavBytes, SpatialSnapshot spatial, SceneSemanticSnapshot scene,
            string localText = "")
        {
            if (speechIntentService == null)
            {
                if (LocalFirstTranscriptionPolicy.IsUsableLocalFallback(localText))
                {
                    yield return ProcessTypedCommand(localText, spatial, scene);
                    yield break;
                }

                Apologise("Speech intent service reference is missing.", this);
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
                // The cloud attempt failed. The local transcript was too weak to accept up front, but it is
                // still better than nothing when it is plausible speech; otherwise say so plainly.
                if (LocalFirstTranscriptionPolicy.IsUsableLocalFallback(localText))
                {
                    Debug.LogWarning(
                        $"[VoiceCommandRouter] Cloud transcription failed; falling back to local '{localText}'.",
                        this);
                    yield return ProcessTypedCommand(localText, spatial, scene);
                    yield break;
                }

                Apologise("Cloud transcription failed and the local transcript is not usable.", this);
                yield break;
            }

            HandleResult(finalResult, spatial);
        }

        private IEnumerator ProcessTypedCommand(string text, SpatialSnapshot spatial, SceneSemanticSnapshot scene)
        {
            ArchStatusBus.Info("TYPE: " + text, "TYPE");
            onTranscriptReady?.Invoke(text);

            SpeechIntentResult finalResult = null;
            if (OpenAiSpeechIntentService.TryBuildLocalPreflightCommand(text, out VoiceIntentCommand preflightCommand))
            {
                finalResult = new SpeechIntentResult
                {
                    success = true,
                    transcript = text,
                    command = preflightCommand,
                    error = string.Empty
                };
            }
            else if (speechIntentService != null && speechIntentService.IsConfigured)
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
            // The service normally preserves the original transcript, but normalize it
            // here too so every downstream safeguard sees the user's actual words.
            if (finalResult?.command != null && !string.IsNullOrWhiteSpace(finalResult.transcript))
                finalResult.command.transcript = finalResult.transcript;

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

            // A completed clarification is an explicit user decision, not an implied one. Remember it
            // so the implicit-generation guard below does not reject the answer that authorised it
            // (the transcript is "yes", which contains no prompt of its own).
            bool clarificationCompleted = TryCompletePendingClarification(finalResult, out VoiceIntentCommand completedCommand);
            if (clarificationCompleted)
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

            // "Load <name>" is a local-storage command, never a world-generation
            // prompt. Override an occasional model misclassification before dispatch.
            if (TryBuildLocalSavedWorldLoadOverride(finalResult, out VoiceIntentCommand savedWorldLoad))
            {
                finalResult.success = true;
                finalResult.error = "";
                finalResult.command = savedWorldLoad;
            }

            // Never treat an unrecognized phrase as implicit permission to spend a
            // World Labs generation request.  The dispatcher repeats this check as
            // a final safeguard for any command that bypasses the router.
            if (!clarificationCompleted &&
                !(finalResult.command?.user_confirmed ?? false) &&
                finalResult.command != null &&
                finalResult.command.intent == VoiceIntentType.GenerateWorld &&
                !LocalTypedIntentParser.IsExplicitWorldGenerationRequest(
                    FirstNonEmpty(finalResult.transcript, finalResult.command.transcript)))
            {
                string transcript = FirstNonEmpty(finalResult.transcript, finalResult.command.transcript);
                Debug.LogWarning($"[VoiceCommandRouter] Blocked implicit world generation for transcript '{transcript}'.");
                finalResult.command.intent = VoiceIntentType.Unknown;
                finalResult.command.should_execute = false;
                finalResult.command.spoken_response =
                    "I didn't understand that as a command. Say create a world, or load a saved world by name.";
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

                // Silence is the worst answer to an utterance: if nothing actionable came back and
                // nobody has said anything about it yet, acknowledge the miss. A pending question
                // (AskClarification) is left alone so the dialogue stays open.
                if (string.IsNullOrWhiteSpace(finalResult.command.spoken_response) &&
                    finalResult.command.intent != VoiceIntentType.AskClarification &&
                    (finalResult.command.intent == VoiceIntentType.Unknown || !finalResult.command.should_execute))
                {
                    finalResult.command.spoken_response = "Sorry, I didn't understand that.";
                }

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

        public static bool TryBuildLocalSavedWorldLoadOverrideForTests(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            return TryBuildLocalSavedWorldLoadOverride(result, out command);
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

        private static bool TryBuildLocalSavedWorldLoadOverride(SpeechIntentResult result, out VoiceIntentCommand command)
        {
            command = null;
            string text = FirstNonEmpty(result?.transcript, result?.command?.transcript);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            VoiceIntentCommand local = LocalTypedIntentParser.Parse(text);
            if (local == null || local.intent != VoiceIntentType.LoadWorldConfig || !local.should_execute)
                return false;

            command = local;
            Debug.Log($"[VoiceCommandRouter] Enforcing local saved-world load for '{text}' before world generation.");
            return true;
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

            // A missed saved-world load proposes a world generation and asks "shall I create it?".
            // It carries the prompt so the confirmation can execute without re-parsing the utterance.
            if (command.intent == VoiceIntentType.GenerateWorld &&
                !string.IsNullOrWhiteSpace(command.world_prompt) &&
                !string.IsNullOrWhiteSpace(command.spoken_response))
            {
                slot = CommandClarificationSlot.Confirmation;
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
                case VoiceIntentType.DescribeTarget:
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
            string userMessage = BuildUserFacingErrorMessage(message);
            Debug.LogError(message);
            ArchStatusBus.Error(userMessage, IsOpenAiRelatedError(message) ? "OPENAI" : "ERROR");
            onError?.Invoke(userMessage);

            if (ShouldSpeakError(message))
                onAssistantResponse?.Invoke(userMessage);
        }

        bool TryHandleDebugSimulationCommand(string text)
        {
            if (!enableOpenAiErrorSimulationCommands && !Application.isEditor && !Debug.isDebugBuild)
                return false;

            if (string.Equals(text, "simulate openai quota error", StringComparison.OrdinalIgnoreCase))
            {
                SimulateOpenAiQuotaErrorForDebug();
                return true;
            }

            if (string.Equals(text, "simulate openai rate limit", StringComparison.OrdinalIgnoreCase))
            {
                SimulateOpenAiRateLimitForDebug();
                return true;
            }

            return false;
        }

        [ContextMenu("Debug/Simulate OpenAI Quota Error")]
        public void SimulateOpenAiQuotaErrorForDebug()
        {
            EmitError(
                "Transcription request failed: HTTP 429 Too Many Requests\n" +
                "{ \"error\": { \"message\": \"You exceeded your current quota.\", \"code\": \"insufficient_quota\" } }");
        }

        [ContextMenu("Debug/Simulate OpenAI Rate Limit")]
        public void SimulateOpenAiRateLimitForDebug()
        {
            EmitError(
                "Intent request failed: HTTP 429 Too Many Requests\n" +
                "retry-after: 12\n" +
                "x-ratelimit-reset-requests: 12s\n" +
                "{ \"error\": { \"message\": \"Rate limit reached.\", \"code\": \"rate_limit_exceeded\" } }");
        }

        static bool ShouldSpeakError(string message)
        {
            return IsOpenAiRelatedError(message);
        }

        static string BuildUserFacingErrorMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Something went wrong.";

            if (ContainsAny(message, "insufficient_quota", "exceeded your current quota"))
                return "OpenAI quota is exhausted. I heard you, but I can't transcribe or interpret commands until the API quota or billing is fixed.";

            if (ContainsAny(message, "HTTP/1.1 429", "Too Many Requests", "rate_limit_exceeded"))
            {
                string resetDetail = ExtractOpenAiResetDetail(message);
                return string.IsNullOrWhiteSpace(resetDetail)
                    ? "OpenAI is rate limiting command processing. Please wait a moment and try again."
                    : $"OpenAI is rate limiting command processing. Try again in about {resetDetail}.";
            }

            if (ContainsAny(message, "invalid_api_key", "incorrect api key", "401", "unauthorized"))
                return "OpenAI rejected the API key. Please check the OpenAI key configured for this build.";

            if (ContainsAny(message, "response_format", "json_schema", "schema", "unsupported_parameter"))
                return "OpenAI rejected the command parser request format. The app needs an OpenAI schema/configuration update.";

            if (IsOpenAiRelatedError(message))
                return "OpenAI command processing failed. Check the status panel or device log for details.";

            return message.Trim();
        }

        static bool IsOpenAiRelatedError(string message)
        {
            return ContainsAny(
                message,
                "OpenAI",
                "Transcription request failed",
                "Intent request failed",
                "Proxy request failed",
                "audio/transcriptions",
                "insufficient_quota",
                "rate_limit_exceeded",
                "invalid_api_key",
                "response_format",
                "json_schema");
        }

        static bool ContainsAny(string haystack, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(haystack) || needles == null)
                return false;

            foreach (string needle in needles)
            {
                if (!string.IsNullOrWhiteSpace(needle) &&
                    haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        static string ExtractOpenAiResetDetail(string message)
        {
            string retryAfter = ExtractHeaderValue(message, "retry-after");
            if (!string.IsNullOrWhiteSpace(retryAfter))
                return FormatResetValue(retryAfter);

            string requestReset = ExtractHeaderValue(message, "x-ratelimit-reset-requests");
            if (!string.IsNullOrWhiteSpace(requestReset))
                return FormatResetValue(requestReset);

            string inputReset = ExtractHeaderValue(message, "x-ratelimit-reset-input-tokens");
            if (!string.IsNullOrWhiteSpace(inputReset))
                return FormatResetValue(inputReset);

            string tokenReset = ExtractHeaderValue(message, "x-ratelimit-reset-tokens");
            if (!string.IsNullOrWhiteSpace(tokenReset))
                return FormatResetValue(tokenReset);

            return "";
        }

        static string ExtractHeaderValue(string message, string headerName)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(headerName))
                return "";

            string[] lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!line.StartsWith(headerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int colon = line.IndexOf(':');
                if (colon < 0 || colon + 1 >= line.Length)
                    continue;

                return line.Substring(colon + 1).Trim();
            }

            return "";
        }

        static string FormatResetValue(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (float.TryParse(value, out float seconds))
            {
                if (seconds < 60f)
                    return $"{Mathf.CeilToInt(seconds)} seconds";

                int minutes = Mathf.CeilToInt(seconds / 60f);
                return minutes == 1 ? "1 minute" : $"{minutes} minutes";
            }

            return value;
        }

        public static string BuildUserFacingErrorMessageForTests(string message)
        {
            return BuildUserFacingErrorMessage(message);
        }
    }
}

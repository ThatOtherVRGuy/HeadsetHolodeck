using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SpeechIntent
{
    public class OpenAiSpeechIntentService : MonoBehaviour
    {
        public OpenAiSpeechIntentConfig config;

        public bool IsConfigured
        {
            get
            {
                if (config == null)
                    return false;
                return config.useProxyServer || !string.IsNullOrWhiteSpace(ResolveApiKey());
            }
        }

        public IEnumerator Interpret(
            byte[] wavBytes,
            SpatialSnapshot spatial,
            SceneSemanticSnapshot scene,
            Action<SpeechIntentResult> onComplete,
            Action<string> onTranscript = null)
        {
            if (config == null)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = "OpenAiSpeechIntentConfig is not assigned."
                });
                yield break;
            }

            if (wavBytes == null || wavBytes.Length == 0)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = "No audio bytes were provided."
                });
                yield break;
            }

            if (config.useProxyServer)
            {
                yield return SendProxyInterpretRequest(wavBytes, spatial, scene, onComplete);
                yield break;
            }

            string apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                string message = "No OpenAI API key found. Set OPENAI_API_KEY in " +
                                 RuntimeDotEnv.ExpectedPersistentPath +
                                 ", assign it in OpenAiSpeechIntentConfig, or enable proxy mode.";
                ArchStatusBus.Error(message);
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = message
                });
                yield break;
            }

            string transcript = null;
            string transcriptionError = null;

            yield return TranscribeAudio(wavBytes, text => transcript = text, err => transcriptionError = err);

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                Debug.Log($"[OpenAiSpeechIntentService] Transcript: \"{transcript}\"");
                onTranscript?.Invoke(transcript);
            }

            if (!string.IsNullOrWhiteSpace(transcriptionError))
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = transcriptionError
                });
                yield break;
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = "OpenAI transcription returned an empty transcript."
                });
                yield break;
            }

            SpeechIntentResult intentResult = null;
            string intentError = null;

            yield return InterpretTranscript(
                transcript,
                spatial,
                scene,
                result => intentResult = result,
                err => intentError = err);

            if (!string.IsNullOrWhiteSpace(intentError))
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = intentError
                });
                yield break;
            }

            if (intentResult == null)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = "Intent stage completed without a result."
                });
                yield break;
            }

            intentResult.transcript = transcript;
            if (intentResult.command != null && string.IsNullOrWhiteSpace(intentResult.command.transcript))
            {
                intentResult.command.transcript = transcript;
            }

            onComplete?.Invoke(intentResult);
        }

        public IEnumerator InterpretText(
            string transcript,
            SpatialSnapshot spatial,
            SceneSemanticSnapshot scene,
            Action<SpeechIntentResult> onComplete)
        {
            if (config == null)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = "OpenAiSpeechIntentConfig is not assigned."
                });
                yield break;
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = "No text was provided."
                });
                yield break;
            }

            if (config.useProxyServer)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = "Typed intent interpretation is not available in proxy mode yet."
                });
                yield break;
            }

            string apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = "No OpenAI API key found for typed intent interpretation."
                });
                yield break;
            }

            SpeechIntentResult intentResult = null;
            string intentError = null;
            yield return InterpretTranscript(
                transcript,
                spatial,
                scene,
                result => intentResult = result,
                err => intentError = err);

            if (!string.IsNullOrWhiteSpace(intentError))
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = intentError
                });
                yield break;
            }

            if (intentResult == null)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    transcript = transcript,
                    error = "Typed intent stage completed without a result."
                });
                yield break;
            }

            intentResult.transcript = transcript;
            if (intentResult.command != null && string.IsNullOrWhiteSpace(intentResult.command.transcript))
                intentResult.command.transcript = transcript;

            onComplete?.Invoke(intentResult);
        }

        private IEnumerator SendProxyInterpretRequest(
            byte[] wavBytes,
            SpatialSnapshot spatial,
            SceneSemanticSnapshot scene,
            Action<SpeechIntentResult> onComplete)
        {
            WWWForm form = new WWWForm();
            form.AddBinaryData("file", wavBytes, "utterance.wav", "audio/wav");
            form.AddField("spatial_context_json", SpeechIntentJson.Serialize(spatial));
            form.AddField("scene_context_json", SpeechIntentJson.Serialize(scene));

            using UnityWebRequest request = UnityWebRequest.Post(config.proxyInterpretUrl, form);
            request.timeout = config.timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = $"Proxy request failed: {request.error}\n{request.downloadHandler?.text}"
                });
                yield break;
            }

            try
            {
                SpeechIntentResult result = SpeechIntentJson.Deserialize<SpeechIntentResult>(request.downloadHandler.text);
                onComplete?.Invoke(result);
            }
            catch (Exception ex)
            {
                onComplete?.Invoke(new SpeechIntentResult
                {
                    success = false,
                    error = $"Proxy response JSON parse failed: {ex.Message}"
                });
            }
        }

        private IEnumerator TranscribeAudio(byte[] wavBytes, Action<string> onText, Action<string> onError)
        {
            WWWForm form = new WWWForm();
            form.AddField("model", config.transcriptionModel);
            form.AddField("response_format", "json");
            if (!string.IsNullOrWhiteSpace(config.transcriptionLanguage))
                form.AddField("language", config.transcriptionLanguage);
            form.AddBinaryData("file", wavBytes, "utterance.wav", "audio/wav");

            string url = $"{config.openAiBaseUrl.TrimEnd('/')}/audio/transcriptions";
            using UnityWebRequest request = UnityWebRequest.Post(url, form);

            request.timeout = config.timeoutSeconds;
            request.SetRequestHeader("Authorization", $"Bearer {ResolveApiKey()}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Transcription request failed: {request.error}\n{request.downloadHandler?.text}");
                yield break;
            }

            try
            {
                TranscriptionResponse response = SpeechIntentJson.Deserialize<TranscriptionResponse>(request.downloadHandler.text);
                onText?.Invoke(response?.text ?? string.Empty);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Transcription JSON parse failed: {ex.Message}");
            }
        }

        private IEnumerator InterpretTranscript(
            string transcript,
            SpatialSnapshot spatial,
            SceneSemanticSnapshot scene,
            Action<SpeechIntentResult> onResult,
            Action<string> onError)
        {
            string schema = BuildCommandJsonSchema();
            string developerInstructions = BuildDeveloperInstructions();

            JObject requestBody = new JObject
            {
                ["model"] = config.intentModel,
                ["store"] = config.storeRequests,
                ["temperature"] = config.intentTemperature,
                ["input"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "developer",
                        ["content"] = developerInstructions
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = BuildUserMessage(transcript, spatial, scene)
                    }
                },
                ["text"] = new JObject
                {
                    ["format"] = new JObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "voice_intent_command",
                        ["strict"] = true,
                        ["schema"] = JObject.Parse(schema)
                    }
                }
            };

            string requestJson = requestBody.ToString(Formatting.None);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(requestJson);

            string url = $"{config.openAiBaseUrl.TrimEnd('/')}/responses";
            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = config.timeoutSeconds;
            request.SetRequestHeader("Authorization", $"Bearer {ResolveApiKey()}");
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Intent request failed: {request.error}\n{request.downloadHandler?.text}");
                yield break;
            }

            try
            {
                JObject root = JObject.Parse(request.downloadHandler.text);
                string outputText = ExtractOutputText(root);
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    onError?.Invoke("Responses API returned no output text.");
                    yield break;
                }

                VoiceIntentCommand command = SpeechIntentJson.Deserialize<VoiceIntentCommand>(outputText);
                if (command == null)
                {
                    onError?.Invoke("Structured intent command was null after JSON parse.");
                    yield break;
                }

                SpeechIntentResult result = new SpeechIntentResult
                {
                    success = true,
                    transcript = transcript,
                    command = command,
                    raw_model_json = outputText
                };

                onResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Intent parse failed: {ex.Message}");
            }
        }

        private string ExtractOutputText(JObject root)
        {
            if (root == null)
            {
                return null;
            }

            string direct = root.Value<string>("output_text");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            JToken output = root["output"];
            JArray outputArray = output as JArray;
            if (outputArray == null)
            {
                return null;
            }

            for (int i = 0; i < outputArray.Count; i++)
            {
                JToken content = outputArray[i]?["content"];
                JArray contentArray = content as JArray;
                if (contentArray == null)
                {
                    continue;
                }

                for (int j = 0; j < contentArray.Count; j++)
                {
                    JToken part = contentArray[j];
                    string text = part?.Value<string>("text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the API key from the config field if set, otherwise falls back to
        /// OPENAI_API_KEY from the process environment or runtime .env.
        /// </summary>
        private string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(config.openAiApiKey))
                return config.openAiApiKey;

            return RuntimeDotEnv.GetEnvironmentOrDotEnv("OPENAI_API_KEY");
        }

        private string BuildDeveloperInstructions()
        {
            StringBuilder sb = new StringBuilder();
            // Structural rules — keep in sync with the JSON schema and VoiceIntentType enum.
            sb.AppendLine("You convert speech into a strict JSON command for a Unity VR/XR world-building app.");
            sb.AppendLine("Return only data matching the schema.");
            sb.AppendLine("Never include markdown.");
            sb.AppendLine("Use the transcript verbatim in the transcript field.");
            sb.AppendLine("Choose exactly one intent from the enum-like options represented in the schema.");
            sb.AppendLine("Use should_execute=true only when the command is actionable with reasonable confidence.");
            sb.AppendLine("Use AskClarification with should_execute=false when an action cannot be executed safely because a key detail is missing.");
            sb.AppendLine("If the user is chatting, dictating, or unclear, set intent=Unknown and should_execute=false.");
            sb.AppendLine("For placement phrases like 'put a cube there', 'place a tree over there', or 'put it where I am pointing', set spatial_reference to PointingHit for surface placement or PointingRay for directional/mid-air placement.");
            sb.AppendLine("For phrases that refer to the user's body parts, use spatial_reference=BodyAnchor and body_anchor=Head, LeftHand, or RightHand. Examples: 'create a cube in my right hand' => PlaceObject, object_name='cube', body_anchor=RightHand. 'move the cube to my left hand' => MoveTarget, target_name='cube', body_anchor=LeftHand. 'add bird sounds in my right hand' => CreateAudioSource, sound_prompt='bird sounds', body_anchor=RightHand.");
            sb.AppendLine("For hands, also set target_hand=Left or Right. The runtime maps hands to active hand tracking or controller transforms.");
            sb.AppendLine("For 'this' or 'that' target references in commands like 'delete this cube', 'make this larger', or 'move this 1 meter left of me', use target_reference=PointedObject when the wording implies gaze/hand indication. Keep target_name/object_name as the optional class label, e.g. 'cube'.");
            sb.AppendLine("If a placement command has no location, no pointing phrase, and no spatial cue, ask a clarification such as 'Where?' instead of guessing.");
            sb.AppendLine("For material adjectives used to identify a target, put the adjective in target_material_prompt and the object class/name in target_name or object_name. Example: 'move the red cube up' => MoveTarget, target_name=cube, target_material_prompt=red. If multiple matching targets exist and the user did not say all/every or indicate one by pointing, the runtime will ask which one.");
            sb.AppendLine("For material/color/finish changes such as 'make it red', 'make this metallic blue', 'turn the cube red metallic', or 'make all red cubes matte black', use SetTargetMaterial. Put the new color/finish phrase in material_prompt. If the target itself has a material adjective, put that in target_material_prompt. Example: 'make all red cubes blue' => target_reference=All, target_name=cube, target_material_prompt=red, material_prompt=blue.");
            sb.AppendLine("For movement relative to the user/player, use spatial_reference=RelativeToMe. Examples: 'move me 1 meter forward' targets Me, relative_direction=Forward, relative_distance_meters=1. 'move the cube in front of me' targets the cube, relative_direction=InFront or Forward, and uses a reasonable distance if no distance is specified.");
            sb.AppendLine("For relative directions, use Forward/Back/Left/Right/Up/Down/InFront/Behind from Me's local frame. 'my left' means Me's local left, not world left.");
            sb.AppendLine("For delete/remove/destroy requests, use DeleteTarget. Examples: 'delete all audio' => target_reference=All, target_name='audio'. 'delete all cubes' => target_reference=All, target_name='cubes'. 'delete the cube' => target_reference=NamedObject, target_name='cube'. Never map delete requests to world unload unless the user says end program.");
            sb.AppendLine("For requests to open the real-world headset camera viewfinder/window for preview, use CaptureHeadsetCamera.");
            sb.AppendLine("When the live headset camera preview is active, approval words like 'OK', 'shoot', 'take it', 'capture', or 'capture now' should use ConfirmHeadsetCameraCapture.");
            sb.AppendLine("For requests to capture, save, create, or update the currently loaded world's thumbnail/card image, use CaptureWorldThumbnail. This is a virtual world screenshot, not the headset passthrough camera.");
            sb.AppendLine("For requests to capture, save, create, or update the currently loaded world's panorama/pano, use CaptureWorldPanorama. This is a virtual 360 panorama saved with the world, not the headset passthrough camera.");
            sb.AppendLine("For requests to search the internet/Pixabay for an image, use SearchImages and put the search phrase in image_search_query.");
            sb.AppendLine("For requests like 'use this image' or 'select this image' while the image search panel is active, use SelectImageSearchResult.");
            sb.AppendLine("For 'next image' or 'previous image' while browsing image search results, use NextImageSearchResult or PreviousImageSearchResult.");
            sb.AppendLine("For requests to create a world from the currently previewed/captured headset camera image, use GenerateWorldFromCapture and put any style or content guidance in world_prompt.");
            sb.AppendLine("For requests to create a 3D object from the currently previewed/captured headset camera image, use GenerateObjectFromCapture and put object/style guidance in object_name or world_prompt.");
            sb.AppendLine("For requests to add, create, download, generate, or place new sounds/audio/ambience, use CreateAudioSource. This must not generate or alter the visual world.");
            sb.AppendLine("For requests like 'play seagull sounds' or 'play [name]', first treat the named sound as an existing audio source and use ControlAudioSource with audio_control=play_now. Do not create/download a new sound unless the user clearly says add/create/get/download/generate a new one.");
            sb.AppendLine("For collective audio requests, put separate layers in sound_prompts, such as birds, river rapids, and wind in trees. Set sound_count to the number of layers.");
            sb.AppendLine("Use sound_provider=auto unless the user asks for a specific library. Use xeno-canto for bird species or birdsong when appropriate.");
            sb.AppendLine("For requests to change an existing sound, such as make it louder, quieter, mute, unmute, play now, make ambient/2D, make spatialized/3D, or set interval timing, use ControlAudioSource.");
            sb.AppendLine("For requests that target every sound, such as 'stop all sounds', 'mute all sounds', or 'make all audio quieter', use ControlAudioSource with target_reference=All and target_name='all'. This applies only to world/program audio, not UX audio such as TTS or button cues.");
            sb.AppendLine("For 'stop [sound]' or 'stop all sounds', use audio_control=stop. Stop means stop playback but do not destroy the audio source.");
            sb.AppendLine("For audio_playback_mode, choose loop for continuous ambience like ocean, rain, river, wind, crowds, or machinery; once for one-shot effects; interval for fixed repeated playback; random_interval for natural calls like birds, frogs, thunder, bells, or sparse events.");
            // Domain-specific routing hints live in OpenAiSpeechIntentConfig.additionalDeveloperInstructions
            // so they can be edited in the Inspector without recompiling.
            if (!string.IsNullOrWhiteSpace(config.additionalDeveloperInstructions))
            {
                sb.AppendLine();
                sb.AppendLine(config.additionalDeveloperInstructions.Trim());
            }
            return sb.ToString();
        }

        private string BuildUserMessage(string transcript, SpatialSnapshot spatial, SceneSemanticSnapshot scene)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Interpret this utterance for the Unity app.");
            sb.AppendLine();
            sb.AppendLine("Transcript:");
            sb.AppendLine(transcript);
            sb.AppendLine();
            sb.AppendLine("Spatial context JSON:");
            sb.AppendLine(SpeechIntentJson.Serialize(spatial, Formatting.Indented));
            sb.AppendLine();
            sb.AppendLine("Scene context JSON:");
            sb.AppendLine(SpeechIntentJson.Serialize(scene, Formatting.Indented));
            return sb.ToString();
        }

        private string BuildCommandJsonSchema()
        {
            return @"
{
  ""type"": ""object"",
  ""additionalProperties"": false,
  ""required"": [
    ""transcript"",
    ""intent"",
    ""confidence"",
    ""should_execute"",
    ""spoken_response"",
    ""world_prompt"",
    ""image_search_query"",
    ""ui_panel"",
    ""target_entity"",
    ""lighting_preset"",
    ""target_hand"",
    ""spatial_reference"",
    ""body_anchor"",
    ""object_name"",
    ""placement_mode"",
    ""target_reference"",
    ""target_name"",
    ""target_material_prompt"",
    ""scale_multiplier"",
    ""reset_to_default_scale"",
    ""rotation_axis"",
    ""rotation_degrees"",
    ""relative_direction"",
    ""relative_distance_meters"",
    ""material_prompt"",
    ""content_path"",
    ""generation_model"",
    ""config_name"",
    ""sound_prompt"",
    ""sound_prompts"",
    ""sound_category"",
    ""sound_species"",
    ""sound_provider"",
    ""sound_count"",
    ""sound_max_duration_seconds"",
    ""audio_loop"",
    ""audio_volume"",
    ""audio_volume_delta"",
    ""audio_playback_mode"",
    ""audio_interval_seconds"",
    ""audio_interval_variance_seconds"",
    ""audio_control"",
    ""audio_muted"",
    ""audio_play_now"",
    ""audio_spatial_blend"",
    ""reason""
  ],
  ""properties"": {
    ""transcript"": { ""type"": ""string"" },
    ""intent"": {
      ""type"": ""string"",
      ""enum"": [
        ""Unknown"",
        ""AskClarification"",
        ""GenerateWorld"",
        ""SwitchToStaticWorld"",
        ""ShowUi"",
        ""SetSunDirection"",
        ""SetLightingPreset"",
        ""PlaceObject"",
        ""MoveTarget"",
        ""ScaleTarget"",
        ""RotateTarget"",
        ""Show3dWorld"",
        ""ShowPanoWorld"",
        ""ResetTransform"",
        ""LoadSplat"",
        ""LoadPanorama"",
        ""SetGenerationModel"",
        ""ShowMeshWorld"",
        ""SaveWorldConfig"",
        ""LoadWorldConfig"",
        ""CreateAudioSource"",
        ""ControlAudioSource"",
        ""DeleteTarget"",
        ""QuitApplication"",
        ""CaptureHeadsetCamera"",
        ""ConfirmHeadsetCameraCapture"",
        ""GenerateWorldFromCapture"",
        ""GenerateObjectFromCapture"",
        ""SearchImages"",
        ""SelectImageSearchResult"",
        ""NextImageSearchResult"",
        ""PreviousImageSearchResult"",
        ""CaptureWorldThumbnail"",
        ""CaptureWorldPanorama"",
        ""SetTargetMaterial""
      ]
    },
    ""confidence"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 1
    },
    ""should_execute"": { ""type"": ""boolean"" },
    ""spoken_response"": { ""type"": ""string"" },
    ""world_prompt"": { ""type"": ""string"" },
    ""image_search_query"": { ""type"": ""string"" },
    ""ui_panel"": { ""type"": ""string"" },
    ""target_entity"": { ""type"": ""string"" },
    ""lighting_preset"": { ""type"": ""string"" },
    ""target_hand"": {
      ""type"": ""string"",
      ""enum"": [""None"", ""Left"", ""Right"", ""Either"", ""Both""]
    },
    ""spatial_reference"": {
      ""type"": ""string"",
      ""enum"": [""None"", ""PointingRay"", ""PointingHit"", ""HandMidpoint"", ""HeadForward"", ""WorldOrigin"", ""RelativeToMe"", ""BodyAnchor""]
    },
    ""body_anchor"": {
      ""type"": ""string"",
      ""enum"": [""None"", ""Head"", ""LeftHand"", ""RightHand""]
    },
    ""object_name"": { ""type"": ""string"" },
    ""placement_mode"": { ""type"": ""string"" },
    ""target_reference"": {
      ""type"": ""string"",
      ""enum"": [""None"", ""CurrentWorld"", ""LastCreatedObject"", ""LastInteractedTarget"", ""LastCreatedOrInteracted"", ""PointedObject"", ""NamedObject"", ""CurrentSelection"", ""All""]
    },
    ""target_name"": { ""type"": ""string"" },
    ""target_material_prompt"": {
      ""type"": ""string"",
      ""description"": ""Material/color/finish qualifier used to find the target, e.g. red in 'move the red cube'. Empty when no material qualifier is used.""
    },
    ""scale_multiplier"": { ""type"": ""number"" },
    ""reset_to_default_scale"": { ""type"": ""boolean"" },
    ""rotation_axis"": {
      ""type"": ""string"",
      ""enum"": [""None"", ""X"", ""Y"", ""Z""]
    },
    ""rotation_degrees"": { ""type"": ""number"" },
    ""relative_direction"": {
      ""type"": ""string"",
      ""enum"": [""None"", ""Forward"", ""Back"", ""Left"", ""Right"", ""Up"", ""Down"", ""InFront"", ""Behind""]
    },
    ""relative_distance_meters"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 100
    },
    ""material_prompt"": {
      ""type"": ""string"",
      ""description"": ""Color/finish phrase for SetTargetMaterial, e.g. red, blue metallic, matte black. Empty for non-material intents.""
    },
    ""content_path"": {
      ""type"": ""string"",
      ""description"": ""File name, relative path, or full URL for LoadSplat or LoadPanorama.""
    },
    ""generation_model"": {
      ""type"": ""string"",
      ""description"": ""Target model tier for SetGenerationModel intent. One of: draft, fast, standard, high.""
    },
    ""config_name"": {
      ""type"": ""string"",
      ""description"": ""Name for save-as or load-by-name. Empty string for plain save or to open the panel.""
    },
    ""sound_prompt"": {
      ""type"": ""string"",
      ""description"": ""Primary sound prompt for CreateAudioSource. Empty for non-audio intents.""
    },
    ""sound_prompts"": {
      ""type"": ""array"",
      ""description"": ""Multiple separate sound layers for CreateAudioSource, e.g. birds in trees, river rapids, wind."",
      ""items"": { ""type"": ""string"" }
    },
    ""sound_category"": {
      ""type"": ""string"",
      ""description"": ""Optional category such as ambience, birds, water, weather, machinery, crowd.""
    },
    ""sound_species"": {
      ""type"": ""string"",
      ""description"": ""Optional bird/wildlife species for xeno-canto search.""
    },
    ""sound_provider"": {
      ""type"": ""string"",
      ""description"": ""auto, freesound, openverse, or xeno-canto.""
    },
    ""sound_count"": {
      ""type"": ""integer"",
      ""minimum"": 0,
      ""maximum"": 5
    },
    ""sound_max_duration_seconds"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 300
    },
    ""audio_loop"": { ""type"": ""boolean"" },
    ""audio_volume"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 1
    },
    ""audio_volume_delta"": {
      ""type"": ""number"",
      ""minimum"": -1,
      ""maximum"": 1,
      ""description"": ""Relative volume change for ControlAudioSource. Use positive for louder, negative for quieter.""
    },
    ""audio_playback_mode"": {
      ""type"": ""string"",
      ""description"": ""auto, loop, once, interval, or random_interval.""
    },
    ""audio_interval_seconds"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 3600
    },
    ""audio_interval_variance_seconds"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 3600
    },
    ""audio_control"": {
      ""type"": ""string"",
      ""description"": ""For ControlAudioSource: stop, louder, softer, mute, unmute, set_volume, set_interval, set_random_interval, make_ambient, make_spatialized, play_now, play_once, or loop. Empty for non-audio-control intents.""
    },
    ""audio_muted"": { ""type"": ""boolean"" },
    ""audio_play_now"": { ""type"": ""boolean"" },
    ""audio_spatial_blend"": {
      ""type"": ""number"",
      ""minimum"": 0,
      ""maximum"": 1
    },
    ""reason"": { ""type"": ""string"" }
  }
}";
        }
    }
}

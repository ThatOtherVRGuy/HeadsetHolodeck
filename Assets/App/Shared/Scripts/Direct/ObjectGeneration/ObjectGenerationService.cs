using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using Holodeck.Save;
using SpeechIntent;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class ObjectGenerationService : MonoBehaviour
    {
        public static event Action<string> UserFacingFailure;

        [Header("Dependencies")]
        public HeadsetCameraCaptureService captureService;
        public ObjectPlacementController objectPlacement;
        public HitemObjectGenerationProvider hitemProvider;
        public ThreeDAIStudioObjectGenerationProvider threeDAIStudioProvider;
        public ThreeDAIStudioCreditService threeDAIStudioCreditService;
        public CachedObjectStore cachedObjectStore;
        public ObjectThumbnailCaptureService thumbnailCaptureService;
        public WorldConfigAutoSave worldConfigAutoSave;
        public InteractionMemory interactionMemory;

        [Header("Placement")]
        public ObjectGenerationProviderId providerMode = ObjectGenerationProviderId.Auto;
        public Transform defaultParent;

        [Tooltip("Distance in front of the viewer for a generated object that was given no location. " +
                 "Owner decided 1 m: close enough to inspect, far enough not to be in your face.")]
        public float defaultDistanceMeters = 1f;

        [Tooltip("How far to look downward for a surface under a newly placed object, so it starts resting " +
                 "there instead of dropping from head height and bouncing away.")]
        public float settleRaycastMeters = 10f;

        public float defaultHeightOffsetMeters = 0f;
        public float targetMaxSizeMeters = 0.75f;

        [Header("Generated Object Physics")]
        public bool forceGeneratedObjectsPhysical = true;
        public bool generatedObjectsUseGravity = true;
        public bool generatedObjectsKinematic = false;
        public float generatedObjectMass = 1f;
        public bool generatedObjectAddColliderIfMissing = true;
        public bool generatedObjectAddRigidbody = true;

        [Header("Progress Spinner")]
        public ObjectGenerationSpinnerController objectGenerationSpinnerPrefab;
        public float defaultSpinnerDiameterMeters = 1f;
        public float spinnerHeightOffsetMeters = 0f;
        public bool useTargetMaxSizeForSpinner = true;

        [Header("Long Running Jobs")]
        [Min(5f)] public float longWaitPromptSeconds = 180f;
        [Min(5f)] public float longWaitRepeatSeconds = 120f;
        [Min(10f)] public float hardTimeoutSeconds = 900f;
        [Min(1f)] public float importTimeoutSeconds = 45f;
        [Min(1f)] public float thumbnailTimeoutSeconds = 15f;

        [Header("Thumbnails")]
        public bool captureThumbnailsForGeneratedObjects = true;

        Coroutine _activeJob;
        Coroutine _activeProviderCoroutine;
        string _activeObjectName = "";
        string _activeProviderName = "";
        ObjectGenerationSpinnerController _activeSpinner;
        float _activeJobStartedAt;
        float _nextLongWaitPromptAt;
        bool _activeJobCancelRequested;

        public bool IsBusy => _activeJob != null;
        public bool IsConfigured => ResolveProvider() != null;
        public string LastFailureMessage { get; private set; } = "";
        public string LastStatusMessage { get; private set; } = "";

        public string BusyMessage
        {
            get
            {
                string objectName = string.IsNullOrWhiteSpace(_activeObjectName) ? "an object" : _activeObjectName;
                return $"Object generation is already running for {objectName}. Please wait.";
            }
        }

        public static ObjectGenerationService GetOrCreate()
        {
            ObjectGenerationService existing = FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
            if (existing != null)
                return existing;

            GameObject go = new GameObject("ObjectGenerationService");
            return go.AddComponent<ObjectGenerationService>();
        }

        public bool GenerateFromLastCapture(string prompt)
        {
            if (IsBusy)
            {
                SetWarning(BusyMessage);
                return false;
            }

            ResolveDependencies();
            if (captureService == null || captureService.LastCapturedTexture == null)
            {
                SetWarning("No image selected for object generation.");
                return false;
            }

            IObjectGenerationProvider provider = ResolveProvider();
            if (provider == null)
            {
                SetWarning("Object generator credentials missing. Set THREEDAISTUDIO_API_KEY, or HITEM_ACCESS_KEY and HITEM_SECRET_KEY.");
                return false;
            }

            ObjectGenerationRequest request = new ObjectGenerationRequest
            {
                image = captureService.LastCapturedTexture,
                imageSource = captureService.LastCaptureSource,
                prompt = prompt ?? "",
                objectName = string.IsNullOrWhiteSpace(prompt) ? "Generated Object" : prompt.Trim(),
                fileName = BuildImageFileName(prompt)
            };

            BeginActiveJob(provider, request, ObjectGenerationCapability.ImageTo3D);
            _activeJob = StartCoroutine(GenerateCoroutine(provider, request, ObjectGenerationCapability.ImageTo3D, null, null));
            return true;
        }

        public bool GenerateFromText(string prompt, VoiceIntentCommand placementCommand, SpatialSnapshot spatial, bool allowCacheReuse = true)
        {
            if (IsBusy)
            {
                SetWarning(BusyMessage);
                return false;
            }

            ResolveDependencies();

            string cleanPrompt = string.IsNullOrWhiteSpace(prompt) ? "Generated Object" : prompt.Trim();
            ObjectGenerationRequest request = new ObjectGenerationRequest
            {
                prompt = cleanPrompt,
                objectName = cleanPrompt,
                fileName = $"{SanitizeName(cleanPrompt)}.glb"
            };

            // Consult the local cache before provider credentials and before the credit preflight: asking
            // for the same model twice should re-import what was already paid for, not buy it again.
            // allowCacheReuse is false when the user has explicitly asked for a new model after being
            // offered the cached one.
            CachedObjectRecord cached = allowCacheReuse ? FindReusableCachedObject(cleanPrompt) : null;
            if (cached != null)
            {
                SetInfo($"Using the cached model for '{cleanPrompt}'; no generation was requested.");
                ArchStatusBus.Info($"Reusing cached model for {cleanPrompt}.", "OBJECT");
                BeginActiveJob(null, request, ObjectGenerationCapability.TextTo3D);
                _activeJob = StartCoroutine(ReuseCachedObjectCoroutine(cached, request, placementCommand, spatial));
                return true;
            }

            IObjectGenerationProvider provider = ResolveProvider(ObjectGenerationCapability.TextTo3D);
            if (provider == null)
            {
                SetWarning("Text object generator credentials missing. Set THREEDAISTUDIO_API_KEY.");
                return false;
            }

            BeginActiveJob(provider, request, ObjectGenerationCapability.TextTo3D);
            _activeJob = StartCoroutine(GenerateCoroutine(provider, request, ObjectGenerationCapability.TextTo3D, placementCommand, spatial));
            return true;
        }

        /// <summary>
        /// Imports a model file that is already on disk, e.g. a sideloaded "castle.glb". Uses the same
        /// import path as a generated model, so the result is wrapped, placed, saved and grabbable exactly
        /// like one; no provider is contacted and no credits are spent.
        /// </summary>
        public bool ImportModelFile(string filePath, VoiceIntentCommand placementCommand)
        {
            if (IsBusy)
            {
                SetWarning(BusyMessage);
                return false;
            }

            ResolveDependencies();

            if (!SideloadPathResolver.TryResolve(filePath, out string resolved, out string note))
            {
                SetWarning($"I couldn't find {filePath} in the sideload folder.", speak: true);
                ArchStatusBus.Warning($"Sideload file not found: {filePath}", "OBJECT");
                return false;
            }

            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                SetWarning($"The sideloaded file for {filePath} is missing.", speak: true);
                return false;
            }

            string objectName = Path.GetFileNameWithoutExtension(resolved);
            var request = new ObjectGenerationRequest
            {
                prompt = objectName,
                objectName = objectName,
                fileName = Path.GetFileName(resolved)
            };

            Debug.Log($"[ObjectGenerationService] Sideloading '{resolved}' ({note}).", this);
            BeginActiveJob(null, request, ObjectGenerationCapability.ImageTo3D);
            _activeJob = StartCoroutine(ImportModelFileCoroutine(resolved, request, placementCommand));
            return true;
        }

        IEnumerator ImportModelFileCoroutine(string path, ObjectGenerationRequest request, VoiceIntentCommand placementCommand)
        {
            byte[] bytes = null;
            bool readTimedOut = false;

            Task<byte[]> read = Task.Run(() => File.ReadAllBytes(path));
            yield return WaitForTask(read, importTimeoutSeconds, "read sideloaded model", timedOut => readTimedOut = timedOut);

            if (!readTimedOut && read.Status == TaskStatus.RanToCompletion)
            {
                try
                {
                    bytes = read.Result;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjectGenerationService] Sideload read failed: {ex.Message}", this);
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                SetWarning($"I could not read {request.fileName}.", speak: true);
                ClearActiveJob();
                yield break;
            }

            var result = new ObjectGenerationResult
            {
                success = true,
                providerName = "sideload",
                taskId = string.Empty,
                modelUrl = string.Empty,
                modelBytes = bytes
            };

            GameObject instance = null;
            string importError = null;
            SetInfo($"Importing sideloaded model: {request.objectName}.");
            yield return ImportGlbCoroutine(result, request, null, placementCommand, null, (go, error) =>
            {
                instance = go;
                importError = error;
            });

            if (instance == null)
            {
                SetWarning(string.IsNullOrWhiteSpace(importError) ? "Sideloaded model import failed." : importError, speak: true);
                ClearActiveJob();
                yield break;
            }

            interactionMemory?.RegisterCreatedObject(instance);
            worldConfigAutoSave?.RegisterObjectMutation(
                BuildGeneratedObjectCommand(request, placementCommand, ObjectGenerationCapability.ImageTo3D), instance);
            TryRefreshObjectCatalogs();
            HideSpinner();
            SetSuccess($"Loaded {request.objectName} from the sideload folder.");
            ClearActiveJob();
        }

        /// <summary>
        /// Returns a cached record whose recorded name matches the request and whose model file still
        /// exists, or null. A record without a readable GLB is not reusable, so it does not block a fresh
        /// generation.
        /// </summary>
        CachedObjectRecord FindReusableCachedObject(string prompt)
        {
            if (cachedObjectStore == null || string.IsNullOrWhiteSpace(prompt))
                return null;

            System.Collections.Generic.List<CachedObjectRecord> matches = cachedObjectStore.FindAllByName(prompt);
            if (matches == null)
                return null;

            foreach (CachedObjectRecord record in matches)
            {
                string path = cachedObjectStore.GetModelAbsolutePath(record);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return record;
            }

            return null;
        }

        /// <summary>
        /// Re-imports an already-generated model from the object cache. Mirrors the tail of the generation
        /// path so a reused model is registered as a world mutation exactly like a fresh one.
        /// </summary>
        IEnumerator ReuseCachedObjectCoroutine(
            CachedObjectRecord record,
            ObjectGenerationRequest request,
            VoiceIntentCommand placementCommand,
            SpatialSnapshot spatial)
        {
            string path = cachedObjectStore != null ? cachedObjectStore.GetModelAbsolutePath(record) : null;
            byte[] bytes = null;
            bool readTimedOut = false;

            Task<byte[]> read = Task.Run(() => File.ReadAllBytes(path));
            yield return WaitForTask(read, importTimeoutSeconds, "read cached model", timedOut => readTimedOut = timedOut);

            if (!readTimedOut && read.Status == TaskStatus.RanToCompletion)
            {
                try
                {
                    bytes = read.Result;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjectGenerationService] Cached model read failed: {ex.Message}", this);
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                SetWarning($"The cached model for {request.objectName} could not be read, and no new generation was started.", speak: true);
                ClearActiveJob();
                yield break;
            }

            var result = new ObjectGenerationResult
            {
                success = true,
                providerName = record.provider,
                taskId = record.task_id,
                modelUrl = record.model_url,
                modelBytes = bytes
            };

            GameObject instance = null;
            string importError = null;
            SetInfo($"Importing cached model: {request.objectName}.");
            yield return ImportGlbCoroutine(result, request, record, placementCommand, spatial, (go, error) =>
            {
                instance = go;
                importError = error;
            });

            if (instance == null)
            {
                SetWarning(string.IsNullOrWhiteSpace(importError) ? "Cached model import failed." : importError, speak: true);
                ClearActiveJob();
                yield break;
            }

            interactionMemory?.RegisterCreatedObject(instance);
            worldConfigAutoSave?.RegisterObjectMutation(
                BuildGeneratedObjectCommand(request, placementCommand, ObjectGenerationCapability.TextTo3D), instance);
            TryRefreshObjectCatalogs();
            HideSpinner();
            SetSuccess($"Using cached model: {request.objectName}. No credits were spent.");
            ClearActiveJob();
        }

        IEnumerator GenerateCoroutine(
            IObjectGenerationProvider provider,
            ObjectGenerationRequest request,
            ObjectGenerationCapability capability,
            VoiceIntentCommand placementCommand,
            SpatialSnapshot spatial)
        {
            SetInfo($"Creating 3D object: {request.objectName}.");
            bool preflightOk = true;
            string preflightError = null;
            yield return PreflightCredits(provider, capability, (ok, error) =>
            {
                preflightOk = ok;
                preflightError = error;
            });

            if (!preflightOk)
            {
                SetWarning(preflightError, speak: true);
                ClearActiveJob();
                yield break;
            }

            ShowSpinner(placementCommand, spatial);
            ObjectGenerationResult result = null;
            bool providerDone = false;
            IEnumerator providerRoutine = capability == ObjectGenerationCapability.TextTo3D
                ? provider.GenerateFromText(request, r => result = r)
                : provider.GenerateFromImage(request, r => result = r);
            _activeProviderCoroutine = StartCoroutine(RunProviderCoroutine(providerRoutine, () => providerDone = true));

            while (!providerDone)
            {
                if (_activeJobCancelRequested)
                {
                    StopActiveProviderCoroutine();
                    HideSpinner();
                    ClearActiveJob();
                    yield break;
                }

                if (hardTimeoutSeconds > 0f && Time.realtimeSinceStartup - _activeJobStartedAt >= hardTimeoutSeconds)
                {
                    StopActiveProviderCoroutine();
                    HideSpinner();
                    SetWarning($"Object generation timed out while creating {FirstNonEmpty(_activeObjectName, request.objectName, "object")}. Please try again later.", speak: true);
                    ClearActiveJob();
                    yield break;
                }

                MaybePromptForLongWait();
                yield return null;
            }
            _activeProviderCoroutine = null;

            if (result == null || !result.success)
            {
                string error = result != null ? result.error : "Object generation failed.";
                HideSpinner();
                SetWarning(error, speak: true);
                ClearActiveJob();
                yield break;
            }

            if (!LooksLikeGlb(result.modelBytes, out string validationError))
            {
                HideSpinner();
                SetWarning($"Generated model arrived, but it was not a usable GLB: {validationError}", speak: true);
                ClearActiveJob();
                yield break;
            }

            SetInfo($"Object generation finished. Caching {request.objectName}.");
            CachedObjectRecord cachedRecord = TryCacheGeneratedObject(request, result);
            if (cachedRecord == null)
            {
                HideSpinner();
                SetWarning("Generated model arrived, but it could not be cached.", speak: true);
                ClearActiveJob();
                yield break;
            }

            GameObject instance = null;
            string importError = null;
            SetInfo($"Importing generated object: {request.objectName}.");
            yield return ImportGlbCoroutine(result, request, cachedRecord, placementCommand, spatial, (go, error) =>
            {
                instance = go;
                importError = error;
            });

            if (instance == null)
            {
                HideSpinner();
                string cachedNote = cachedRecord != null ? " The model was saved in the object library for retry." : "";
                SetWarning((string.IsNullOrWhiteSpace(importError) ? "Generated object import failed." : importError) + cachedNote, speak: true);
                ClearActiveJob();
                yield break;
            }

            interactionMemory?.RegisterCreatedObject(instance);
            worldConfigAutoSave?.RegisterObjectMutation(BuildGeneratedObjectCommand(request, placementCommand, capability), instance);
            if (captureThumbnailsForGeneratedObjects && cachedRecord != null)
                yield return CaptureThumbnail(cachedRecord, instance);
            TryRefreshObjectCatalogs();
            HideSpinner();
            SetSuccess($"Generated object is ready: {request.objectName}.");
            ClearActiveJob();
        }

        void BeginActiveJob(IObjectGenerationProvider provider, ObjectGenerationRequest request, ObjectGenerationCapability capability)
        {
            LastFailureMessage = "";
            _activeObjectName = request != null ? request.objectName : "";
            _activeProviderName = provider != null ? provider.ProviderName : "";
            _activeJobStartedAt = Time.realtimeSinceStartup;
            _nextLongWaitPromptAt = _activeJobStartedAt + Mathf.Max(5f, longWaitPromptSeconds);
            _activeJobCancelRequested = false;
            SetInfo($"Started {FormatCapability(capability)} for {FirstNonEmpty(_activeObjectName, "object")} with {FirstNonEmpty(_activeProviderName, "object provider")}.");
        }

        void ClearActiveJob()
        {
            _activeJob = null;
            _activeProviderCoroutine = null;
            _activeObjectName = "";
            _activeProviderName = "";
            _activeJobStartedAt = 0f;
            _nextLongWaitPromptAt = 0f;
            _activeJobCancelRequested = false;
        }

        public bool CancelActiveGeneration(string reason = null)
        {
            if (!IsBusy)
                return false;

            _activeJobCancelRequested = true;
            if (_activeJob != null)
                StopCoroutine(_activeJob);
            StopActiveProviderCoroutine();

            HideSpinner();
            string message = string.IsNullOrWhiteSpace(reason)
                ? $"Cancelled object generation for {FirstNonEmpty(_activeObjectName, "object")}."
                : reason;
            SetWarning(message, speak: true);
            ClearActiveJob();
            return true;
        }

        public bool ContinueActiveGeneration()
        {
            if (!IsBusy)
                return false;

            _nextLongWaitPromptAt = Time.realtimeSinceStartup + Mathf.Max(5f, longWaitRepeatSeconds);
            string message = $"Okay, I will keep waiting for {FirstNonEmpty(_activeObjectName, "the object")}.";
            SetInfo(message);
            UserFacingFailure?.Invoke(message);
            return true;
        }

        void OnDisable()
        {
            if (_activeJob != null)
                StopCoroutine(_activeJob);
            StopActiveProviderCoroutine();
            HideSpinner();
            ClearActiveJob();
        }

        void OnDestroy()
        {
            if (_activeJob != null)
                StopCoroutine(_activeJob);
            StopActiveProviderCoroutine();
            HideSpinner();
            ClearActiveJob();
        }

        void StopActiveProviderCoroutine()
        {
            if (_activeProviderCoroutine == null)
                return;
            StopCoroutine(_activeProviderCoroutine);
            _activeProviderCoroutine = null;
        }

        IEnumerator RunProviderCoroutine(IEnumerator providerRoutine, Action onDone)
        {
            while (providerRoutine != null)
            {
                bool hasNext;
                try
                {
                    hasNext = providerRoutine.MoveNext();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[ObjectGenerationService] Object generation provider threw an exception: " + ex, this);
                    break;
                }

                if (!hasNext)
                    break;

                yield return providerRoutine.Current;
            }

            onDone?.Invoke();
        }

        void MaybePromptForLongWait()
        {
            if (_nextLongWaitPromptAt <= 0f || Time.realtimeSinceStartup < _nextLongWaitPromptAt)
                return;

            string message = $"Still creating {FirstNonEmpty(_activeObjectName, "the object")} with {FirstNonEmpty(_activeProviderName, "the object generator")}. Say continue waiting or cancel object generation.";
            LastStatusMessage = message;
            Debug.LogWarning("[ObjectGenerationService] " + message, this);
            ArchStatusBus.Warning(message, "OBJECT");
            UserFacingFailure?.Invoke(message);
            _nextLongWaitPromptAt = Time.realtimeSinceStartup + Mathf.Max(5f, longWaitRepeatSeconds);
        }

        void ShowSpinner(VoiceIntentCommand placementCommand, SpatialSnapshot spatial)
        {
            HideSpinner();
            ResolveSpinnerPose(placementCommand, spatial, out Vector3 position, out Quaternion rotation);
            position += Vector3.up * spinnerHeightOffsetMeters;
            float diameter = ResolveSpinnerDiameter();
            _activeSpinner = ObjectGenerationSpinnerController.CreateRuntimeSpinner(
                position,
                rotation,
                diameter,
                objectGenerationSpinnerPrefab);
            SetInfo($"Object generation marker placed at {position}.");
        }

        void HideSpinner()
        {
            if (_activeSpinner == null)
                return;

            _activeSpinner.Dismiss();
            _activeSpinner = null;
        }

        void ResolveSpinnerPose(VoiceIntentCommand placementCommand, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            if (placementCommand != null)
            {
                objectPlacement ??= FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
                if (objectPlacement != null && objectPlacement.TryResolvePlacementPose(placementCommand, spatial, out position, out rotation))
                    return;
            }

            position = ResolveDefaultPosition(spatial);
            rotation = ResolveDefaultRotation(spatial);
        }

        float ResolveSpinnerDiameter()
        {
            if (useTargetMaxSizeForSpinner && targetMaxSizeMeters > 0f)
                return targetMaxSizeMeters;
            return Mathf.Max(0.05f, defaultSpinnerDiameterMeters);
        }

        void SetInfo(string message)
        {
            LastStatusMessage = message ?? "";
            Debug.Log("[ObjectGenerationService] " + LastStatusMessage, this);
            ArchStatusBus.Info(LastStatusMessage, "OBJECT");
        }

        void SetSuccess(string message)
        {
            LastStatusMessage = message ?? "";
            Debug.Log("[ObjectGenerationService] " + LastStatusMessage, this);
            ArchStatusBus.Success(LastStatusMessage, "OBJECT");
        }

        void SetWarning(string message, bool speak = false)
        {
            LastFailureMessage = string.IsNullOrWhiteSpace(message) ? "Object generation failed." : message;
            LastStatusMessage = LastFailureMessage;
            Debug.LogWarning("[ObjectGenerationService] " + LastFailureMessage, this);
            ArchStatusBus.Warning(LastFailureMessage, "OBJECT");
            if (speak)
                UserFacingFailure?.Invoke(LastFailureMessage);
        }

        void AnnounceNonFatalWarning(string message)
        {
            string clean = string.IsNullOrWhiteSpace(message) ? "Object generation completed with a warning." : message;
            LastStatusMessage = clean;
            Debug.LogWarning("[ObjectGenerationService] " + clean, this);
            ArchStatusBus.Warning(clean, "OBJECT");
            UserFacingFailure?.Invoke(clean);
        }

        IEnumerator WaitForTask(Task task, float timeoutSeconds, string phase, Action<bool> onComplete)
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, timeoutSeconds);
            while (task != null && !task.IsCompleted)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogWarning($"[ObjectGenerationService] Timed out during {phase}.", this);
                    onComplete?.Invoke(true);
                    yield break;
                }

                yield return null;
            }

            onComplete?.Invoke(false);
        }

        static bool LooksLikeGlb(byte[] bytes, out string error)
        {
            error = "";
            if (bytes == null || bytes.Length == 0)
            {
                error = "empty model data";
                return false;
            }

            if (bytes.Length < 20)
            {
                error = $"model data was too small ({bytes.Length} bytes)";
                return false;
            }

            if (bytes[0] != (byte)'g' || bytes[1] != (byte)'l' || bytes[2] != (byte)'T' || bytes[3] != (byte)'F')
            {
                error = "download did not start with a GLB header";
                return false;
            }

            return true;
        }

        IEnumerator ImportGlbCoroutine(
            ObjectGenerationResult result,
            ObjectGenerationRequest request,
            CachedObjectRecord cachedRecord,
            VoiceIntentCommand placementCommand,
            SpatialSnapshot spatial,
            Action<GameObject, string> onComplete,
            Action<GameObject> onRootCreated = null)
        {
            ArchStatusBus.Info("Importing generated object.", "OBJECT");

            GameObject root = null;
            GltfImport gltf = null;
            bool handedOff = false;

            try
            {
                root = new GameObject($"GeneratedObject_{SanitizeName(request.objectName)}");
                root.transform.SetParent(defaultParent != null ? defaultParent : ResolveDefaultParent(), true);
                ResolveImportPose(placementCommand, spatial, out Vector3 position, out Quaternion rotation);
                root.transform.SetPositionAndRotation(position, rotation);
                onRootCreated?.Invoke(root);

                gltf = new GltfImport();
                Task<bool> loadTask = gltf.LoadGltfBinary(result.modelBytes);
                yield return WaitForTask(loadTask, importTimeoutSeconds, "parse generated GLB", timedOut =>
                {
                    if (timedOut)
                        onComplete?.Invoke(null, $"Timed out while parsing generated GLB after {importTimeoutSeconds:0.#} seconds.");
                });
                if (!loadTask.IsCompleted)
                    yield break;

                if (loadTask.IsFaulted || !loadTask.Result)
                {
                    string message = loadTask.Exception != null ? loadTask.Exception.GetBaseException().Message : "glTFast failed to parse generated GLB.";
                    onComplete?.Invoke(null, message);
                    yield break;
                }

                Task instantiateTask = gltf.InstantiateSceneAsync(root.transform);
                yield return WaitForTask(instantiateTask, importTimeoutSeconds, "instantiate generated GLB", timedOut =>
                {
                    if (timedOut)
                        onComplete?.Invoke(null, $"Timed out while instantiating generated GLB after {importTimeoutSeconds:0.#} seconds.");
                });
                if (!instantiateTask.IsCompleted)
                    yield break;

                if (instantiateTask.IsFaulted)
                {
                    string message = instantiateTask.Exception != null ? instantiateTask.Exception.GetBaseException().Message : "glTFast failed to instantiate generated GLB.";
                    onComplete?.Invoke(null, message);
                    yield break;
                }

                GeneratedGltfHandle handle = root.AddComponent<GeneratedGltfHandle>();
                handle.gltf = gltf;
                gltf = null;
                handedOff = true;
                ApplyCachedObjectReference(root, cachedRecord);
                LogGeneratedObjectDiagnostics(root, "after-instantiate");
                bool repairedTextures = GeneratedObjectGlbTextureRepair.RepairMissingBaseColorTextures(
                    root,
                    result.modelBytes,
                    objectPlacement != null ? objectPlacement.defaultGeneratedColor : Color.white);
                if (repairedTextures)
                    Debug.Log("[ObjectGenerationDiagnostics] Applied embedded GLB base-color texture repair.", this);
                LogGeneratedObjectDiagnostics(root, "after-texture-repair");
                NormalizeSize(root);
                LogGeneratedObjectDiagnostics(root, "after-normalize-size");
                InteractableObjectWrapper.NormalizeRendering(
                    root,
                    objectPlacement != null ? objectPlacement.defaultGeneratedMaterial : null,
                    objectPlacement != null ? objectPlacement.defaultGeneratedColor : Color.gray);
                LogGeneratedObjectDiagnostics(root, "after-normalize-rendering");
                EnsureGeneratedObjectInteraction(root, request.objectName);
                SettleOnSurface(root);
                LogGeneratedObjectDiagnostics(root, "after-interaction-physics");
                onComplete?.Invoke(root, null);
            }
            finally
            {
                if (!handedOff)
                {
                    gltf?.Dispose();
                    if (root != null)
                        Destroy(root);
                }
            }
        }

        public IEnumerator ImportCachedObject(
            CachedObjectRecord record,
            VoiceIntentCommand placementCommand,
            SpatialSnapshot spatial,
            Action<GameObject, string> onComplete,
            Action<GameObject> onRootCreated = null)
        {
            ResolveDependencies();

            string modelPath = cachedObjectStore != null ? cachedObjectStore.GetModelAbsolutePath(record) : null;
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                onComplete?.Invoke(null, "Cached object model is missing.");
                yield break;
            }

            byte[] modelBytes;
            try
            {
                modelBytes = File.ReadAllBytes(modelPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObjectGenerationService] Could not read cached object model '{modelPath}': {ex.Message}", this);
                onComplete?.Invoke(null, "Cached object model is missing.");
                yield break;
            }

            string objectName = string.IsNullOrWhiteSpace(record.canonical_name)
                ? "Cached Object"
                : record.canonical_name.Trim();

            var request = new ObjectGenerationRequest
            {
                prompt = record.source_prompt ?? objectName,
                objectName = objectName,
                fileName = Path.GetFileName(modelPath)
            };

            var result = new ObjectGenerationResult
            {
                success = true,
                providerName = record.provider ?? "",
                taskId = record.task_id ?? "",
                modelUrl = record.model_url ?? "",
                modelBytes = modelBytes
            };

            yield return ImportGlbCoroutine(result, request, record, placementCommand, spatial, onComplete, onRootCreated);
        }

        public void EnsureGeneratedObjectInteraction(GameObject instance, string objectName)
        {
            if (instance == null)
                return;

            objectPlacement ??= FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
            if (!forceGeneratedObjectsPhysical)
            {
                if (objectPlacement != null)
                    objectPlacement.WrapExistingGeometry(instance, objectName);
                return;
            }

            Material fallbackMaterial = objectPlacement != null ? objectPlacement.defaultGeneratedMaterial : null;
            Color fallbackColor = objectPlacement != null ? objectPlacement.defaultGeneratedColor : Color.gray;
            bool addTrackable = objectPlacement == null || objectPlacement.autoAddTrackableComponent;
            bool addGrab = objectPlacement == null || objectPlacement.addGrabInteractable;
            bool applyMaterialWhenMissing = objectPlacement == null || objectPlacement.applyDefaultMaterialWhenMissing;

            InteractableObjectWrapper.Wrap(
                instance,
                objectName,
                addTrackable,
                generatedObjectAddColliderIfMissing,
                generatedObjectAddRigidbody,
                addGrab,
                generatedObjectsUseGravity,
                generatedObjectsKinematic,
                generatedObjectMass,
                fallbackMaterial,
                fallbackColor,
                applyMaterialWhenMissing,
                forceRootBoundingBoxCollider: true,
                forceRootRigidbody: true);
        }

        /// <summary>
        /// Drops a newly placed object onto the surface beneath it before physics takes over. Without this
        /// the object spawns at eye height and falls, so it lands wherever gravity and a bounce leave it,
        /// which reads as the placement having been ignored.
        /// </summary>
        void SettleOnSurface(GameObject root)
        {
            if (root == null || settleRaycastMeters <= 0f) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            // Cast from above the object so the ray cannot begin inside its own collider.
            Vector3 origin = bounds.center + Vector3.up * (bounds.extents.y + 0.05f);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, settleRaycastMeters, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return;

            bool found = false;
            float surfaceY = 0f;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(root.transform)) continue;
                if (!found || hit.point.y > surfaceY)
                {
                    surfaceY = hit.point.y;
                    found = true;
                }
            }

            if (!found) return;

            float delta = surfaceY - bounds.min.y + 0.002f;
            if (Mathf.Abs(delta) < 0.001f) return;

            root.transform.position += Vector3.up * delta;
            Debug.Log($"[ObjectGenerationService] Settled '{root.name}' onto the surface at y={surfaceY:0.###} (moved {delta:0.###} m).", this);
        }

        void LogGeneratedObjectDiagnostics(GameObject root, string phase)
        {
            if (root == null)
                return;

            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                Rigidbody rootBody = root.GetComponent<Rigidbody>();
                BoxCollider rootBox = root.GetComponent<BoxCollider>();
                Bounds bounds;
                bool hasBounds = TryCollectRendererBounds(root, renderers, out bounds);

                var sb = new StringBuilder(2048);
                sb.Append("[ObjectGenerationDiagnostics] phase=").Append(phase)
                    .Append(" root='").Append(root.name).Append('\'')
                    .Append(" renderers=").Append(renderers != null ? renderers.Length : 0)
                    .Append(" colliders=").Append(colliders != null ? colliders.Length : 0)
                    .Append(" rootRigidbody=").Append(rootBody != null);

                if (rootBody != null)
                {
                    sb.Append(" rootRbGravity=").Append(rootBody.useGravity)
                        .Append(" rootRbKinematic=").Append(rootBody.isKinematic)
                        .Append(" rootRbDetectCollisions=").Append(rootBody.detectCollisions)
                        .Append(" rootRbCollision=").Append(rootBody.collisionDetectionMode)
                        .Append(" rootRbMass=").Append(rootBody.mass.ToString("0.###"));
                }

                sb.Append(" rootBox=").Append(rootBox != null);
                if (rootBox != null)
                {
                    sb.Append(" rootBoxEnabled=").Append(rootBox.enabled)
                        .Append(" rootBoxCenter=").Append(FormatVector(rootBox.center))
                        .Append(" rootBoxSize=").Append(FormatVector(rootBox.size));
                }

                if (hasBounds)
                {
                    sb.Append(" rendererBoundsCenter=").Append(FormatVector(bounds.center))
                        .Append(" rendererBoundsSize=").Append(FormatVector(bounds.size));
                }

                Debug.Log(sb.ToString(), this);

                if (renderers == null)
                    return;

                int rendererLimit = Mathf.Min(renderers.Length, 8);
                for (int r = 0; r < rendererLimit; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null)
                        continue;

                    Material[] materials = renderer.sharedMaterials;
                    int materialCount = materials != null ? materials.Length : 0;
                    Debug.Log($"[ObjectGenerationDiagnostics] phase={phase} renderer[{r}] name='{renderer.name}' type={renderer.GetType().Name} enabled={renderer.enabled} materialCount={materialCount}", this);

                    int materialLimit = Mathf.Min(materialCount, 6);
                    for (int m = 0; m < materialLimit; m++)
                    {
                        Material material = materials[m];
                        if (material == null)
                        {
                            Debug.Log($"[ObjectGenerationDiagnostics] phase={phase} renderer[{r}].material[{m}] null", this);
                            continue;
                        }

                        Debug.Log(
                            $"[ObjectGenerationDiagnostics] phase={phase} renderer[{r}].material[{m}] " +
                            $"name='{material.name}' shader='{(material.shader != null ? material.shader.name : "<null>")}' " +
                            $"color={FormatColor(ReadMaterialColor(material))} " +
                            $"baseMap={FormatTexture(material, "_BaseMap")} " +
                            $"mainTex={FormatTexture(material, "_MainTex")} " +
                            $"baseColorTexture={FormatTexture(material, "baseColorTexture")} " +
                            $"normalMap={FormatTexture(material, "_BumpMap")} " +
                            $"metallicGlossMap={FormatTexture(material, "_MetallicGlossMap")}",
                            this);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObjectGenerationDiagnostics] phase={phase} failed: {ex.Message}", this);
            }
        }

        static bool TryCollectRendererBounds(GameObject root, Renderer[] renderers, out Bounds bounds)
        {
            bounds = new Bounds(root != null ? root.transform.position : Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            if (renderers == null)
                return false;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        static Color ReadMaterialColor(Material material)
        {
            if (material == null)
                return Color.clear;
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
            return Color.clear;
        }

        static string FormatTexture(Material material, string propertyName)
        {
            if (material == null || string.IsNullOrWhiteSpace(propertyName) || !material.HasProperty(propertyName))
                return "<no-property>";

            Texture texture = material.GetTexture(propertyName);
            if (texture == null)
                return "<null>";

            return $"'{texture.name}' {texture.width}x{texture.height}";
        }

        static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
        }

        static string FormatColor(Color value)
        {
            return $"({value.r:0.###},{value.g:0.###},{value.b:0.###},{value.a:0.###})";
        }

        void ResolveImportPose(VoiceIntentCommand placementCommand, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            objectPlacement ??= FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
            if (objectPlacement != null && placementCommand != null &&
                objectPlacement.TryResolvePlacementPose(placementCommand, spatial, out position, out rotation))
            {
                return;
            }

            position = ResolveDefaultPosition(spatial);
            rotation = ResolveDefaultRotation(spatial);
        }

        void ResolveDependencies()
        {
            if (captureService == null)
                captureService = FindFirstObjectByType<HeadsetCameraCaptureService>(FindObjectsInactive.Include);
            if (objectPlacement == null)
                objectPlacement = FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
            if (hitemProvider == null)
                hitemProvider = FindFirstObjectByType<HitemObjectGenerationProvider>(FindObjectsInactive.Include);
            if (hitemProvider == null)
                hitemProvider = gameObject.AddComponent<HitemObjectGenerationProvider>();
            if (threeDAIStudioProvider == null)
                threeDAIStudioProvider = FindFirstObjectByType<ThreeDAIStudioObjectGenerationProvider>(FindObjectsInactive.Include);
            if (threeDAIStudioProvider == null)
                threeDAIStudioProvider = gameObject.AddComponent<ThreeDAIStudioObjectGenerationProvider>();
            if (threeDAIStudioCreditService == null)
                threeDAIStudioCreditService = FindFirstObjectByType<ThreeDAIStudioCreditService>(FindObjectsInactive.Include);
            if (threeDAIStudioCreditService == null)
                threeDAIStudioCreditService = ThreeDAIStudioCreditService.GetOrCreate();
            if (cachedObjectStore == null)
                cachedObjectStore = FindFirstObjectByType<CachedObjectStore>(FindObjectsInactive.Include);
            if (cachedObjectStore == null)
                cachedObjectStore = CachedObjectStore.GetOrCreate();
            if (thumbnailCaptureService == null)
                thumbnailCaptureService = FindFirstObjectByType<ObjectThumbnailCaptureService>(FindObjectsInactive.Include);
            if (thumbnailCaptureService == null)
                thumbnailCaptureService = gameObject.AddComponent<ObjectThumbnailCaptureService>();
            if (worldConfigAutoSave == null)
                worldConfigAutoSave = FindFirstObjectByType<WorldConfigAutoSave>(FindObjectsInactive.Include);
            if (interactionMemory == null)
                interactionMemory = FindFirstObjectByType<InteractionMemory>(FindObjectsInactive.Include);
        }

        IEnumerator CaptureThumbnail(CachedObjectRecord cachedRecord, GameObject instance)
        {
            ResolveDependencies();
            if (thumbnailCaptureService == null || cachedObjectStore == null || cachedRecord == null || instance == null)
            {
                AnnounceNonFatalWarning("Object is ready, but thumbnail capture was skipped because a dependency was missing.");
                yield break;
            }

            SetInfo($"Capturing thumbnail for {FirstNonEmpty(cachedRecord.canonical_name, instance.name)}.");
            bool ok = false;
            string message = null;
            bool done = false;
            IEnumerator capture = thumbnailCaptureService.CapturePrimaryThumbnail(cachedObjectStore, cachedRecord, instance, (success, result) =>
            {
                ok = success;
                message = result;
                done = true;
            });

            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, thumbnailTimeoutSeconds);
            while (!done)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    AnnounceNonFatalWarning($"Object is ready, but thumbnail capture timed out after {thumbnailTimeoutSeconds:0.#} seconds.");
                    yield break;
                }

                if (!capture.MoveNext())
                    break;

                yield return capture.Current;
            }

            if (ok)
                SetInfo($"Cached thumbnail: {message}.");
            else
                AnnounceNonFatalWarning($"Object is ready, but thumbnail capture failed: {FirstNonEmpty(message, "unknown error")}.");
        }

        void TryRefreshObjectCatalogs()
        {
            try
            {
                CachedObjectCatalogPanel[] panels = FindObjectsByType<CachedObjectCatalogPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (CachedObjectCatalogPanel panel in panels)
                {
                    if (panel == null)
                        continue;

                    if (panel.isActiveAndEnabled)
                        panel.Refresh();
                    else
                        panel.RefreshInBackground(this);
                }
            }
            catch (Exception ex)
            {
                AnnounceNonFatalWarning("Object is ready, but the object catalog could not refresh: " + ex.Message);
            }
        }

        CachedObjectRecord TryCacheGeneratedObject(ObjectGenerationRequest request, ObjectGenerationResult result)
        {
            if (result == null || result.modelBytes == null)
                return null;

            ResolveDependencies();
            if (cachedObjectStore == null)
                return null;

            try
            {
                return cachedObjectStore.SaveGeneratedObject(
                    request.objectName,
                    request.prompt,
                    result.providerName,
                    result.taskId,
                    result.modelUrl,
                    result.modelBytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObjectGenerationService] Could not cache generated object; importing in-memory GLB instead. {ex.Message}", this);
                return null;
            }
        }

        void ApplyCachedObjectReference(GameObject root, CachedObjectRecord cachedRecord)
        {
            if (root == null || cachedRecord == null)
                return;

            CachedObjectReference reference =
                root.GetComponent<CachedObjectReference>() ?? root.AddComponent<CachedObjectReference>();

            reference.cachedObjectId = cachedRecord.object_id ?? "";
            reference.cachedModelPath = cachedRecord.model_path ?? "";
        }

        static VoiceIntentCommand BuildGeneratedObjectCommand(
            ObjectGenerationRequest request,
            VoiceIntentCommand placementCommand,
            ObjectGenerationCapability capability)
        {
            if (placementCommand != null)
                return placementCommand;

            string prompt = request != null ? request.prompt : "";
            string objectName = request != null ? request.objectName : "";
            return new VoiceIntentCommand
            {
                transcript = prompt,
                intent = capability == ObjectGenerationCapability.ImageTo3D
                    ? VoiceIntentType.GenerateObjectFromCapture
                    : VoiceIntentType.PlaceObject,
                should_execute = true,
                object_name = objectName,
                world_prompt = prompt,
                spoken_response = ""
            };
        }

        IObjectGenerationProvider ResolveProvider()
        {
            ResolveDependencies();
            return ResolveProvider(ObjectGenerationCapability.ImageTo3D);
        }

        IObjectGenerationProvider ResolveProvider(ObjectGenerationCapability capability)
        {
            if (providerMode == ObjectGenerationProviderId.ThreeDAIStudioTripo)
                return IsUsable(threeDAIStudioProvider, capability) ? threeDAIStudioProvider : null;

            if (providerMode == ObjectGenerationProviderId.Hitem)
                return IsUsable(hitemProvider, capability) ? hitemProvider : null;

            if (IsUsable(threeDAIStudioProvider, capability))
                return threeDAIStudioProvider;
            if (IsUsable(hitemProvider, capability))
                return hitemProvider;

            return null;
        }

        IEnumerator PreflightCredits(IObjectGenerationProvider provider, ObjectGenerationCapability capability, Action<bool, string> onComplete)
        {
            ObjectGenerationCreditEstimate estimate = provider?.EstimateCredits(capability);
            if (estimate == null || !estimate.known || estimate.requiredCredits <= 0)
            {
                onComplete?.Invoke(true, null);
                yield break;
            }

            if (provider is not ThreeDAIStudioObjectGenerationProvider)
            {
                onComplete?.Invoke(true, null);
                yield break;
            }

            if (threeDAIStudioCreditService == null)
                threeDAIStudioCreditService = ThreeDAIStudioCreditService.GetOrCreate();

            if (threeDAIStudioCreditService == null || !threeDAIStudioCreditService.IsConfigured)
            {
                onComplete?.Invoke(true, null);
                yield break;
            }

            SetInfo($"{estimate.description} needs about {estimate.requiredCredits} credits.");

            bool balanceOk = false;
            decimal balance = 0m;
            string balanceError = null;
            yield return threeDAIStudioCreditService.RefreshBalance((ok, value, error) =>
            {
                balanceOk = ok;
                balance = value;
                balanceError = error;
            });

            if (!balanceOk)
            {
                string warning = string.IsNullOrWhiteSpace(balanceError)
                    ? "Could not check 3dAIStudio credit balance."
                    : balanceError;
                onComplete?.Invoke(false, warning);
                yield break;
            }

            string balanceText = ThreeDAIStudioCreditService.FormatCredits(balance);
            string message = $"3dAIStudio balance: {balanceText} credits. This request needs about {estimate.requiredCredits} credits.";
            SetInfo(message);

            if (balance < estimate.requiredCredits)
            {
                onComplete?.Invoke(false, $"Insufficient 3dAIStudio credits. Available {balanceText}, required {estimate.requiredCredits}. Check provider credits or billing before trying again.");
                yield break;
            }

            onComplete?.Invoke(true, null);
        }

        static bool IsUsable(IObjectGenerationProvider provider, ObjectGenerationCapability capability)
        {
            return provider != null && provider.IsConfigured && provider.SupportsCapability(capability);
        }

        Transform ResolveDefaultParent()
        {
            if (objectPlacement != null && objectPlacement.defaultParent != null)
                return objectPlacement.defaultParent;
            return null;
        }

        /// <summary>
        /// Places a generated object that was given no location at <see cref="defaultDistanceMeters"/> in
        /// front of where the viewer is actually looking. The head pose from the current spatial snapshot
        /// is preferred because it is the live XR gaze; the camera is only a fallback, and an XR rig often
        /// leaves its head camera untagged, which previously collapsed the placement to world origin.
        /// </summary>
        Vector3 ResolveDefaultPosition(SpatialSnapshot spatial)
        {
            if (TryResolveViewPose(spatial, out Vector3 position, out Vector3 forward))
            {
                return position +
                       forward * defaultDistanceMeters +
                       Vector3.up * defaultHeightOffsetMeters;
            }

            return Vector3.forward * defaultDistanceMeters;
        }

        /// <summary>Turns the object to face the viewer, rather than looking the same way they do.</summary>
        Quaternion ResolveDefaultRotation(SpatialSnapshot spatial)
        {
            if (TryResolveViewPose(spatial, out _, out Vector3 forward) && TryFlatten(forward, out Vector3 flat))
                return Quaternion.LookRotation(-flat, Vector3.up);

            return Quaternion.identity;
        }

        /// <summary>
        /// Resolves the viewer's position and gaze direction, preferring the spatial snapshot and falling
        /// back to the active camera.
        /// </summary>
        static bool TryResolveViewPose(SpatialSnapshot spatial, out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero;
            forward = Vector3.forward;

            if (spatial != null &&
                (spatial.head_forward.sqrMagnitude > 0.01f || spatial.head_position.sqrMagnitude > 0.01f))
            {
                position = spatial.head_position;
                forward = spatial.head_forward.sqrMagnitude > 0.01f
                    ? spatial.head_forward.normalized
                    : Vector3.forward;
                return true;
            }

            Transform reference = ResolveUserReference();
            if (reference == null)
                return false;

            position = reference.position;
            forward = reference.forward.sqrMagnitude > 0.0001f ? reference.forward.normalized : Vector3.forward;
            return true;
        }

        static bool TryFlatten(Vector3 direction, out Vector3 flattened)
        {
            flattened = new Vector3(direction.x, 0f, direction.z);
            bool usable = flattened.sqrMagnitude > 0.0001f;
            if (usable) flattened.Normalize();
            return usable;
        }

        static Transform ResolveUserReference()
        {
            if (Camera.main != null)
                return Camera.main.transform;

            // XR rigs frequently leave the head camera untagged, and the old two-step lookup then returned
            // null, sending generated objects to world origin instead of in front of the user.
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (cameras != null)
            {
                foreach (Camera cam in cameras)
                {
                    if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                    if (cam.stereoTargetEye != StereoTargetEyeMask.None) return cam.transform;
                }

                foreach (Camera cam in cameras)
                {
                    if (cam != null && cam.enabled && cam.gameObject.activeInHierarchy) return cam.transform;
                }
            }

            GameObject mainCamera = GameObject.Find("Main Camera");
            return mainCamera != null ? mainCamera.transform : null;
        }

        void NormalizeSize(GameObject root)
        {
            if (root == null || targetMaxSizeMeters <= 0f)
                return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float max = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (max <= 0.0001f)
                return;

            float scale = targetMaxSizeMeters / max;
            root.transform.localScale *= scale;

            Vector3 offset = root.transform.position - bounds.center;
            root.transform.position += offset * scale;
        }

        static string BuildImageFileName(string prompt)
        {
            string name = SanitizeName(prompt);
            if (string.IsNullOrWhiteSpace(name))
                name = "hitem_prompt";
            return $"{name}.jpg";
        }

        static string FormatCapability(ObjectGenerationCapability capability)
        {
            return capability == ObjectGenerationCapability.TextTo3D
                ? "text-to-3D object generation"
                : "image-to-3D object generation";
        }

        static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";

            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();

            return "";
        }

        static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Object";

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            value = value.Trim().Replace(' ', '_');
            return value.Length > 48 ? value.Substring(0, 48) : value;
        }
    }

    public sealed class GeneratedGltfHandle : MonoBehaviour
    {
        public GltfImport gltf;

        void OnDestroy()
        {
            gltf?.Dispose();
            gltf = null;
        }
    }
}

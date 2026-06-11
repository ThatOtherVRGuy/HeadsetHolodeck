using System;
using System.Collections;
using System.IO;
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
        public float defaultDistanceMeters = 2f;
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

        public bool GenerateFromText(string prompt, VoiceIntentCommand placementCommand, SpatialSnapshot spatial)
        {
            if (IsBusy)
            {
                SetWarning(BusyMessage);
                return false;
            }

            ResolveDependencies();
            IObjectGenerationProvider provider = ResolveProvider(ObjectGenerationCapability.TextTo3D);
            if (provider == null)
            {
                SetWarning("Text object generator credentials missing. Set THREEDAISTUDIO_API_KEY.");
                return false;
            }

            string cleanPrompt = string.IsNullOrWhiteSpace(prompt) ? "Generated Object" : prompt.Trim();
            ObjectGenerationRequest request = new ObjectGenerationRequest
            {
                prompt = cleanPrompt,
                objectName = cleanPrompt,
                fileName = $"{SanitizeName(cleanPrompt)}.glb"
            };

            BeginActiveJob(provider, request, ObjectGenerationCapability.TextTo3D);
            _activeJob = StartCoroutine(GenerateCoroutine(provider, request, ObjectGenerationCapability.TextTo3D, placementCommand, spatial));
            return true;
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

            position = ResolveDefaultPosition();
            rotation = ResolveDefaultRotation();
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
                NormalizeSize(root);
                InteractableObjectWrapper.NormalizeRendering(
                    root,
                    objectPlacement != null ? objectPlacement.defaultGeneratedMaterial : null,
                    objectPlacement != null ? objectPlacement.defaultGeneratedColor : Color.gray);
                EnsureGeneratedObjectInteraction(root, request.objectName);
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
                applyMaterialWhenMissing);
        }

        void ResolveImportPose(VoiceIntentCommand placementCommand, SpatialSnapshot spatial, out Vector3 position, out Quaternion rotation)
        {
            objectPlacement ??= FindFirstObjectByType<ObjectPlacementController>(FindObjectsInactive.Include);
            if (objectPlacement != null && placementCommand != null &&
                objectPlacement.TryResolvePlacementPose(placementCommand, spatial, out position, out rotation))
            {
                return;
            }

            position = ResolveDefaultPosition();
            rotation = ResolveDefaultRotation();
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

        Vector3 ResolveDefaultPosition()
        {
            Transform reference = ResolveUserReference();
            if (reference == null)
                return Vector3.forward * defaultDistanceMeters;

            return reference.position +
                   reference.forward * defaultDistanceMeters +
                   Vector3.up * defaultHeightOffsetMeters;
        }

        Quaternion ResolveDefaultRotation()
        {
            Transform reference = ResolveUserReference();
            if (reference == null)
                return Quaternion.identity;

            Vector3 forward = reference.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        static Transform ResolveUserReference()
        {
            if (Camera.main != null)
                return Camera.main.transform;

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

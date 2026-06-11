using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Holodeck.Direct;
using Holodeck.Save;
using Newtonsoft.Json.Linq;
using SpeechIntent;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace HeadsetHolodeck.EditorTests
{
    public static class CachedObjectBatchTests
    {
        public static void RunGeneratedObjectPhysicsTests()
        {
            try
            {
                TestGeneratedObjectWrapperAddsPhysicalColliderAndGravity();
                TestGeneratedObjectWrapperReusesExistingComponents();
                TestObjectPlacementDefaultsGravityForWrappedGeneratedGeometry();
                Debug.Log("[CachedObjectBatchTests] Generated object physics tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CachedObjectBatchTests] Generated object physics tests failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void RunCachedObjectStoreTests()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "CachedObjectStoreTests_" + Guid.NewGuid().ToString("N"));
            CachedObjectStore store = null;

            try
            {
                Directory.CreateDirectory(tempRoot);
                store = CachedObjectStore.CreateForTesting(tempRoot);

                CachedObjectRecord record = store.SaveGeneratedObject(
                    "teddy bear",
                    "make a teddy bear",
                    "test-provider",
                    "task-123",
                    "https://example.invalid/model.glb",
                    new byte[] { 1, 2, 3, 4 });

                string objectDir = Path.Combine(store.CachedObjectsPath, record.object_id);
                AssertTrue(File.Exists(Path.Combine(objectDir, "model.glb")), "Expected model.glb to exist.");
                AssertTrue(File.Exists(Path.Combine(objectDir, "object.json")), "Expected object.json to exist.");

                CachedObjectRecord loaded = store.Load(record.object_id);
                AssertTrue(loaded != null, "Expected record to load.");
                AssertEqual("teddy bear", loaded.canonical_name, "Expected canonical name to round trip.");
                AssertEqual("test-provider", loaded.provider, "Expected provider to round trip.");
                AssertEqual("task-123", loaded.task_id, "Expected task id to round trip.");

                CachedObjectRecord found = store.FindByName("Teddy Bear");
                AssertTrue(found != null, "Expected FindByName to return a record.");
                AssertEqual(record.object_id, found.object_id, "Expected FindByName to return the saved record.");

                record.aliases.Add("plush bear");
                record.tags.Add("toy");
                store.Save(record);

                List<CachedObjectRecord> aliasMatches = store.FindAllByName("The Plush Bear");
                AssertEqual(1, aliasMatches.Count, "Expected FindAllByName to match aliases case-insensitively.");
                AssertEqual(record.object_id, aliasMatches[0].object_id, "Expected alias match to return the saved record.");

                List<CachedObjectRecord> tagMatches = store.FindAllByName("toy");
                AssertEqual(1, tagMatches.Count, "Expected FindAllByName to match tags.");
                AssertEqual(record.object_id, tagMatches[0].object_id, "Expected tag match to return the saved record.");

                List<CachedObjectRecord> listed = store.ListAll();
                AssertEqual(1, listed.Count, "Expected ListAll to include saved cached object.");
                AssertEqual(record.object_id, listed[0].object_id, "Expected ListAll to return the saved record.");

                TestCachedObjectChoiceController();
                TestPrimitiveRenderingNormalization();
                TestGeneratedObjectWrapperAddsPhysicalColliderAndGravity();
                TestGeneratedObjectWrapperReusesExistingComponents();
                TestObjectGenerationSpinnerLifecycle();
                TestThreeDAIStudioStatusUrlFallback();
                TestCachedObjectCatalogActivatesInstantiatedCards(store);
                TestCachedObjectCardThumbnailColorSurvivesLcarsStyling();
                TestLcarsElbowPreservesOuterCornerHeight();
                TestCachedObjectChoiceReplyAllowsPunctuation();
                TestGenerationControlParser();
                TestDispatcherBeginsCachedObjectChoice(store);
                TestDispatcherAsksWhereBeforeGeneratingWithoutPlacement();

                record.model_path = "../escape.glb";
                string unsafeModelPath = store.GetModelAbsolutePath(record);
                AssertTrue(unsafeModelPath == null || IsPathUnder(store.CachedObjectsPath, unsafeModelPath), "Expected unsafe model path to fail safely or remain contained.");

                record.thumbnail_path = "../escape.png";
                string unsafeThumbnailPath = store.GetThumbnailAbsolutePath(record);
                AssertTrue(unsafeThumbnailPath == null || IsPathUnder(Path.Combine(store.CachedObjectsPath, record.object_id), unsafeThumbnailPath), "Expected unsafe thumbnail path to fail safely or remain contained.");

                CachedObjectRecord escapedLoad = store.Load("../escape");
                AssertTrue(escapedLoad == null, "Expected escaped object id load to return null.");

                TestCachedObjectReferenceSerializer();
                TestSavedObjectCachedReferenceDetection(store, record);
                TestSafeWorldSourcePathResolution(tempRoot);
                TestCachedObjectThumbnailMetadata(store, record);
                TestCachedObjectMetadataLoadFallback(store, record);

                AssertTrue(store.Delete(record.object_id), "Expected cached object delete to succeed.");
                AssertTrue(store.Load(record.object_id) == null, "Expected deleted cached object not to load.");

                WorldConfigStore worldStore = WorldConfigStore.CreateForTesting(tempRoot);
                try
                {
                    WorldConfig first = worldStore.CreateConfig(null, "Collision World", null);
                    WorldConfig second = worldStore.CreateConfig(null, "Collision World", null);
                    AssertTrue(first.config_id != second.config_id, "Expected duplicate display names to produce unique config ids.");
                    AssertTrue(Directory.Exists(worldStore.GetConfigFolderPath(first)), "Expected first config folder to exist.");
                    AssertTrue(Directory.Exists(worldStore.GetConfigFolderPath(second)), "Expected second config folder to exist.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(worldStore.gameObject);
                }

                Debug.Log("[CachedObjectBatchTests] Cached object store tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CachedObjectBatchTests] Failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                if (store != null)
                    UnityEngine.Object.DestroyImmediate(store.gameObject);

                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CachedObjectBatchTests] Could not delete temp root: " + ex.Message);
                }
            }
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void AssertEqual(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }

        static void AssertEqual(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        static void AssertContains(string haystack, string needle, string message)
        {
            if (haystack == null || needle == null || haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException($"{message} Expected '{haystack}' to contain '{needle}'.");
        }

        static void AssertColorApproximately(Color expected, Color actual, string message)
        {
            if (Mathf.Abs(expected.r - actual.r) > 0.001f ||
                Mathf.Abs(expected.g - actual.g) > 0.001f ||
                Mathf.Abs(expected.b - actual.b) > 0.001f ||
                Mathf.Abs(expected.a - actual.a) > 0.001f)
            {
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
            }
        }

        static void TestCachedObjectChoiceController()
        {
            var go = new GameObject("CachedObjectChoiceControllerTest");
            try
            {
                var choice = go.AddComponent<CachedObjectChoiceController>();
                var command = new VoiceIntentCommand { object_name = "teddy bear" };
                var spatial = new SpatialSnapshot
                {
                    head_position = new Vector3(1f, 2f, 3f),
                    head_forward = Vector3.forward
                };
                var record = new CachedObjectRecord { object_id = "bear_001", canonical_name = "teddy bear" };

                choice.BeginChoice(command, spatial, new List<CachedObjectRecord> { record });
                AssertTrue(choice.HasPendingChoice, "Expected cached object choice to be pending.");
                AssertEqual(1, choice.PendingMatches.Count, "Expected one pending cached object match.");

                spatial.head_position = Vector3.zero;
                AssertTrue(choice.PendingSpatial.head_position == new Vector3(1f, 2f, 3f), "Expected pending spatial snapshot to be copied.");

                AssertTrue(choice.TryConsumeUseSaved(out VoiceIntentCommand savedCommand, out SpatialSnapshot savedSpatial, out CachedObjectRecord savedRecord), "Expected saved cached object choice to be consumed.");
                AssertTrue(savedCommand == command, "Expected saved choice to return original command.");
                AssertTrue(savedSpatial != null, "Expected saved choice to return copied spatial snapshot.");
                AssertEqual("bear_001", savedRecord.object_id, "Expected saved choice to return first cached record.");
                AssertTrue(!choice.HasPendingChoice, "Expected choice to clear after consume.");

                choice.BeginChoice(command, spatial, new List<CachedObjectRecord> { record });
                AssertTrue(choice.TryConsumeCreateNew(out VoiceIntentCommand newCommand, out SpatialSnapshot newSpatial), "Expected create-new cached object choice to be consumed.");
                AssertTrue(newCommand == command, "Expected create-new choice to return original command.");
                AssertTrue(newSpatial != null, "Expected create-new choice to return copied spatial snapshot.");
                AssertTrue(!choice.HasPendingChoice, "Expected choice to clear after create-new consume.");

                choice.BeginChoice(command, spatial, new List<CachedObjectRecord> { record });
                var sameIdRecord = new CachedObjectRecord { object_id = "bear_001", canonical_name = "teddy bear" };
                AssertTrue(choice.TryConsumeUseSaved(sameIdRecord, out _, out _, out CachedObjectRecord sameIdMatch), "Expected saved choice to accept same object id from another record instance.");
                AssertEqual("bear_001", sameIdMatch.object_id, "Expected same-id choice to return pending cached record.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void TestPrimitiveRenderingNormalization()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Renderer renderer = cube.GetComponent<Renderer>();
                AssertTrue(renderer != null, "Expected primitive cube renderer.");

                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (unlitShader != null)
                {
                    var unlit = new Material(unlitShader) { name = "Primitive Test Unlit" };
                    if (unlit.HasProperty("_BaseColor"))
                        unlit.SetColor("_BaseColor", Color.red);
                    else if (unlit.HasProperty("_Color"))
                        unlit.SetColor("_Color", Color.red);
                    renderer.sharedMaterial = unlit;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                InteractableObjectWrapper.NormalizeRendering(cube, null, Color.gray);

                AssertTrue(renderer.shadowCastingMode == ShadowCastingMode.On, "Expected primitive to cast shadows after normalization.");
                AssertTrue(renderer.receiveShadows, "Expected primitive to receive shadows after normalization.");
                AssertTrue(renderer.sharedMaterial != null, "Expected primitive material after normalization.");
                AssertTrue(renderer.sharedMaterial.shader != null, "Expected primitive material shader after normalization.");
                AssertTrue(renderer.sharedMaterial.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) < 0, "Expected primitive unlit material to be replaced with a lit shader.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        static void TestObjectGenerationSpinnerLifecycle()
        {
            Type spinnerType = Type.GetType("Holodeck.Direct.ObjectGenerationSpinnerController, Assembly-CSharp");
            AssertTrue(spinnerType != null, "Expected ObjectGenerationSpinnerController type to exist.");

            MethodInfo createMethod = spinnerType.GetMethod("CreateRuntimeSpinner", BindingFlags.Public | BindingFlags.Static);
            AssertTrue(createMethod != null, "Expected CreateRuntimeSpinner factory method.");

            object spinner = createMethod.Invoke(null, new object[] { new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 45f, 0f), 1.5f, null });
            AssertTrue(spinner != null, "Expected runtime spinner instance.");

            var component = spinner as Component;
            AssertTrue(component != null, "Expected spinner to be a Unity component.");
            AssertTrue(component.gameObject.name.Contains("ObjectGenerationSpinner"), "Expected spinner object to be named.");
            AssertTrue(Vector3.Distance(component.transform.position, new Vector3(1f, 2f, 3f)) < 0.001f, "Expected spinner position to match request.");
            AssertTrue(Mathf.Abs(component.transform.localScale.x - 1.5f) < 0.001f, "Expected spinner diameter to drive local scale.");
            ParticleSystem particleSystem = component.GetComponentInChildren<ParticleSystem>();
            AssertTrue(particleSystem != null, "Expected spinner to contain a particle system.");
            AssertTrue(!particleSystem.main.prewarm, "Expected spinner particle system not to be prewarmed.");

            MethodInfo dismissMethod = spinnerType.GetMethod("Dismiss", BindingFlags.Public | BindingFlags.Instance);
            AssertTrue(dismissMethod != null, "Expected Dismiss method.");
            dismissMethod.Invoke(spinner, null);
            AssertTrue(component == null, "Expected Dismiss to destroy spinner game object.");
        }

        static void TestGeneratedObjectWrapperAddsPhysicalColliderAndGravity()
        {
            GameObject root = new GameObject("GeneratedObject_Test");
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.name = "GeneratedVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                visual.transform.localScale = new Vector3(0.4f, 1f, 0.4f);

                InteractableObjectWrapper.Wrap(
                    root,
                    "test generated object",
                    addTrackable: true,
                    addColliderIfMissing: true,
                    addRigidbody: true,
                    addGrabInteractable: false,
                    useGravity: true,
                    isKinematic: false,
                    mass: 2f,
                    fallbackMaterial: null,
                    fallbackColor: Color.gray,
                    applyMaterialWhenMissing: true);

                Rigidbody body = root.GetComponent<Rigidbody>();
                Collider collider = root.GetComponent<Collider>();
                SpeechIntentTrackable trackable = root.GetComponent<SpeechIntentTrackable>();

                AssertTrue(body != null, "Expected generated object wrapper to add a Rigidbody.");
                AssertTrue(body.useGravity, "Expected generated object Rigidbody to use gravity by default.");
                AssertTrue(!body.isKinematic, "Expected generated object Rigidbody to be non-kinematic.");
                AssertTrue(Mathf.Abs(body.mass - 2f) < 0.001f, "Expected generated object Rigidbody mass to be applied.");
                AssertTrue(collider != null, "Expected generated object wrapper to add a root Collider.");
                AssertTrue(collider is BoxCollider, "Expected generated object wrapper to add a BoxCollider from renderer bounds.");
                AssertTrue(trackable != null, "Expected generated object wrapper to add SpeechIntentTrackable.");
                AssertEqual("test generated object", trackable.canonicalName, "Expected generated object canonical name.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void TestObjectPlacementDefaultsGravityForWrappedGeneratedGeometry()
        {
            GameObject controllerObject = new GameObject("ObjectPlacementController_Test");
            GameObject root = new GameObject("GeneratedObjectPlacement_Test");
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.name = "GeneratedVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);

                ObjectPlacementController controller = controllerObject.AddComponent<ObjectPlacementController>();
                controller.addGrabInteractable = false;
                controller.WrapExistingGeometry(root, "default physics object");

                Rigidbody body = root.GetComponent<Rigidbody>();
                Collider collider = root.GetComponent<Collider>();

                AssertTrue(body != null, "Expected ObjectPlacementController to add a Rigidbody to generated geometry.");
                AssertTrue(body.useGravity, "Expected generated geometry wrapped by ObjectPlacementController to use gravity by default.");
                AssertTrue(!body.isKinematic, "Expected generated geometry wrapped by ObjectPlacementController to be non-kinematic by default.");
                AssertTrue(collider != null, "Expected ObjectPlacementController to add a Collider to generated geometry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        static void TestGeneratedObjectWrapperReusesExistingComponents()
        {
            GameObject root = new GameObject("GeneratedObject_ExistingComponents_Test");
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                Collider visualCollider = visual.GetComponent<Collider>();
                visual.name = "GeneratedVisual";
                visual.transform.SetParent(root.transform, false);

                Rigidbody existingBody = root.AddComponent<Rigidbody>();
                existingBody.useGravity = false;
                existingBody.isKinematic = true;
                SpeechIntentTrackable existingTrackable = root.AddComponent<SpeechIntentTrackable>();
                existingTrackable.canonicalName = "existing name";

                InteractableObjectWrapper.Wrap(
                    root,
                    "new name should not overwrite",
                    addTrackable: true,
                    addColliderIfMissing: true,
                    addRigidbody: true,
                    addGrabInteractable: false,
                    useGravity: true,
                    isKinematic: false,
                    mass: 3f,
                    fallbackMaterial: null,
                    fallbackColor: Color.gray,
                    applyMaterialWhenMissing: true);

                AssertEqual(1, root.GetComponents<Rigidbody>().Length, "Expected wrapper to reuse the existing Rigidbody.");
                AssertEqual(1, root.GetComponents<SpeechIntentTrackable>().Length, "Expected wrapper to reuse the existing SpeechIntentTrackable.");
                AssertEqual(0, root.GetComponents<Collider>().Length, "Expected wrapper not to add a root Collider when a child Collider exists.");
                AssertTrue(visualCollider != null && visualCollider == visual.GetComponent<Collider>(), "Expected child Collider to be preserved.");

                Rigidbody body = root.GetComponent<Rigidbody>();
                AssertTrue(body.useGravity, "Expected reused Rigidbody gravity setting to be updated.");
                AssertTrue(!body.isKinematic, "Expected reused Rigidbody kinematic setting to be updated.");
                AssertTrue(Mathf.Abs(body.mass - 3f) < 0.001f, "Expected reused Rigidbody mass to be updated.");
                AssertEqual("existing name", existingTrackable.canonicalName, "Expected existing canonical name not to be overwritten.");

                UnityEngine.Object.DestroyImmediate(existingBody);
                Rigidbody childBody = visual.AddComponent<Rigidbody>();
                childBody.useGravity = false;
                childBody.isKinematic = true;

                InteractableObjectWrapper.Wrap(
                    root,
                    "new name should still not overwrite",
                    addTrackable: true,
                    addColliderIfMissing: true,
                    addRigidbody: true,
                    addGrabInteractable: false,
                    useGravity: true,
                    isKinematic: false,
                    mass: 4f,
                    fallbackMaterial: null,
                    fallbackColor: Color.gray,
                    applyMaterialWhenMissing: true);

                AssertEqual(0, root.GetComponents<Rigidbody>().Length, "Expected wrapper not to add a root Rigidbody when a child Rigidbody exists.");
                AssertEqual(1, visual.GetComponents<Rigidbody>().Length, "Expected wrapper to reuse the existing child Rigidbody.");
                AssertTrue(childBody.useGravity, "Expected reused child Rigidbody gravity setting to be updated.");
                AssertTrue(!childBody.isKinematic, "Expected reused child Rigidbody kinematic setting to be updated.");
                AssertTrue(Mathf.Abs(childBody.mass - 4f) < 0.001f, "Expected reused child Rigidbody mass to be updated.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void TestThreeDAIStudioStatusUrlFallback()
        {
            Type providerType = Type.GetType("Holodeck.Direct.ThreeDAIStudioObjectGenerationProvider, Assembly-CSharp");
            AssertTrue(providerType != null, "Expected ThreeDAIStudioObjectGenerationProvider type to exist.");

            MethodInfo findUrl = providerType.GetMethod("FindUrlInJson", BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue(findUrl != null, "Expected raw JSON URL fallback method.");

            string documentedJson = @"{
                ""status"": ""FINISHED"",
                ""results"": [
                    {
                        ""asset"": ""https://storage.3daistudio.com/assets/abc123.glb"",
                        ""asset_type"": ""3D_MODEL"",
                        ""metadata"": null
                    }
                ]
            }";
            string documentedUrl = (string)findUrl.Invoke(null, new object[] { documentedJson, true });
            AssertEqual("https://storage.3daistudio.com/assets/abc123.glb", documentedUrl, "Expected documented 3dAIStudio result asset URL.");

            string nestedJson = @"{
                ""status"": ""FINISHED"",
                ""data"": {
                    ""output"": {
                        ""files"": [
                            {
                                ""url"": ""https://cdn.example.com/download?id=42"",
                                ""type"": ""model/gltf-binary""
                            }
                        ]
                    }
                }
            }";
            string nestedUrl = (string)findUrl.Invoke(null, new object[] { nestedJson, true });
            AssertEqual("https://cdn.example.com/download?id=42", nestedUrl, "Expected nested model URL fallback.");

            string signedJson = @"{
                ""status"": ""FINISHED"",
                ""results"": [
                    {
                        ""asset"": ""https://storage.3daistudio.com/signed-download/abc123?token=secret"",
                        ""asset_type"": ""3D_MODEL""
                    },
                    {
                        ""thumbnail_url"": ""https://storage.3daistudio.com/thumb.png""
                    }
                ]
            }";
            string signedUrl = (string)findUrl.Invoke(null, new object[] { signedJson, true });
            AssertEqual("https://storage.3daistudio.com/signed-download/abc123?token=secret", signedUrl, "Expected signed model URL without .glb extension.");
        }

        static void TestGenerationControlParser()
        {
            AssertTrue(LocalTypedIntentParser.TryParseGenerationControlReply("cancel object generation", out VoiceIntentCommand cancelObject), "Expected object generation cancel to parse.");
            AssertTrue(cancelObject.intent == VoiceIntentType.CancelGeneration, "Expected CancelGeneration intent.");
            AssertEqual("object", cancelObject.target_entity, "Expected object generation target.");

            AssertTrue(LocalTypedIntentParser.TryParseGenerationControlReply("keep waiting.", out VoiceIntentCommand continueAll), "Expected continue waiting reply to parse.");
            AssertTrue(continueAll.intent == VoiceIntentType.ContinueGeneration, "Expected ContinueGeneration intent.");
            AssertEqual("all", continueAll.target_entity, "Expected all generation target.");

            VoiceIntentCommand cancelWorld = LocalTypedIntentParser.Parse("stop world generation");
            AssertTrue(cancelWorld.intent == VoiceIntentType.CancelGeneration, "Expected local parser to emit CancelGeneration.");
            AssertEqual("world", cancelWorld.target_entity, "Expected world generation target.");
        }

        static void TestCachedObjectCatalogActivatesInstantiatedCards(CachedObjectStore store)
        {
            CachedObjectRecord floorLamp = store.SaveGeneratedObject(
                "floor lamp",
                "make a floor lamp",
                "test-provider",
                "task-floor-lamp",
                "https://example.invalid/floor-lamp.glb",
                new byte[] { 5, 6, 7, 8 });
            CachedObjectRecord vase = store.SaveGeneratedObject(
                "vase",
                "make a vase",
                "test-provider",
                "task-vase",
                "https://example.invalid/vase.glb",
                new byte[] { 9, 10, 11, 12 });
            GameObject panelGo = new GameObject("CachedObjectCatalogPanelTest");
            GameObject contentGo = new GameObject("CardListContent");
            GameObject prefabGo = new GameObject("CachedObjectCardPrefab");
            try
            {
                panelGo.SetActive(true);
                CachedObjectCatalogPanel panel = panelGo.AddComponent<CachedObjectCatalogPanel>();

                RectTransform content = contentGo.AddComponent<RectTransform>();
                content.SetParent(panelGo.transform, false);
                CachedObjectCardUI prefab = prefabGo.AddComponent<CachedObjectCardUI>();
                prefabGo.SetActive(false);

                panel.cachedObjectStore = store;
                panel.cardListContent = content;
                panel.cardPrefab = prefab;
                panel.cardsPerFrame = 2;

                MethodInfo refresh = typeof(CachedObjectCatalogPanel).GetMethod("RefreshCoroutine", BindingFlags.NonPublic | BindingFlags.Instance);
                AssertTrue(refresh != null, "Expected CachedObjectCatalogPanel refresh coroutine.");
                var routine = (System.Collections.IEnumerator)refresh.Invoke(panel, null);
                int yields = 0;
                while (routine.MoveNext())
                {
                    yields++;
                }

                AssertEqual(3, content.childCount, "Expected catalog refresh to create one card per cached object.");
                AssertTrue(content.GetChild(0).gameObject.activeSelf, "Expected instantiated catalog card to be active even when template prefab is inactive.");
                AssertTrue(yields >= 1, "Expected catalog refresh to spread card creation across frames.");
            }
            finally
            {
                store.Delete(floorLamp.object_id);
                store.Delete(vase.object_id);
                UnityEngine.Object.DestroyImmediate(prefabGo);
                UnityEngine.Object.DestroyImmediate(contentGo);
                UnityEngine.Object.DestroyImmediate(panelGo);
            }
        }

        static void TestCachedObjectCardThumbnailColorSurvivesLcarsStyling()
        {
            GameObject panelGo = new GameObject("LcarsPanelStylerCardThumbnailTest");
            GameObject cardGo = new GameObject("CachedObjectCard");
            GameObject thumbnailGo = new GameObject("Thumbnail");
            try
            {
                cardGo.transform.SetParent(panelGo.transform, false);
                thumbnailGo.transform.SetParent(cardGo.transform, false);
                cardGo.AddComponent<CachedObjectCardUI>();
                RawImage thumbnail = thumbnailGo.AddComponent<RawImage>();
                Color expected = new Color(0.31f, 0.42f, 0.53f, 0.64f);
                thumbnail.color = expected;

                LcarsPanelStyler.StylePanel(panelGo);

                AssertColorApproximately(expected, thumbnail.color, "Expected cached object card thumbnail color to remain controlled by prefab/Inspector settings.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thumbnailGo);
                UnityEngine.Object.DestroyImmediate(cardGo);
                UnityEngine.Object.DestroyImmediate(panelGo);
            }
        }

        static void TestLcarsElbowPreservesOuterCornerHeight()
        {
            GameObject root = new GameObject("LcarsElbowTest", typeof(RectTransform));
            GameObject outer = new GameObject("OuterCorner", typeof(RectTransform), typeof(Image));
            try
            {
                outer.transform.SetParent(root.transform, false);
                RectTransform outerRt = outer.GetComponent<RectTransform>();
                outerRt.sizeDelta = new Vector2(90f, 44f);

                LcarsElbowGraphic elbow = root.AddComponent<LcarsElbowGraphic>();
                elbow.size = new Vector2(300f, 200f);
                elbow.cornerSize = 90f;
                elbow.outerCorner = outer.GetComponent<Image>();

                elbow.Rebuild();

                AssertTrue(Mathf.Abs(outerRt.sizeDelta.x - 90f) < 0.001f, "Expected LCARS outer corner width to follow cornerSize.");
                AssertTrue(Mathf.Abs(outerRt.sizeDelta.y - 44f) < 0.001f, "Expected LCARS outer corner height to preserve inspector value.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void TestCachedObjectChoiceReplyAllowsPunctuation()
        {
            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("Use it.", out VoiceIntentCommand useSaved), "Expected 'Use it.' to be parsed as a cached object choice reply.");
            AssertEqual("use_saved", useSaved.object_choice_action, "Expected punctuated use reply to select saved object.");

            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("Create a new one?", out VoiceIntentCommand createNew), "Expected 'Create a new one?' to be parsed as a cached object choice reply.");
            AssertEqual("create_new", createNew.object_choice_action, "Expected punctuated create reply to create new object.");
        }

        static void TestDispatcherBeginsCachedObjectChoice(CachedObjectStore store)
        {
            var dispatcherGo = new GameObject("CachedObjectDispatcherTest");
            var choiceGo = new GameObject("CachedObjectChoiceControllerTest");
            try
            {
                var dispatcher = dispatcherGo.AddComponent<WorldActionDispatcher>();
                var choice = choiceGo.AddComponent<CachedObjectChoiceController>();
                dispatcher.cachedObjectStore = store;
                dispatcher.cachedObjectChoiceController = choice;

                var command = new VoiceIntentCommand
                {
                    intent = VoiceIntentType.PlaceObject,
                    should_execute = true,
                    object_name = "teddy bear"
                };

                bool began = dispatcher.TryBeginCachedObjectChoiceForTests(command, new SpatialSnapshot());
                AssertTrue(began, "Expected dispatcher to begin cached object choice.");
                AssertTrue(choice.HasPendingChoice, "Expected dispatcher choice controller to have a pending choice.");
                AssertEqual(1, choice.PendingMatches.Count, "Expected one dispatcher cached object match.");
                AssertContains(command.spoken_response, "saved teddy bear", "Expected spoken response to mention saved object.");
                AssertContains(command.spoken_response, "create a new one", "Expected spoken response to offer creating new object.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dispatcherGo);
                UnityEngine.Object.DestroyImmediate(choiceGo);
            }
        }

        static void TestDispatcherAsksWhereBeforeGeneratingWithoutPlacement()
        {
            var dispatcherGo = new GameObject("GeneratedPlacementValidationDispatcherTest");
            var placementGo = new GameObject("GeneratedPlacementValidationObjectPlacementTest");
            var configGo = new GameObject("GeneratedPlacementValidationObjectGenerationConfigTest");
            try
            {
                DestroyObjectGenerationServices();

                ObjectGenerationApiConfig config = configGo.AddComponent<ObjectGenerationApiConfig>();
                typeof(ObjectGenerationApiConfig)
                    .GetField("threeDAIStudioApiKey", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(config, "test-key");

                var dispatcher = dispatcherGo.AddComponent<WorldActionDispatcher>();
                var placement = placementGo.AddComponent<ObjectPlacementController>();
                placement.requirePointingForPlacement = true;
                placement.createDebugPlaceholderIfMissing = false;
                dispatcher.objectPlacement = placement;

                var command = new VoiceIntentCommand
                {
                    intent = VoiceIntentType.PlaceObject,
                    should_execute = true,
                    object_name = "teddy bear",
                    spatial_reference = SpatialReferenceMode.None
                };

                dispatcher.Execute(command, new SpatialSnapshot());
                AssertTrue(!string.IsNullOrWhiteSpace(command.spoken_response), "Expected unresolved generated placement to ask for clarification.");
                AssertContains(command.spoken_response, "where", "Expected unresolved generated placement to ask where before API generation.");
                AssertTrue(FindObjectGenerationService() == null, "Expected unresolved placement to return before object generation service creation.");
            }
            finally
            {
                DestroyObjectGenerationServices();
                UnityEngine.Object.DestroyImmediate(dispatcherGo);
                UnityEngine.Object.DestroyImmediate(placementGo);
                UnityEngine.Object.DestroyImmediate(configGo);
            }
        }

        static ObjectGenerationService FindObjectGenerationService()
        {
            return UnityEngine.Object.FindFirstObjectByType<ObjectGenerationService>(FindObjectsInactive.Include);
        }

        static void DestroyObjectGenerationServices()
        {
            ObjectGenerationService[] services = UnityEngine.Object.FindObjectsByType<ObjectGenerationService>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ObjectGenerationService service in services)
                UnityEngine.Object.DestroyImmediate(service.gameObject);
        }

        static void TestCachedObjectReferenceSerializer()
        {
            var serializer = new CachedObjectReferenceSerializer();
            var go = new GameObject("CachedObjectReferenceSerializerTest");
            try
            {
                AssertTrue(serializer.Save(go) == null, "Expected missing cached reference to save as null.");

                CachedObjectReference reference = go.AddComponent<CachedObjectReference>();
                reference.cachedModelPath = "model.glb";
                AssertTrue(serializer.Save(go) == null, "Expected empty cached object id to save as null.");

                reference.cachedObjectId = "object_123";
                JObject saved = serializer.Save(go);
                AssertTrue(saved != null, "Expected cached reference to save.");
                AssertEqual("object_123", saved["cached_object_id"]?.Value<string>(), "Expected cached object id to save.");
                AssertEqual("model.glb", saved["cached_model_path"]?.Value<string>(), "Expected cached model path to save.");

                var restoredGo = new GameObject("CachedObjectReferenceSerializerRestoreTest");
                try
                {
                    serializer.Restore(restoredGo, saved, null);
                    CachedObjectReference restored = restoredGo.GetComponent<CachedObjectReference>();
                    AssertTrue(restored != null, "Expected cached reference to restore.");
                    AssertEqual("object_123", restored.cachedObjectId, "Expected cached object id to restore.");
                    AssertEqual("model.glb", restored.cachedModelPath, "Expected cached model path to restore.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(restoredGo);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void TestSavedObjectCachedReferenceDetection(CachedObjectStore store, CachedObjectRecord record)
        {
            var savedObject = new SavedObject
            {
                instance_id = "generated_001",
                display_name = "teddy bear"
            };
            savedObject.components.Add(new SavedComponent
            {
                type = "CachedObjectReference",
                data = new JObject
                {
                    ["cached_object_id"] = record.object_id,
                    ["cached_model_path"] = record.model_path
                }
            });

            string cachedObjectId = WorldConfigRestorer.TryGetCachedObjectId(savedObject);
            AssertEqual(record.object_id, cachedObjectId, "Expected saved object cached reference id to be detected.");

            CachedObjectRecord loaded = store.Load(cachedObjectId);
            AssertTrue(loaded != null, "Expected detected cached object id to load.");

            string modelPath = store.GetModelAbsolutePath(loaded);
            AssertTrue(File.Exists(modelPath), "Expected detected cached model path to exist.");
        }

        static void TestSafeWorldSourcePathResolution(string worldsRoot)
        {
            string validPath = WorldConfigRestorer.ResolveCachedWorldAssetPath(worldsRoot, "world_001", "../CachedWorlds/world.spz");
            AssertTrue(validPath != null, "Expected sibling cached world path to resolve.");
            AssertTrue(IsPathUnder(worldsRoot, validPath), "Expected sibling cached world path to remain under worlds root.");

            string escapedPath = WorldConfigRestorer.ResolveCachedWorldAssetPath(worldsRoot, "world_001", "../../outside.spz");
            AssertTrue(escapedPath == null, "Expected escaped cached world path to be rejected.");

            string absolutePath = WorldConfigRestorer.ResolveCachedWorldAssetPath(worldsRoot, "world_001", Path.Combine(Path.GetTempPath(), "outside.spz"));
            AssertTrue(absolutePath == null, "Expected absolute cached world path to be rejected.");
        }

        static void TestCachedObjectMetadataLoadFallback(CachedObjectStore store, CachedObjectRecord record)
        {
            string objectDir = Path.Combine(store.CachedObjectsPath, record.object_id);
            File.WriteAllText(Path.Combine(objectDir, "object.json"), "{ malformed json");

            bool loaded = WorldConfigRestorer.TryLoadCachedObjectRecord(store, record.object_id, out CachedObjectRecord fallbackRecord, out string error);
            AssertTrue(!loaded, "Expected malformed cached object metadata to fail safely.");
            AssertTrue(fallbackRecord == null, "Expected no record when cached object metadata is malformed.");
            AssertTrue(!string.IsNullOrWhiteSpace(error), "Expected malformed cached object metadata to report an error.");
        }

        static void TestCachedObjectThumbnailMetadata(CachedObjectStore store, CachedObjectRecord record)
        {
            string relativePath = store.SaveThumbnailFrame(record, new byte[] { 137, 80, 78, 71 }, 0, ".png");
            AssertEqual("thumb_000.png", relativePath, "Expected primary thumbnail frame path.");
            AssertEqual("thumb_000.png", record.thumbnail_path, "Expected thumbnail_path to point at primary frame.");
            AssertEqual(1, record.thumbnail_frames.Count, "Expected one thumbnail frame.");
            AssertEqual("thumb_000.png", record.thumbnail_frames[0], "Expected first thumbnail frame path.");
            AssertTrue(record.thumbnail_animation != null, "Expected thumbnail animation metadata.");
            AssertEqual("still", record.thumbnail_animation.mode, "Expected single-frame thumbnail mode.");

            string absolutePath = store.GetThumbnailAbsolutePath(record);
            AssertTrue(File.Exists(absolutePath), "Expected thumbnail image file to exist.");

            CachedObjectRecord loaded = store.Load(record.object_id);
            AssertEqual("thumb_000.png", loaded.thumbnail_path, "Expected saved thumbnail_path to round trip.");
            AssertEqual(1, loaded.thumbnail_frames.Count, "Expected saved thumbnail frame list to round trip.");
        }

        static bool IsPathUnder(string rootPath, string candidatePath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(root, StringComparison.Ordinal);
        }
    }
}

using System;
using SpeechIntent;
using SpeechIntent.Behaviors;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class SpeechIntentBatchTests
    {
        public static void RunDistanceAndMovementTests()
        {
            try
            {
                TestLocalParserConvertsDistanceUnitsToMeters();
                TestMoveTargetOffsetsNamedObjectUpFromCurrentPosition();
                TestMoveTargetOffsetsNamedObjectEvenWithStrayMeEntity();
                TestMoveSelfAliasesToWorldOriginResolveMeObject();
                TestMoveTargetOffsetsNamedObjectWhenSpatialReferenceIsMissing();
                TestMoveTargetUsesTranscriptMetersWhenModelDistanceIsWrong();
                TestMoveTargetInFrontOfMePlacesRelativeToMe();
                TestCreateObjectInFrontOfMeParsesAsRelativePlacement();
                TestCreateObjectDefaultsOneMeterInFrontOfMe();
                TestCreateObjectParsesCreationAttributes();
                TestExistingObjectPhysicsCommandsParseLocally();
                TestRouterOverridesOpenAiClarificationWithLocalPhysicsCommand();
                TestDispatcherAppliesWeightlessToLastObject();
                TestDispatcherDoesNotResolveSingularDeleteAsAll();
                TestDispatcherResolvesQualifiedDeleteAfterClarification();
                TestDispatcherKeepsQualifiedDeletePendingWhenStillAmbiguous();
                TestDispatcherRejectsPointedTargetOutsideStackedQualifier();
                TestDispatcherAcceptsPointedTargetInsideStackedQualifier();
                TestDialogStateCompletesPendingTargetSpatialQualifier();
                TestLocalParserParsesDeleteSpatialQualifier();
                TestSceneResolverResolvesTopmostQualifiedTarget();
                TestSceneResolverResolvesBottommostQualifiedTarget();
                TestSceneResolverKeepsLevelBottommostTargetsAmbiguous();
                TestSceneResolverKeepsSideBySideBottommostTargetsAmbiguous();
                TestRouterCompletesBottomOneAfterDeleteAmbiguity();
                TestCreateSoundParsesQuietVolume();
                TestTargetRelativePlacementParsesMeAndTargetFrames();
                TestTargetRelativePlacementResolvesMeAndTargetFrames();
                TestOpenAiSchemaAllowsBehaviorCommands();
                TestDialogStateCompletesPendingRotationDegrees();
                TestPlacementClarificationUnderstandsRelativeLocation();
                TestCachedObjectChoiceRepliesParseLocally();
                TestBehaviorCommandsParseLocally();
                TestRuntimeBehaviorHostSpinTicks();
                TestBehaviorCommandControllerAttachesSpin();
                TestBehaviorCommandControllerRejectsProtectedTargets();
                TestBehaviorCommandControllerReportsMissingCapability();
                TestRuntimeBehaviorHostHandFollowUsesLiveSpatialContext();
                TestBehaviorCommandControllerUsesBodyAnchorForHandBehavior();
                TestBehaviorCommandControllerRejectsAmbiguousSecondaryTarget();
                TestBehaviorCommandControllerRejectsExistingKinematicThrowTarget();
                TestLocalParserParsesWorldRotation();
                TestLocalParserParsesSpawnPointCommands();
                TestLocalParserParsesTeleportPadCommands();
                TestTeleportPadPlacementResolvesUnderFoot();
                TestDialogStateCompletesPendingTargetQualifier();
                TestRouterStoresTargetClarificationSlot();
                TestSwitchToStaticWorldShowsArchMenu();
                Debug.Log("[SpeechIntentBatchTests] All distance and movement tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[SpeechIntentBatchTests] Failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static void TestLocalParserConvertsDistanceUnitsToMeters()
        {
            AssertParsedDistance("move blaster 10 inches left", 0.254f);
            AssertParsedDistance("move blaster 10 feet up", 3.048f);
            AssertParsedDistance("move blaster 50 centimeters forward", 0.5f);
            AssertParsedDistance("move blaster 12 millimeters down", 0.012f);
            AssertParsedDistance("move blaster 2 yards right", 1.8288f);
        }

        static void TestMoveTargetOffsetsNamedObjectUpFromCurrentPosition()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("TargetTransformController");
            GameObject me = new GameObject("Me");
            GameObject blaster = new GameObject("blaster");

            try
            {
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                TargetTransformController controller = controllerObject.AddComponent<TargetTransformController>();
                controller.entityResolver = resolver;
                controller.meReference = me.transform;

                me.transform.position = new Vector3(0f, 0.182f, 0f);
                blaster.transform.position = new Vector3(0f, 1.54344f, 0f);

                VoiceIntentCommand command = LocalTypedIntentParser.Parse("move blaster 10 feet up");
                bool moved = controller.TryMoveTarget(command, new SpatialSnapshot(), out GameObject target);

                AssertTrue(moved, "Expected move command to succeed.");
                AssertSame(blaster, target, "Expected target to resolve to the blaster GameObject.");
                AssertApproximately(4.59144f, blaster.transform.position.y, 0.0001f, "Expected blaster Y to be offset by 10 feet in meters.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blaster);
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestCachedObjectChoiceRepliesParseLocally()
        {
            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("use saved", out VoiceIntentCommand useSaved), "Expected 'use saved' cached choice reply to parse.");
            AssertEqual(VoiceIntentType.SelectCachedObject, useSaved.intent, "Expected 'use saved' to parse as cached object choice.");
            AssertEqual("use_saved", useSaved.object_choice_action, "Expected 'use saved' action.");

            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("make new", out VoiceIntentCommand makeNew), "Expected 'make new' cached choice reply to parse.");
            AssertEqual("create_new", makeNew.object_choice_action, "Expected make-new action.");

            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("generate new", out VoiceIntentCommand generateNew), "Expected 'generate new' cached choice reply to parse.");
            AssertEqual("create_new", generateNew.object_choice_action, "Expected generate-new action.");

            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("create new", out VoiceIntentCommand createNew), "Expected 'create new' cached choice reply to parse.");
            AssertEqual(VoiceIntentType.SelectCachedObject, createNew.intent, "Expected 'create new' to parse as cached object choice.");
            AssertEqual("create_new", createNew.object_choice_action, "Expected 'create new' action.");

            AssertTrue(LocalTypedIntentParser.TryParseCachedObjectChoiceReply("cancel", out VoiceIntentCommand cancel), "Expected 'cancel' cached choice reply to parse.");
            AssertEqual(VoiceIntentType.SelectCachedObject, cancel.intent, "Expected 'cancel' to parse as cached object choice.");
            AssertEqual("cancel", cancel.object_choice_action, "Expected cancel action.");

            VoiceIntentCommand createWorld = LocalTypedIntentParser.Parse("create new world");
            AssertTrue(createWorld.intent != VoiceIntentType.SelectCachedObject, "Expected normal local parser not to treat 'create new world' as cached object choice without pending context.");
            AssertTrue(!LocalTypedIntentParser.TryParseCachedObjectChoiceReply("create new world", out _), "Expected cached choice reply parser not to intercept longer create-new commands.");
        }

        static void TestLocalParserParsesSpawnPointCommands()
        {
            VoiceIntentCommand save = LocalTypedIntentParser.Parse("save spawn point");
            AssertEqual(VoiceIntentType.SaveSpawnPoint, save.intent, "Expected save spawn point intent.");
            AssertTrue(save.should_execute, "Expected save spawn point to execute.");

            VoiceIntentCommand next = LocalTypedIntentParser.Parse("next spawn point");
            AssertEqual(VoiceIntentType.NextSpawnPoint, next.intent, "Expected next spawn point intent.");
            AssertTrue(next.should_execute, "Expected next spawn point to execute.");

            VoiceIntentCommand previous = LocalTypedIntentParser.Parse("previous spawn point");
            AssertEqual(VoiceIntentType.PreviousSpawnPoint, previous.intent, "Expected previous spawn point intent.");
            AssertTrue(previous.should_execute, "Expected previous spawn point to execute.");

            VoiceIntentCommand suggest = LocalTypedIntentParser.Parse("suggest spawn point");
            AssertEqual(VoiceIntentType.SuggestSpawnPoint, suggest.intent, "Expected suggest spawn point intent.");
            AssertTrue(suggest.should_execute, "Expected suggest spawn point to execute.");

            VoiceIntentCommand remove = LocalTypedIntentParser.Parse("remove this spawn point");
            AssertEqual(VoiceIntentType.RemoveSpawnPoint, remove.intent, "Expected remove spawn point intent.");
            AssertTrue(remove.should_execute, "Expected remove spawn point to execute.");

            VoiceIntentCommand deleteCurrent = LocalTypedIntentParser.Parse("delete spawn point");
            AssertEqual(VoiceIntentType.RemoveSpawnPoint, deleteCurrent.intent, "Expected delete spawn point to remove current spawn point.");
            AssertTrue(deleteCurrent.should_execute, "Expected delete spawn point to execute.");

            VoiceIntentCommand deleteAll = LocalTypedIntentParser.Parse("delete all spawn points");
            AssertEqual(VoiceIntentType.RemoveAllSpawnPoints, deleteAll.intent, "Expected delete all spawn points intent.");
            AssertTrue(deleteAll.should_execute, "Expected delete all spawn points to execute.");
        }

        static void TestBehaviorCommandsParseLocally()
        {
            VoiceIntentCommand spin = LocalTypedIntentParser.Parse("make cube spin");
            AssertEqual(VoiceIntentType.AttachBehavior, spin.intent, "Expected AttachBehavior for spin.");
            AssertEqual("spin", spin.behavior_name, "Expected spin behavior.");
            AssertEqual("cube", spin.target_name, "Expected cube target.");
            AssertEqual(TargetReferenceMode.NamedObject, spin.target_reference, "Expected named target for cube spin.");
            AssertTrue(spin.should_execute, "Expected spin command to execute.");

            VoiceIntentCommand pointedSpin = LocalTypedIntentParser.Parse("make this spin");
            AssertEqual(VoiceIntentType.AttachBehavior, pointedSpin.intent, "Expected AttachBehavior for this spin.");
            AssertEqual("spin", pointedSpin.behavior_name, "Expected spin behavior for this spin.");
            AssertEqual(TargetReferenceMode.PointedObject, pointedSpin.target_reference, "Expected pointed target for this spin.");

            VoiceIntentCommand orbit = LocalTypedIntentParser.Parse("make ball orbit table");
            AssertEqual(VoiceIntentType.AttachBehavior, orbit.intent, "Expected AttachBehavior for orbit.");
            AssertEqual("orbit", orbit.behavior_name, "Expected orbit behavior.");
            AssertEqual("ball", orbit.target_name, "Expected ball subject.");
            AssertEqual("table", orbit.behavior_secondary_target_name, "Expected table orbit center.");

            VoiceIntentCommand follow = LocalTypedIntentParser.Parse("make this follow my left hand");
            AssertEqual(VoiceIntentType.AttachBehavior, follow.intent, "Expected AttachBehavior for follow hand.");
            AssertEqual("follow_hand", follow.behavior_name, "Expected follow_hand behavior.");
            AssertEqual(TargetReferenceMode.PointedObject, follow.target_reference, "Expected pointed object for follow hand.");
            AssertEqual(HandSelection.Left, follow.target_hand, "Expected left hand selection.");

            VoiceIntentCommand give = LocalTypedIntentParser.Parse("give me the blaster");
            AssertEqual(VoiceIntentType.AttachBehavior, give.intent, "Expected AttachBehavior for give me.");
            AssertEqual("attach_to_hand", give.behavior_name, "Expected attach_to_hand behavior.");
            AssertEqual("blaster", give.target_name, "Expected blaster target.");
            AssertEqual(HandSelection.Either, give.target_hand, "Expected either hand for give me.");

            VoiceIntentCommand stopAll = LocalTypedIntentParser.Parse("stop all behaviors");
            AssertEqual(VoiceIntentType.StopBehavior, stopAll.intent, "Expected StopBehavior for stop all behaviors.");
            AssertTrue(stopAll.behavior_stop_all, "Expected stop-all flag.");
            AssertTrue(stopAll.should_execute, "Expected stop-all command to execute.");

            VoiceIntentCommand removeAll = LocalTypedIntentParser.Parse("remove all behaviors");
            AssertEqual(VoiceIntentType.StopBehavior, removeAll.intent, "Expected StopBehavior for remove all behaviors.");
            AssertTrue(removeAll.behavior_stop_all, "Expected remove-all behavior flag.");
            AssertEqual(TargetReferenceMode.All, removeAll.target_reference, "Expected all target reference for remove all behaviors.");

            VoiceIntentCommand stopFollowing = LocalTypedIntentParser.Parse("stop this following my hand");
            AssertEqual(VoiceIntentType.StopBehavior, stopFollowing.intent, "Expected StopBehavior for stop this following my hand.");
            AssertEqual("follow_hand", stopFollowing.behavior_name, "Expected follow_hand stop behavior.");
            AssertEqual(TargetReferenceMode.PointedObject, stopFollowing.target_reference, "Expected pointed object for stop this following my hand.");

            VoiceIntentCommand removeSpinFromCube = LocalTypedIntentParser.Parse("remove spin behavior from cube");
            AssertEqual(VoiceIntentType.StopBehavior, removeSpinFromCube.intent, "Expected StopBehavior for remove spin behavior from cube.");
            AssertEqual("spin", removeSpinFromCube.behavior_name, "Expected spin stop behavior.");
            AssertEqual("cube", removeSpinFromCube.target_name, "Expected cube target for remove spin behavior.");
            AssertEqual(TargetReferenceMode.NamedObject, removeSpinFromCube.target_reference, "Expected named target for remove spin behavior.");
        }

        static void TestRuntimeBehaviorHostSpinTicks()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                RuntimeBehaviorHost host = cube.AddComponent<RuntimeBehaviorHost>();
                bool started = host.StartSpin("spin-test", Vector3.up, 90f, Space.Self);
                AssertTrue(started, "Expected spin behavior to start.");

                Quaternion before = cube.transform.rotation;
                host.TickForTests(1f);
                Quaternion after = cube.transform.rotation;

                AssertTrue(Quaternion.Angle(before, after) > 80f, "Expected one second of spin to rotate the cube.");
                AssertTrue(host.StopBehavior("spin"), "Expected spin behavior to stop.");
                AssertTrue(!host.HasBehavior("spin"), "Expected spin behavior to be removed.");
                AssertTrue(!host.StopAll(), "Expected StopAll to report no work when no behaviors are active.");

                AssertTrue(host.StartSpin("spin-test", Vector3.up, 90f, Space.Self), "Expected spin behavior to restart.");
                AssertTrue(host.StopAll(), "Expected StopAll to report active behaviors were stopped.");
                AssertEqual(0, host.BehaviorCount, "Expected StopAll to remove active behaviors.");

                AssertTrue(host.StartSpin("spin-test", Vector3.up, 90f, Space.Self), "Expected spin behavior to restart for StopAllHosts.");
                int stoppedHosts = RuntimeBehaviorHost.StopAllHosts();
                AssertTrue(stoppedHosts >= 1, "Expected StopAllHosts to count the active behavior host.");
                AssertEqual(0, host.BehaviorCount, "Expected StopAllHosts to remove active behaviors.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        static void TestBehaviorCommandControllerAttachesSpin()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("BehaviorCommandController");
            GameObject cube = new GameObject("cube");
            try
            {
                cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
                controller.entityResolver = resolver;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "make cube spin",
                    intent = VoiceIntentType.AttachBehavior,
                    should_execute = true,
                    behavior_name = "spin",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "cube"
                };

                BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
                AssertTrue(result.success, "Expected behavior command to succeed.");
                RuntimeBehaviorHost host = cube.GetComponent<RuntimeBehaviorHost>();
                AssertTrue(host != null, "Expected RuntimeBehaviorHost on cube.");
                AssertTrue(host.HasBehavior("spin"), "Expected cube to have spin behavior.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestBehaviorCommandControllerRejectsProtectedTargets()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("BehaviorCommandController");
            GameObject arch = new GameObject("Arch");
            try
            {
                arch.AddComponent<SpeechIntentTrackable>().canonicalName = "Arch";
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
                controller.entityResolver = resolver;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "make arch spin",
                    intent = VoiceIntentType.AttachBehavior,
                    should_execute = true,
                    behavior_name = "spin",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "Arch"
                };

                BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
                AssertTrue(!result.success, "Expected protected target to be rejected.");
                AssertTrue(arch.GetComponent<RuntimeBehaviorHost>() == null, "Expected no host on protected target.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(arch);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestBehaviorCommandControllerReportsMissingCapability()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("BehaviorCommandController");
            GameObject cube = new GameObject("cube");
            try
            {
                cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
                controller.entityResolver = resolver;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "make cube melt",
                    intent = VoiceIntentType.AttachBehavior,
                    should_execute = true,
                    behavior_name = "melt",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "cube"
                };

                BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
                AssertTrue(!result.success, "Expected missing behavior to fail.");
                AssertTrue(result.missingCapability != null, "Expected missing capability report.");
                AssertEqual("melt", result.missingCapability.requested_behavior, "Expected missing behavior name.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestRuntimeBehaviorHostHandFollowUsesLiveSpatialContext()
        {
            GameObject target = new GameObject("hand target");
            GameObject providerObject = new GameObject("SpatialContextProvider");
            GameObject leftHand = new GameObject("LeftHand");
            try
            {
                RuntimeBehaviorHost host = target.AddComponent<RuntimeBehaviorHost>();
                SpatialContextProvider provider = providerObject.AddComponent<SpatialContextProvider>();
                provider.leftHandSource = leftHand.AddComponent<PointingSource>();

                leftHand.transform.position = new Vector3(1f, 0f, 0f);
                leftHand.transform.rotation = Quaternion.identity;

                bool started = host.StartHandFollow(
                    "follow_hand",
                    BodyAnchor.LeftHand,
                    new SpatialSnapshot(),
                    Vector3.zero,
                    false,
                    provider);

                AssertTrue(started, "Expected hand follow to start with live spatial context.");
                host.TickForTests(0.1f);
                AssertVectorApproximately(new Vector3(1f, 0f, 0f), target.transform.position, 0.0001f, "Expected target to use initial live left hand position.");

                leftHand.transform.position = new Vector3(2f, 0f, 0f);
                host.TickForTests(0.1f);
                AssertVectorApproximately(new Vector3(2f, 0f, 0f), target.transform.position, 0.0001f, "Expected target to follow updated live left hand position.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(leftHand);
                UnityEngine.Object.DestroyImmediate(providerObject);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static void TestBehaviorCommandControllerUsesBodyAnchorForHandBehavior()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("BehaviorCommandController");
            GameObject cube = new GameObject("cube");
            try
            {
                cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
                controller.entityResolver = resolver;

                SpatialSnapshot spatial = new SpatialSnapshot
                {
                    left_hand = new HandRaySnapshot
                    {
                        is_available = true,
                        origin = new Vector3(10f, 0f, 0f),
                        direction = Vector3.forward
                    },
                    right_hand = new HandRaySnapshot
                    {
                        is_available = true,
                        origin = new Vector3(20f, 0f, 0f),
                        direction = Vector3.forward
                    }
                };

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "make cube follow my left hand",
                    intent = VoiceIntentType.AttachBehavior,
                    should_execute = true,
                    behavior_name = "follow_hand",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "cube",
                    target_hand = HandSelection.None,
                    body_anchor = BodyAnchor.LeftHand
                };

                BehaviorCommandResult result = controller.Execute(command, spatial);
                AssertTrue(result.success, "Expected body-anchor hand behavior to succeed.");

                RuntimeBehaviorHost host = cube.GetComponent<RuntimeBehaviorHost>();
                AssertTrue(host != null, "Expected hand behavior host.");
                host.TickForTests(0.1f);
                AssertVectorApproximately(new Vector3(10f, 0f, 0.15f), cube.transform.position, 0.0001f, "Expected body_anchor LeftHand to take precedence over inferred right hand.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestBehaviorCommandControllerRejectsAmbiguousSecondaryTarget()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("BehaviorCommandController");
            GameObject cube = new GameObject("cube");
            GameObject tableA = new GameObject("table");
            GameObject tableB = new GameObject("table");
            try
            {
                cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
                tableA.AddComponent<SpeechIntentTrackable>().canonicalName = "table";
                tableB.AddComponent<SpeechIntentTrackable>().canonicalName = "table";
                tableA.transform.position = new Vector3(1f, 0f, 0f);
                tableB.transform.position = new Vector3(2f, 0f, 0f);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
                controller.entityResolver = resolver;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "make cube orbit table",
                    intent = VoiceIntentType.AttachBehavior,
                    should_execute = true,
                    behavior_name = "orbit",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "cube",
                    behavior_secondary_target_name = "table"
                };

                BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
                AssertTrue(!result.success, "Expected ambiguous secondary target to fail.");
                AssertEqual("Which table?", result.message, "Expected ambiguity message for secondary target.");
                AssertTrue(cube.GetComponent<RuntimeBehaviorHost>() == null, "Expected no orbit host when secondary target is ambiguous.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tableB);
                UnityEngine.Object.DestroyImmediate(tableA);
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestBehaviorCommandControllerRejectsExistingKinematicThrowTarget()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("BehaviorCommandController");
            GameObject cube = new GameObject("cube");
            GameObject destination = new GameObject("destination");
            try
            {
                cube.AddComponent<SpeechIntentTrackable>().canonicalName = "cube";
                Rigidbody body = cube.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                destination.AddComponent<SpeechIntentTrackable>().canonicalName = "destination";
                destination.transform.position = Vector3.forward;

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                BehaviorCommandController controller = controllerObject.AddComponent<BehaviorCommandController>();
                controller.entityResolver = resolver;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "throw cube to destination",
                    intent = VoiceIntentType.AttachBehavior,
                    should_execute = true,
                    behavior_name = "throw",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "cube",
                    behavior_secondary_target_name = "destination"
                };

                BehaviorCommandResult result = controller.Execute(command, new SpatialSnapshot());
                AssertTrue(!result.success, "Expected existing kinematic body to be rejected.");
                AssertEqual("That object is kinematic and cannot be thrown yet.", result.message, "Expected kinematic failure message.");
                AssertTrue(body.isKinematic, "Expected throw rejection to preserve kinematic setting.");
                AssertTrue(!body.useGravity, "Expected throw rejection to preserve gravity setting.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(destination);
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestMoveTargetOffsetsNamedObjectEvenWithStrayMeEntity()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("TargetTransformController");
            GameObject me = new GameObject("Me");
            GameObject blaster = new GameObject("blaster");

            try
            {
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                TargetTransformController controller = controllerObject.AddComponent<TargetTransformController>();
                controller.entityResolver = resolver;
                controller.meReference = me.transform;

                me.transform.position = new Vector3(0f, 0.182f, 0f);
                blaster.transform.position = new Vector3(0f, 1.54344f, 0f);

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "move blaster 10 feet up",
                    intent = VoiceIntentType.MoveTarget,
                    should_execute = true,
                    target_entity = "Me",
                    target_name = "blaster",
                    object_name = "blaster",
                    target_reference = TargetReferenceMode.NamedObject,
                    spatial_reference = SpatialReferenceMode.RelativeToMe,
                    relative_direction = RelativeDirection.Up,
                    relative_distance_meters = 3.048f
                };

                bool moved = controller.TryMoveTarget(command, new SpatialSnapshot(), out GameObject target);

                AssertTrue(moved, "Expected mixed OpenAI-style move command to succeed.");
                AssertSame(blaster, target, "Expected mixed command to resolve to blaster, not Me.");
                AssertApproximately(4.59144f, blaster.transform.position.y, 0.0001f, "Expected mixed command to offset blaster Y by 10 feet in meters.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blaster);
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestMoveSelfAliasesToWorldOriginResolveMeObject()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("TargetTransformController");
            GameObject me = new GameObject("Me");
            GameObject distractorMe = new GameObject("me");
            GameObject distractorMyself = new GameObject("myself");
            GameObject distractorUser = new GameObject("user");
            GameObject distractorI = new GameObject("i");

            try
            {
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                TargetTransformController controller = controllerObject.AddComponent<TargetTransformController>();
                controller.entityResolver = resolver;
                controller.meReference = me.transform;

                foreach (string alias in new[] { "me", "myself", "user", "i" })
                {
                    me.transform.position = new Vector3(3f, 4f, 5f);
                    distractorMe.transform.position = Vector3.one;
                    distractorMyself.transform.position = Vector3.one * 2f;
                    distractorUser.transform.position = Vector3.one * 3f;
                    distractorI.transform.position = Vector3.one * 4f;

                    VoiceIntentCommand command = new VoiceIntentCommand
                    {
                        transcript = $"put {alias} at world origin",
                        intent = VoiceIntentType.MoveTarget,
                        should_execute = true,
                        target_name = alias,
                        target_reference = TargetReferenceMode.NamedObject,
                        spatial_reference = SpatialReferenceMode.WorldOrigin
                    };

                    bool moved = controller.TryMoveTarget(command, new SpatialSnapshot(), out GameObject target);

                    AssertTrue(moved, $"Expected move-self command to succeed for alias '{alias}'.");
                    AssertSame(me, target, $"Expected alias '{alias}' to resolve to the Me GameObject.");
                    AssertVectorApproximately(Vector3.zero, me.transform.position, 0.0001f, $"Expected alias '{alias}' to move Me to world origin.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(distractorI);
                UnityEngine.Object.DestroyImmediate(distractorUser);
                UnityEngine.Object.DestroyImmediate(distractorMyself);
                UnityEngine.Object.DestroyImmediate(distractorMe);
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestMoveTargetUsesTranscriptMetersWhenModelDistanceIsWrong()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("TargetTransformController");
            GameObject me = new GameObject("Me");
            GameObject blaster = new GameObject("blaster");

            try
            {
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                TargetTransformController controller = controllerObject.AddComponent<TargetTransformController>();
                controller.entityResolver = resolver;
                controller.meReference = me.transform;

                me.transform.position = new Vector3(0f, 0.182f, 0f);
                blaster.transform.position = new Vector3(0f, 1.54344f, 0f);

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "move blaster 10 feet up",
                    intent = VoiceIntentType.MoveTarget,
                    should_execute = true,
                    target_name = "blaster",
                    object_name = "blaster",
                    target_reference = TargetReferenceMode.NamedObject,
                    spatial_reference = SpatialReferenceMode.RelativeToMe,
                    relative_direction = RelativeDirection.Up,
                    relative_distance_meters = 10f
                };

                bool moved = controller.TryMoveTarget(command, new SpatialSnapshot(), out GameObject target);

                AssertTrue(moved, "Expected command with unconverted model distance to succeed.");
                AssertSame(blaster, target, "Expected target to resolve to blaster.");
                AssertApproximately(4.59144f, blaster.transform.position.y, 0.0001f, "Expected transcript-derived feet conversion to override raw 10.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blaster);
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestMoveTargetOffsetsNamedObjectWhenSpatialReferenceIsMissing()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("TargetTransformController");
            GameObject me = new GameObject("Me");
            GameObject blaster = new GameObject("Blaster");

            try
            {
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                TargetTransformController controller = controllerObject.AddComponent<TargetTransformController>();
                controller.entityResolver = resolver;
                controller.meReference = me.transform;

                me.transform.position = new Vector3(0f, 0.182f, 0f);
                blaster.transform.position = new Vector3(2.611f, 0.9508f, -0.161f);

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    transcript = "Move blaster 10 feet up.",
                    intent = VoiceIntentType.MoveTarget,
                    should_execute = true,
                    target_name = "blaster",
                    target_reference = TargetReferenceMode.NamedObject,
                    spatial_reference = SpatialReferenceMode.None,
                    relative_direction = RelativeDirection.Up,
                    relative_distance_meters = 3.048f
                };

                bool moved = controller.TryMoveTarget(command, new SpatialSnapshot(), out GameObject target);

                AssertTrue(moved, "Expected missing-spatial-reference relative command to succeed.");
                AssertSame(blaster, target, "Expected target to resolve to Blaster.");
                AssertVectorApproximately(new Vector3(2.611f, 3.9988f, -0.161f), blaster.transform.position, 0.0001f, "Expected missing-spatial-reference command to offset Blaster upward.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blaster);
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestMoveTargetInFrontOfMePlacesRelativeToMe()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("TargetTransformController");
            GameObject me = new GameObject("Me");
            GameObject cube = new GameObject("cube");

            try
            {
                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                TargetTransformController controller = controllerObject.AddComponent<TargetTransformController>();
                controller.entityResolver = resolver;
                controller.meReference = me.transform;
                controller.defaultRelativeToMeDistance = 2f;

                me.transform.position = new Vector3(10f, 1f, 20f);
                me.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                cube.transform.position = Vector3.zero;

                VoiceIntentCommand command = LocalTypedIntentParser.Parse("move cube in front of me");
                bool moved = controller.TryMoveTarget(command, new SpatialSnapshot(), out GameObject target);

                AssertTrue(moved, "Expected in-front-of-me move command to succeed.");
                AssertSame(cube, target, "Expected target to resolve to the cube GameObject.");
                AssertVectorApproximately(new Vector3(10f, 1f, 22f), cube.transform.position, 0.0001f, "Expected cube to be placed in front of Me.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cube);
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void AssertParsedDistance(string transcript, float expectedMeters)
        {
            VoiceIntentCommand command = LocalTypedIntentParser.Parse(transcript);
            AssertEqual(VoiceIntentType.MoveTarget, command.intent, "Expected MoveTarget for: " + transcript);
            AssertEqual("blaster", command.target_name, "Expected target name blaster for: " + transcript);
            AssertApproximately(expectedMeters, command.relative_distance_meters, 0.0001f, "Expected converted meters for: " + transcript);
        }

        static void TestCreateObjectInFrontOfMeParsesAsRelativePlacement()
        {
            VoiceIntentCommand command = LocalTypedIntentParser.Parse("create a teddy bear 1 meter in front of me");

            AssertEqual(VoiceIntentType.PlaceObject, command.intent, "Expected PlaceObject for arbitrary object creation.");
            AssertEqual("teddy bear", command.object_name, "Expected object name without placement phrase.");
            AssertEqual(SpatialReferenceMode.RelativeToMe, command.spatial_reference, "Expected placement relative to Me.");
            AssertEqual(RelativeDirection.InFront, command.relative_direction, "Expected in-front relative direction.");
            AssertApproximately(1f, command.relative_distance_meters, 0.0001f, "Expected parsed meter distance.");
        }

        static void TestCreateObjectDefaultsOneMeterInFrontOfMe()
        {
            GameObject controllerObject = new GameObject("ObjectPlacementController");

            try
            {
                ObjectPlacementController controller = controllerObject.AddComponent<ObjectPlacementController>();
                controller.requirePointingForPlacement = true;

                VoiceIntentCommand command = LocalTypedIntentParser.Parse("create a cube");
                AssertEqual(VoiceIntentType.PlaceObject, command.intent, "Expected generic create object command.");
                AssertEqual("cube", command.object_name, "Expected cube object name.");

                bool resolved = controller.TryResolvePlacementPose(
                    command,
                    new SpatialSnapshot
                    {
                        head_position = new Vector3(0f, 1.6f, 0f),
                        head_forward = Vector3.forward
                    },
                    out Vector3 position,
                    out _);

                AssertTrue(resolved, "Expected placement to resolve without pointing.");
                AssertVectorApproximately(new Vector3(0f, 1.6f, 1f), position, 0.0001f, "Expected default placement one meter in front of Me/head.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        static void TestCreateObjectParsesCreationAttributes()
        {
            VoiceIntentCommand greenCube = LocalTypedIntentParser.Parse("create a green cube");
            AssertEqual(VoiceIntentType.PlaceObject, greenCube.intent, "Expected create green cube to place an object.");
            AssertEqual("cube", greenCube.object_name, "Expected color adjective to be removed from object name.");
            AssertEqual("green", greenCube.material_prompt, "Expected green material prompt.");
            AssertEqual(SpatialReferenceMode.RelativeToMe, greenCube.spatial_reference, "Expected default relative placement.");
            AssertEqual(RelativeDirection.InFront, greenCube.relative_direction, "Expected default in-front direction.");
            AssertApproximately(1f, greenCube.relative_distance_meters, 0.0001f, "Expected default one meter placement.");

            VoiceIntentCommand wideCube = LocalTypedIntentParser.Parse("make a 2 meter wide cube");
            AssertEqual(VoiceIntentType.PlaceObject, wideCube.intent, "Expected wide cube creation.");
            AssertEqual("cube", wideCube.object_name, "Expected size adjective to be removed from object name.");
            AssertApproximately(2f, wideCube.object_width_meters, 0.0001f, "Expected absolute width in meters.");

            VoiceIntentCommand weightlessSphere = LocalTypedIntentParser.Parse("make a weightless sphere");
            AssertEqual(VoiceIntentType.PlaceObject, weightlessSphere.intent, "Expected weightless sphere creation.");
            AssertEqual("sphere", weightlessSphere.object_name, "Expected physics adjective to be removed from object name.");
            AssertTrue(weightlessSphere.object_weightless, "Expected weightless flag.");

            VoiceIntentCommand pronounScale = LocalTypedIntentParser.Parse("make it bigger");
            AssertTrue(pronounScale.intent != VoiceIntentType.PlaceObject, "Expected generic object creation not to steal pronoun modification commands.");
        }

        static void TestExistingObjectPhysicsCommandsParseLocally()
        {
            VoiceIntentCommand weightless = LocalTypedIntentParser.Parse("make it weightless");
            AssertEqual(VoiceIntentType.ModifyPhysics, weightless.intent, "Expected existing-object physics command.");
            AssertEqual(TargetReferenceMode.LastCreatedOrInteracted, weightless.target_reference, "Expected pronoun physics target to use last object.");
            AssertEqual("set_weightless", weightless.physics_action, "Expected weightless physics action.");
            AssertTrue(weightless.object_weightless, "Expected weightless flag.");

            VoiceIntentCommand named = LocalTypedIntentParser.Parse("make the sphere weightless");
            AssertEqual(VoiceIntentType.ModifyPhysics, named.intent, "Expected named physics command.");
            AssertEqual(TargetReferenceMode.NamedObject, named.target_reference, "Expected named target mode.");
            AssertEqual("sphere", named.target_name, "Expected named target.");

            VoiceIntentCommand makeHeavy = LocalTypedIntentParser.Parse("make the cube have weight");
            AssertEqual(VoiceIntentType.ModifyPhysics, makeHeavy.intent, "Expected have-weight command to modify physics.");
            AssertEqual("enable_gravity", makeHeavy.physics_action, "Expected have-weight command to enable gravity.");
            AssertEqual(TargetReferenceMode.NamedObject, makeHeavy.target_reference, "Expected named target for cube weight command.");
            AssertEqual("cube", makeHeavy.target_name, "Expected cube target for have-weight command.");
        }

        static void TestRouterOverridesOpenAiClarificationWithLocalPhysicsCommand()
        {
            SpeechIntentResult result = new SpeechIntentResult
            {
                success = true,
                transcript = "Make it weightless.",
                command = new VoiceIntentCommand
                {
                    transcript = "Make it weightless.",
                    intent = VoiceIntentType.AskClarification,
                    should_execute = false,
                    reason = "No dedicated intent."
                }
            };

            AssertTrue(VoiceCommandRouter.TryBuildLocalExecutableOverrideForTests(result, out VoiceIntentCommand command), "Expected local override for executable physics command.");
            AssertEqual(VoiceIntentType.ModifyPhysics, command.intent, "Expected physics override command.");
            AssertEqual(TargetReferenceMode.LastCreatedOrInteracted, command.target_reference, "Expected last object target.");
        }

        static void TestDispatcherAppliesWeightlessToLastObject()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject memoryObject = new GameObject("InteractionMemory");
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                sphere.name = "sphere";
                Rigidbody body = sphere.AddComponent<Rigidbody>();
                body.useGravity = true;
                body.mass = 2f;
                body.linearVelocity = new Vector3(0f, -10f, 0f);
                body.angularVelocity = new Vector3(1f, 2f, 3f);

                InteractionMemory memory = memoryObject.AddComponent<InteractionMemory>();
                memory.RegisterCreatedObject(sphere);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.interactionMemory = memory;

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.interactionMemory = memory;

                VoiceIntentCommand command = LocalTypedIntentParser.Parse("make it weightless");
                dispatcher.Execute(command, new SpatialSnapshot());

                AssertTrue(!body.useGravity, "Expected weightless command to disable gravity.");
                AssertTrue(body.isKinematic, "Expected weightless command to make the Rigidbody kinematic.");
                AssertVectorApproximately(Vector3.zero, body.linearVelocity, 0.0001f, "Expected weightless command to zero linear velocity.");
                AssertVectorApproximately(Vector3.zero, body.angularVelocity, 0.0001f, "Expected weightless command to zero angular velocity.");
                AssertApproximately(0.0001f, body.mass, 0.00001f, "Expected weightless command to set near-zero mass.");

                VoiceIntentCommand restore = LocalTypedIntentParser.Parse("make the sphere have weight");
                dispatcher.Execute(restore, new SpatialSnapshot());

                AssertTrue(body.useGravity, "Expected have-weight command to re-enable gravity.");
                AssertTrue(!body.isKinematic, "Expected have-weight command to make the Rigidbody dynamic.");
                AssertApproximately(1f, body.mass, 0.0001f, "Expected have-weight command to restore ordinary mass.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sphere);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
                UnityEngine.Object.DestroyImmediate(memoryObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestDispatcherDoesNotResolveSingularDeleteAsAll()
        {
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject redSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject greenSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                redSphere.name = "Red Sphere";
                greenSphere.name = "Green Sphere";
                redSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                greenSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                VoiceIntentCommand command = LocalTypedIntentParser.Parse("delete the sphere");
                AssertEqual(VoiceIntentType.DeleteTarget, command.intent, "Expected delete command.");
                AssertEqual(TargetReferenceMode.NamedObject, command.target_reference, "Expected singular named delete target.");

                var method = typeof(WorldActionDispatcher).GetMethod(
                    "ResolveDeleteTargets",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected ResolveDeleteTargets method.");

                var targets = method.Invoke(dispatcher, new object[] { command, new SpatialSnapshot() }) as System.Collections.Generic.List<GameObject>;
                AssertTrue(targets != null, "Expected delete targets list.");
                AssertEqual(0, targets.Count, "Expected singular ambiguous delete to resolve no targets until clarified.");
                AssertTrue(!command.should_execute, "Expected singular ambiguous delete to wait for clarification.");
                AssertTrue(command.spoken_response.Contains("Which sphere", StringComparison.OrdinalIgnoreCase), "Expected delete command to ask which sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(greenSphere);
                UnityEngine.Object.DestroyImmediate(redSphere);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
            }
        }

        static void TestDispatcherResolvesQualifiedDeleteAfterClarification()
        {
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject transformControllerObject = new GameObject("TargetTransformController");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject redSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject greenSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                redSphere.name = "Red Sphere";
                greenSphere.name = "Green Sphere";
                redSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                greenSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                RuntimeMaterialCatalog.TryParseDescriptor("green", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor green);
                catalog.ApplyTo(redSphere, red, true);
                catalog.ApplyTo(greenSphere, green, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;
                TargetTransformController targetTransformController = transformControllerObject.AddComponent<TargetTransformController>();
                targetTransformController.entityResolver = resolver;

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.targetTransformController = targetTransformController;

                var pending = new VoiceIntentCommand
                {
                    transcript = "delete the sphere",
                    intent = VoiceIntentType.DeleteTarget,
                    should_execute = false,
                    spoken_response = "Which sphere?",
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "sphere",
                    object_name = "sphere"
                };
                var dialog = new CommandDialogStateManager();
                dialog.BeginClarification(pending, CommandClarificationSlot.Target, pending.spoken_response);
                var reply = new SpeechIntentResult { transcript = "green" };
                AssertTrue(dialog.TryComplete(reply, out VoiceIntentCommand command), "Expected bare color reply to complete target clarification.");
                AssertEqual("green", command.target_material_prompt, "Expected completed delete command to carry green material qualifier.");

                var method = typeof(WorldActionDispatcher).GetMethod(
                    "ResolveDeleteTargets",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected ResolveDeleteTargets method.");

                var targets = method.Invoke(dispatcher, new object[] { command, new SpatialSnapshot() }) as System.Collections.Generic.List<GameObject>;
                AssertTrue(targets != null, "Expected delete targets list.");
                AssertEqual(1, targets.Count, "Expected qualified delete to resolve one target.");
                AssertTrue(targets[0] == greenSphere, "Expected qualified delete to resolve the green sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(greenSphere);
                UnityEngine.Object.DestroyImmediate(redSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(transformControllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
            }
        }

        static void TestDispatcherKeepsQualifiedDeletePendingWhenStillAmbiguous()
        {
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject transformControllerObject = new GameObject("TargetTransformController");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject firstGreenSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject secondGreenSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject redSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                firstGreenSphere.name = "First Green Sphere";
                secondGreenSphere.name = "Second Green Sphere";
                redSphere.name = "Red Sphere";
                firstGreenSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                secondGreenSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                redSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("green", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor green);
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                catalog.ApplyTo(firstGreenSphere, green, true);
                catalog.ApplyTo(secondGreenSphere, green, true);
                catalog.ApplyTo(redSphere, red, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;
                TargetTransformController targetTransformController = transformControllerObject.AddComponent<TargetTransformController>();
                targetTransformController.entityResolver = resolver;

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.targetTransformController = targetTransformController;

                VoiceIntentCommand command = BuildClarifiedDeleteCommand("green");
                var method = typeof(WorldActionDispatcher).GetMethod(
                    "ResolveDeleteTargets",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected ResolveDeleteTargets method.");

                var targets = method.Invoke(dispatcher, new object[] { command, new SpatialSnapshot() }) as System.Collections.Generic.List<GameObject>;
                AssertTrue(targets != null, "Expected delete targets list.");
                AssertEqual(0, targets.Count, "Expected still-ambiguous green delete to resolve no targets.");
                AssertTrue(!command.should_execute, "Expected still-ambiguous green delete to wait for another clarification.");
                AssertTrue(command.spoken_response.Contains("Which green sphere", StringComparison.OrdinalIgnoreCase), "Expected stacked clarification to ask which green sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(redSphere);
                UnityEngine.Object.DestroyImmediate(secondGreenSphere);
                UnityEngine.Object.DestroyImmediate(firstGreenSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(transformControllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
            }
        }

        static void TestDispatcherRejectsPointedTargetOutsideStackedQualifier()
        {
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject transformControllerObject = new GameObject("TargetTransformController");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject redSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject greenSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                redSphere.name = "Red Sphere";
                greenSphere.name = "Green Sphere";
                redSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                greenSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                RuntimeMaterialCatalog.TryParseDescriptor("green", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor green);
                catalog.ApplyTo(redSphere, red, true);
                catalog.ApplyTo(greenSphere, green, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;
                TargetTransformController targetTransformController = transformControllerObject.AddComponent<TargetTransformController>();
                targetTransformController.entityResolver = resolver;

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.targetTransformController = targetTransformController;

                VoiceIntentCommand pending = BuildClarifiedDeleteCommand("green");
                var dialog = new CommandDialogStateManager();
                dialog.BeginClarification(pending, CommandClarificationSlot.Target, "Which green sphere?");
                AssertTrue(dialog.TryComplete(new SpeechIntentResult { transcript = "this one" }, out VoiceIntentCommand command), "Expected this-one reply to complete target clarification.");
                AssertEqual(TargetReferenceMode.PointedObject, command.target_reference, "Expected this-one reply to use pointed target.");
                AssertEqual("green", command.target_material_prompt, "Expected pointed reply to preserve existing green qualifier.");

                SpatialSnapshot spatial = new SpatialSnapshot
                {
                    right_hand = new HandRaySnapshot
                    {
                        is_available = true,
                        is_pointing = true,
                        has_hit = true,
                        hit_object_name = redSphere.name,
                        hit_object_path = redSphere.name
                    }
                };

                var method = typeof(WorldActionDispatcher).GetMethod(
                    "ResolveDeleteTargets",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected ResolveDeleteTargets method.");

                var targets = method.Invoke(dispatcher, new object[] { command, spatial }) as System.Collections.Generic.List<GameObject>;
                AssertTrue(targets != null, "Expected delete targets list.");
                AssertEqual(0, targets.Count, "Expected pointed red sphere not to satisfy green sphere qualifier.");
                AssertTrue(command.spoken_response.Contains("green sphere", StringComparison.OrdinalIgnoreCase), "Expected mismatch response to mention green sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(greenSphere);
                UnityEngine.Object.DestroyImmediate(redSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(transformControllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
            }
        }

        static void TestDispatcherAcceptsPointedTargetInsideStackedQualifier()
        {
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject transformControllerObject = new GameObject("TargetTransformController");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject redSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject greenSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                redSphere.name = "Red Sphere";
                greenSphere.name = "Green Sphere";
                redSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                greenSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                RuntimeMaterialCatalog.TryParseDescriptor("green", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor green);
                catalog.ApplyTo(redSphere, red, true);
                catalog.ApplyTo(greenSphere, green, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;
                TargetTransformController targetTransformController = transformControllerObject.AddComponent<TargetTransformController>();
                targetTransformController.entityResolver = resolver;

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.targetTransformController = targetTransformController;

                VoiceIntentCommand command = BuildClarifiedDeleteCommand("green");
                command.target_reference = TargetReferenceMode.PointedObject;

                SpatialSnapshot spatial = new SpatialSnapshot
                {
                    right_hand = new HandRaySnapshot
                    {
                        is_available = true,
                        is_pointing = true,
                        has_hit = true,
                        hit_object_name = greenSphere.name,
                        hit_object_path = greenSphere.name
                    }
                };

                var method = typeof(WorldActionDispatcher).GetMethod(
                    "ResolveDeleteTargets",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected ResolveDeleteTargets method.");

                var targets = method.Invoke(dispatcher, new object[] { command, spatial }) as System.Collections.Generic.List<GameObject>;
                AssertTrue(targets != null, "Expected delete targets list.");
                AssertEqual(1, targets.Count, "Expected pointed green sphere to satisfy green sphere qualifier.");
                AssertTrue(targets[0] == greenSphere, "Expected pointed qualified delete to resolve the green sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(greenSphere);
                UnityEngine.Object.DestroyImmediate(redSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(transformControllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
            }
        }

        static void TestDialogStateCompletesPendingTargetSpatialQualifier()
        {
            var dialog = new CommandDialogStateManager();
            var pending = BuildClarifiedDeleteCommand("red");
            pending.should_execute = false;
            pending.spoken_response = "Which red sphere?";

            dialog.BeginClarification(pending, CommandClarificationSlot.Target, pending.spoken_response);

            AssertTrue(dialog.TryComplete(new SpeechIntentResult { transcript = "the one on top" }, out VoiceIntentCommand completed), "Expected top-position reply to complete target clarification.");
            AssertEqual(VoiceIntentType.DeleteTarget, completed.intent, "Expected completed command to preserve delete intent.");
            AssertEqual("sphere", completed.target_name, "Expected completed command to preserve target name.");
            AssertEqual("red", completed.target_material_prompt, "Expected completed command to preserve material qualifier.");
            AssertEqual("topmost", completed.target_spatial_qualifier, "Expected completed command to add topmost qualifier.");
            AssertTrue(completed.should_execute, "Expected completed command to be executable.");

            dialog.BeginClarification(pending, CommandClarificationSlot.Target, pending.spoken_response);
            AssertTrue(dialog.TryComplete(new SpeechIntentResult { transcript = "The bottom one." }, out VoiceIntentCommand bottomCompleted), "Expected punctuated bottom-position reply to complete target clarification.");
            AssertEqual("bottommost", bottomCompleted.target_spatial_qualifier, "Expected punctuated bottom-one reply to add bottommost qualifier.");
        }

        static void TestLocalParserParsesDeleteSpatialQualifier()
        {
            VoiceIntentCommand command = LocalTypedIntentParser.Parse("delete the red sphere on top");

            AssertEqual(VoiceIntentType.DeleteTarget, command.intent, "Expected delete command.");
            AssertEqual(TargetReferenceMode.NamedObject, command.target_reference, "Expected named delete target.");
            AssertEqual("sphere", command.target_name, "Expected target name without material/spatial qualifiers.");
            AssertEqual("red", command.target_material_prompt, "Expected material qualifier.");
            AssertEqual("topmost", command.target_spatial_qualifier, "Expected topmost spatial qualifier.");
            AssertTrue(command.should_execute, "Expected direct topmost delete command to execute.");
        }

        static void TestSceneResolverResolvesTopmostQualifiedTarget()
        {
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject lowerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject upperSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                lowerSphere.name = "Lower Red Sphere";
                upperSphere.name = "Upper Red Sphere";
                lowerSphere.transform.position = new Vector3(0f, 0f, 0f);
                upperSphere.transform.position = new Vector3(0f, 2f, 0f);
                lowerSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                upperSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                catalog.ApplyTo(lowerSphere, red, true);
                catalog.ApplyTo(upperSphere, red, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    intent = VoiceIntentType.DeleteTarget,
                    should_execute = true,
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "sphere",
                    object_name = "sphere",
                    target_material_prompt = "red",
                    target_spatial_qualifier = "topmost"
                };

                SceneTargetResolution resolution = resolver.ResolveTargets(command, new SpatialSnapshot());
                AssertEqual(SceneTargetResolutionStatus.Single, resolution.status, "Expected topmost qualifier to resolve a single sphere.");
                AssertTrue(resolution.Target == upperSphere, "Expected topmost qualifier to resolve the upper sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(upperSphere);
                UnityEngine.Object.DestroyImmediate(lowerSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestSceneResolverResolvesBottommostQualifiedTarget()
        {
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject lowerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject upperSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                lowerSphere.name = "Lower Red Sphere";
                upperSphere.name = "Upper Red Sphere";
                lowerSphere.transform.position = new Vector3(0f, 0f, 0f);
                upperSphere.transform.position = new Vector3(0f, 2f, 0f);
                lowerSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                upperSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                catalog.ApplyTo(lowerSphere, red, true);
                catalog.ApplyTo(upperSphere, red, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    intent = VoiceIntentType.DeleteTarget,
                    should_execute = true,
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "sphere",
                    object_name = "sphere",
                    target_material_prompt = "red",
                    target_spatial_qualifier = "bottommost"
                };

                SceneTargetResolution resolution = resolver.ResolveTargets(command, new SpatialSnapshot());
                AssertEqual(SceneTargetResolutionStatus.Single, resolution.status, "Expected bottommost qualifier to resolve a single sphere.");
                AssertTrue(resolution.Target == lowerSphere, "Expected bottommost qualifier to resolve the lower sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(upperSphere);
                UnityEngine.Object.DestroyImmediate(lowerSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestSceneResolverKeepsLevelBottommostTargetsAmbiguous()
        {
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject firstSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject secondSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                firstSphere.name = "First Red Sphere";
                secondSphere.name = "Second Red Sphere";
                firstSphere.transform.position = new Vector3(-1f, 0f, 0f);
                secondSphere.transform.position = new Vector3(1f, 0f, 0f);
                firstSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                secondSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                catalog.ApplyTo(firstSphere, red, true);
                catalog.ApplyTo(secondSphere, red, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    intent = VoiceIntentType.DeleteTarget,
                    should_execute = true,
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "sphere",
                    object_name = "sphere",
                    target_material_prompt = "red",
                    target_spatial_qualifier = "bottommost"
                };

                SceneTargetResolution resolution = resolver.ResolveTargets(command, new SpatialSnapshot());
                AssertEqual(SceneTargetResolutionStatus.Ambiguous, resolution.status, "Expected bottommost qualifier to remain ambiguous when red spheres are level.");
                AssertTrue(resolution.message.Contains("can't tell", StringComparison.OrdinalIgnoreCase), "Expected unclear bottom relation message for level spheres.");
                AssertTrue(resolution.message.Contains("red sphere", StringComparison.OrdinalIgnoreCase), "Expected unclear bottom relation message to name red sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondSphere);
                UnityEngine.Object.DestroyImmediate(firstSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestSceneResolverKeepsSideBySideBottommostTargetsAmbiguous()
        {
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject slightlyLowerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject slightlyHigherSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                slightlyLowerSphere.name = "Slightly Lower Red Sphere";
                slightlyHigherSphere.name = "Slightly Higher Red Sphere";
                slightlyLowerSphere.transform.position = new Vector3(-2f, 0f, 0f);
                slightlyHigherSphere.transform.position = new Vector3(2f, 0.08f, 0f);
                slightlyLowerSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                slightlyHigherSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                catalog.ApplyTo(slightlyLowerSphere, red, true);
                catalog.ApplyTo(slightlyHigherSphere, red, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;

                VoiceIntentCommand command = new VoiceIntentCommand
                {
                    intent = VoiceIntentType.DeleteTarget,
                    should_execute = true,
                    target_reference = TargetReferenceMode.NamedObject,
                    target_name = "sphere",
                    object_name = "sphere",
                    target_material_prompt = "red",
                    target_spatial_qualifier = "bottommost"
                };

                SceneTargetResolution resolution = resolver.ResolveTargets(command, new SpatialSnapshot());
                AssertEqual(SceneTargetResolutionStatus.Ambiguous, resolution.status, "Expected bottommost qualifier to remain ambiguous for side-by-side floor spheres.");
                AssertTrue(resolution.message.Contains("can't tell", StringComparison.OrdinalIgnoreCase), "Expected unclear bottom relation message.");
                AssertTrue(resolution.message.Contains("red sphere", StringComparison.OrdinalIgnoreCase), "Expected unclear bottom relation message to name red sphere.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(slightlyHigherSphere);
                UnityEngine.Object.DestroyImmediate(slightlyLowerSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestRouterCompletesBottomOneAfterDeleteAmbiguity()
        {
            GameObject routerObject = new GameObject("VoiceCommandRouter");
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject resolverObject = new GameObject("SceneEntityResolver");
            GameObject transformControllerObject = new GameObject("TargetTransformController");
            GameObject catalogObject = new GameObject("RuntimeMaterialCatalog");
            GameObject lowerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject upperSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                lowerSphere.name = "Lower Red Sphere";
                upperSphere.name = "Upper Red Sphere";
                lowerSphere.transform.position = new Vector3(0f, 0f, 0f);
                upperSphere.transform.position = new Vector3(0f, 2f, 0f);
                lowerSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";
                upperSphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                RuntimeMaterialCatalog catalog = catalogObject.AddComponent<RuntimeMaterialCatalog>();
                RuntimeMaterialCatalog.TryParseDescriptor("red", RuntimeMaterialDescriptor.Default, out RuntimeMaterialDescriptor red);
                catalog.ApplyTo(lowerSphere, red, true);
                catalog.ApplyTo(upperSphere, red, true);

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                resolver.materialCatalog = catalog;
                TargetTransformController targetTransformController = transformControllerObject.AddComponent<TargetTransformController>();
                targetTransformController.entityResolver = resolver;

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.targetTransformController = targetTransformController;

                VoiceCommandRouter router = routerObject.AddComponent<VoiceCommandRouter>();
                router.dispatcher = dispatcher;

                var method = typeof(VoiceCommandRouter).GetMethod(
                    "HandleResult",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected router HandleResult method.");

                SpeechIntentResult ambiguous = new SpeechIntentResult
                {
                    success = true,
                    transcript = "Delete red sphere.",
                    command = LocalTypedIntentParser.Parse("delete red sphere")
                };
                method.Invoke(router, new object[] { ambiguous, new SpatialSnapshot() });
                AssertTrue(!ambiguous.command.should_execute, "Expected first delete to wait for clarification.");
                AssertTrue(ambiguous.command.spoken_response.Contains("Which red sphere", StringComparison.OrdinalIgnoreCase), "Expected first delete to ask which red sphere.");

                router.dispatcher = null;
                SpeechIntentResult reply = new SpeechIntentResult
                {
                    success = true,
                    transcript = "the bottom one",
                    command = new VoiceIntentCommand
                    {
                        transcript = "the bottom one",
                        intent = VoiceIntentType.Unknown,
                        should_execute = false,
                        spoken_response = "I'm not sure what action you want for the bottom one."
                    }
                };
                method.Invoke(router, new object[] { reply, new SpatialSnapshot() });

                AssertEqual(VoiceIntentType.DeleteTarget, reply.command.intent, "Expected router to complete pending delete instead of using Unknown model response.");
                AssertEqual("bottommost", reply.command.target_spatial_qualifier, "Expected bottom-one reply to add bottommost qualifier.");
                AssertTrue(reply.command.should_execute, "Expected bottom-one reply to execute the pending delete.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(upperSphere);
                UnityEngine.Object.DestroyImmediate(lowerSphere);
                UnityEngine.Object.DestroyImmediate(catalogObject);
                UnityEngine.Object.DestroyImmediate(transformControllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
                UnityEngine.Object.DestroyImmediate(routerObject);
            }
        }

        static VoiceIntentCommand BuildClarifiedDeleteCommand(string materialPrompt)
        {
            return new VoiceIntentCommand
            {
                transcript = materialPrompt,
                intent = VoiceIntentType.DeleteTarget,
                should_execute = true,
                spoken_response = $"Using {materialPrompt}.",
                target_reference = TargetReferenceMode.NamedObject,
                target_name = "sphere",
                object_name = "sphere",
                target_material_prompt = materialPrompt
            };
        }

        static void TestCreateSoundParsesQuietVolume()
        {
            VoiceIntentCommand command = LocalTypedIntentParser.Parse("make a quiet sound of a harp");

            AssertEqual(VoiceIntentType.CreateAudioSource, command.intent, "Expected quiet sound request to create audio.");
            AssertEqual("harp", command.sound_prompt, "Expected sound prompt without loudness adjective.");
            AssertApproximately(0.25f, command.audio_volume, 0.0001f, "Expected quiet volume.");
        }

        static void TestTargetRelativePlacementParsesMeAndTargetFrames()
        {
            VoiceIntentCommand meFrame = LocalTypedIntentParser.Parse("create a cube to the left of the sphere");
            AssertEqual(VoiceIntentType.PlaceObject, meFrame.intent, "Expected target-relative placement.");
            AssertEqual("cube", meFrame.object_name, "Expected placed object name.");
            AssertEqual(SpatialReferenceMode.RelativeToTarget, meFrame.spatial_reference, "Expected target-relative spatial mode.");
            AssertEqual("sphere", meFrame.target_name, "Expected target object name.");
            AssertEqual(RelativeDirection.Left, meFrame.relative_direction, "Expected left direction.");
            AssertEqual("me_frame", meFrame.placement_mode, "Expected default Me-oriented frame.");

            VoiceIntentCommand targetFrame = LocalTypedIntentParser.Parse("create a cube to the sphere's left");
            AssertEqual(VoiceIntentType.PlaceObject, targetFrame.intent, "Expected possessive target-relative placement.");
            AssertEqual("cube", targetFrame.object_name, "Expected placed object name.");
            AssertEqual(SpatialReferenceMode.RelativeToTarget, targetFrame.spatial_reference, "Expected target-relative spatial mode.");
            AssertEqual("sphere", targetFrame.target_name, "Expected target object name.");
            AssertEqual(RelativeDirection.Left, targetFrame.relative_direction, "Expected left direction.");
            AssertEqual("target_local", targetFrame.placement_mode, "Expected target-local frame for possessive placement.");

            VoiceIntentCommand above = LocalTypedIntentParser.Parse("create a cube above the sphere");
            AssertEqual(VoiceIntentType.PlaceObject, above.intent, "Expected above-target placement.");
            AssertEqual(SpatialReferenceMode.RelativeToTarget, above.spatial_reference, "Expected target-relative spatial mode for above.");
            AssertEqual(RelativeDirection.Up, above.relative_direction, "Expected above to map to Up.");

            VoiceIntentCommand below = LocalTypedIntentParser.Parse("create a cube below the sphere");
            AssertEqual(VoiceIntentType.PlaceObject, below.intent, "Expected below-target placement.");
            AssertEqual(SpatialReferenceMode.RelativeToTarget, below.spatial_reference, "Expected target-relative spatial mode for below.");
            AssertEqual(RelativeDirection.Down, below.relative_direction, "Expected below to map to Down.");
        }

        static void TestTargetRelativePlacementResolvesMeAndTargetFrames()
        {
            GameObject resolverObject = new GameObject("Resolver");
            GameObject controllerObject = new GameObject("ObjectPlacementController");
            GameObject sphere = new GameObject("sphere");

            try
            {
                sphere.transform.position = new Vector3(10f, 1f, 10f);
                sphere.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                sphere.AddComponent<SpeechIntentTrackable>().canonicalName = "sphere";

                SceneEntityResolver resolver = resolverObject.AddComponent<SceneEntityResolver>();
                ObjectPlacementController controller = controllerObject.AddComponent<ObjectPlacementController>();
                controller.entityResolver = resolver;

                SpatialSnapshot spatial = new SpatialSnapshot
                {
                    head_position = Vector3.zero,
                    head_forward = Vector3.forward
                };

                VoiceIntentCommand meFrame = LocalTypedIntentParser.Parse("create a cube to the left of the sphere");
                AssertTrue(controller.TryResolvePlacementPose(meFrame, spatial, out Vector3 meFramePosition, out _), "Expected Me-frame target placement to resolve.");
                AssertVectorApproximately(new Vector3(9f, 1f, 10f), meFramePosition, 0.0001f, "Expected left of sphere to use Me's left by default.");

                VoiceIntentCommand targetFrame = LocalTypedIntentParser.Parse("create a cube to the sphere's left");
                AssertTrue(controller.TryResolvePlacementPose(targetFrame, spatial, out Vector3 targetFramePosition, out _), "Expected target-local placement to resolve.");
                AssertVectorApproximately(new Vector3(10f, 1f, 11f), targetFramePosition, 0.0001f, "Expected sphere's left to use the sphere's local left.");

                VoiceIntentCommand above = LocalTypedIntentParser.Parse("create a cube above the sphere");
                AssertTrue(controller.TryResolvePlacementPose(above, spatial, out Vector3 abovePosition, out _), "Expected above-target placement to resolve.");
                AssertVectorApproximately(new Vector3(10f, 2f, 10f), abovePosition, 0.0001f, "Expected above sphere to move upward.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sphere);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(resolverObject);
            }
        }

        static void TestPlacementClarificationUnderstandsRelativeLocation()
        {
            bool parsed = VoiceCommandRouter.TryParsePlacementLocationOnlyForTests(
                "one meter in front of me",
                out SpatialReferenceMode spatialReference,
                out BodyAnchor bodyAnchor,
                out HandSelection handSelection,
                out RelativeDirection relativeDirection,
                out float relativeDistanceMeters);

            AssertTrue(parsed, "Expected relative placement clarification to parse.");
            AssertEqual(SpatialReferenceMode.RelativeToMe, spatialReference, "Expected RelativeToMe clarification.");
            AssertEqual(BodyAnchor.None, bodyAnchor, "Expected no body anchor.");
            AssertEqual(HandSelection.None, handSelection, "Expected no hand selection.");
            AssertEqual(RelativeDirection.InFront, relativeDirection, "Expected in-front direction.");
            AssertApproximately(1f, relativeDistanceMeters, 0.0001f, "Expected one meter distance.");
        }

        static void TestDialogStateCompletesPendingRotationDegrees()
        {
            var dialog = new CommandDialogStateManager();
            var pending = new VoiceIntentCommand
            {
                transcript = "rotate world on x axis",
                intent = VoiceIntentType.RotateTarget,
                should_execute = false,
                spoken_response = "How many degrees should I rotate the world?",
                target_reference = TargetReferenceMode.CurrentWorld,
                target_entity = "world",
                rotation_axis = RotationAxis.X,
                rotation_degrees = 0f
            };

            dialog.BeginClarification(pending, CommandClarificationSlot.RotationDegrees, pending.spoken_response);

            var reply = new SpeechIntentResult
            {
                success = false,
                transcript = "90",
                command = new VoiceIntentCommand
                {
                    transcript = "90",
                    intent = VoiceIntentType.AskClarification,
                    should_execute = false,
                    spoken_response = "What do you want me to rotate 90 degrees?"
                }
            };

            AssertTrue(dialog.TryComplete(reply, out VoiceIntentCommand completed), "Expected pending rotate command to complete from numeric reply.");
            AssertEqual(VoiceIntentType.RotateTarget, completed.intent, "Expected completed command to preserve rotate intent.");
            AssertEqual(TargetReferenceMode.CurrentWorld, completed.target_reference, "Expected completed command to preserve world target.");
            AssertEqual(RotationAxis.X, completed.rotation_axis, "Expected completed command to preserve X axis.");
            AssertApproximately(90f, completed.rotation_degrees, 0.0001f, "Expected completed command to use follow-up degrees.");
            AssertTrue(completed.should_execute, "Expected completed command to be executable.");
            AssertTrue(!dialog.HasPendingClarification, "Expected dialog state to clear after completion.");
        }

        static void TestLocalParserParsesWorldRotation()
        {
            VoiceIntentCommand full = LocalTypedIntentParser.Parse("rotate world by 90 degrees on x axis");
            AssertEqual(VoiceIntentType.RotateTarget, full.intent, "Expected world rotate intent.");
            AssertEqual(TargetReferenceMode.CurrentWorld, full.target_reference, "Expected current-world target reference.");
            AssertEqual(RotationAxis.X, full.rotation_axis, "Expected X axis.");
            AssertApproximately(90f, full.rotation_degrees, 0.0001f, "Expected 90 degree rotation.");
            AssertTrue(full.should_execute, "Expected full rotate command to execute immediately.");

            VoiceIntentCommand missingDegrees = LocalTypedIntentParser.Parse("rotate world on x axis");
            AssertEqual(VoiceIntentType.RotateTarget, missingDegrees.intent, "Expected incomplete world rotate intent.");
            AssertEqual(TargetReferenceMode.CurrentWorld, missingDegrees.target_reference, "Expected current-world target reference for incomplete rotate.");
            AssertEqual(RotationAxis.X, missingDegrees.rotation_axis, "Expected X axis for incomplete rotate.");
            AssertApproximately(0f, missingDegrees.rotation_degrees, 0.0001f, "Expected missing degrees to stay unset.");
            AssertTrue(!missingDegrees.should_execute, "Expected incomplete rotate command to wait for clarification.");
            AssertTrue(missingDegrees.spoken_response.ToLowerInvariant().Contains("how many degrees"), "Expected incomplete rotate command to ask for degrees.");
        }

        static void TestDialogStateCompletesPendingTargetQualifier()
        {
            var dialog = new CommandDialogStateManager();
            var pending = new VoiceIntentCommand
            {
                transcript = "make the sphere bigger",
                intent = VoiceIntentType.ScaleTarget,
                should_execute = false,
                spoken_response = "Which sphere?",
                target_reference = TargetReferenceMode.NamedObject,
                target_name = "sphere",
                scale_multiplier = 1.5f
            };

            dialog.BeginClarification(pending, CommandClarificationSlot.Target, pending.spoken_response);

            var reply = new SpeechIntentResult
            {
                success = false,
                transcript = "the red one",
                command = new VoiceIntentCommand
                {
                    transcript = "the red one",
                    intent = VoiceIntentType.AskClarification,
                    should_execute = false
                }
            };

            AssertTrue(dialog.TryComplete(reply, out VoiceIntentCommand completed), "Expected pending target command to complete from material qualifier reply.");
            AssertEqual(VoiceIntentType.ScaleTarget, completed.intent, "Expected completed command to preserve scale intent.");
            AssertEqual(TargetReferenceMode.NamedObject, completed.target_reference, "Expected completed command to preserve named target mode.");
            AssertEqual("sphere", completed.target_name, "Expected completed command to preserve ambiguous target name.");
            AssertEqual("red", completed.target_material_prompt, "Expected completed command to use follow-up material qualifier.");
            AssertApproximately(1.5f, completed.scale_multiplier, 0.0001f, "Expected completed command to preserve scale multiplier.");
            AssertTrue(completed.should_execute, "Expected completed target command to be executable.");
            AssertTrue(!dialog.HasPendingClarification, "Expected dialog state to clear after target completion.");
        }

        static void TestRouterStoresTargetClarificationSlot()
        {
            var command = new VoiceIntentCommand
            {
                transcript = "make the cube bigger",
                intent = VoiceIntentType.ScaleTarget,
                should_execute = false,
                spoken_response = "Which cube?",
                target_reference = TargetReferenceMode.NamedObject,
                target_name = "cube",
                scale_multiplier = 1.5f
            };

            AssertTrue(VoiceCommandRouter.TryGetClarificationSlotForTests(command, out CommandClarificationSlot slot), "Expected router to treat Which-target replies as pending clarification.");
            AssertEqual(CommandClarificationSlot.Target, slot, "Expected target clarification slot.");
        }

        static void TestSwitchToStaticWorldShowsArchMenu()
        {
            GameObject dispatcherObject = new GameObject("WorldActionDispatcher");
            GameObject uiObject = new GameObject("UiPanelController");
            GameObject archRoot = new GameObject("ArchLCARS");

            try
            {
                UiPanelController uiPanels = uiObject.AddComponent<UiPanelController>();
                uiPanels.deferStartupPanels = false;
                uiPanels.showPanelsExclusively = false;
                uiPanels.panels.Add(new UiPanelController.PanelEntry
                {
                    key = "arch_menu",
                    root = archRoot
                });

                WorldActionDispatcher dispatcher = dispatcherObject.AddComponent<WorldActionDispatcher>();
                dispatcher.uiPanels = uiPanels;

                archRoot.SetActive(false);
                dispatcher.Execute(new VoiceIntentCommand
                {
                    intent = VoiceIntentType.SwitchToStaticWorld,
                    should_execute = true,
                    transcript = "end program"
                }, new SpatialSnapshot());

                AssertTrue(archRoot.activeSelf, "Expected switching to static world to show the Arch menu.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(archRoot);
                UnityEngine.Object.DestroyImmediate(uiObject);
                UnityEngine.Object.DestroyImmediate(dispatcherObject);
            }
        }

        static void TestLocalParserParsesTeleportPadCommands()
        {
            VoiceIntentCommand create = LocalTypedIntentParser.Parse("add teleport here");
            AssertEqual(VoiceIntentType.PlaceObject, create.intent, "Expected add teleport here to place an object.");
            AssertEqual("teleport pad", create.object_name, "Expected teleport pad object name.");
            AssertEqual("under_foot", create.placement_mode, "Expected teleport pad placement under foot.");

            VoiceIntentCommand removeThis = LocalTypedIntentParser.Parse("delete this teleporter");
            AssertEqual(VoiceIntentType.DeleteTarget, removeThis.intent, "Expected delete this teleporter to parse as delete.");
            AssertEqual(TargetReferenceMode.PointedObject, removeThis.target_reference, "Expected this teleporter to use pointed target.");
            AssertEqual("teleporter", removeThis.target_name, "Expected teleporter target name.");

            VoiceIntentCommand removeAll = LocalTypedIntentParser.Parse("remove all teleports");
            AssertEqual(VoiceIntentType.DeleteTarget, removeAll.intent, "Expected remove all teleports to parse as delete.");
            AssertEqual(TargetReferenceMode.All, removeAll.target_reference, "Expected all teleports to target all.");
            AssertEqual("teleports", removeAll.target_name, "Expected teleports target name.");
        }

        static void TestTeleportPadPlacementResolvesUnderFoot()
        {
            GameObject me = new GameObject("Me");
            GameObject controllerObject = new GameObject("ObjectPlacementController");

            try
            {
                me.transform.position = new Vector3(2f, 0f, 3f);

                ObjectPlacementController controller = controllerObject.AddComponent<ObjectPlacementController>();
                VoiceIntentCommand command = LocalTypedIntentParser.Parse("add teleport here");

                bool resolved = controller.TryResolvePlacementPose(
                    command,
                    new SpatialSnapshot
                    {
                        head_position = new Vector3(2f, 1.6f, 3f),
                        head_forward = Vector3.forward
                    },
                    out Vector3 position,
                    out Quaternion rotation);

                AssertTrue(resolved, "Expected teleport pad placement to resolve.");
                AssertVectorApproximately(new Vector3(2f, 0f, 3f), position, 0.0001f, "Expected teleport pad at Me's feet when no floor raycast hits.");
                AssertVectorApproximately(Vector3.forward, rotation * Vector3.forward, 0.0001f, "Expected teleport pad to face head forward.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(me);
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        static void TestOpenAiSchemaAllowsBehaviorCommands()
        {
            GameObject serviceObject = new GameObject("OpenAiSpeechIntentService");
            try
            {
                OpenAiSpeechIntentService service = serviceObject.AddComponent<OpenAiSpeechIntentService>();
                var method = typeof(OpenAiSpeechIntentService).GetMethod("BuildCommandJsonSchema", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                AssertTrue(method != null, "Expected to find OpenAI schema builder.");
                string schema = method.Invoke(service, null) as string;
                AssertTrue(schema != null && schema.Contains(@"""AttachBehavior"""), "Expected schema to allow AttachBehavior intent.");
                AssertTrue(schema.Contains(@"""StopBehavior"""), "Expected schema to allow StopBehavior intent.");
                AssertTrue(schema.Contains(@"""behavior_name"""), "Expected schema to include behavior_name field.");
                AssertTrue(schema.Contains(@"""behavior_stop_all"""), "Expected schema to include behavior_stop_all field.");
                AssertTrue(schema.Contains(@"""ModifyPhysics"""), "Expected schema to allow ModifyPhysics intent.");
                AssertTrue(schema.Contains(@"""physics_action"""), "Expected schema to include physics_action field.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(serviceObject);
            }
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }

        static void AssertSame(UnityEngine.Object expected, UnityEngine.Object actual, string message)
        {
            if (!ReferenceEquals(expected, actual))
                throw new InvalidOperationException($"{message} Expected={expected?.name}, Actual={actual?.name}");
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}");
        }

        static void AssertApproximately(float expected, float actual, float tolerance, string message)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}, Tolerance={tolerance}");
        }

        static void AssertVectorApproximately(Vector3 expected, Vector3 actual, float tolerance, string message)
        {
            if (Vector3.Distance(expected, actual) > tolerance)
                throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}, Tolerance={tolerance}");
        }
    }
}

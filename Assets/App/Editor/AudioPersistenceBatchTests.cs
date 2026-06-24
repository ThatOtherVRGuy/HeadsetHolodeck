using System;
using System.Collections.Generic;
using System.Reflection;
using Holodeck.Save;
using SpeechIntent;
using SpeechIntent.Audio;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class AudioPersistenceBatchTests
    {
        public static void RunAll()
        {
            try
            {
                TestAutomaticAmbienceIsSkippedWhenSavedAudioExists();
                TestRemovingAudioRemovesItsSavedObject();
                Debug.Log("[AudioPersistenceBatchTests] All tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AudioPersistenceBatchTests] Tests failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static void TestAutomaticAmbienceIsSkippedWhenSavedAudioExists()
        {
            MethodInfo method = typeof(AudioWorldActionController).GetMethod(
                "ShouldCreateAutomaticAmbience",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            AssertTrue(method != null, "AudioWorldActionController should decide whether restored worlds need automatic ambience.");

            var savedWorld = new WorldConfig
            {
                objects = new List<SavedObject>
                {
                    new SavedObject
                    {
                        components = new List<SavedComponent> { new SavedComponent { type = "AudioSource" } }
                    }
                }
            };

            bool shouldCreate = (bool)method.Invoke(null, new object[] { savedWorld });
            AssertTrue(!shouldCreate, "A restored world with saved audio must not add a replacement automatic ambience layer.");
        }

        static void TestRemovingAudioRemovesItsSavedObject()
        {
            GameObject autoSaveObject = new GameObject("AudioPersistenceAutoSave_Test");
            GameObject audioObject = new GameObject("AudioPersistenceAudio_Test");
            try
            {
                var autoSave = autoSaveObject.AddComponent<WorldConfigAutoSave>();
                var trackable = audioObject.AddComponent<SpeechIntentTrackable>();
                trackable.configInstanceId = "audio-to-remove";
                autoSave.ActiveConfig = new WorldConfig
                {
                    objects = new List<SavedObject>
                    {
                        new SavedObject { instance_id = "audio-to-remove" },
                        new SavedObject { instance_id = "keep-me" }
                    }
                };

                MethodInfo method = typeof(WorldConfigAutoSave).GetMethod(
                    "RemoveSavedObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                AssertTrue(method != null, "WorldConfigAutoSave should remove a destroyed object's saved record.");

                bool removed = (bool)method.Invoke(autoSave, new object[] { audioObject });
                AssertTrue(removed, "Expected the matching saved audio record to be removed.");
                AssertTrue(autoSave.ActiveConfig.objects.Count == 1 && autoSave.ActiveConfig.objects[0].instance_id == "keep-me",
                    "Removing audio must preserve unrelated saved objects.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(audioObject);
                UnityEngine.Object.DestroyImmediate(autoSaveObject);
            }
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}

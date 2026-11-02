using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Holodeck.Save;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class WorldConfigMigrationBatchTests
    {
        public static void RunReadOnlyConfigMigrationTest()
        {
            string root = Path.Combine(Path.GetTempPath(), "HolodeckMigrationBatch_" + Guid.NewGuid().ToString("N"));
            WorldConfigStore store = null;
            try
            {
                store = WorldConfigStore.CreateForTesting(root);
                WorldConfig config = store.CreateConfig(
                    new WorldSourceData { type = "worldlabs", world_id = "world-123", display_name = "Cabin" },
                    "Cabin",
                    null);
                string legacyId = config.config_id;
                config.world_transform = new WorldTransformData
                {
                    position = new Vector3(2f, 3f, 4f),
                    rotation = Quaternion.Euler(0f, 90f, 0f),
                    scale = new Vector3(2f, 2f, 2f)
                };
                config.prompts.Add(new PromptEntry { transcript = "Move world 2 meters forward." });
                store.SaveConfig(config);

                WorldConfig migrated = store.MigrateConfigToWritableCopy(config);
                AssertTrue(ReferenceEquals(config, migrated), "Migration must preserve the active config reference.");
                AssertTrue(migrated.config_id != legacyId, "Migration must use a new writable config ID.");
                AssertEqual(legacyId, migrated.migrated_from_config_id, "Migration must retain the legacy config ID.");
                AssertTrue(File.Exists(Path.Combine(root, migrated.config_id, "world.json")), "Migrated world.json was not written.");
                AssertApproximately(2f, migrated.world_transform.scale.x, "World transform was not preserved.");

                MethodInfo loadExisting = typeof(WorldConfigStore).GetMethod(
                    "LoadExistingConfigs",
                    BindingFlags.Static | BindingFlags.NonPublic);
                AssertTrue(loadExisting != null, "Could not locate config scan method.");
                var scanned = (List<WorldConfig>)loadExisting.Invoke(null, new object[] { root });
                AssertEqual(1, scanned.Count, "Legacy config should be suppressed after rescan.");
                AssertEqual(migrated.config_id, scanned[0].config_id, "Rescan selected the wrong config.");

                Debug.Log("[WorldConfigMigrationBatchTests] Read-only config migration test passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[WorldConfigMigrationBatchTests] Read-only config migration test failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                if (store != null) UnityEngine.Object.DestroyImmediate(store.gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual)) throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        static void AssertApproximately(float expected, float actual, string message)
        {
            if (Mathf.Abs(expected - actual) > 0.0001f)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}

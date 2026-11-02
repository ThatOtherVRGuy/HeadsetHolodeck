using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Holodeck.Save;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class WorldConfigStoreBatchTests
    {
        public static void RunMigrationTests()
        {
            try
            {
                TestSpawnPointSerializesAsPlainJson();
                TestMultipleStoresCreateOneConfigPerLooseSplat().GetAwaiter().GetResult();
                Debug.Log("[WorldConfigStoreBatchTests] Migration tests passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[WorldConfigStoreBatchTests] Migration tests failed: " + ex);
                EditorApplication.Exit(1);
                throw;
            }
        }

        static Task TestMultipleStoresCreateOneConfigPerLooseSplat()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "WorldConfigStoreBatchTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempRoot);
                File.WriteAllBytes(Path.Combine(tempRoot, "ImportedRoom.spz"), new byte[] { 1, 2, 3, 4 });

                string cachedRoot = Path.Combine(tempRoot, "CachedWorlds");
                MethodInfo migrationMethod = typeof(WorldConfigStore).GetMethod(
                    "GetOrCreateMigrationTask",
                    BindingFlags.NonPublic | BindingFlags.Static);

                AssertTrue(migrationMethod != null, "Could not find WorldConfigStore migration gate.");

                var firstMigration = (Task)migrationMethod.Invoke(null, new object[] { tempRoot, cachedRoot });
                var secondMigration = (Task)migrationMethod.Invoke(null, new object[] { tempRoot, cachedRoot });
                Task.WhenAll(firstMigration, secondMigration).GetAwaiter().GetResult();

                string[] configJsonFiles = Directory.GetFiles(tempRoot, "world.json", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) != "CachedWorlds")
                    .ToArray();

                AssertEqual(1, configJsonFiles.Length, "Expected one migrated world config for one loose splat.");
                AssertTrue(File.Exists(Path.Combine(tempRoot, "CachedWorlds", "ImportedRoom.spz")), "Expected splat to move into CachedWorlds.");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }

            return Task.CompletedTask;
        }

        static void TestSpawnPointSerializesAsPlainJson()
        {
            var config = new WorldConfig
            {
                config_id = "SpawnJson",
                display_name = "Spawn Json",
                world_source = new WorldSourceData { type = "local_splat" }
            };
            config.spawn_points.Add(new SpawnPointData
            {
                name = "Entry",
                source = "manual",
                position = new Vector3(1f, 1.6f, 2f),
                rotation = Quaternion.Euler(0f, 90f, 0f),
                look_at = new Vector3(1f, 1.4f, 3f),
                confidence = 1f
            });

            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            AssertTrue(json.Contains("\"spawn_points\""), "Expected spawn_points in JSON.");
            AssertTrue(json.Contains("\"position\""), "Expected position in spawn point JSON.");
            AssertTrue(json.Contains("\"x\""), "Expected compact vector x field.");
            AssertTrue(!json.Contains("normalized"), "Spawn point JSON must not serialize Unity Vector3 computed properties.");

            WorldConfig restored = JsonConvert.DeserializeObject<WorldConfig>(json);
            AssertEqual(1, restored.spawn_points.Count, "Expected one restored spawn point.");
            Vector3 restoredPosition = restored.spawn_points[0].position;
            AssertApproximately(1.6f, restoredPosition.y, 0.0001f, "Expected restored spawn point position.");
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        static void AssertApproximately(float expected, float actual, float tolerance, string message)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}

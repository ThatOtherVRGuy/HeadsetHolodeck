// Assets/App/Save/Tests/WorldConfigSerializationTests.cs
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Holodeck.Save;
using System.Collections.Generic;

namespace Holodeck.Save.Tests
{
    public class WorldConfigSerializationTests
    {
        [Test]
        public void WorldConfig_JsonRoundTrip_PreservesAllFields()
        {
            var config = new WorldConfig
            {
                config_id    = "Beach_2026-04-15T103000Z",
                display_name = "Beach",
                created_at   = "2026-04-15T10:30:00Z",
                modified_at  = "2026-04-15T11:00:00Z",
                generation_model = "Standard",
                world_source = new WorldSourceData
                {
                    type         = "worldlabs",
                    world_id     = "abc123",
                    display_name = "Sunny Beach",
                    cached_splat = "../CachedWorlds/abc123.spz",
                    cached_pano  = null
                },
                world_transform = new WorldTransformData
                {
                    position = new UnityEngine.Vector3(1f, 2f, 3f),
                    rotation = UnityEngine.Quaternion.Euler(0f, 45f, 0f),
                    scale = new UnityEngine.Vector3(2f, 2f, 2f)
                },
                lighting = new LightingData { preset = "Golden Hour", sun_azimuth = 220f, sun_elevation = 35f }
            };
            config.spawn_points.Add(new SpawnPointData
            {
                name = "Entry",
                source = "manual",
                position = new UnityEngine.Vector3(1f, 1.6f, 2f),
                rotation = UnityEngine.Quaternion.Euler(0f, 90f, 0f),
                created_at = "2026-04-15T10:35:00Z"
            });
            config.prompts.Add(new PromptEntry
            {
                timestamp  = "2026-04-15T10:30:00Z",
                type       = "world_creation",
                intent     = "GenerateWorld",
                transcript = "a sunny beach with palm trees"
            });
            config.objects.Add(new SavedObject
            {
                instance_id  = "chair_abc123",
                prefab_name  = "beach chair",
                display_name = "Beach Chair",
                components   = new List<SavedComponent>
                {
                    new SavedComponent
                    {
                        type = "Transform",
                        data = JObject.Parse("{\"position\":{\"x\":1.2,\"y\":0,\"z\":2.5}}")
                    }
                }
            });

            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var restored = JsonConvert.DeserializeObject<WorldConfig>(json);

            Assert.AreEqual(config.config_id,                 restored.config_id);
            Assert.AreEqual(config.display_name,              restored.display_name);
            Assert.AreEqual(config.world_source.world_id,     restored.world_source.world_id);
            Assert.AreEqual(config.world_source.cached_splat, restored.world_source.cached_splat);
            Assert.IsNull(restored.world_source.cached_pano);
            Assert.AreEqual(1, restored.prompts.Count);
            Assert.AreEqual("a sunny beach with palm trees", restored.prompts[0].transcript);
            Assert.AreEqual(1, restored.objects.Count);
            Assert.AreEqual("chair_abc123", restored.objects[0].instance_id);
            Assert.AreEqual("Transform", restored.objects[0].components[0].type);
            Assert.AreEqual(1.2f, (float)restored.objects[0].components[0].data["position"]["x"], 0.001f);
            Assert.AreEqual(1, restored.schema_version);
            Assert.AreEqual(220f, restored.lighting.sun_azimuth, 0.001f);
            Assert.IsNotNull(restored.world_transform);
            Assert.AreEqual(2f, restored.world_transform.position.y, 0.001f);
            Assert.AreEqual(45f, ((UnityEngine.Quaternion)restored.world_transform.rotation).eulerAngles.y, 0.001f);
            Assert.AreEqual(2f, restored.world_transform.scale.x, 0.001f);
            Assert.AreEqual(1, restored.spawn_points.Count);
            Assert.AreEqual("Entry", restored.spawn_points[0].name);
            Assert.AreEqual("manual", restored.spawn_points[0].source);
            Assert.AreEqual(1f, restored.spawn_points[0].position.x, 0.001f);
            UnityEngine.Quaternion restoredRotation = restored.spawn_points[0].rotation;
            Assert.AreEqual(90f, restoredRotation.eulerAngles.y, 0.001f);
        }

        [Test]
        public void WorldConfig_NullOptionalFields_SerializesCleanly()
        {
            var config = new WorldConfig
            {
                config_id    = "MinimalConfig",
                display_name = "Minimal",
                world_source = new WorldSourceData { type = "local_splat", cached_splat = "../CachedWorlds/file.spz" }
            };

            string json = JsonConvert.SerializeObject(config);
            var restored = JsonConvert.DeserializeObject<WorldConfig>(json);

            Assert.IsNull(restored.lighting);
            Assert.IsNull(restored.world_source.world_id);
            Assert.AreEqual(0, restored.prompts.Count);
            Assert.AreEqual(0, restored.objects.Count);
            Assert.AreEqual(0, restored.spawn_points.Count);
        }
    }
}

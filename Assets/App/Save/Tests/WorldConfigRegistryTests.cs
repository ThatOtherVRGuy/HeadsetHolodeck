// Assets/App/Save/Tests/WorldConfigRegistryTests.cs
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Holodeck.Save;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Holodeck.Save.Tests
{
    public class WorldConfigRegistryTests
    {
        // Minimal in-test serializer
        class FakeSerializer : IComponentSerializer
        {
            public string TypeName => "Fake";
            public bool SaveCalled;
            public bool RestoreCalled;
            public JObject Save(GameObject go) { SaveCalled = true; return new JObject { ["value"] = 42 }; }
            public void Restore(GameObject go, JObject data, RestorationContext ctx) { RestoreCalled = true; }
        }

        [SetUp]
        public void SetUp() => WorldConfigComponentRegistry.ClearForTesting();

        [Test]
        public void Register_AddsSerializer()
        {
            WorldConfigComponentRegistry.Register(new FakeSerializer());
            Assert.AreEqual(1, WorldConfigComponentRegistry.CountForTesting());
        }

        [Test]
        public void Register_NullSerializer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                WorldConfigComponentRegistry.Register(null));
        }

        [Test]
        public void SaveAll_CallsSaveOnRegisteredSerializer()
        {
            var fake = new FakeSerializer();
            WorldConfigComponentRegistry.Register(fake);
            var go = new GameObject("TestGO");
            try
            {
                List<SavedComponent> result = WorldConfigComponentRegistry.SaveAll(go);
                Assert.IsTrue(fake.SaveCalled);
                Assert.AreEqual(1, result.Count);
                Assert.AreEqual("Fake", result[0].type);
                Assert.AreEqual(42, result[0].data["value"].Value<int>());
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void RestoreAll_CallsRestoreOnMatchingSerializer()
        {
            var fake = new FakeSerializer();
            WorldConfigComponentRegistry.Register(fake);
            var go = new GameObject("TestGO");
            var ctx = new RestorationContext { ConfigFolderPath = "/tmp", Config = new WorldConfig() };
            var components = new List<SavedComponent>
            {
                new SavedComponent { type = "Fake", data = new JObject() }
            };
            try
            {
                WorldConfigComponentRegistry.RestoreAll(go, components, ctx);
                Assert.IsTrue(fake.RestoreCalled);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void RestoreAll_UnknownType_LogsWarningAndDoesNotThrow()
        {
            var go = new GameObject("TestGO");
            var ctx = new RestorationContext { ConfigFolderPath = "/tmp", Config = new WorldConfig() };
            var components = new List<SavedComponent>
            {
                new SavedComponent { type = "DoesNotExist", data = new JObject() }
            };
            try
            {
                Assert.DoesNotThrow(() =>
                    WorldConfigComponentRegistry.RestoreAll(go, components, ctx));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

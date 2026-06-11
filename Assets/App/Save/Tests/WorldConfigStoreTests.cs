// Assets/App/Save/Tests/WorldConfigStoreTests.cs
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;
using Holodeck.Save;
using Newtonsoft.Json;
using System.Linq;

namespace Holodeck.Save.Tests
{
    public class WorldConfigStoreTests
    {
        string _tempRoot;
        WorldConfigStore _store;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "HolodeckSaveTests_" + System.Guid.NewGuid().ToString("N")[..8]);
            _store = WorldConfigStore.CreateForTesting(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void CreateConfig_WritesJsonToDisk()
        {
            var source = new WorldSourceData { type = "worldlabs", world_id = "abc123", display_name = "Beach" };
            var prompt = new PromptEntry { timestamp = "2026-04-15T10:00:00Z", type = "world_creation", transcript = "a beach" };

            WorldConfig config = _store.CreateConfig(source, "My Beach", prompt);

            string expectedPath = Path.Combine(_tempRoot, config.config_id, "world.json");
            Assert.IsTrue(File.Exists(expectedPath), $"world.json not found at {expectedPath}");

            var loaded = JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(expectedPath));
            Assert.AreEqual("My Beach", loaded.display_name);
            Assert.AreEqual("worldlabs", loaded.world_source.type);
            Assert.AreEqual("abc123", loaded.world_source.world_id);
            Assert.AreEqual(1, loaded.prompts.Count);
        }

        [Test]
        public void SaveConfig_UpdatesModifiedAt()
        {
            var source = new WorldSourceData { type = "local_splat" };
            WorldConfig config = _store.CreateConfig(source, "Test", null);
            string originalModifiedAt = config.modified_at;

            System.Threading.Thread.Sleep(1100);
            config.display_name = "Updated";
            _store.SaveConfig(config);

            string json = File.ReadAllText(Path.Combine(_tempRoot, config.config_id, "world.json"));
            var reloaded = JsonConvert.DeserializeObject<WorldConfig>(json);
            Assert.AreEqual("Updated", reloaded.display_name);
            Assert.AreNotEqual(originalModifiedAt, reloaded.modified_at);
        }

        [Test]
        public void DeleteConfig_RemovesFolderAndInMemory()
        {
            var source = new WorldSourceData { type = "local_splat" };
            WorldConfig config = _store.CreateConfig(source, "ToDelete", null);
            string folder = Path.Combine(_tempRoot, config.config_id);
            Assert.IsTrue(Directory.Exists(folder));

            _store.DeleteConfig(config.config_id);

            Assert.IsFalse(Directory.Exists(folder));
            Assert.AreEqual(0, _store.ListConfigs().Count);
        }

        [Test]
        public void ListConfigs_ReturnsAllCreated()
        {
            _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "A", null);
            _store.CreateConfig(new WorldSourceData { type = "worldlabs", world_id = "x" }, "B", null);

            Assert.AreEqual(2, _store.ListConfigs().Count);
        }

        [Test]
        public void HasConfigForWorldId_ReturnsTrueWhenExists()
        {
            _store.CreateConfig(new WorldSourceData { type = "worldlabs", world_id = "wl_abc" }, "Test", null);
            Assert.IsTrue(_store.HasConfigForWorldId("wl_abc"));
            Assert.IsFalse(_store.HasConfigForWorldId("wl_xyz"));
            Assert.IsFalse(_store.HasConfigForWorldId(null));
        }

        [Test]
        public void ForkConfig_CreatesSeparateFolderWithSameObjects()
        {
            var source = new WorldSourceData { type = "worldlabs", world_id = "wl1", display_name = "Beach" };
            WorldConfig original = _store.CreateConfig(source, "Beach Empty", null);
            original.objects.Add(new SavedObject { instance_id = "chair_001", prefab_name = "beach chair" });
            _store.SaveConfig(original);

            WorldConfig fork = _store.ForkConfig(original, "Beach With Chairs");

            Assert.AreNotEqual(original.config_id, fork.config_id);
            Assert.AreEqual("Beach With Chairs", fork.display_name);
            Assert.AreEqual(1, fork.objects.Count);
            Assert.AreEqual("chair_001", fork.objects[0].instance_id);
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempRoot, fork.config_id)));
            Assert.AreNotEqual(original.created_at, fork.created_at);
        }

        [Test]
        public async Task ScanAndMigrateAsync_WithMultipleStores_CreatesOneConfigPerLooseSplat()
        {
            string looseSplat = Path.Combine(_tempRoot, "ImportedRoom.spz");
            File.WriteAllBytes(looseSplat, new byte[] { 1, 2, 3, 4 });

            WorldConfigStore secondStore = WorldConfigStore.CreateForTesting(_tempRoot);

            await Task.WhenAll(
                _store.ScanAndMigrateAsync(),
                secondStore.ScanAndMigrateAsync());

            string[] configJsonFiles = Directory.GetFiles(_tempRoot, "world.json", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) != "CachedWorlds")
                .ToArray();

            Assert.AreEqual(1, configJsonFiles.Length, "Expected one migrated world config for one loose splat even with multiple WorldConfigStore instances.");
            Assert.AreEqual(1, _store.ListConfigs().Count, "Expected first store to load one config.");
            Assert.AreEqual(1, secondStore.ListConfigs().Count, "Expected second store to load one config.");
            Assert.IsTrue(File.Exists(Path.Combine(_tempRoot, "CachedWorlds", "ImportedRoom.spz")), "Expected loose splat to move into CachedWorlds.");
        }
    }
}

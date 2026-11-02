// Assets/App/Shared/Save/Tests/WorldConfigStoreTests.cs
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;
using Holodeck.Save;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;

namespace Holodeck.Save.Tests
{
    public class WorldConfigStoreTests
    {
        string _tempRoot;
        WorldConfigStore _store;
        readonly List<WorldConfigStore> _stores = new List<WorldConfigStore>();

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "HolodeckSaveTests_" + System.Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempRoot);
            _store = CreateStore();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (WorldConfigStore store in _stores)
                if (store != null)
                    UnityEngine.Object.DestroyImmediate(store.gameObject);
            _stores.Clear();

            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        WorldConfigStore CreateStore()
        {
            WorldConfigStore store = WorldConfigStore.CreateForTesting(_tempRoot);
            _stores.Add(store);
            return store;
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
            // Timestamps have second precision. Backdate the source so this checks
            // that a fork gets a fresh timestamp without depending on wall-clock delays.
            const string originalCreatedAt = "2000-01-01T000000Z";
            original.created_at = originalCreatedAt;
            original.objects.Add(new SavedObject { instance_id = "chair_001", prefab_name = "beach chair" });
            _store.SaveConfig(original);

            WorldConfig fork = _store.ForkConfig(original, "Beach With Chairs");

            Assert.AreNotEqual(original.config_id, fork.config_id);
            Assert.AreEqual("Beach With Chairs", fork.display_name);
            Assert.AreEqual(1, fork.objects.Count);
            Assert.AreEqual("chair_001", fork.objects[0].instance_id);
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempRoot, fork.config_id)));
            Assert.AreNotEqual(original.created_at, fork.created_at);
            Assert.AreEqual(originalCreatedAt, original.created_at, "Forking must not change the source timestamp.");
            Assert.AreEqual(fork.created_at, fork.modified_at);
        }

        [Test]
        public async Task MigrateConfigToWritableCopy_PreservesDataAndHidesLegacyDuplicateOnNextScan()
        {
            WorldConfig config = _store.CreateConfig(
                new WorldSourceData { type = "worldlabs", world_id = "world-123", display_name = "Cabin" },
                "Cabin",
                null);
            string legacyId = config.config_id;
            config.world_transform = new WorldTransformData
            {
                position = new UnityEngine.Vector3(2f, 3f, 4f),
                rotation = UnityEngine.Quaternion.Euler(0f, 90f, 0f),
                scale = new UnityEngine.Vector3(2f, 2f, 2f)
            };
            config.prompts.Add(new PromptEntry { transcript = "Move world 2 meters forward." });
            _store.SaveConfig(config);

            WorldConfig migrated = _store.MigrateConfigToWritableCopy(config);

            Assert.AreSame(config, migrated, "Migration should preserve existing ActiveConfig references.");
            Assert.AreNotEqual(legacyId, migrated.config_id);
            Assert.AreEqual(legacyId, migrated.migrated_from_config_id);
            Assert.IsTrue(File.Exists(Path.Combine(_tempRoot, migrated.config_id, "world.json")));
            Assert.AreEqual(2f, migrated.world_transform.scale.x, 0.001f);
            Assert.AreEqual(1, migrated.prompts.Count);

            WorldConfigStore reloadedStore = CreateStore();
            await reloadedStore.ScanAndMigrateAsync();
            Assert.AreEqual(1, reloadedStore.ListConfigs().Count,
                "The migrated config should suppress its read-only legacy duplicate on later scans.");
            Assert.AreEqual(migrated.config_id, reloadedStore.ListConfigs()[0].config_id);
        }

        [Test]
        public async Task ScanAndMigrateAsync_WithMultipleStores_CreatesOneConfigPerLooseSplat()
        {
            string looseSplat = Path.Combine(_tempRoot, "ImportedRoom.spz");
            File.WriteAllBytes(looseSplat, new byte[] { 1, 2, 3, 4 });

            WorldConfigStore secondStore = CreateStore();

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

        // ── Save durability: temporary writes, atomic replacement, backup and recovery ─────────────

        [Test]
        public void FirstSave_WritesPrimaryWithoutBackupAndRetainsWriterLock()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            string dir = ConfigDir(config);
            string path = ConfigPath(config);

            Assert.IsTrue(File.Exists(path), "The first save must commit world.json.");
            Assert.IsFalse(File.Exists(WorldConfigFile.BackupPath(path)),
                "A first save has no previous generation to preserve as world.json.bak.");
            Assert.AreEqual("First", ReadDisplayName(path));
            AssertNoTemporaries(dir, "A successful save must remove its temporary files.");
            Assert.IsTrue(File.Exists(WorldConfigFile.WriterLockPath(dir)),
                "The writer lock file is intentionally retained so concurrent writers keep locking one inode.");
        }

        [Test]
        public void RepeatedSave_KeepsPreviousGenerationAsBackup()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);

            config.display_name = "Second";
            _store.SaveConfig(config);

            Assert.AreEqual("Second", ReadDisplayName(path), "The newest committed save is the primary.");
            Assert.AreEqual("First", ReadDisplayName(backup), "The previous generation becomes the recovery backup.");
            AssertNoTemporaries(dir, "A successful save must remove its temporary files.");

            config.display_name = "Third";
            _store.SaveConfig(config);

            Assert.AreEqual("Third", ReadDisplayName(path));
            Assert.AreEqual("Second", ReadDisplayName(backup), "The backup tracks one generation behind the primary.");
            AssertNoTemporaries(dir, "Repeated saves must not accumulate temporary files.");
        }

        [Test]
        public void SaveConfig_WhenPrimaryIsCorrupt_RecoversPreviousGenerationAndRepairsOnNextSave()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "Original", null);
            config.display_name = "Committed";
            _store.SaveConfig(config);

            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);
            string committedJson = File.ReadAllText(path);

            // A process killed inside a direct overwrite leaves a truncated primary behind.
            File.WriteAllText(path, committedJson.Substring(0, committedJson.Length / 2));

            WorldConfig recovered = _store.LoadConfig(config.config_id);

            Assert.IsNotNull(recovered, "A corrupt primary with a valid backup must still load.");
            Assert.AreEqual("Original", recovered.display_name,
                "Recovery yields the previous committed generation, not the interrupted document.");
            Assert.IsTrue(File.Exists(path) && File.Exists(backup), "Recovery must retain both files.");
            Assert.AreEqual((long)(committedJson.Length / 2), new FileInfo(path).Length,
                "Recovery must not silently rewrite the primary it could not trust.");

            // The next successful save repairs the primary and must not clobber the valid backup with
            // the corrupt primary it just read.
            config.display_name = "Repaired";
            _store.SaveConfig(config);

            Assert.AreEqual("Repaired", ReadDisplayName(path));
            Assert.AreEqual("Original", ReadDisplayName(backup), "A corrupt primary must never replace a valid backup.");
            Assert.AreEqual("Repaired", _store.LoadConfig(config.config_id).display_name);
            AssertNoTemporaries(dir, "The repairing save must clean up after itself.");
        }

        [Test]
        public void LoadConfig_WhenPrimaryIsMissing_RecoversFromBackup()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "Original", null);
            config.display_name = "Committed";
            _store.SaveConfig(config);

            string path = ConfigPath(config);
            File.Delete(path);

            WorldConfig recovered = _store.LoadConfig(config.config_id);

            Assert.IsNotNull(recovered, "A missing primary with a valid backup must recover.");
            Assert.AreEqual("Original", recovered.display_name);
            Assert.IsFalse(File.Exists(path), "Recovery is read-only: it does not recreate the primary.");
        }

        [Test]
        public void LoadConfig_WhenNoSaveFileExists_ReturnsNull()
        {
            string id = "Empty_2026-04-15T103000Z";
            Directory.CreateDirectory(Path.Combine(_tempRoot, id));

            Assert.IsNull(_store.LoadConfig(id), "A folder with no committed save must load as null.");
            Assert.IsNull(_store.LoadConfig("DoesNotExist_2026-04-15T103000Z"), "An unknown config id must load as null.");
            Assert.IsNull(_store.LoadConfig(null), "A null config id must load as null.");
        }

        [Test]
        public void LoadConfig_IgnoresAbandonedTemporaryFiles()
        {
            string id = "Abandoned_2026-04-15T103000Z";
            string dir = Path.Combine(_tempRoot, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "world.json.deadbeef.tmp"),
                "{\"display_name\":\"Torn\",\"world_source\":{\"type\":\"local_splat\"}}");

            Assert.IsNull(_store.LoadConfig(id), "An abandoned temporary file is never a commit.");
        }

        [Test]
        public void LoadConfig_WhenPrimaryAndBackupAreBothCorrupt_ThrowsAndRetainsFiles()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "Original", null);
            config.display_name = "Committed";
            _store.SaveConfig(config);

            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);
            File.WriteAllText(path, "{\"display_name\":\"Torn\",\"world");
            File.WriteAllText(backup, "not json at all");

            IOException failure = Assert.Throws<IOException>(() => _store.LoadConfig(config.config_id));

            StringAssert.Contains(path, failure.Message);
            StringAssert.Contains(backup, failure.Message);
            StringAssert.Contains("retained", failure.Message);
            Assert.AreEqual("{\"display_name\":\"Torn\",\"world", File.ReadAllText(path),
                "A failed load must not delete or rewrite the files it could not parse.");
            Assert.AreEqual("not json at all", File.ReadAllText(backup));
        }

        [Test]
        public async Task ScanAndMigrateAsync_RecoversConfigFromBackupWhenPrimaryIsCorrupt()
        {
            WorldConfig config = _store.CreateConfig(
                new WorldSourceData { type = "local_splat" }, "Original", null);
            config.display_name = "Committed";
            _store.SaveConfig(config);
            File.WriteAllText(ConfigPath(config), "{\"display_name\":\"Committed\",\"wor");

            WorldConfigStore reloaded = CreateStore();
            await reloaded.ScanAndMigrateAsync();

            Assert.AreEqual(1, reloaded.ListConfigs().Count,
                "A world whose primary is corrupt but whose backup is valid must still appear in My Worlds.");
            Assert.AreEqual(config.config_id, reloaded.ListConfigs()[0].config_id);
            Assert.AreEqual("Original", reloaded.ListConfigs()[0].display_name);
        }

        [Test]
        public void SaveConfig_WhenPrimaryCommitFails_KeepsLastValidSave()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            config.display_name = "Second";
            _store.SaveConfig(config);

            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);

            IOException failure = Assert.Throws<IOException>(() => WorldConfigFile.Write(
                path,
                new WorldConfig
                {
                    config_id = config.config_id,
                    display_name = "Third",
                    world_source = new WorldSourceData { type = "local_splat" }
                },
                (stage, _) =>
                {
                    if (stage == "before-primary-commit") throw new InvalidOperationException("simulated interruption");
                }));

            StringAssert.Contains("commit primary", failure.Message, "The failure must name the stage that failed.");
            StringAssert.Contains("not deleted", failure.Message, "The failure must state that committed data survived.");
            Assert.AreEqual("Second", ReadDisplayName(path), "An interrupted commit must not replace the primary.");
            Assert.IsNotNull(ReadConfig(backup), "A recovery backup must remain readable.");
            AssertNoTemporaries(dir, "A failed save must remove its temporary files.");
        }

        [Test]
        public void SaveConfig_WhenBackupWriteIsInterrupted_KeepsPrimaryAndBackupIntact()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            config.display_name = "Second";
            _store.SaveConfig(config);

            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);
            string backupBefore = File.ReadAllText(backup);

            IOException failure = Assert.Throws<IOException>(() => WorldConfigFile.Write(
                path,
                new WorldConfig
                {
                    config_id = config.config_id,
                    display_name = "Third",
                    world_source = new WorldSourceData { type = "local_splat" }
                },
                (stage, _) =>
                {
                    if (stage == "backup-flushed") throw new InvalidOperationException("simulated interruption");
                }));

            StringAssert.Contains("preserve valid primary", failure.Message);
            Assert.AreEqual("Second", ReadDisplayName(path), "The committed primary must be untouched.");
            Assert.AreEqual(backupBefore, File.ReadAllText(backup), "An interrupted backup step must not rewrite world.json.bak.");
            AssertNoTemporaries(dir, "A failed save must remove its temporary files.");
        }

        [Test]
        public void SaveConfig_WhenTemporaryWriteIsInterrupted_KeepsCommittedFilesIntact()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            config.display_name = "Second";
            _store.SaveConfig(config);

            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);
            string primaryBefore = File.ReadAllText(path);
            string backupBefore = File.ReadAllText(backup);

            IOException failure = Assert.Throws<IOException>(() => WorldConfigFile.Write(
                path,
                new WorldConfig
                {
                    config_id = config.config_id,
                    display_name = "Third",
                    world_source = new WorldSourceData { type = "local_splat" }
                },
                (stage, _) =>
                {
                    if (stage == "temp-flushed") throw new InvalidOperationException("simulated interruption");
                }));

            StringAssert.Contains("write temporary config", failure.Message);
            Assert.AreEqual(primaryBefore, File.ReadAllText(path));
            Assert.AreEqual(backupBefore, File.ReadAllText(backup));
            AssertNoTemporaries(dir, "An interrupted temporary write must be cleaned up.");
        }

        [Test]
        public void SaveConfig_ClearsTemporariesAbandonedByAPreviousProcess()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            File.WriteAllText(Path.Combine(dir, "world.json.0123456789abcdef.tmp"), "{\"partial\":");
            File.WriteAllText(Path.Combine(dir, "world.json.bak.0123456789abcdef.tmp"), "{\"partial\":");

            config.display_name = "Second";
            _store.SaveConfig(config);

            Assert.AreEqual("Second", ReadDisplayName(path));
            Assert.IsFalse(File.Exists(Path.Combine(dir, "world.json.0123456789abcdef.tmp")),
                "Temporaries abandoned by a killed process must be cleared by the next successful save.");
            Assert.IsFalse(File.Exists(Path.Combine(dir, "world.json.bak.0123456789abcdef.tmp")));
            AssertNoTemporaries(dir, "Only the abandoned temporaries that were cleaned up may exist.");
        }

        [Test]
        public async Task SaveConfig_ConcurrentWriters_CommitWholeDocuments()
        {
            WorldConfig seed = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "Concurrent", null);
            string dir = ConfigDir(seed);
            string path = ConfigPath(seed);

            // A second store over the same root mimics two components (or two app instances) saving
            // the same world at once.
            WorldConfigStore second = CreateStore();
            await second.ScanAndMigrateAsync();

            const int Writers = 4;
            const int Rounds = 3;
            const int Objects = 500;
            var errors = new List<Exception>();
            var tasks = new List<Task>();

            for (int writer = 0; writer < Writers; writer++)
            {
                int writerId = writer;
                tasks.Add(Task.Run(() =>
                {
                    WorldConfigStore store = writerId % 2 == 0 ? _store : second;
                    WorldConfig mine = CloneConfig(seed);
                    for (int i = 0; i < Objects; i++)
                        mine.objects.Add(new SavedObject { instance_id = $"w{writerId}_{i}", display_name = "Object " + i });

                    for (int round = 0; round < Rounds; round++)
                    {
                        mine.display_name = $"Writer{writerId}_{round}";
                        try { store.SaveConfig(mine); }
                        catch (Exception ex) { lock (errors) errors.Add(ex); }
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());

            Assert.IsEmpty(errors, $"Concurrent writes must serialize instead of failing or interleaving: {string.Join("; ", errors.ConvertAll(e => e.Message))}");

            WorldConfig committed = ReadConfig(path);
            Assert.IsNotNull(committed, "The final primary must be a complete document.");
            Assert.IsTrue(committed.display_name != null && committed.display_name.StartsWith("Writer"),
                $"The final primary must be one writer's complete document, not a mixture (found '{committed.display_name}').");
            Assert.AreEqual(Objects, committed.objects.Count, "No writer's payload may be truncated or interleaved.");
            Assert.AreEqual(committed.display_name, _store.LoadConfig(seed.config_id).display_name);
            Assert.IsNotNull(ReadConfig(WorldConfigFile.BackupPath(path)), "A recovery backup must be readable after concurrent saves.");
            AssertNoTemporaries(dir, "Concurrent saves must each clean up their temporary files.");
        }

        [Test]
        public void SaveConfig_WhenAnotherWriterHoldsTheLock_ReportsTimeoutAndKeepsCommittedData()
        {
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "First", null);
            config.display_name = "Second";
            _store.SaveConfig(config);

            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string primaryBefore = File.ReadAllText(path);

            // Another process (a second app instance, or the editor and a build) owns the writer
            // lock. The save must fail loudly instead of interleaving or truncating the file.
            using (new FileStream(WorldConfigFile.WriterLockPath(dir), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                IOException failure = Assert.Throws<IOException>(() => WorldConfigFile.Write(
                    path,
                    new WorldConfig
                    {
                        config_id = config.config_id,
                        display_name = "Third",
                        world_source = new WorldSourceData { type = "local_splat" }
                    }));

                StringAssert.Contains("waiting for another writer", failure.Message);
                StringAssert.Contains("No committed data", failure.Message);
            }

            Assert.AreEqual(primaryBefore, File.ReadAllText(path), "A blocked write must not touch the committed primary.");
            Assert.AreEqual("Second", ReadDisplayName(path));
            AssertNoTemporaries(dir, "A blocked write must not leave temporary files behind.");
        }

        [Test]
        public void SaveConfig_WhenFolderIsNotWritable_MigratesToWritableCopyAndKeepsOriginalFiles()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            WorldConfig config = _store.CreateConfig(new WorldSourceData { type = "local_splat" }, "ReadOnly", null);
            config.display_name = "Committed";
            _store.SaveConfig(config);

            string dir = ConfigDir(config);
            string path = ConfigPath(config);
            string backup = WorldConfigFile.BackupPath(path);
            string originalId = config.config_id;
            string primaryBefore = File.ReadAllText(path);
            string backupBefore = File.ReadAllText(backup);

            bool injected;
            try
            {
                File.SetAttributes(dir, FileAttributes.ReadOnly);
                injected = !CanCreateFileIn(dir);
            }
            finally
            {
                File.SetAttributes(dir, FileAttributes.Normal);
            }

            if (!injected)
            {
                Assert.Ignore("This file system does not deny directory writes through the read-only attribute.");
                return;
            }

            try
            {
                File.SetAttributes(dir, FileAttributes.ReadOnly);

                // The real Android case: a folder owned by a previous install cannot be written, so
                // the store repairs itself by creating a writable copy instead of losing the world.
                config.display_name = "Repaired";
                _store.SaveConfig(config);
            }
            finally
            {
                File.SetAttributes(dir, FileAttributes.Normal);
            }

            Assert.AreEqual(primaryBefore, File.ReadAllText(path), "The unwritable folder's committed save must be untouched.");
            Assert.AreEqual(backupBefore, File.ReadAllText(backup));
            Assert.AreNotEqual(originalId, config.config_id, "An unwritable folder must move the config to a writable copy.");
            Assert.AreEqual(originalId, config.migrated_from_config_id);
            Assert.AreEqual("Repaired", ReadDisplayName(ConfigPath(config)),
                "The writable copy must contain the save that could not be written in place.");
            Assert.AreEqual("Repaired", _store.LoadConfig(config.config_id).display_name);
#else
            Assert.Ignore("Read-only directory denial is only exercised on POSIX file systems.");
#endif
        }

        [Test]
        public async Task SaveConfig_LegacyConfigWithoutBackupLoadsScansAndSavesInPlace()
        {
            const string id = "Legacy_2026-04-15T103000Z";
            string dir = Path.Combine(_tempRoot, id);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "world.json");
            string legacyJson =
                "{\n" +
                "  \"schema_version\": 1,\n" +
                "  \"config_id\": \"" + id + "\",\n" +
                "  \"display_name\": \"Legacy Cabin\",\n" +
                "  \"created_at\": \"2026-04-15T10:30:00Z\",\n" +
                "  \"modified_at\": \"2026-04-15T10:30:00Z\",\n" +
                "  \"world_source\": { \"type\": \"local_splat\", \"cached_splat\": \"../CachedWorlds/legacy.spz\" },\n" +
                "  \"prompts\": [],\n" +
                "  \"spawn_points\": [],\n" +
                "  \"objects\": [ { \"instance_id\": \"legacy_1\", \"display_name\": \"Chair\" } ]\n" +
                "}";
            File.WriteAllText(path, legacyJson);

            WorldConfig loaded = _store.LoadConfig(id);
            Assert.IsNotNull(loaded, "A save written before this work must still load.");
            Assert.AreEqual(1, loaded.schema_version);
            Assert.AreEqual("Legacy Cabin", loaded.display_name);

            await _store.ScanAndMigrateAsync();
            Assert.AreEqual(1, _store.ListConfigs().Count, "Startup scan must pick up the legacy folder once.");
            Assert.AreEqual(id, _store.ListConfigs()[0].config_id, "A legacy folder id must not be rewritten.");
            Assert.AreEqual(1, _store.ListConfigs()[0].objects.Count);

            WorldConfig tracked = _store.ListConfigs()[0];
            tracked.display_name = "Legacy Cabin Renamed";
            _store.SaveConfig(tracked);

            WorldConfig reloaded = _store.LoadConfig(id);
            Assert.AreEqual("Legacy Cabin Renamed", reloaded.display_name);
            Assert.AreEqual(1, reloaded.schema_version, "The save schema version must not change.");
            Assert.AreEqual(legacyJson, File.ReadAllText(WorldConfigFile.BackupPath(path)),
                "The pre-existing file becomes the recovery backup byte for byte.");
            AssertNoTemporaries(dir, "Migrating a legacy save must clean up temporary files.");
        }

        [Test]
        public void SaveConfig_FileProtocolPreservesEverySchemaField()
        {
            var config = new WorldConfig
            {
                config_id = "Schema_2026-04-15T103000Z",
                display_name = "Schema",
                created_at = "2026-04-15T10:30:00Z",
                modified_at = "2026-04-15T11:00:00Z",
                generation_model = "Standard",
                migrated_from_config_id = "SchemaLegacy",
                world_source = new WorldSourceData
                {
                    type = "worldlabs",
                    world_id = "abc123",
                    display_name = "Sunny Beach",
                    cached_splat = "../CachedWorlds/abc123.spz",
                    cached_pano = "../CachedWorlds/abc123.jpg",
                    cached_thumbnail = "../CachedWorlds/abc123.png"
                },
                world_transform = new WorldTransformData
                {
                    position = new UnityEngine.Vector3(1f, 2f, 3f),
                    rotation = UnityEngine.Quaternion.Euler(0f, 45f, 0f),
                    scale = new UnityEngine.Vector3(2f, 2f, 2f)
                },
                lighting = new LightingData { preset = "Golden Hour", sun_azimuth = 220f, sun_elevation = 35f }
            };
            config.prompts.Add(new PromptEntry
            {
                timestamp = "2026-04-15T10:30:00Z",
                type = "world_creation",
                intent = "GenerateWorld",
                transcript = "a sunny beach"
            });
            config.spawn_points.Add(new SpawnPointData
            {
                id = "spawn_1",
                name = "Entry",
                source = "manual",
                method = "manual",
                created_at = "2026-04-15T10:35:00Z",
                position = new UnityEngine.Vector3(1f, 1.6f, 2f),
                rotation = UnityEngine.Quaternion.Euler(0f, 90f, 0f),
                look_at = new UnityEngine.Vector3(1f, 1.4f, 3f),
                confidence = 1f
            });
            config.objects.Add(new SavedObject
            {
                instance_id = "chair_abc123",
                prefab_name = "beach chair",
                display_name = "Beach Chair",
                components = new List<SavedComponent>
                {
                    new SavedComponent
                    {
                        type = "Transform",
                        data = Newtonsoft.Json.Linq.JObject.Parse("{\"position\":{\"x\":1.2,\"y\":0,\"z\":2.5}}")
                    }
                }
            });

            string dir = Path.Combine(_tempRoot, config.config_id);
            Directory.CreateDirectory(dir);
            WorldConfigFile.Write(Path.Combine(dir, "world.json"), config);

            WorldConfig restored = _store.LoadConfig(config.config_id);

            Assert.AreEqual(1, restored.schema_version);
            Assert.AreEqual(config.config_id, restored.config_id);
            Assert.AreEqual("Schema", restored.display_name);
            Assert.AreEqual("Standard", restored.generation_model);
            Assert.AreEqual("SchemaLegacy", restored.migrated_from_config_id);
            Assert.AreEqual("abc123", restored.world_source.world_id);
            Assert.AreEqual("../CachedWorlds/abc123.spz", restored.world_source.cached_splat);
            Assert.AreEqual("../CachedWorlds/abc123.jpg", restored.world_source.cached_pano);
            Assert.AreEqual("../CachedWorlds/abc123.png", restored.world_source.cached_thumbnail);
            Assert.AreEqual(2f, restored.world_transform.position.y, 0.001f);
            Assert.AreEqual(220f, restored.lighting.sun_azimuth, 0.001f);
            Assert.AreEqual(1, restored.prompts.Count);
            Assert.AreEqual(1, restored.spawn_points.Count);
            Assert.AreEqual("Entry", restored.spawn_points[0].name);
            Assert.AreEqual(1, restored.objects.Count);
            Assert.AreEqual("chair_abc123", restored.objects[0].instance_id);
            Assert.AreEqual(1.2f, (float)restored.objects[0].components[0].data["position"]["x"], 0.001f);
        }

        // ── Fixture helpers ───────────────────────────────────────────────────────────────────────

        string ConfigDir(WorldConfig config) => Path.Combine(_tempRoot, config.config_id);

        string ConfigPath(WorldConfig config) => Path.Combine(ConfigDir(config), WorldConfigFile.PrimaryFileName);

        static WorldConfig CloneConfig(WorldConfig source) =>
            JsonConvert.DeserializeObject<WorldConfig>(JsonConvert.SerializeObject(source));

        static WorldConfig ReadConfig(string path)
        {
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<WorldConfig>(File.ReadAllText(path));
        }

        static string ReadDisplayName(string path) => ReadConfig(path)?.display_name;

        static void AssertNoTemporaries(string directory, string message) =>
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"), message);

        static bool CanCreateFileIn(string directory)
        {
            string probe = Path.Combine(directory, "write-probe.tmp");
            try
            {
                File.WriteAllText(probe, "probe");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); }
                catch (Exception) { /* reporting the injection result matters more than the probe file */ }
            }
        }
    }
}

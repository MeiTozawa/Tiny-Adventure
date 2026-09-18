using System.Collections.Generic;
using NUnit.Framework;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// ゲーム設定サービス GameSettingsService の FOV 設定・範囲クランプ・イベント発火・保存永続化テスト。
    /// </summary>
    public sealed class GameSettingsServiceTests
    {
        private sealed class MockSettingsStorage : ISettingsStorage
        {
            public readonly Dictionary<string, float> FloatValues = new();
            public int SaveCallCount;

            public float GetFloat(string key, float defaultValue)
            {
                return FloatValues.TryGetValue(key, out float val) ? val : defaultValue;
            }

            public void SetFloat(string key, float value)
            {
                FloatValues[key] = value;
            }

            public void Save()
            {
                SaveCallCount++;
            }
        }

        private MockSettingsStorage storage;
        private GameSettingsService service;

        [SetUp]
        public void SetUp()
        {
            storage = new MockSettingsStorage();
            service = new GameSettingsService(storage);
        }

        [Test]
        public void DefaultFov_Is85Degrees()
        {
            Assert.That(service.CurrentFov, Is.EqualTo(85f).Within(0.001f),
                "変更前はデフォルトの一人称 FOV が 85 度である必要があります。");
        }

        [TestCase(59f, 60f)]
        [TestCase(20f, 60f)]
        [TestCase(111f, 110f)]
        [TestCase(160f, 110f)]
        [TestCase(75f, 75f)]
        [TestCase(90f, 90f)]
        public void SetFov_ClampsWithinRange60To110(float inputFov, float expectedClampedFov)
        {
            service.SetFov(inputFov);
            Assert.That(service.CurrentFov, Is.EqualTo(expectedClampedFov).Within(0.001f),
                $"入力 FOV {inputFov} は [60, 110] の範囲にクランプされて {expectedClampedFov} になる必要があります。");
        }

        [Test]
        public void SetFov_FiresFovChangedEvent()
        {
            float receivedFov = -1f;
            int eventCount = 0;
            service.FovChanged += fov =>
            {
                receivedFov = fov;
                eventCount++;
            };

            service.SetFov(95f);

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(receivedFov, Is.EqualTo(95f).Within(0.001f));

            // 同一値を設定した場合は重複してイベントを発火しない
            service.SetFov(95f);
            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetToDefault_Restores85Degrees()
        {
            service.SetFov(105f);
            Assert.That(service.CurrentFov, Is.EqualTo(105f).Within(0.001f));

            service.ResetToDefault();
            Assert.That(service.CurrentFov, Is.EqualTo(85f).Within(0.001f));
        }

        [Test]
        public void StoragePersistence_SavesAndLoadsCorrectly()
        {
            service.SetFov(100f);

            Assert.That(storage.FloatValues[GameSettingsService.FovStorageKey], Is.EqualTo(100f).Within(0.001f));
            Assert.That(storage.SaveCallCount, Is.GreaterThanOrEqualTo(1));

            // 既存ストレージから新サービスインスタンスをロード
            var newService = new GameSettingsService(storage);
            Assert.That(newService.CurrentFov, Is.EqualTo(100f).Within(0.001f));
        }
    }
}

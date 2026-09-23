using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatFeedbackProfileTests
    {
        private CombatFeedbackProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DefaultValuesConformToSpecifications()
        {
            Assert.That(profile.HitStop.normalSeconds, Is.EqualTo(0.04f).Within(0.001f), "通常ヒットのヒットストップのデフォルトは0.04秒である必要があります。");
            Assert.That(profile.HitStop.lethalSeconds, Is.EqualTo(0.08f).Within(0.001f), "致命ヒットのヒットストップのデフォルトは0.08秒である必要があります。");
            Assert.That(profile.HitStop.maximumSeconds, Is.EqualTo(0.12f).Within(0.001f), "ヒットストップの最大上限のデフォルトは0.12秒である必要があります。");
            Assert.That(profile.HitStop.normalSeconds, Is.LessThanOrEqualTo(profile.HitStop.lethalSeconds), "通常ヒットのヒットストップが致命ヒットストップを超えてはなりません。");
            Assert.That(profile.HitStop.lethalSeconds, Is.LessThanOrEqualTo(profile.HitStop.maximumSeconds), "致命ヒットのヒットストップが最大上限を超えてはなりません。");

            Assert.That(profile.Camera.enterSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 変化時間は 0.001 秒以上である必要があります。");
            Assert.That(profile.Camera.recoverSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 復帰時間は 0.001 秒以上である必要があります。");
            Assert.That(profile.NormalHit.volume, Is.InRange(0f, 1f), "通常ヒット音量は [0, 1] の範囲内である必要があります。");
            Assert.That(profile.LethalHit.volume, Is.InRange(0f, 1f), "致命ヒット音量は [0, 1] の範囲内である必要があります。");
        }

        [Test]
        public void ClampingRestrictsExtremeValues()
        {
            var hitStop = new HitStopSettings
            {
                normalSeconds = -0.5f,
                lethalSeconds = 10f,
                maximumSeconds = 1.0f
            };

            var camera = new CameraFeedbackSettings
            {
                enterSeconds = -0.1f,
                recoverSeconds = -0.2f
            };

            var normalHit = new HitFeedbackVariant
            {
                lifetimeSeconds = -1f,
                spawnScale = Vector3.zero,
                volume = 2.5f,
                pitchRange = new Vector2(-0.5f, 5f)
            };

            var lethalHit = new HitFeedbackVariant
            {
                lifetimeSeconds = 0f,
                spawnScale = new Vector3(-1f, -1f, -1f),
                volume = -0.5f,
                pitchRange = new Vector2(2f, 1f)
            };

            profile.SetConfig(
                normalHit,
                lethalHit,
                null,
                null,
                hitStop,
                camera,
                AttackFeedbackSettings.Default);

            Assert.That(profile.HitStop.maximumSeconds, Is.LessThanOrEqualTo(0.5f), "最大ヒットストップ上限は0.5秒を超えてはなりません。");
            Assert.That(profile.HitStop.normalSeconds, Is.GreaterThanOrEqualTo(0.01f), "通常ヒットストップ時間は0.01秒以上である必要があります。");
            Assert.That(profile.HitStop.lethalSeconds, Is.LessThanOrEqualTo(profile.HitStop.maximumSeconds), "致命ヒットストップ時間は最大上限を超えてはなりません。");

            Assert.That(profile.Camera.enterSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 変化時間が最小値にクランプされる必要があります。");
            Assert.That(profile.Camera.recoverSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 復帰時間が最小値にクランプされる必要があります。");

            Assert.That(profile.NormalHit.lifetimeSeconds, Is.GreaterThanOrEqualTo(0.05f), "エフェクトの持続時間は0.05秒以上である必要があります。");
            Assert.That(profile.NormalHit.spawnScale.x, Is.GreaterThan(0f), "スケールが無効な場合は正の値にフォールバックする必要があります。");
            Assert.That(profile.NormalHit.volume, Is.EqualTo(1.0f), "範囲外の音量は 1.0 にクランプされる必要があります。");
            Assert.That(profile.LethalHit.volume, Is.EqualTo(0f), "負の音量は 0 にクランプされる必要があります。");
        }

        [Test]
        public void ValidateConfigurationReportsErrorsForMissingAssets()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*normalHit\\.impactPrefab.*"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*lethalHit\\.impactPrefab.*"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*normalHit\\.hitClip.*"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*lethalHit\\.hitClip.*"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*enemyDeathClip.*"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*playerDeathClip.*"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*swordWhooshClip.*"));

            Result result = profile.ValidateConfiguration();

            Assert.That(result.IsErr, Is.True, "未設定の Profile 構成の検証は失敗する必要があります。");
            Assert.That(result.Error, Is.EqualTo(GameError.InvalidParameter));
        }

        [Test]
        public void ValidateConfiguration_WhenAllAssetsConfigured_ReturnsOk()
        {
            var dummyClip = AudioClip.Create("dummy", 10, 1, 1000, false);
            var dummyPrefab = new GameObject("DummyImpact");

            var normalHit = HitFeedbackVariant.DefaultNormal;
            normalHit.impactPrefab = dummyPrefab;
            normalHit.hitClip = dummyClip;

            var lethalHit = HitFeedbackVariant.DefaultLethal;
            lethalHit.impactPrefab = dummyPrefab;
            lethalHit.hitClip = dummyClip;

            profile.SetConfig(
                normalHit,
                lethalHit,
                dummyClip,
                dummyClip,
                dummyClip,
                dummyClip,
                dummyClip);

            Result result = profile.ValidateConfiguration();

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Error, Is.EqualTo(GameError.None));

            Object.DestroyImmediate(dummyPrefab);
            Object.DestroyImmediate(dummyClip);
        }
    }
}

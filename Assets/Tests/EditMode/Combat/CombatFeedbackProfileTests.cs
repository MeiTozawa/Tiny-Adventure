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
            Assert.That(profile.HitStop.normalSeconds, Is.EqualTo(0.04f).Within(0.001f), "普通命中停顿默认应为0.04秒。");
            Assert.That(profile.HitStop.lethalSeconds, Is.EqualTo(0.08f).Within(0.001f), "致死命中停顿默认应为0.08秒。");
            Assert.That(profile.HitStop.maximumSeconds, Is.EqualTo(0.12f).Within(0.001f), "最大停顿上限默认应为0.12秒。");
            Assert.That(profile.HitStop.normalSeconds, Is.LessThanOrEqualTo(profile.HitStop.lethalSeconds), "普通命中停顿不应大于致死停顿。");
            Assert.That(profile.HitStop.lethalSeconds, Is.LessThanOrEqualTo(profile.HitStop.maximumSeconds), "致死停顿不应超过最大停顿上限。");

            Assert.That(profile.Camera.enterSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 进入时间必须大于等于 0.001 秒。");
            Assert.That(profile.Camera.recoverSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 恢复时间必须大于等于 0.001 秒。");
            Assert.That(profile.NormalHit.volume, Is.InRange(0f, 1f), "普通命中音量必须在 [0, 1] 范围。");
            Assert.That(profile.LethalHit.volume, Is.InRange(0f, 1f), "致死命中音量必须在 [0, 1] 范围。");
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

            profile.ConfigureForTests(
                normalHit,
                lethalHit,
                null,
                null,
                hitStop,
                camera,
                AttackFeedbackSettings.Default);

            Assert.That(profile.HitStop.maximumSeconds, Is.LessThanOrEqualTo(0.5f), "最大停顿上限不应超过0.5秒。");
            Assert.That(profile.HitStop.normalSeconds, Is.GreaterThanOrEqualTo(0.01f), "普通停顿时间不应小于0.01秒。");
            Assert.That(profile.HitStop.lethalSeconds, Is.LessThanOrEqualTo(profile.HitStop.maximumSeconds), "致死停顿时间不应超过最大上限。");

            Assert.That(profile.Camera.enterSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 进入时间被 Clamp 到最小值。");
            Assert.That(profile.Camera.recoverSeconds, Is.GreaterThanOrEqualTo(0.001f), "FOV 恢复时间被 Clamp 到最小值。");

            Assert.That(profile.NormalHit.lifetimeSeconds, Is.GreaterThanOrEqualTo(0.05f), "特效生命周期不应小于0.05秒。");
            Assert.That(profile.NormalHit.spawnScale.x, Is.GreaterThan(0f), "缩放非法时应回退为正值。");
            Assert.That(profile.NormalHit.volume, Is.EqualTo(1.0f), "超标音量应 Clamp 为 1.0。");
            Assert.That(profile.LethalHit.volume, Is.EqualTo(0f), "负音量应 Clamp 为 0。");
        }

        [Test]
        public void ValidateConfigurationReportsDiagnosticsForMissingAssets()
        {
            bool isValid = profile.ValidateConfiguration(out List<string> diagnostics);

            Assert.That(isValid, Is.False, "空 Profile 配置验证应当失败。");
            Assert.That(diagnostics, Is.Not.Empty, "应当输出中文诊断列表。");

            string fullReport = string.Join("\n", diagnostics);
            Assert.That(fullReport, Does.Contain("normalHit.impactPrefab"), "缺少普通命中特效应报告字段。");
            Assert.That(fullReport, Does.Contain("lethalHit.impactPrefab"), "缺少致死命中特效应报告字段。");
            Assert.That(fullReport, Does.Contain("SFX_Hit_Normal.mp3"), "缺少普通命中音效应报告建议文件名。");
            Assert.That(fullReport, Does.Contain("SFX_Hit_Lethal.mp3"), "缺少致死命中音效应报告建议文件名。");
            Assert.That(fullReport, Does.Contain("SFX_Enemy_Die.mp3"), "缺少敌人死亡音效应报告建议文件名。");
            Assert.That(fullReport, Does.Contain("SFX_Player_Die.mp3"), "缺少玩家死亡音效应报告建议文件名。");
            Assert.That(fullReport, Does.Contain("SFX_Sword_Whoosh..mp3"), "缺少挥刀音效应报告准确的双句点文件名。");
        }
    }
}

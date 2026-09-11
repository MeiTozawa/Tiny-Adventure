using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatVfxControllerTests
    {
        private GameObject vfxControllerGo;
        private CombatVfxController vfxController;
        private RecordingVfxSpawner spawner;
        private CombatFeedbackProfile profile;

        private GameObject normalPrefab;
        private GameObject lethalPrefab;

        private GameObject sourceGo;
        private GameObject targetGo;
        private CombatantMarker source;
        private CombatantMarker target;

        [SetUp]
        public void SetUp()
        {
            vfxControllerGo = new GameObject("CombatVfxController");
            vfxController = vfxControllerGo.AddComponent<CombatVfxController>();
            spawner = new RecordingVfxSpawner();

            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();

            normalPrefab = new GameObject("MockNormalImpactPrefab");
            lethalPrefab = new GameObject("MockLethalImpactPrefab");

            var normalVariant = profile.NormalHit;
            normalVariant.impactPrefab = normalPrefab;
            normalVariant.positionOffset = new Vector3(0f, 0.5f, 0f);
            normalVariant.rotationOffset = Vector3.zero;
            normalVariant.spawnScale = new Vector3(1f, 1f, 1f);
            normalVariant.lifetimeSeconds = 0.3f;

            var lethalVariant = profile.LethalHit;
            lethalVariant.impactPrefab = lethalPrefab;
            lethalVariant.positionOffset = new Vector3(0f, 0.8f, 0f);
            lethalVariant.rotationOffset = new Vector3(0f, 45f, 0f);
            lethalVariant.spawnScale = new Vector3(2f, 2f, 2f);
            lethalVariant.lifetimeSeconds = 0.6f;

            profile.ConfigureForTests(normalVariant, lethalVariant);
            vfxController.ConfigureForTests(spawner, profile);

            sourceGo = new GameObject("Source");
            source = sourceGo.AddComponent<CombatantMarker>();
            source.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");

            targetGo = new GameObject("Target");
            target = targetGo.AddComponent<CombatantMarker>();
            target.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            targetGo.transform.position = new Vector3(0f, 0f, 2f);
        }

        [TearDown]
        public void TearDown()
        {
            if (vfxControllerGo != null) Object.DestroyImmediate(vfxControllerGo);
            if (profile != null) Object.DestroyImmediate(profile);
            if (normalPrefab != null) Object.DestroyImmediate(normalPrefab);
            if (lethalPrefab != null) Object.DestroyImmediate(lethalPrefab);
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
            if (targetGo != null) Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void NormalHit_SpawnsNormalVfxPrefab_WithCorrectTransform()
        {
            Vector3 hitPoint = new Vector3(0f, 1f, 2f);
            Vector3 direction = Vector3.forward;
            var key = new FeedbackDeduplicationKey(source, target, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                source,
                target,
                default,
                hitPoint,
                direction,
                true,
                false,
                key);

            vfxController.Play(request);

            Assert.That(spawner.SpawnRecords.Count, Is.EqualTo(1), "应该生成 1 次 VFX。");
            var record = spawner.SpawnRecords[0];
            Assert.That(record.Prefab, Is.SameAs(normalPrefab), "普通命中应使用 normal impact prefab。");
            Assert.That(record.Position, Is.EqualTo(hitPoint + profile.NormalHit.positionOffset), "生成位置应叠加 positionOffset。");
            Assert.That(record.Scale, Is.EqualTo(profile.NormalHit.spawnScale), "生成缩放应符合配置。");

            Assert.That(spawner.ScheduledDestroys.Count, Is.EqualTo(1));
            Assert.That(spawner.ScheduledDestroys[0].Lifetime, Is.EqualTo(profile.NormalHit.lifetimeSeconds));
        }

        [Test]
        public void LethalHit_SpawnsLethalVfxPrefab_WithCorrectTransform()
        {
            Vector3 hitPoint = new Vector3(0f, 1f, 2f);
            Vector3 direction = Vector3.forward;
            var key = new FeedbackDeduplicationKey(source, target, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Lethal,
                source,
                target,
                default,
                hitPoint,
                direction,
                true,
                false,
                key);

            vfxController.Play(request);

            Assert.That(spawner.SpawnRecords.Count, Is.EqualTo(1));
            var record = spawner.SpawnRecords[0];
            Assert.That(record.Prefab, Is.SameAs(lethalPrefab), "致死命中应使用 lethal impact prefab。");
            Assert.That(record.Position, Is.EqualTo(hitPoint + profile.LethalHit.positionOffset));
            Assert.That(record.Scale, Is.EqualTo(profile.LethalHit.spawnScale));
        }

        [Test]
        public void MissingPrefab_ReportsDiagnostic_WithoutThrowing()
        {
            var emptyProfile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            vfxController.ConfigureForTests(spawner, emptyProfile);

            var key = new FeedbackDeduplicationKey(source, target, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                source,
                target,
                default,
                Vector3.zero,
                Vector3.forward,
                true,
                false,
                key);

            string reported = string.Empty;
            vfxController.DiagnosticReported += diag => reported = diag;

            Assert.DoesNotThrow(() => vfxController.Play(request));
            Assert.That(spawner.SpawnRecords.Count, Is.EqualTo(0));
            StringAssert.Contains("缺少", reported, "缺失 Prefab 时应报告中文诊断。");

            Object.DestroyImmediate(emptyProfile);
        }

        [Test]
        public void SpawnerException_IsHandledGracefully()
        {
            spawner.ThrowOnSpawn = true;
            string reported = string.Empty;
            vfxController.DiagnosticReported += diag => reported = diag;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("生成命中特效异常"));

            var key = new FeedbackDeduplicationKey(source, target, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                source,
                target,
                default,
                Vector3.zero,
                Vector3.forward,
                true,
                false,
                key);

            Assert.DoesNotThrow(() => vfxController.Play(request));
            StringAssert.Contains("异常", reported);
        }

        [Test]
        public void ClearRuntimeState_CleansUpActiveInstances()
        {
            var normalVariant = profile.NormalHit;
            var inst = new GameObject("DummyInstance");

            vfxController.ClearRuntimeState();
            Assert.That(vfxController.ActiveInstanceCount, Is.EqualTo(0));

            Object.DestroyImmediate(inst);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatFeedbackExceptionIsolationTests
    {
        private GameObject controllerGo;
        private CombatFeedbackController controller;
        private RecordingDamageFeedbackSource damageSource;
        private StubGameplayStateProvider stateProvider;
        private CombatFeedbackProfile profile;
        private StubCombatantRegistry registry;

        private GameObject playerGo;
        private GameObject enemyGo;
        private CombatantMarker playerMarker;
        private CombatantMarker enemyMarker;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatFeedbackController");
            controller = controllerGo.AddComponent<CombatFeedbackController>();

            damageSource = new RecordingDamageFeedbackSource();
            stateProvider = new StubGameplayStateProvider();
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            registry = new StubCombatantRegistry();

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            enemyGo = new GameObject("Enemy");
            enemyMarker = enemyGo.AddComponent<CombatantMarker>();
            enemyMarker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            enemyGo.transform.position = new Vector3(0, 0, 2);

            registry.Register(playerMarker);
            registry.Register(enemyMarker);
        }

        [TearDown]
        public void TearDown()
        {
            if (controllerGo != null) Object.DestroyImmediate(controllerGo);
            if (playerGo != null) Object.DestroyImmediate(playerGo);
            if (enemyGo != null) Object.DestroyImmediate(enemyGo);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void Property8_SingleModuleException_IsolatesAndAllowsDownstreamModulesToExecute()
        {
            // 5つのモジュールを作成し、2つ目のモジュール（VFX）で例外を発生させる
            var animModule = new RecordingFeedbackModule { Name = "Animation" };
            var faultingVfx = new RecordingFeedbackModule { Name = "Vfx", ThrowOnPlay = true };
            var audioModule = new RecordingFeedbackModule { Name = "Audio" };
            var hitStopModule = new RecordingFeedbackModule { Name = "HitStop" };
            var cameraModule = new RecordingFeedbackModule { Name = "Camera" };

            controller.Construct(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { animModule, faultingVfx, audioModule, hitStopModule, cameraModule });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*RecordingFeedbackModule.*"));

            var dmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 10f, 1);
            Assert.DoesNotThrow(() => damageSource.Raise(enemyMarker, dmg));

            // 1. 前段モジュールは正常に実行される
            Assert.That(animModule.PlayRequests.Count, Is.EqualTo(1));

            // 2. 例外発生モジュール
            Assert.That(faultingVfx.PlayRequests.Count, Is.EqualTo(0));

            // 3. 後続モジュールも正常にリクエストを受信して実行される
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Property8_ClearRuntimeStateException_IsIsolated()
        {
            var normalModule = new RecordingFeedbackModule { Name = "Normal" };
            var throwingModule = new RecordingFeedbackModule { Name = "Throwing" };
            var faultingModule = new FaultingClearModule { ShouldThrow = false };

            controller.Construct(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { normalModule, faultingModule, throwingModule });

            int beforeNormal = normalModule.ClearCount;
            int beforeThrowing = throwingModule.ClearCount;

            faultingModule.ShouldThrow = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*クリーンアップ例外.*"));
            Assert.DoesNotThrow(() => controller.ClearRuntimeState());

            // normalModule と throwingModule の双方がクリーンアップ呼び出しを受信
            Assert.That(normalModule.ClearCount, Is.EqualTo(beforeNormal + 1));
            Assert.That(throwingModule.ClearCount, Is.EqualTo(beforeThrowing + 1));

            faultingModule.ShouldThrow = false;
        }

        private sealed class FaultingClearModule : ICombatFeedbackModule
        {
            public bool ShouldThrow { get; set; }

            public void Play(CombatFeedbackRequest request) { }

            public void ClearRuntimeState()
            {
                if (ShouldThrow)
                {
                    throw new System.InvalidOperationException("模擬クリーンアップ例外");
                }
            }
        }
    }
}

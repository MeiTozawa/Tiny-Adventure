using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatFeedbackPropertiesTests
    {
        private GameObject controllerGo;
        private CombatFeedbackController controller;
        private RecordingDamageFeedbackSource damageSource;
        private StubGameplayStateProvider stateProvider;
        private CombatFeedbackProfile profile;
        private StubCombatantRegistry registry;

        private RecordingAnimationFeedback animModule;
        private RecordingFeedbackModule vfxModule;
        private RecordingFeedbackModule audioModule;
        private RecordingFeedbackModule hitStopModule;
        private RecordingFeedbackModule cameraModule;

        private GameObject playerGo;
        private GameObject enemyGo;
        private CombatantMarker playerMarker;
        private CombatantMarker enemyMarker;
        private HealthComponent enemyHealth;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatFeedbackController");
            controller = controllerGo.AddComponent<CombatFeedbackController>();

            damageSource = new RecordingDamageFeedbackSource();
            stateProvider = new StubGameplayStateProvider();
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            registry = new StubCombatantRegistry();

            animModule = new RecordingAnimationFeedback();
            vfxModule = new RecordingFeedbackModule { Name = "Vfx" };
            audioModule = new RecordingFeedbackModule { Name = "Audio" };
            hitStopModule = new RecordingFeedbackModule { Name = "HitStop" };
            cameraModule = new RecordingFeedbackModule { Name = "Camera" };

            controller.Construct(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { animModule, vfxModule, audioModule, hitStopModule, cameraModule });

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            enemyGo = new GameObject("Enemy");
            enemyMarker = enemyGo.AddComponent<CombatantMarker>();
            enemyMarker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            enemyHealth = enemyGo.AddComponent<HealthComponent>();
            enemyHealth.EnterDemo();
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
        public void Property1_FeedbackClassificationFidelity_NormalVsLethal()
        {
            // 1. 通常攻撃のヒット（対象生存かつHPが0より大きい）
            var normalDmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 10f, 1);
            damageSource.Raise(enemyMarker, normalDmg);

            Assert.That(vfxModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(vfxModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(audioModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(hitStopModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(cameraModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(animModule.NormalHitRequests.Count, Is.EqualTo(1));
            Assert.That(animModule.LethalHitRequests.Count, Is.EqualTo(0));

            // 2. 致命攻撃のヒット（全HPを削る）
            var lethalDmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 1000f, 2);
            enemyHealth.Receive(lethalDmg);
            damageSource.Raise(enemyMarker, lethalDmg);

            Assert.That(vfxModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(vfxModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(audioModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(hitStopModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(cameraModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(animModule.LethalHitRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Property2_SameAttackSequence_DeduplicatesHitFeedback()
        {
            // 同一攻撃シーケンス（sequenceId = 42）を同一対象に対して連続5回トリガー
            var dmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 10f, 42);

            for (int i = 0; i < 5; i++)
            {
                damageSource.Raise(enemyMarker, dmg);
            }

            // 全ての表現サブモジュールは1回のみ呼び出しを受信
            Assert.That(vfxModule.PlayRequests.Count, Is.EqualTo(1), "同一攻撃シーケンスの同一対象に対しては VFX を1回のみ再生する必要があります。");
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(1), "同一攻撃シーケンスの同一対象に対しては Audio を1回のみ再生する必要があります。");
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(1), "同一攻撃シーケンスの同一対象に対しては HitStop を1回のみ適用する必要があります。");
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(1), "同一攻撃シーケンスの同一対象に対しては Camera を1回のみ適用する必要があります。");
            Assert.That(animModule.NormalHitRequests.Count, Is.EqualTo(1), "同一攻撃シーケンスの同一対象に対してはアニメーション被弾を1回のみ再生する必要があります。");
        }

        [Test]
        public void Property9_DeathAudioPlaysAtMostOnce_AndDecouplesFromHitLethalAudio()
        {
            // CombatDeathAudioRouter の独立性と最大1回再生を検証
            var deathRouterGo = new GameObject("DeathRouter");
            var deathRouter = deathRouterGo.AddComponent<CombatDeathAudioRouter>();

            var audioGo = new GameObject("CombatAudio");
            var audioController = audioGo.AddComponent<CombatAudioController>();
            var playbackAdapter = new RecordingAudioPlaybackAdapter();

            var deathClip = AudioClip.Create("SFX_Enemy_Die", 100, 1, 44100, false);
            var lethalHitClip = AudioClip.Create("SFX_Hit_Lethal", 100, 1, 44100, false);

            var lethalVariant = profile.LethalHit;
            lethalVariant.hitClip = lethalHitClip;

            profile.SetConfig(
                profile.NormalHit,
                lethalVariant,
                enemyDeath: deathClip);

            audioController.Construct(playbackAdapter, profile);

            var stubEnemyHealth = new StubHealthDeathSource
            {
                Marker = enemyMarker
            };

            deathRouter.Construct(null, new[] { stubEnemyHealth }, audioController);

            // 1. 致命ヒットをトリガー（SFX_Hit_Lethal は HitFeedback 経由で再生）
            var lethalDmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 100f, 1);
            var lethalRequest = new CombatFeedbackRequest(
                CombatHitType.Lethal,
                playerMarker,
                enemyMarker,
                lethalDmg,
                enemyMarker.transform.position,
                Vector3.forward,
                true,
                false,
                default);

            audioController.Play(lethalRequest);
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(1));
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(lethalHitClip), "致命ヒット時は SFX_Hit_Lethal を再生する必要があります。");

            // 2. 対象の死亡イベント発火（SFX_Enemy_Die は DeathRouter 経由で再生）
            stubEnemyHealth.TriggerDied();
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2));
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(deathClip), "死亡イベントは独立して SFX_Enemy_Die を再生する必要があります。");

            // 3. 死亡イベントの再発火（重複除外、死亡音を再再生しない）
            stubEnemyHealth.TriggerDied();
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2), "重複した死亡イベントで死亡音を再再生してはなりません。");

            Object.DestroyImmediate(deathRouterGo);
            Object.DestroyImmediate(audioGo);
            Object.DestroyImmediate(deathClip);
            Object.DestroyImmediate(lethalHitClip);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatAudioControllerTests
    {
        private GameObject audioGo;
        private CombatAudioController audioController;
        private RecordingAudioPlaybackAdapter playbackAdapter;
        private CombatFeedbackProfile profile;

        private AudioClip normalHitClip;
        private AudioClip lethalHitClip;
        private AudioClip playerHurtClip;
        private AudioClip enemyHurtClip;
        private AudioClip playerDeathClip;
        private AudioClip enemyDeathClip;
        private AudioClip whooshClip;

        private GameObject playerGo;
        private GameObject enemyGo;
        private CombatantMarker playerMarker;
        private CombatantMarker enemyMarker;

        [SetUp]
        public void SetUp()
        {
            audioGo = new GameObject("CombatAudioController");
            audioController = audioGo.AddComponent<CombatAudioController>();
            playbackAdapter = new RecordingAudioPlaybackAdapter();

            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();

            normalHitClip = AudioClip.Create("SFX_Hit_Normal", 100, 1, 44100, false);
            lethalHitClip = AudioClip.Create("SFX_Hit_Lethal", 100, 1, 44100, false);
            playerHurtClip = AudioClip.Create("SFX_Player_Hurt", 100, 1, 44100, false);
            enemyHurtClip = AudioClip.Create("SFX_Enemy_Hurt", 100, 1, 44100, false);
            playerDeathClip = AudioClip.Create("SFX_Player_Die", 100, 1, 44100, false);
            enemyDeathClip = AudioClip.Create("SFX_Enemy_Die", 100, 1, 44100, false);
            whooshClip = AudioClip.Create("SFX_Sword_Whoosh..", 100, 1, 44100, false);

            var normalVariant = profile.NormalHit;
            normalVariant.hitClip = normalHitClip;
            normalVariant.volume = 0.8f;
            normalVariant.pitchRange = new Vector2(0.95f, 1.05f);

            var lethalVariant = profile.LethalHit;
            lethalVariant.hitClip = lethalHitClip;
            lethalVariant.volume = 1f;
            lethalVariant.pitchRange = new Vector2(0.9f, 1.1f);

            profile.ConfigureForTests(
                normalVariant,
                lethalVariant,
                enemyDeathClip,
                playerDeathClip,
                playerHurtClip,
                enemyHurtClip,
                whooshClip);

            audioController.ConfigureForTests(playbackAdapter, profile);

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");

            enemyGo = new GameObject("Enemy");
            enemyMarker = enemyGo.AddComponent<CombatantMarker>();
            enemyMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
        }

        [TearDown]
        public void TearDown()
        {
            if (audioGo != null) Object.DestroyImmediate(audioGo);
            if (profile != null) Object.DestroyImmediate(profile);
            if (playerGo != null) Object.DestroyImmediate(playerGo);
            if (enemyGo != null) Object.DestroyImmediate(enemyGo);

            if (normalHitClip != null) Object.DestroyImmediate(normalHitClip);
            if (lethalHitClip != null) Object.DestroyImmediate(lethalHitClip);
            if (playerHurtClip != null) Object.DestroyImmediate(playerHurtClip);
            if (enemyHurtClip != null) Object.DestroyImmediate(enemyHurtClip);
            if (playerDeathClip != null) Object.DestroyImmediate(playerDeathClip);
            if (enemyDeathClip != null) Object.DestroyImmediate(enemyDeathClip);
            if (whooshClip != null) Object.DestroyImmediate(whooshClip);
        }

        [Test]
        public void NormalHit_PlaysNormalHitClip_AndTargetHurtClip()
        {
            var key = new FeedbackDeduplicationKey(playerMarker, enemyMarker, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                playerMarker,
                enemyMarker,
                default,
                enemyMarker.transform.position,
                Vector3.forward,
                true,
                false,
                key);

            audioController.Play(request);

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2), "ヒットSEと敵被弾SEが再生される必要があります。");
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(normalHitClip), "1つ目は通常ヒットSEである必要があります。");
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(enemyHurtClip), "2つ目は敵被弾SEである必要があります。");
        }

        [Test]
        public void LethalHit_PlaysLethalHitClip_AndTargetHurtClip()
        {
            var key = new FeedbackDeduplicationKey(playerMarker, enemyMarker, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Lethal,
                playerMarker,
                enemyMarker,
                default,
                enemyMarker.transform.position,
                Vector3.forward,
                true,
                false,
                key);

            audioController.Play(request);

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2));
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(lethalHitClip), "致命ヒットは SFX_Hit_Lethal を再生する必要があります。");
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(enemyHurtClip));
        }

        [Test]
        public void PlayerHurt_PlaysPlayerHurtClip()
        {
            var key = new FeedbackDeduplicationKey(enemyMarker, playerMarker, 1);
            var request = new CombatFeedbackRequest(
                CombatHitType.Normal,
                enemyMarker,
                playerMarker,
                default,
                playerMarker.transform.position,
                Vector3.back,
                false,
                true,
                key);

            audioController.Play(request);

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2));
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(playerHurtClip), "プレイヤー被弾時は SFX_Player_Hurt を再生する必要があります。");
        }

        [Test]
        public void PlayDeath_RoutesPlayerAndEnemyDeathClipsCorrectly()
        {
            var enemyDeathRequest = new DeathAudioRequest(
                enemyMarker, false, "death_enemy", enemyMarker.transform.position, "Enemy_Melee");
            audioController.PlayDeath(enemyDeathRequest);

            var playerDeathRequest = new DeathAudioRequest(
                playerMarker, true, "death_player", playerMarker.transform.position, "Knight");
            audioController.PlayDeath(playerDeathRequest);

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2));
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(enemyDeathClip), "敵死亡時は SFX_Enemy_Die を再生する必要があります。");
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(playerDeathClip), "プレイヤー死亡時は SFX_Player_Die を再生する必要があります。");
        }

        [Test]
        public void PlayWhoosh_PlaysSwordWhooshClip()
        {
            var context = new AttackFeedbackContext(playerMarker, 1, playerMarker.transform.position);
            audioController.PlayWhoosh(context);

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(1));
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(whooshClip), "剣撃時は SFX_Sword_Whoosh.. を再生する必要があります。");
        }

        [Test]
        public void PlayWhoosh_RapidConsecutiveCallsFromSameSource_AreDebounced()
        {
            var context = new AttackFeedbackContext(playerMarker, 1, playerMarker.transform.position);
            audioController.PlayWhoosh(context);
            audioController.PlayWhoosh(context);

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(1), "短時間の同一キャラクターによる連続 PlayWhoosh はデバウンスされる必要があります。");
        }

        [Test]
        public void DeathRouter_DeduplicatesDeathPerTarget()
        {
            var routerGo = new GameObject("DeathRouter");
            var router = routerGo.AddComponent<CombatDeathAudioRouter>();

            var stubPlayer = new StubHealthDeathSource { Marker = playerMarker };
            var stubEnemy = new StubHealthDeathSource { Marker = enemyMarker };

            router.ConfigureForTests(stubPlayer, new[] { stubEnemy }, audioController);

            stubEnemy.TriggerDied();
            stubEnemy.TriggerDied(); // 重複死亡イベント

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(1), "同一敵の重複死亡通知は重複排除され、1回のみ再生される必要があります。");
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(enemyDeathClip));

            stubPlayer.TriggerDied();
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2), "プレイヤー死亡は独立して1回再生される必要があります。");
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(playerDeathClip));

            Object.DestroyImmediate(routerGo);
        }

        [Test]
        public void LethalHitAndDeathAudio_AreSeparateRequests()
        {
            var routerGo = new GameObject("DeathRouter");
            var router = routerGo.AddComponent<CombatDeathAudioRouter>();
            var stubEnemy = new StubHealthDeathSource { Marker = enemyMarker };
            router.ConfigureForTests(null, new[] { stubEnemy }, audioController);

            // 1. 致命ヒット
            var key = new FeedbackDeduplicationKey(playerMarker, enemyMarker, 1);
            var hitRequest = new CombatFeedbackRequest(
                CombatHitType.Lethal,
                playerMarker,
                enemyMarker,
                default,
                enemyMarker.transform.position,
                Vector3.forward,
                true,
                false,
                key);

            audioController.Play(hitRequest);

            // 2. 死亡ライフサイクル
            stubEnemy.TriggerDied();

            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(3), "致命ヒット（Hit+Hurt）に続いて死亡ルーティング（Death）が発生し、合計3回再生される必要があります。");
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(lethalHitClip), "1つ目は SFX_Hit_Lethal。");
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(enemyHurtClip), "2つ目は SFX_Enemy_Hurt。");
            Assert.That(playbackAdapter.PlayRecords[2].Clip, Is.SameAs(enemyDeathClip), "3つ目は SFX_Enemy_Die。");

            Object.DestroyImmediate(routerGo);
        }

        [Test]
        public void MissingClips_HandledGracefullyWithDiagnostics()
        {
            var emptyProfile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            audioController.ConfigureForTests(playbackAdapter, emptyProfile);

            var key = new FeedbackDeduplicationKey(playerMarker, enemyMarker, 1);
            var hitRequest = new CombatFeedbackRequest(
                CombatHitType.Normal,
                playerMarker,
                enemyMarker,
                default,
                enemyMarker.transform.position,
                Vector3.forward,
                true,
                false,
                key);

            Assert.DoesNotThrow(() => audioController.Play(hitRequest));
            Assert.DoesNotThrow(() => audioController.PlayDeath(new DeathAudioRequest(enemyMarker, false, "d", Vector3.zero, "k")));
            Assert.DoesNotThrow(() => audioController.PlayWhoosh(new AttackFeedbackContext(playerMarker, 1, Vector3.zero)));

            Object.DestroyImmediate(emptyProfile);
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatAttackFeedbackTests
    {
        private GameObject attackFeedbackGo;
        private CombatAttackFeedback attackFeedback;

        private GameObject audioGo;
        private CombatAudioController audioController;
        private RecordingAudioPlaybackAdapter audioAdapter;
        private CombatFeedbackProfile profile;
        private AudioClip whooshClip;

        private GameObject trailGo;
        private SwordTrailController swordTrail;
        private TrailRenderer trailRenderer;

        private GameObject playerGo;
        private CombatantMarker playerMarker;

        [SetUp]
        public void SetUp()
        {
            attackFeedbackGo = new GameObject("CombatAttackFeedback");
            attackFeedback = attackFeedbackGo.AddComponent<CombatAttackFeedback>();

            audioGo = new GameObject("CombatAudioController");
            audioController = audioGo.AddComponent<CombatAudioController>();
            audioAdapter = new RecordingAudioPlaybackAdapter();
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            whooshClip = AudioClip.Create("SFX_Sword_Whoosh..", 100, 1, 44100, false);
            profile.SetConfig(
                profile.NormalHit,
                profile.LethalHit,
                whoosh: whooshClip);
            audioController.SetDependencies(audioAdapter, profile);

            trailGo = new GameObject("SwordTrail");
            trailRenderer = trailGo.AddComponent<TrailRenderer>();
            trailRenderer.emitting = false;
            swordTrail = trailGo.AddComponent<SwordTrailController>();
            var trailField = typeof(SwordTrailController).GetField("trailRenderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (trailField != null) trailField.SetValue(swordTrail, trailRenderer);

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            attackFeedback.SetDependencies(audioController, swordTrail, playerMarker);
        }

        [TearDown]
        public void TearDown()
        {
            if (attackFeedbackGo != null) Object.DestroyImmediate(attackFeedbackGo);
            if (audioGo != null) Object.DestroyImmediate(audioGo);
            if (profile != null) Object.DestroyImmediate(profile);
            if (trailGo != null) Object.DestroyImmediate(trailGo);
            if (playerGo != null) Object.DestroyImmediate(playerGo);
            if (whooshClip != null) Object.DestroyImmediate(whooshClip);
        }

        [Test]
        public void AttackStarted_TriggersWhooshAudio_AndEnablesSwordTrail()
        {
            Assert.That(swordTrail.IsEmitting, Is.False, "初期状態では軌跡が無効である必要があります。");

            attackFeedback.HandleAttackStarted(1);

            Assert.That(swordTrail.IsEmitting, Is.True, "攻撃開始時に軌跡が有効化される必要があります。");
            Assert.That(audioAdapter.PlayRecords.Count, Is.EqualTo(1), "剣撃SEが1回再生される必要があります。");
            Assert.That(audioAdapter.PlayRecords[0].Clip, Is.SameAs(whooshClip), "正しい SFX_Sword_Whoosh.. が再生される必要があります。");
        }

        [Test]
        public void AttackEnded_DisablesSwordTrail()
        {
            attackFeedback.HandleAttackStarted(1);
            Assert.That(swordTrail.IsEmitting, Is.True);

            attackFeedback.HandleAttackEnded(1);
            Assert.That(swordTrail.IsEmitting, Is.False, "攻撃終了時に軌跡が無効化される必要があります。");
        }

        [Test]
        public void EmptySwing_DoesNotTriggerHitFeedback()
        {
            // 空振り動作: アクションタイムラインのみ実行
            attackFeedback.HandleAttackStarted(1);
            attackFeedback.HandleAttackEnded(1);

            // 剣撃SEと軌跡の開閉のみが発生し、ヒットSEは発生しないことを検証
            Assert.That(audioAdapter.PlayRecords.Count, Is.EqualTo(1));
            Assert.That(audioAdapter.PlayRecords[0].Clip, Is.SameAs(whooshClip));
            Assert.That(swordTrail.IsEmitting, Is.False);
        }

        [Test]
        public void ClearRuntimeState_ResetsSwordTrail()
        {
            attackFeedback.HandleAttackStarted(1);
            Assert.That(swordTrail.IsEmitting, Is.True);

            attackFeedback.ClearRuntimeState();
            Assert.That(swordTrail.IsEmitting, Is.False, "ClearRuntimeState 後は軌跡が無効状態である必要があります。");
        }
    }
}

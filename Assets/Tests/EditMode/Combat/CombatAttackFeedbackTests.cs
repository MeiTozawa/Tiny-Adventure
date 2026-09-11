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
            profile.ConfigureForTests(
                profile.NormalHit,
                profile.LethalHit,
                whoosh: whooshClip);
            audioController.ConfigureForTests(audioAdapter, profile);

            trailGo = new GameObject("SwordTrail");
            trailRenderer = trailGo.AddComponent<TrailRenderer>();
            trailRenderer.emitting = false;
            swordTrail = trailGo.AddComponent<SwordTrailController>();
            var trailField = typeof(SwordTrailController).GetField("trailRenderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (trailField != null) trailField.SetValue(swordTrail, trailRenderer);

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");

            attackFeedback.ConfigureForTests(audioController, swordTrail, playerMarker);
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
            Assert.That(swordTrail.IsEmitting, Is.False, "初始状态下刀光不应发射。");

            attackFeedback.HandleAttackStarted(1);

            Assert.That(swordTrail.IsEmitting, Is.True, "攻击开始时应开启刀光。");
            Assert.That(audioAdapter.PlayRecords.Count, Is.EqualTo(1), "应该播放 1 次挥刀音效。");
            Assert.That(audioAdapter.PlayRecords[0].Clip, Is.SameAs(whooshClip), "应播放正确的 SFX_Sword_Whoosh.. 音效。");
        }

        [Test]
        public void AttackEnded_DisablesSwordTrail()
        {
            attackFeedback.HandleAttackStarted(1);
            Assert.That(swordTrail.IsEmitting, Is.True);

            attackFeedback.HandleAttackEnded(1);
            Assert.That(swordTrail.IsEmitting, Is.False, "攻击结束时应关闭刀光。");
        }

        [Test]
        public void EmptySwing_DoesNotTriggerHitFeedback()
        {
            // 空挥动作：仅执行动作时间轴
            attackFeedback.HandleAttackStarted(1);
            attackFeedback.HandleAttackEnded(1);

            // 验证仅产生挥刀音与刀光启闭，没有产生任何命中音效
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
            Assert.That(swordTrail.IsEmitting, Is.False, "ClearRuntimeState 后刀光应处于关闭状态。");
        }
    }
}

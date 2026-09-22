using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatCameraFeedbackPropertiesTests
    {
        private GameObject feedbackGo;
        private CombatCameraFeedback cameraFeedback;
        private RecordingImpulseEmitter impulseEmitter;
        private RecordingFovPunchAdapter fovPunchAdapter;
        private CombatFeedbackProfile profile;

        private static CombatFeedbackRequest CreateRequest(CombatHitType hitType, bool isPlayerTarget = false)
        {
            return new CombatFeedbackRequest(
                hitType,
                null,
                null,
                default,
                Vector3.zero,
                Vector3.forward,
                !isPlayerTarget,
                isPlayerTarget,
                default);
        }

        [SetUp]
        public void SetUp()
        {
            feedbackGo = new GameObject("CombatCameraFeedback");
            cameraFeedback = feedbackGo.AddComponent<CombatCameraFeedback>();

            impulseEmitter = new RecordingImpulseEmitter();
            fovPunchAdapter = new RecordingFovPunchAdapter();
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();

            cameraFeedback.SetDependencies(impulseEmitter, fovPunchAdapter, profile);
        }

        [TearDown]
        public void TearDown()
        {
            if (feedbackGo != null) Object.DestroyImmediate(feedbackGo);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void Property5_FovPunch_RestoresBaseValueOnClear()
        {
            // 単一の通常ヒット
            cameraFeedback.Play(CreateRequest(CombatHitType.Normal));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));
            Assert.That(fovPunchAdapter.PunchRecords[0].Offset, Is.EqualTo(profile.Camera.normalHitFovOffset));

            // 連続した致命ヒット
            cameraFeedback.Play(CreateRequest(CombatHitType.Lethal));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(2));
            Assert.That(fovPunchAdapter.PunchRecords[1].Offset, Is.EqualTo(profile.Camera.lethalHitFovOffset));

            // プレイヤー被弾
            cameraFeedback.Play(CreateRequest(CombatHitType.Normal, isPlayerTarget: true));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(3));
            Assert.That(fovPunchAdapter.PunchRecords[2].Offset, Is.EqualTo(profile.Camera.playerHurtFovOffset));

            // クリーンアップ時に初期値へ復帰
            int beforeClear = fovPunchAdapter.ClearCount;
            cameraFeedback.ClearRuntimeState();
            Assert.That(fovPunchAdapter.ClearCount, Is.EqualTo(beforeClear + 1));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(0));
        }

        [Test]
        public void Property6_ImpulseEmitterIndependentOfDirectCameraTransform()
        {
            var cameraGo = new GameObject("MainCameraStub");
            cameraGo.transform.position = new Vector3(5, 10, 15);
            cameraGo.transform.rotation = Quaternion.Euler(10, 20, 30);
            cameraGo.transform.localScale = Vector3.one;

            var request = CreateRequest(CombatHitType.Lethal);
            cameraFeedback.Play(request);

            // Impulse アダプターの呼び出しのみを検証
            Assert.That(impulseEmitter.ImpulseRecords.Count, Is.EqualTo(1));
            Assert.That(impulseEmitter.ImpulseRecords[0].Settings.amplitude, Is.EqualTo(profile.Camera.lethalHitImpulse.amplitude));

            // カメラ Transform 属性の直接変更を禁止
            Assert.That(cameraGo.transform.position, Is.EqualTo(new Vector3(5, 10, 15)), "カメラ Transform の位置を直接変更してはなりません。");
            Assert.That(Quaternion.Angle(cameraGo.transform.rotation, Quaternion.Euler(10, 20, 30)), Is.LessThan(0.001f), "カメラ Transform の回転を直接変更してはなりません。");
            Assert.That(cameraGo.transform.localScale, Is.EqualTo(Vector3.one), "カメラ Transform のスケールを直接変更してはなりません。");

            Object.DestroyImmediate(cameraGo);
        }
    }
}

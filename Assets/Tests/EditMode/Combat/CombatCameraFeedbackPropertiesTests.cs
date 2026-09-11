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

            cameraFeedback.ConfigureForTests(impulseEmitter, fovPunchAdapter, profile);
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
            // 单次普通命中
            cameraFeedback.Play(CreateRequest(CombatHitType.Normal));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));
            Assert.That(fovPunchAdapter.PunchRecords[0].Offset, Is.EqualTo(profile.Camera.normalHitFovOffset));

            // 连续致死命中
            cameraFeedback.Play(CreateRequest(CombatHitType.Lethal));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(2));
            Assert.That(fovPunchAdapter.PunchRecords[1].Offset, Is.EqualTo(profile.Camera.lethalHitFovOffset));

            // 玩家受击
            cameraFeedback.Play(CreateRequest(CombatHitType.Normal, isPlayerTarget: true));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(3));
            Assert.That(fovPunchAdapter.PunchRecords[2].Offset, Is.EqualTo(profile.Camera.playerHurtFovOffset));

            // 清理时恢复原值
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

            // 验证仅调用 Impulse 适配器
            Assert.That(impulseEmitter.ImpulseRecords.Count, Is.EqualTo(1));
            Assert.That(impulseEmitter.ImpulseRecords[0].Settings.amplitude, Is.EqualTo(profile.Camera.lethalHitImpulse.amplitude));

            // 严禁改写任何相机 Transform 属性
            Assert.That(cameraGo.transform.position, Is.EqualTo(new Vector3(5, 10, 15)), "相机 Transform 位置不得被直接修改。");
            Assert.That(Quaternion.Angle(cameraGo.transform.rotation, Quaternion.Euler(10, 20, 30)), Is.LessThan(0.001f), "相机 Transform 旋转不得被直接修改。");
            Assert.That(cameraGo.transform.localScale, Is.EqualTo(Vector3.one), "相机 Transform 缩放不得被直接修改。");

            Object.DestroyImmediate(cameraGo);
        }
    }
}

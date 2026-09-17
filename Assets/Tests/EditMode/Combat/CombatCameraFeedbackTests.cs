using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatCameraFeedbackTests
    {
        private GameObject feedbackGo;
        private CombatCameraFeedback cameraFeedback;
        private RecordingImpulseEmitter impulseEmitter;
        private RecordingFovPunchAdapter fovPunchAdapter;
        private CombatFeedbackProfile profile;

        private static CombatFeedbackRequest CreateRequest(
            CombatHitType hitType,
            Vector3 direction,
            bool isPlayerTarget = false,
            bool isPlayerAttack = true)
        {
            return new CombatFeedbackRequest(
                hitType,
                null,
                null,
                default,
                Vector3.zero,
                direction,
                isPlayerAttack,
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
            if (feedbackGo != null)
            {
                Object.DestroyImmediate(feedbackGo);
            }

            if (profile != null)
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PlayNormalHit_EmitsNormalImpulseAndFovPunch()
        {
            var request = CreateRequest(CombatHitType.Normal, new Vector3(0, 0, 1));

            cameraFeedback.Play(request);

            Assert.That(impulseEmitter.ImpulseRecords.Count, Is.EqualTo(1));
            var impulse = impulseEmitter.ImpulseRecords[0];
            Assert.That(impulse.Settings.amplitude, Is.EqualTo(profile.Camera.normalHitImpulse.amplitude));
            Assert.That(impulse.Direction, Is.EqualTo(Vector3.forward));

            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));
            var fov = fovPunchAdapter.PunchRecords[0];
            Assert.That(fov.Offset, Is.EqualTo(profile.Camera.normalHitFovOffset));
            Assert.That(fov.EnterSeconds, Is.EqualTo(profile.Camera.enterSeconds));
            Assert.That(fov.RecoverSeconds, Is.EqualTo(profile.Camera.recoverSeconds));
        }

        [Test]
        public void PlayLethalHit_EmitsLethalImpulseAndFovPunch()
        {
            var request = CreateRequest(CombatHitType.Lethal, new Vector3(1, 0, 0));

            cameraFeedback.Play(request);

            Assert.That(impulseEmitter.ImpulseRecords.Count, Is.EqualTo(1));
            var impulse = impulseEmitter.ImpulseRecords[0];
            Assert.That(impulse.Settings.amplitude, Is.EqualTo(profile.Camera.lethalHitImpulse.amplitude));
            Assert.That(impulse.Direction, Is.EqualTo(Vector3.right));

            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));
            var fov = fovPunchAdapter.PunchRecords[0];
            Assert.That(fov.Offset, Is.EqualTo(profile.Camera.lethalHitFovOffset));
        }

        [Test]
        public void PlayPlayerHurt_EmitsPlayerHurtImpulseAndFovOffset()
        {
            var request = CreateRequest(CombatHitType.Normal, Vector3.back, isPlayerTarget: true, isPlayerAttack: false);

            cameraFeedback.Play(request);

            Assert.That(impulseEmitter.ImpulseRecords.Count, Is.EqualTo(1));
            var impulse = impulseEmitter.ImpulseRecords[0];
            Assert.That(impulse.Settings.amplitude, Is.EqualTo(profile.Camera.playerHurtImpulse.amplitude));

            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));
            var fov = fovPunchAdapter.PunchRecords[0];
            Assert.That(fov.Offset, Is.EqualTo(profile.Camera.playerHurtFovOffset));
        }

        [Test]
        public void PlayPlayerHurt_TriggersTraumaImpulseAndImpactJoltOnFirstPersonControllers()
        {
            var playerGo = new GameObject("Player");
            var cameraTargetGo = new GameObject("CameraTarget");
            cameraTargetGo.transform.SetParent(playerGo.transform, false);
            playerGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var fpCamGo = new GameObject("TestFPCam");
            var fpCam = fpCamGo.AddComponent<FirstPersonCameraController>();

            var vmGo = new GameObject("TestViewmodel");
            var vm = vmGo.AddComponent<FirstPersonViewmodelController>();

            try
            {
                cameraFeedback.ConfigurePlayerHitControllers(fpCam, vm, playerGo.transform);

                var request = CreateRequest(CombatHitType.Normal, new Vector3(0, 0, 1), isPlayerTarget: true, isPlayerAttack: false);
                cameraFeedback.Play(request);

                Assert.That(fpCam.HitTraumaSpring.IsActive, Is.True, "プレイヤー被弾時にカメラ受撃物理スプリングが活性化される必要があります。");
                Assert.That(vm.IsJolting, Is.True, "プレイヤー被弾時に視口武器のJolt反動が活性化される必要があります。");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(vmGo);
                Object.DestroyImmediate(fpCamGo);
            }
        }

        [Test]
        public void ClearRuntimeState_ClearsFovPunch()
        {
            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);
            cameraFeedback.Play(request);

            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));

            int beforeClear = fovPunchAdapter.ClearCount;
            cameraFeedback.ClearRuntimeState();

            Assert.That(fovPunchAdapter.ClearCount, Is.EqualTo(beforeClear + 1));
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(0));
        }

        [Test]
        public void ZeroDirection_DefaultsToDownVector()
        {
            var request = CreateRequest(CombatHitType.Normal, Vector3.zero);
            cameraFeedback.Play(request);

            Assert.That(impulseEmitter.ImpulseRecords.Count, Is.EqualTo(1));
            Assert.That(impulseEmitter.ImpulseRecords[0].Direction, Is.EqualTo(Vector3.down));
        }

        [Test]
        public void ImpulseEmitterException_DoesNotCrash()
        {
            impulseEmitter.ThrowOnGenerate = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Cinemachine Impulse.*"));

            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);
            Assert.DoesNotThrow(() => cameraFeedback.Play(request));

            // FOV punch 仍旧能正常被调用
            Assert.That(fovPunchAdapter.PunchRecords.Count, Is.EqualTo(1));
        }

        [Test]
        public void FovPunchException_DoesNotCrash()
        {
            fovPunchAdapter.ThrowOnPunch = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*FOV.*"));

            var request = CreateRequest(CombatHitType.Normal, Vector3.forward);
            Assert.DoesNotThrow(() => cameraFeedback.Play(request));
        }

        [Test]
        public void CameraTransformNeverModifiedDirectly()
        {
            var camGo = new GameObject("TestMainCamera");
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(10, 20, 30);
            camGo.transform.rotation = Quaternion.Euler(15, 45, 0);

            var request = CreateRequest(CombatHitType.Lethal, Vector3.forward);
            cameraFeedback.Play(request);

            Assert.That(camGo.transform.position, Is.EqualTo(new Vector3(10, 20, 30)), "相机 Transform 位置绝不允许被直接修改！");
            Assert.That(camGo.transform.rotation, Is.EqualTo(Quaternion.Euler(15, 45, 0)), "相机 Transform 旋转绝不允许被直接修改！");

            Object.DestroyImmediate(camGo);
        }
    }
}

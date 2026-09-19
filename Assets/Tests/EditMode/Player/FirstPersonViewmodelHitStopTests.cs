using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class FirstPersonViewmodelHitStopTests
    {
        private GameObject cameraGo;
        private Camera testCamera;
        private GameObject viewmodelGo;
        private FirstPersonViewmodelController controller;

        [SetUp]
        public void SetUp()
        {
            cameraGo = new GameObject("TestCamera");
            testCamera = cameraGo.AddComponent<Camera>();

            viewmodelGo = new GameObject("TestViewmodel");
            controller = viewmodelGo.AddComponent<FirstPersonViewmodelController>();
            controller.SetTargetCamera(testCamera);
        }

        [TearDown]
        public void TearDown()
        {
            if (viewmodelGo != null)
            {
                Object.DestroyImmediate(viewmodelGo);
            }
            if (cameraGo != null)
            {
                Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void HitStop_PausesAttackProgression_AndResumesOnEnd()
        {
            controller.TriggerAttack(0, 1.0f);
            Assert.That(controller.IsAttacking, Is.True);

            // 0.1秒進める
            controller.Evaluate(0.1f);
            Vector3 posAt01 = controller.transform.position;

            // ヒットストップ開始
            var token = new HitStopToken(1, 100.0, 100.04);
            controller.BeginHitStop(token);
            Assert.That(controller.IsHitStopPaused, Is.True);

            // ヒットストップ中に0.02秒経過させる（attackTimerは進まない）
            controller.Evaluate(0.02f);
            Assert.That(controller.IsAttacking, Is.True);

            // ヒットストップ終了
            controller.EndHitStop(token);
            Assert.That(controller.IsHitStopPaused, Is.False);

            // 終了後に0.02秒進めると正常に進行を再開する
            controller.Evaluate(0.02f);
            Assert.That(controller.IsAttacking, Is.True);
        }

        [Test]
        public void CancelAttack_ClearsHitStopJitter()
        {
            controller.TriggerAttack(0, 1.0f);
            controller.BeginHitStop(new HitStopToken(2, 100.0, 100.04));

            controller.CancelAttack();

            Assert.That(controller.IsAttacking, Is.False);
        }
    }
}

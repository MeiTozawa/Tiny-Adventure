using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// PlayerController の踏み込み突進（Forward Lunge）機能の単体・契約テストです。
    /// </summary>
    public sealed class PlayerControllerLungeTests
    {
        private GameObject playableFloor;
        private GameObject playerObject;
        private PlayerController controller;

        [SetUp]
        public void SetUp()
        {
            playableFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playableFloor.name = "PlayableFloor";
            playableFloor.transform.position = new Vector3(0f, -0.5f, 0f);
            playableFloor.transform.localScale = new Vector3(20f, 1f, 20f);

            playerObject = new GameObject("Knight_LungeTest");
            playerObject.transform.position = Vector3.zero;
            playerObject.AddComponent<CharacterController>();
            controller = playerObject.AddComponent<PlayerController>();

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }

            if (playableFloor != null)
            {
                Object.DestroyImmediate(playableFloor);
            }
        }

        [Test]
        public void StartAttackLungeGeneratesForwardLungeMotionAndCompletesAfterDuration()
        {
            controller.StartAttackLunge(Vector3.forward, 2.0f, 0.2f);
            Assert.That(controller.IsLunging, Is.True);

            // 0.1秒（半分の時間）進める
            controller.ProcessMovement(Vector2.zero, 0.1f);
            Assert.That(controller.LastLungeMotion.z, Is.GreaterThan(0.5f), "踏み込み突進の変位が前方に生成されていません。");
            Assert.That(controller.IsLunging, Is.True);

            // さらに0.15秒進めて完了させる（このフレームで残りの突進を完了）
            controller.ProcessMovement(Vector2.zero, 0.15f);
            Assert.That(controller.IsLunging, Is.False);

            // 完了後の次フレームでは突進変位はゼロ
            controller.ProcessMovement(Vector2.zero, 0.05f);
            Assert.That(controller.LastLungeMotion, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void CancelLungeImmediatelyStopsLungeState()
        {
            controller.StartAttackLunge(Vector3.forward, 2.0f, 0.2f);
            Assert.That(controller.IsLunging, Is.True);
            controller.CancelLunge();
            Assert.That(controller.IsLunging, Is.False);
            Assert.That(controller.LastLungeMotion, Is.EqualTo(Vector3.zero));
        }
    }
}

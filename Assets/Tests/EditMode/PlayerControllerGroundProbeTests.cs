using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// PlayerControllerの地面検査が自身のColliderを地面として採用しないことを検証します。
    /// </summary>
    public sealed class PlayerControllerGroundProbeTests
    {
        private GameObject playableFloor;
        private GameObject player;
        private GameObject playerChild;

        [SetUp]
        public void セットアップ()
        {
            playableFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playableFloor.name = "PlayableFloor";
            playableFloor.transform.position = new Vector3(0f, -0.5f, 0f);
            playableFloor.transform.localScale = new Vector3(10f, 1f, 10f);

            player = new GameObject("Player");
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerController>();

            playerChild = new GameObject("PlayerChildCollider");
            playerChild.transform.SetParent(player.transform, false);
            playerChild.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            playerChild.AddComponent<BoxCollider>();

            Physics.SyncTransforms();
        }

        [TearDown]
        public void 後始末()
        {
            Object.DestroyImmediate(playerChild);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(playableFloor);
        }

        [Test]
        public void 地面検査はPlayer自身と子Colliderを除外してPlayableFloorを返す()
        {
            PlayerController controller = player.GetComponent<PlayerController>();
            MethodInfo tryGetGround = typeof(PlayerController).GetMethod(
                "TryGetGround",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(tryGetGround, Is.Not.Null);

            object[] arguments = { player.transform.position, null };
            bool foundGround = (bool)tryGetGround.Invoke(controller, arguments);
            RaycastHit hit = (RaycastHit)arguments[1];

            Assert.That(foundGround, Is.True);
            Assert.That(hit.collider, Is.EqualTo(playableFloor.GetComponent<Collider>()));
            Assert.That(hit.collider.transform.IsChildOf(player.transform), Is.False);
        }
    }
}
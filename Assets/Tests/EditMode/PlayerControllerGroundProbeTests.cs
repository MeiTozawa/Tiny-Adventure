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
        public void SetUp()
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
        public void TearDown()
        {
            Object.DestroyImmediate(playerChild);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(playableFloor);
        }

        [Test]
        public void GroundProbeExcludesPlayerAndChildColliders()
        {
            PlayerController controller = player.GetComponent<PlayerController>();
            MethodInfo getGround = typeof(PlayerController).GetMethod(
                "GetGround",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(getGround, Is.Not.Null);

            object[] arguments = { player.transform.position };
            Result<RaycastHit> result = (Result<RaycastHit>)getGround.Invoke(controller, arguments);

            Assert.That(result.IsOk, Is.True);
            RaycastHit hit = result.Value;
            Assert.That(hit.collider, Is.EqualTo(playableFloor.GetComponent<Collider>()));
            Assert.That(hit.collider.transform.IsChildOf(player.transform), Is.False);
        }
    }
}
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// PlayerCombatController、FirstPersonCameraController、および
    /// FirstPersonViewmodelController間の連携（出刀トリガー、中断、LookSway）を検証します。
    /// </summary>
    public sealed class PlayerCombatViewmodelWiringTests
    {
        private GameObject playerObject;
        private PlayerCombatController combatController;
        private FirstPersonViewmodelController viewmodelController;
        private CombatantMarker playerMarker;
        private GameObject cameraObject;
        private Camera testCamera;
        private ComboAttackConfigSO comboConfig;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("MainCamera");
            testCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";

            playerObject = new GameObject("Player");
            playerObject.AddComponent<CharacterController>();
            playerObject.AddComponent<InputReader>();
            playerMarker = playerObject.AddComponent<CombatantMarker>();
            var playerController = playerObject.AddComponent<PlayerController>();

            var cameraTarget = new GameObject("CameraTarget");
            cameraTarget.transform.SetParent(playerObject.transform, false);

            var animatorObj = new GameObject("Animator");
            animatorObj.transform.SetParent(playerObject.transform, false);
            var animator = animatorObj.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CharacterCombat.controller");

            var animationDriver = playerObject.AddComponent<PlayerAnimationDriver>();

            GameObject viewmodelObj = new GameObject("FirstPersonViewmodel");
            viewmodelObj.transform.SetParent(playerObject.transform, false);
            viewmodelController = viewmodelObj.AddComponent<FirstPersonViewmodelController>();
            viewmodelController.SetTargetCamera(testCamera);

            GameObject hitboxObj = new GameObject("SwordHitbox");
            hitboxObj.transform.SetParent(viewmodelObj.transform, false);
            var col = hitboxObj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            var hitbox = hitboxObj.AddComponent<CombatHitbox>();

            combatController = playerObject.AddComponent<PlayerCombatController>();
            combatController.SetFallbackGameplayState(GameplayState.Running);

            comboConfig = ScriptableObject.CreateInstance<ComboAttackConfigSO>();
            comboConfig.ComboResetTimeout = 0.45f;
            comboConfig.SetStepsForTests(new[]
            {
                new AttackConfigStep { Damage = 20f, Range = 2.2f, LungeDistance = 0.8f, LungeDuration = 0.12f, SpeedMultiplier = 1.7f },
                new AttackConfigStep { Damage = 25f, Range = 2.2f, LungeDistance = 1.2f, LungeDuration = 0.15f, SpeedMultiplier = 1.6f },
                new AttackConfigStep { Damage = 40f, Range = 2.8f, LungeDistance = 2.2f, LungeDuration = 0.20f, SpeedMultiplier = 1.5f }
            });
            combatController.AttackConfig = comboConfig;

            combatController.ConfigureForTests(
                playerObject.GetComponent<InputReader>(),
                animationDriver,
                animator,
                playerMarker,
                null,
                hitbox,
                null,
                playerController);

            combatController.SetViewmodelController(viewmodelController);
        }

        [TearDown]
        public void TearDown()
        {
            if (comboConfig != null)
            {
                Object.DestroyImmediate(comboConfig);
            }

            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void TryStartAttack_TriggersViewmodelAttack_WithComboIndex()
        {
            bool started = combatController.TryStartAttack(out string diagnostic);
            Assert.That(started, Is.True, $"初段攻撃が正常に開始される必要があります。診断: {diagnostic}");
            Assert.That(viewmodelController.IsAttacking, Is.True, "PlayerCombatControllerの出刀開始と同時に視口武器の出刀が開始される必要があります。");
            Assert.That(viewmodelController.CurrentAttackComboIndex, Is.EqualTo(0), "第1段のコンボインデックス（0）が視口武器に伝達される必要があります。");
        }

        [Test]
        public void CancelAttack_CancelsViewmodelAttack()
        {
            bool started = combatController.TryStartAttack(out string diagnostic);
            Assert.That(started, Is.True, $"出刀開始失敗: {diagnostic}");
            Assert.That(viewmodelController.IsAttacking, Is.True);

            combatController.CancelAttack();
            Assert.That(viewmodelController.IsAttacking, Is.False, "PlayerCombatControllerのCancelAttackにより視口武器の出刀が中断される必要があります。");
        }

        [Test]
        public void CameraLookInput_TransfersToViewmodelController()
        {
            GameObject camRigObj = new GameObject("CM_FirstPerson");
            try
            {
                var camController = camRigObj.AddComponent<FirstPersonCameraController>();
                camController.SetViewmodelController(viewmodelController);

                viewmodelController.Evaluate(0.1f);
                Vector3 initialPos = viewmodelController.transform.position;

                camController.ApplyLookInput(new Vector2(80f, 40f));
                viewmodelController.Evaluate(0.016f);

                Vector3 swayedPos = viewmodelController.transform.position;
                Assert.That(Vector3.Distance(initialPos, swayedPos), Is.GreaterThan(0.001f),
                    "FirstPersonCameraControllerの視線入力がViewmodelControllerのSway変位に反映される必要があります。");
            }
            finally
            {
                Object.DestroyImmediate(camRigObj);
            }
        }
    }
}

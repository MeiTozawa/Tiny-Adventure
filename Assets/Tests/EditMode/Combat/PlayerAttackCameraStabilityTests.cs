using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 第一人称戦闘中における出刀動作の安定性とカメラ防めり込み契約テストです。
    /// 攻撃時の不本意な前方突進（LungeDistance）が徹底排除され、敵接近時の防めり込み間距（>= 1.10m）が遵守されることを検証します。
    /// </summary>
    public sealed class PlayerAttackCameraStabilityTests
    {
        private GameObject playerObject;
        private GameObject enemyObject;
        private PlayerController playerController;
        private PlayerCombatController combatController;
        private Transform cameraTarget;

        [TearDown]
        public void TearDown()
        {
            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }

            if (enemyObject != null)
            {
                Object.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void KnightComboAttackConfig_HasNoLungeProperties_AndAllStepsValid()
        {
            var config = AssetDatabase.LoadAssetAtPath<ComboAttackConfig>("Assets/Combat/Configs/KnightComboAttackConfig.asset");
            Assert.That(config, Is.Not.Null, "KnightComboAttackConfig.assetが見つかりません。");
            Assert.That(config.StepCount, Is.EqualTo(3), "KnightComboAttackConfigは3段コンボである必要があります。");

            for (int i = 0; i < config.StepCount; i++)
            {
                AttackConfigStep step = config.GetStep(i);
                Assert.That(step.Damage, Is.GreaterThan(0f));
                Assert.That(step.Range, Is.GreaterThanOrEqualTo(2.0f));
                Assert.That(step.SpeedMultiplier, Is.GreaterThan(0.5f));
            }

            // 反射により LungeDistance / LungeDuration 属性が型上に存在しないことを検証
            PropertyInfo stepDistProp = typeof(AttackConfigStep).GetProperty("LungeDistance", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            FieldInfo stepDistField = typeof(AttackConfigStep).GetField("LungeDistance", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            PropertyInfo configDistProp = typeof(AttackConfig).GetProperty("LungeDistance", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            PropertyInfo combatDistProp = typeof(PlayerCombatController).GetProperty("LungeDistance", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            Assert.That(stepDistProp, Is.Null, "AttackConfigStepからLungeDistanceプロパティが徹底削除されている必要があります。");
            Assert.That(stepDistField, Is.Null, "AttackConfigStepからLungeDistanceフィールドが徹底削除されている必要があります。");
            Assert.That(configDistProp, Is.Null, "AttackConfigからLungeDistanceプロパティが徹底削除されている必要があります。");
            Assert.That(combatDistProp, Is.Null, "PlayerCombatControllerからLungeDistanceプロパティが徹底削除されている必要があります。");
        }

        [Test]
        public void PlayerAttack_DoesNotDisplacePlayerRootOrCameraTarget()
        {
            SetupPlayer();

            var config = AssetDatabase.LoadAssetAtPath<ComboAttackConfig>("Assets/Combat/Configs/KnightComboAttackConfig.asset");
            combatController.AttackConfig = config;

            Vector3 initialPlayerPos = playerObject.transform.position;
            Vector3 initialCameraPos = cameraTarget.position;

            // 1段目 (横薙ぎ) 攻撃開始
            Assert.That(combatController.StartAttack().IsOk, Is.True);
            playerController.ProcessMovement(Vector2.zero, 0.05f);

            Assert.That(playerObject.transform.position, Is.EqualTo(initialPlayerPos), "攻撃1段目でプレイヤー座標が前移してはいけません。");
            Assert.That(cameraTarget.position, Is.EqualTo(initialCameraPos), "攻撃1段目でCameraTarget（カメラ視点）が前移してはいけません。");

            combatController.AnimationEventCompleteAttack();

            // 2段目 (縦斬り)
            Assert.That(combatController.StartAttack().IsOk, Is.True);
            playerController.ProcessMovement(Vector2.zero, 0.05f);

            Assert.That(playerObject.transform.position, Is.EqualTo(initialPlayerPos), "攻撃2段目でプレイヤー座標が前移してはいけません。");
            Assert.That(cameraTarget.position, Is.EqualTo(initialCameraPos), "攻撃2段目でCameraTargetが前移してはいけません。");

            combatController.AnimationEventCompleteAttack();

            // 3段目 (刺突フィニッシャー)
            Assert.That(combatController.StartAttack().IsOk, Is.True);
            playerController.ProcessMovement(Vector2.zero, 0.05f);

            Assert.That(playerObject.transform.position, Is.EqualTo(initialPlayerPos), "攻撃3段目でプレイヤー座標が前移してはいけません。");
            Assert.That(cameraTarget.position, Is.EqualTo(initialCameraPos), "攻撃3段目でCameraTargetが前移してはいけません。");
        }

        [Test]
        public void PlayerMovement_ClampsAtMinimumEnemyClearance_PreventingModelPenetration()
        {
            SetupPlayer();
            playerObject.transform.position = Vector3.zero;

            // プレイヤーの前方2.0m地点に敵を配置
            enemyObject = new GameObject("Enemy_SafetyTarget");
            enemyObject.transform.position = new Vector3(0f, 0f, 2.0f);
            var col = enemyObject.AddComponent<CapsuleCollider>();
            col.radius = 0.5f;
            col.height = 2.0f;
            col.center = new Vector3(0f, 1f, 0f);
            var marker = enemyObject.AddComponent<CombatantMarker>();
            marker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_SafetyTarget");
            var health = enemyObject.AddComponent<HealthComponent>();
            health.Configure(100f);

            Physics.SyncTransforms();

            // 前方（Wキー）へ激しく前進（1.5m移動を試みる）
            for (int step = 0; step < 30; step++)
            {
                playerController.ProcessMovement(Vector2.up, 0.05f);
                Physics.SyncTransforms();
            }

            float horizontalDistance = Vector2.Distance(
                new Vector2(playerObject.transform.position.x, playerObject.transform.position.z),
                new Vector2(enemyObject.transform.position.x, enemyObject.transform.position.z));

            Assert.That(horizontalDistance, Is.GreaterThanOrEqualTo(PlayerController.MinimumEnemyClearance - 0.001f),
                $"プレイヤーと敵の距離（{horizontalDistance:F2}m）がMinimumEnemyClearance（{PlayerController.MinimumEnemyClearance:F2}m）未満に侵入しています。カメラめり込みの原因になります。");

            float cameraHorizontalDistance = Vector2.Distance(
                new Vector2(cameraTarget.position.x, cameraTarget.position.z),
                new Vector2(enemyObject.transform.position.x, enemyObject.transform.position.z));

            Assert.That(cameraHorizontalDistance, Is.GreaterThanOrEqualTo(PlayerController.MinimumEnemyClearance - 0.001f),
                $"CameraTargetと敵の距離（{cameraHorizontalDistance:F2}m）が安全間距未満です。");
        }

        private void SetupPlayer()
        {
            playerObject = new GameObject("Knight_TestPlayer");
            playerObject.AddComponent<CharacterController>();
            playerObject.AddComponent<InputReader>();
            var marker = playerObject.AddComponent<CombatantMarker>();
            marker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight_TestPlayer");
            playerController = playerObject.AddComponent<PlayerController>();

            var targetObj = new GameObject("CameraTarget");
            targetObj.transform.SetParent(playerObject.transform, false);
            targetObj.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            cameraTarget = targetObj.transform;

            var animatorObj = new GameObject("Animator");
            animatorObj.transform.SetParent(playerObject.transform, false);
            var animator = animatorObj.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CharacterCombat.controller");

            var animationDriver = playerObject.AddComponent<PlayerAnimationDriver>();

            var hitboxObj = new GameObject("SwordHitbox");
            hitboxObj.transform.SetParent(playerObject.transform, false);
            var box = hitboxObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var hitbox = hitboxObj.AddComponent<CombatHitbox>();

            combatController = playerObject.AddComponent<PlayerCombatController>();
            combatController.SetFallbackGameplayState(GameplayState.Running);
            combatController.Construct(
                damageService: null,
                gameFlowController: null,
                inputReader: playerObject.GetComponent<InputReader>(),
                animationDriver: animationDriver,
                targetAnimator: animator,
                combatantMarker: marker,
                swordHitbox: hitbox,
                playerController: playerController);
        }
    }
}

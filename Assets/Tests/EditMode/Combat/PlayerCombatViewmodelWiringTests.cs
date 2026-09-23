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
        private ComboAttackConfig comboConfig;

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

            comboConfig = ScriptableObject.CreateInstance<ComboAttackConfig>();
            comboConfig.ComboResetTimeout = 0.45f;
            comboConfig.SetSteps(new[]
            {
                new AttackConfigStep { Damage = 20f, Range = 2.2f, SpeedMultiplier = 1.7f, WindowCloseNormalizedTime = 0.42f, CompletionNormalizedTime = 0.65f },
                new AttackConfigStep { Damage = 25f, Range = 2.2f, SpeedMultiplier = 1.6f, WindowCloseNormalizedTime = 0.45f, CompletionNormalizedTime = 0.70f },
                new AttackConfigStep { Damage = 40f, Range = 2.8f, SpeedMultiplier = 1.5f, WindowCloseNormalizedTime = 0.45f, CompletionNormalizedTime = 0.75f }
            });
            combatController.AttackConfig = comboConfig;

            combatController.Construct(
                damageService: null,
                gameFlowController: null,
                inputReader: playerObject.GetComponent<InputReader>(),
                animationDriver: animationDriver,
                targetAnimator: animator,
                combatantMarker: playerMarker,
                swordHitbox: hitbox,
                playerController: playerController);

            combatController.SetViewmodelController(viewmodelController);
        }

        [TearDown]
        public void TearDown()
        {
            if (comboConfig != null)
            {
                Object.DestroyImmediate(comboConfig);
            }

            if (playerObject != null)
            {
                playerObject.GetComponent<InputReader>()?.Dispose();
                Object.DestroyImmediate(playerObject);
            }

            if (cameraObject != null)
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void TryStartAttack_TriggersViewmodelAttack_WithComboIndex()
        {
            Result started = combatController.StartAttack();
            Assert.That(started.IsOk, Is.True, $"初段攻撃が正常に開始される必要があります。エラー: {started.Error}");
            Assert.That(viewmodelController.IsAttacking, Is.True, "PlayerCombatControllerの出刀開始と同時に視口武器の出刀が開始される必要があります。");
            Assert.That(viewmodelController.CurrentAttackComboIndex, Is.EqualTo(0), "第1段のコンボインデックス（0）が視口武器に伝達される必要があります。");
        }

        [Test]
        public void ComboLoop_CanRestartFirstAttack_WhileViewmodelIsInRecovery()
        {
            // 1段目開始・完了
            Assert.That(combatController.StartAttack().IsOk, Is.True);
            combatController.CompleteAttack();
            Assert.That(combatController.ComboIndex, Is.EqualTo(1));

            // 2段目開始・完了
            Assert.That(combatController.StartAttack().IsOk, Is.True);
            combatController.CompleteAttack();
            Assert.That(combatController.ComboIndex, Is.EqualTo(2));

            // 3段目開始・完了（3段目完了後はComboIndexが0に循環）
            Assert.That(combatController.StartAttack().IsOk, Is.True);
            combatController.CompleteAttack();
            Assert.That(combatController.ComboIndex, Is.EqualTo(0));

            // 3段目の収刀復帰中（viewmodelController.IsAttacking == true）であっても、
            // コンボ有効期間内であれば初段（0）を即座に再起動できる（無限ループ）
            Assert.That(viewmodelController.IsAttacking, Is.True, "3段目完了直後はViewmodelがまだ収刀動作中である必要があります。");
            Result loopRestarted = combatController.StartAttack();
            Assert.That(loopRestarted.IsOk, Is.True, $"コンボ循環後の初段再起動に失敗しました。エラー: {loopRestarted.Error}");
            Assert.That(combatController.ComboIndex, Is.EqualTo(0));
            Assert.That(viewmodelController.CurrentAttackComboIndex, Is.EqualTo(0));
        }

        [Test]
        public void CancelAttack_CancelsViewmodelAttack()
        {
            Result started = combatController.StartAttack();
            Assert.That(started.IsOk, Is.True, $"出刀開始失敗: {started.Error}");
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

        [Test]
        public void AttackWindow_ClosesDuringRetraction_PreventingDamageAfterStrike()
        {
            // 攻撃開始
            Result started = combatController.StartAttack();
            Assert.That(started.IsOk, Is.True, $"出刀開始失敗: {started.Error}");
            var sequence = combatController.CurrentAttackSequence;
            Assert.That(sequence, Is.Not.Null);

            // 1. 蓄力段階（progress = 0.10f）：窓は未開放
            viewmodelController.AttackKinetics.TriggerAttack(0, 1.0f); // 基準 0.50秒
            viewmodelController.Evaluate(0.05f); // 0.05 / 0.50 = 0.10f
            Assert.That(viewmodelController.AttackProgress, Is.EqualTo(0.10f).Within(0.01f));

            // Tick を進める
            combatController.TickAttackAnimation();
            Assert.That(sequence.IsWindowOpen, Is.False, "蓄力段階では攻撃有効ウィンドウを開いてはなりません。");

            // 2. 出刀打撃段階（progress = 0.30f）：窓が開いている
            viewmodelController.Evaluate(0.10f); // 合計 0.15秒 -> progress = 0.30f
            Assert.That(viewmodelController.AttackProgress, Is.EqualTo(0.30f).Within(0.01f));
            combatController.TickAttackAnimation();
            Assert.That(sequence.IsWindowOpen, Is.True, "出刀打撃段階では攻撃有効ウィンドウが開いている必要があります。");

            // 3. 収刀段階（progress = 0.50f）：窓が強制閉鎖され、收刀中にダメージ判定が残らない
            viewmodelController.Evaluate(0.10f); // 合計 0.25秒 -> progress = 0.50f
            Assert.That(viewmodelController.AttackProgress, Is.EqualTo(0.50f).Within(0.01f));
            combatController.TickAttackAnimation();
            Assert.That(sequence.IsWindowOpen, Is.False, "収刀段階（progress >= 0.45）では攻撃有効ウィンドウが完全に閉じている必要があります。");

            // 収刀中に接触した敵への命中登録が拒否されることを検証
            GameObject dummyEnemy = new GameObject("DummyEnemy");
            try
            {
                var enemyMarker = dummyEnemy.AddComponent<CombatantMarker>();
                enemyMarker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "DummyEnemy");
                var tracker = combatController.SwordHitbox.WindowTracker;
                Assert.That(tracker.RegisterTarget(enemyMarker).IsErr, Is.True,
                    "収刀段階ではHitbox経由の命中登録が拒否されなければなりません。");
            }
            finally
            {
                Object.DestroyImmediate(dummyEnemy);
            }
        }
    }
}

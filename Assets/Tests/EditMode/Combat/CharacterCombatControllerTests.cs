using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// CharacterCombat.controller のコンボ状態機械構造（パラメータ・ステート・遷移条件）のテストです。
    /// </summary>
    public sealed class CharacterCombatControllerTests
    {
        private const string ControllerPath = "Assets/Animations/CharacterCombat.controller";

        private AnimatorController controller;
        private AnimatorStateMachine stateMachine;

        [SetUp]
        public void SetUp()
        {
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null, $"AnimatorControllerが見つかりません: {ControllerPath}");
            stateMachine = controller.layers[0].stateMachine;
        }

        [Test]
        public void Controller_HasComboIndexParameter()
        {
            var comboParam = controller.parameters.FirstOrDefault(p => p.name == "ComboIndex");
            Assert.That(comboParam, Is.Not.Null, "CharacterCombatにComboIndexパラメータが存在しません。");
            Assert.That(comboParam.type, Is.EqualTo(AnimatorControllerParameterType.Int), "ComboIndexの型がIntではありません。");
        }

        [Test]
        public void Controller_ContainsAllThreeComboAttackStates()
        {
            var states = stateMachine.states.Select(s => s.state.name).ToArray();
            Assert.That(states, Does.Contain("Attack_Horizontal"), "Attack_Horizontalステートが存在しません。");
            Assert.That(states, Does.Contain("Attack_Vertical"), "Attack_Verticalステートが存在しません。");
            Assert.That(states, Does.Contain("Attack_Thrust"), "Attack_Thrustステートが存在しません。");
        }

        [Test]
        public void IdleAndLocomotion_TransitionToHorizontalAttackWhenComboIndexZero()
        {
            var idleState = stateMachine.states.First(s => s.state.name == "Idle").state;
            var toHorizontal = idleState.transitions.FirstOrDefault(t => t.destinationState != null && t.destinationState.name == "Attack_Horizontal");
            Assert.That(toHorizontal, Is.Not.Null, "IdleからAttack_Horizontalへの遷移が存在しません。");

            bool hasAttackTrigger = toHorizontal.conditions.Any(c => c.parameter == "AttackTrigger" && c.mode == AnimatorConditionMode.If);
            bool hasComboZero = toHorizontal.conditions.Any(c => c.parameter == "ComboIndex" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, 0f));
            Assert.That(hasAttackTrigger, Is.True, "AttackTrigger条件が設定されていません。");
            Assert.That(hasComboZero, Is.True, "ComboIndex == 0 条件が設定されていません。");
        }

        [Test]
        public void IdleAndLocomotion_TransitionToVerticalAttackWhenComboIndexOne()
        {
            var idleState = stateMachine.states.First(s => s.state.name == "Idle").state;
            var toVertical = idleState.transitions.FirstOrDefault(t => t.destinationState != null && t.destinationState.name == "Attack_Vertical");
            Assert.That(toVertical, Is.Not.Null, "IdleからAttack_Verticalへの遷移が存在しません。");

            bool hasAttackTrigger = toVertical.conditions.Any(c => c.parameter == "AttackTrigger" && c.mode == AnimatorConditionMode.If);
            bool hasComboOne = toVertical.conditions.Any(c => c.parameter == "ComboIndex" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, 1f));
            Assert.That(hasAttackTrigger, Is.True, "AttackTrigger条件が設定されていません。");
            Assert.That(hasComboOne, Is.True, "ComboIndex == 1 条件が設定されていません。");
        }

        [Test]
        public void IdleAndLocomotion_TransitionToThrustAttackWhenComboIndexTwo()
        {
            var idleState = stateMachine.states.First(s => s.state.name == "Idle").state;
            var toThrust = idleState.transitions.FirstOrDefault(t => t.destinationState != null && t.destinationState.name == "Attack_Thrust");
            Assert.That(toThrust, Is.Not.Null, "IdleからAttack_Thrustへの遷移が存在しません。");

            bool hasAttackTrigger = toThrust.conditions.Any(c => c.parameter == "AttackTrigger" && c.mode == AnimatorConditionMode.If);
            bool hasComboTwo = toThrust.conditions.Any(c => c.parameter == "ComboIndex" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, 2f));
            Assert.That(hasAttackTrigger, Is.True, "AttackTrigger条件が設定されていません。");
            Assert.That(hasComboTwo, Is.True, "ComboIndex == 2 条件が設定されていません。");
        }
    }
}

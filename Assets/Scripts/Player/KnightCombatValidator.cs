using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// Knightの戦闘コンポーネント配置、必須参照、Animatorパラメータの適合性を検証する独立検証モジュールです。
    /// 単一責任：KnightおよびPlayerCombatControllerの構成診断と契約検査。
    /// </summary>
    public static class KnightCombatValidator
    {
        /// <summary>
        /// Knightオブジェクト自体を検査し、PlayerCombatControllerの欠落や構成不備を明示します。
        /// </summary>
        public static bool ValidateKnightObject(GameObject knight, out IReadOnlyList<string> diagnostics)
        {
            var results = new List<string>();
            if (knight == null)
            {
                results.Add("KnightにPlayerCombatControllerがありません。");
            }
            else
            {
                PlayerCombatController controller = knight.GetComponent<PlayerCombatController>();
                if (controller == null)
                {
                    results.Add("KnightにPlayerCombatControllerがありません。");
                }
                else
                {
                    return ValidateRequiredReferences(controller, out diagnostics);
                }
            }

            diagnostics = results;
            foreach (string message in results)
            {
                Debug.LogError($"[PlayerCombatController診断] {message}", knight);
            }

            return false;
        }

        /// <summary>
        /// Knightの攻撃経路に必要な参照と日本語診断をまとめて検査します。
        /// </summary>
        public static bool ValidateRequiredReferences(PlayerCombatController controller, out IReadOnlyList<string> diagnostics)
        {
            var results = new List<string>();
            if (controller == null)
            {
                results.Add("PlayerCombatControllerがnullです。");
                diagnostics = results;
                return false;
            }

            if (controller.InputReader == null)
            {
                results.Add("PlayerCombatControllerのInputReader参照がありません。");
            }

            Animator targetAnimator = controller.TargetAnimator;
            if (targetAnimator == null)
            {
                results.Add("PlayerCombatControllerのAnimator参照がありません。");
            }
            else
            {
                if (!targetAnimator.isActiveAndEnabled)
                {
                    results.Add("PlayerCombatControllerのAnimatorが有効ではありません。");
                }

                if (targetAnimator.runtimeAnimatorController == null)
                {
                    results.Add("PlayerCombatControllerのAnimator Controller参照がありません。");
                }

                if (!HasAnimatorParameter(targetAnimator, "AttackTrigger", AnimatorControllerParameterType.Trigger))
                {
                    results.Add("KnightのAttackTriggerがAnimatorにありません。");
                }
            }

            if (controller.AnimationDriver == null)
            {
                results.Add("PlayerCombatControllerのPlayerAnimationDriver参照がありません。");
            }

            if (controller.GameFlowController == null)
            {
                results.Add("PlayerCombatControllerのGameFlow参照がありません。");
            }

            if (controller.DamageService == null)
            {
                results.Add("PlayerCombatControllerのDamageService参照がありません。");
            }

            if (controller.CombatantMarker == null)
            {
                results.Add("PlayerCombatControllerのCombatantMarker参照がありません。");
            }

            CombatHitbox swordHitbox = controller.SwordHitbox;
            if (swordHitbox == null)
            {
                results.Add("PlayerCombatControllerのSwordHitbox参照がありません。");
            }
            else
            {
                if (swordHitbox.gameObject.name != "SwordHitbox")
                {
                    results.Add("KnightのSwordHitboxオブジェクトが見つかりません。SwordSocket配下の名前をSwordHitboxにしてください。");
                }

                Collider collider = swordHitbox.GetComponent<Collider>();
                if (collider == null || !collider.isTrigger)
                {
                    results.Add("SwordHitboxがTrigger Colliderではありません。");
                }
            }

            diagnostics = results;
            return results.Count == 0;
        }

        private static void AddDiagnostics(List<string> destination, IReadOnlyList<string> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (string message in source)
            {
                if (!string.IsNullOrEmpty(message) && !destination.Contains(message))
                {
                    destination.Add(message);
                }
            }
        }

        private static bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType type)
        {
            if (animator == null)
            {
                return false;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == parameterName && parameter.type == type)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

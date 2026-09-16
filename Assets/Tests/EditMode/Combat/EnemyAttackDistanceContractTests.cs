using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 敵の停止距離、近接攻撃距離、急停止制動、攻撃判定盒の幾何到達範囲を検証し、
    /// プレイヤーモデルとの重なり（穿孔）が発生しない安全間距契約を保証します。
    /// </summary>
    public sealed class EnemyAttackDistanceContractTests
    {
        [Test]
        public void EnemyBrainAndMotor_DefaultStoppingDistance_PreventsModelOverlap()
        {
            GameObject enemyObj = new GameObject("TestEnemyDist");
            try
            {
                EnemyMotor motor = enemyObj.AddComponent<EnemyMotor>();
                EnemyBrain brain = enemyObj.AddComponent<EnemyBrain>();

                Assert.That(motor.ConfiguredStoppingDistance, Is.GreaterThanOrEqualTo(2.0f),
                    "EnemyMotorのデフォルト停止距離はプレイヤーとの人体重なりを防ぐため2.0m以上である必要があります。");
                Assert.That(brain.ConfiguredStoppingDistance, Is.GreaterThanOrEqualTo(2.0f),
                    "EnemyBrainのデフォルト停止距離は2.0m以上である必要があります。");
            }
            finally
            {
                Object.DestroyImmediate(enemyObj);
            }
        }

        [Test]
        public void EnemyMeleeCombat_DefaultAttackRange_CoversCombatDistance()
        {
            GameObject enemyObj = new GameObject("TestEnemyCombatRange");
            try
            {
                EnemyMeleeCombat combat = enemyObj.AddComponent<EnemyMeleeCombat>();

                Assert.That(combat.AttackRange, Is.GreaterThanOrEqualTo(2.4f),
                    "EnemyMeleeCombatのデフォルト攻撃範囲は安全停止距離（2.1m）からの斬撃を許可するため2.4m以上である必要があります。");
            }
            finally
            {
                Object.DestroyImmediate(enemyObj);
            }
        }

        [Test]
        public void EnemyPrefab_HitboxAndStoppingDistance_MaintainsSafetyGap()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab");
            Assert.That(prefab, Is.Not.Null, "Assets/Prefabs/KayKitBattle/Enemy_Melee.prefabが見つかりません。");

            EnemyBrain brain = prefab.GetComponent<EnemyBrain>();
            EnemyMotor motor = prefab.GetComponent<EnemyMotor>();
            EnemyMeleeCombat combat = prefab.GetComponent<EnemyMeleeCombat>();
            CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
            CombatHitbox hitbox = prefab.GetComponentsInChildren<CombatHitbox>(true).FirstOrDefault();

            Assert.That(brain, Is.Not.Null, "Enemy_Melee.prefabにEnemyBrainがありません。");
            Assert.That(motor, Is.Not.Null, "Enemy_Melee.prefabにEnemyMotorがありません。");
            Assert.That(combat, Is.Not.Null, "Enemy_Melee.prefabにEnemyMeleeCombatがありません。");
            Assert.That(capsule, Is.Not.Null, "Enemy_Melee.prefabにCapsuleColliderがありません。");
            Assert.That(hitbox, Is.Not.Null, "Enemy_Melee.prefabにCombatHitboxがありません。");

            UnityEditor.SerializedObject brainSo = new UnityEditor.SerializedObject(brain);
            float meleeRange = brainSo.FindProperty("meleeRange").floatValue;
            float stoppingDist = brainSo.FindProperty("configuredStoppingDistance").floatValue;

            UnityEditor.SerializedObject combatSo = new UnityEditor.SerializedObject(combat);
            float attackRange = combatSo.FindProperty("attackRange").floatValue;

            Assert.That(stoppingDist, Is.GreaterThanOrEqualTo(2.0f), "Enemy_Melee.prefabの停止距離は2.0m以上である必要があります。");
            Assert.That(meleeRange, Is.GreaterThanOrEqualTo(2.2f), "Enemy_Melee.prefabの近接判定距離は2.2m以上である必要があります。");
            Assert.That(attackRange, Is.GreaterThanOrEqualTo(2.4f), "Enemy_Melee.prefabの攻撃範囲は2.4m以上である必要があります。");
            Assert.That(capsule.radius, Is.GreaterThanOrEqualTo(0.40f), "Enemy_Melee.prefabの衝突半径はモデル保護のため0.40m以上である必要があります。");

            BoxCollider box = hitbox.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null, "CombatHitboxにBoxColliderがありません。");

            // WeaponSocketのZ位置 + BoxColliderの中心Z + サイズZ/2 で前方最大到達距離を検証
            Transform weaponSocket = hitbox.transform.parent != null && hitbox.transform.parent.name == "WeaponVisual"
                ? hitbox.transform.parent.parent
                : hitbox.transform.parent;
            float forwardReach = (weaponSocket != null ? weaponSocket.localPosition.z : 0f) + box.center.z + (box.size.z * 0.5f);
            Assert.That(forwardReach, Is.GreaterThanOrEqualTo(2.1f),
                $"EnemyHitboxの前方到達距離（{forwardReach:F2}m）は敵の停止距離（{stoppingDist:F2}m）の目標へ届くよう2.1m以上である必要があります。");
        }
    }
}

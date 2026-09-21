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

            Assert.That(combat.AttackConfig, Is.Not.Null, "Enemy_Melee.prefabのEnemyMeleeCombatにAttackConfigが割り当てられていません。");
            float attackRange = combat.AttackRange;

            Assert.That(stoppingDist, Is.GreaterThanOrEqualTo(2.0f), "Enemy_Melee.prefabの停止距離は2.0m以上である必要があります。");
            Assert.That(meleeRange, Is.GreaterThanOrEqualTo(2.2f), "Enemy_Melee.prefabの近接判定距離は2.2m以上である必要があります。");
            Assert.That(attackRange, Is.GreaterThanOrEqualTo(2.4f), "Enemy_Melee.prefabの攻撃範囲は2.4m以上である必要があります。");
            Assert.That(capsule.radius, Is.GreaterThanOrEqualTo(0.40f), "Enemy_Melee.prefabの衝突半径はモデル保護のため0.40m以上である必要があります。");

            BoxCollider box = hitbox.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null, "CombatHitboxにBoxColliderがありません。");

            // WeaponSocketがhandslot.rに接続された状態で、Throw出刀モーション中の前方到達距離を検証
            var modelRoot = prefab.transform.Find("ModelRoot").gameObject;
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/KayKit/Animations/fbx/Rig_Medium/Rig_Medium_General.fbx");
            AnimationClip throwAnim = null;
            foreach (var a in assets)
            {
                if (a is AnimationClip c && c.name == "Throw") throwAnim = c;
            }
            Assert.That(throwAnim, Is.Not.Null, "Throwアニメーションclipが見つかりません。");

            float maxForwardReach = float.MinValue;
            for (float t = 0; t <= throwAnim.length; t += 0.05f)
            {
                throwAnim.SampleAnimation(modelRoot, t);
                Vector3 c = box.center;
                Vector3 ext = box.size * 0.5f;
                Vector3[] corners = new[]
                {
                    c + new Vector3(ext.x, ext.y, ext.z),
                    c + new Vector3(ext.x, ext.y, -ext.z),
                    c + new Vector3(-ext.x, ext.y, ext.z),
                    c + new Vector3(-ext.x, ext.y, -ext.z)
                };
                foreach (var corner in corners)
                {
                    Vector3 worldPt = hitbox.transform.TransformPoint(corner);
                    Vector3 localPt = prefab.transform.InverseTransformPoint(worldPt);
                    if (localPt.z > maxForwardReach) maxForwardReach = localPt.z;
                }
            }

            Assert.That(maxForwardReach, Is.GreaterThanOrEqualTo(2.1f),
                $"EnemyHitboxの出刀時最大前方到達距離（{maxForwardReach:F2}m）は敵の停止距離（{stoppingDist:F2}m）の目標へ届くよう2.1m以上である必要があります。");
        }
    }
}

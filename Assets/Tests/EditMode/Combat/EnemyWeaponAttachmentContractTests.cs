using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 敵の武器骨骼挂載（axe_1handed）および攻撃出刀時の右腕表示安定性に関する契約テストです。
    /// 敵が手持ち戦斧を自然に保持し、攻撃時に第一人称横薙ぎ（Slash_Horizontal）へ誤遷移せず
    /// 野蛮人の力劈（Throw）を再生して右腕がモデル体腔内へ陥没消失しないことを検証します。
    /// </summary>
    public sealed class EnemyWeaponAttachmentContractTests
    {
        private GameObject enemyInstance;

        [TearDown]
        public void TearDown()
        {
            if (enemyInstance != null)
            {
                Object.DestroyImmediate(enemyInstance);
            }
        }

        [Test]
        public void EnemyPrefab_WeaponAttachedToHandslotR_WithAxeModelAndMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab");
            Assert.That(prefab, Is.Not.Null, "Enemy_Melee.prefabが見つかりません。");

            Transform handslotR = prefab.transform.Find("ModelRoot/Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r/handslot.r");
            Assert.That(handslotR, Is.Not.Null, "敵モデル内にhandslot.r骨骼ノードが見つかりません。");

            Transform weaponSocket = handslotR.Find("WeaponSocket");
            Assert.That(weaponSocket, Is.Not.Null, "WeaponSocketがhandslot.rの直下に接続されていません。");

            Transform weaponVisual = weaponSocket.Find("WeaponVisual");
            Assert.That(weaponVisual, Is.Not.Null, "WeaponSocket配下にWeaponVisualが見つかりません。");

            MeshFilter mf = weaponVisual.GetComponent<MeshFilter>();
            Assert.That(mf, Is.Not.Null, "WeaponVisualにMeshFilterがありません。");
            Assert.That(mf.sharedMesh, Is.Not.Null, "WeaponVisualにsharedMeshが設定されていません。");
            Assert.That(mf.sharedMesh.name, Is.EqualTo("axe_1handed"), "敵の武器は野蛮人専用のaxe_1handedである必要があります。");
            Assert.That(mf.sharedMesh.bounds.size.magnitude, Is.GreaterThan(0.5f), "武器メッシュが微縮（1cm以下など）されていないことを検証します。");

            MeshRenderer mr = weaponVisual.GetComponent<MeshRenderer>();
            Assert.That(mr, Is.Not.Null, "WeaponVisualにMeshRendererがありません。");
            Assert.That(mr.sharedMaterial, Is.Not.Null, "WeaponVisualにマテリアルが割り当てられていません。");
            Assert.That(mr.sharedMaterial.name, Does.Contain("barbarian"), "武器マテリアルは野蛮人専用barbarian材質である必要があります。");

            Transform hitbox = weaponVisual.Find("EnemyHitbox");
            Assert.That(hitbox, Is.Not.Null, "WeaponVisual配下にEnemyHitboxが見つかりません。");
            CombatHitbox combatHitbox = hitbox.GetComponent<CombatHitbox>();
            Assert.That(combatHitbox, Is.Not.Null, "EnemyHitboxにCombatHitboxコンポーネントがありません。");

            BoxCollider box = hitbox.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null, "EnemyHitboxにBoxColliderがありません。");
            Assert.That(box.isTrigger, Is.True, "EnemyHitboxのBoxColliderはisTrigger=trueである必要があります。");
        }

        [Test]
        public void EnemyAnimator_AttackRouting_TransitionsToThrow_NotSlashHorizontal()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CharacterCombat.controller");
            Assert.That(controller, Is.Not.Null, "CharacterCombat.controllerが見つかりません。");

            enemyInstance = new GameObject("Test_EnemyRouting");
            var animator = enemyInstance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            // 敵Animatorフラグを設定
            animator.SetBool("IsEnemy", true);
            animator.Update(0.1f);

            // 攻撃トリガー発火
            animator.SetTrigger("AttackTrigger");
            animator.Update(0.01f);

            // 遷移先がAttack（Throw）であり、プレイヤー専用のAttack_Horizontalではないことを検証
            if (animator.IsInTransition(0))
            {
                AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
                Assert.That(nextState.IsName("Attack"), Is.True, "敵攻撃の遷移先はAttack（Throw）である必要があります。");
                Assert.That(nextState.IsName("Attack_Horizontal"), Is.False, "敵攻撃がプレイヤー用Attack_Horizontalへ誤遷移してはいけません。");
            }

            animator.Update(0.06f);
            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(currentState.IsName("Attack"), Is.True, "遷移完了後の敵攻撃状態はAttackである必要があります。");
        }

        [Test]
        public void EnemyAttackAnimation_RightArmRemainsVisibleAndExtruded_ThroughoutThrow()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab");
            enemyInstance = Object.Instantiate(prefab);

            var armRight = enemyInstance.transform.Find("ModelRoot/Barbarian_ArmRight")?.GetComponent<SkinnedMeshRenderer>();
            Assert.That(armRight, Is.Not.Null, "Barbarian_ArmRightが見つかりません。");
            Assert.That(armRight.updateWhenOffscreen, Is.True, "右腕SMRのupdateWhenOffscreenは視錐台カリング防止のためtrueである必要があります。");

            var wrist = enemyInstance.transform.Find("ModelRoot/Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r");
            var hand = enemyInstance.transform.Find("ModelRoot/Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r");

            var modelRoot = enemyInstance.transform.Find("ModelRoot").gameObject;
            var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/KayKit/Animations/fbx/Rig_Medium/Rig_Medium_General.fbx");
            AnimationClip throwAnim = null;
            foreach (var a in assets)
            {
                if (a is AnimationClip c && c.name == "Throw") throwAnim = c;
            }
            Assert.That(throwAnim, Is.Not.Null, "Throwアニメーションが見つかりません。");

            // 下劈劈砍ピーク（t=0.7s 〜 1.0s）において、手首・手骨がキャラクター前方かつ右側に維持され、体内に内折しないことを検証
            for (float t = 0.70f; t <= 1.00f; t += 0.05f)
            {
                throwAnim.SampleAnimation(modelRoot, t);
                Vector3 localWrist = enemyInstance.transform.InverseTransformPoint(wrist.position);
                Vector3 localHand = enemyInstance.transform.InverseTransformPoint(hand.position);

                Assert.That(localWrist.z, Is.GreaterThan(0.20f),
                    $"出刀ピーク時（t={t:F2}s）に右手首が敵前方（Z > 0.2m）に突き出している必要があります。実際: Z={localWrist.z:F2}m");
                Assert.That(localHand.z, Is.GreaterThan(0.20f),
                    $"出刀ピーク時（t={t:F2}s）に右手が敵前方（Z > 0.2m）に突き出している必要があります。実際: Z={localHand.z:F2}m");
                Assert.That(localWrist.x, Is.GreaterThanOrEqualTo(0.0f),
                    $"出刀ピーク時（t={t:F2}s）に右腕が左半身や体内へ内折（X < 0）していないことを検証します。実際: X={localWrist.x:F2}m");
            }
        }
    }
}

using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視口武器の刀光トレイルおよび刀身発光（Blade Glow / Emission）を制御する独立モジュールです。
    /// 単一責任：武器の視覚エフェクト（TrailRenderer と MaterialPropertyBlock）の管理。
    /// </summary>
    [Serializable]
    public sealed class ViewmodelBladeVisuals
    {
        [Header("剣撃エフェクト (Sword Effects)")]
        [Tooltip("視口武器の刀光トレイル。未設定時は自動検索します。")]
        [SerializeField]
        private TrailRenderer swordTrail;

        [Tooltip("攻撃中の刀身流光発光色（HDR）。")]
        [SerializeField]
        private Color bladeGlowColor = new Color(2.5f, 2.2f, 1.4f, 1f);

        private Renderer swordRenderer;
        private MaterialPropertyBlock bladePropertyBlock;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public TrailRenderer SwordTrail => swordTrail;
        public Color BladeGlowColor => bladeGlowColor;

        /// <summary>
        /// 階層下のRendererおよびTrailRenderer参照を解決・初期化します。
        /// </summary>
        public void ResolveVisualReferences(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            if (swordTrail == null)
            {
                swordTrail = root.GetComponentInChildren<TrailRenderer>(true);
            }
            if (swordTrail != null)
            {
                swordTrail.emitting = false;
            }

            if (swordRenderer == null)
            {
                swordRenderer = root.GetComponentInChildren<Renderer>(true);
            }
            if (swordRenderer != null)
            {
                var mats = swordRenderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && !m.IsKeywordEnabled("_EMISSION"))
                    {
                        m.EnableKeyword("_EMISSION");
                    }
                }
            }
            if (bladePropertyBlock == null)
            {
                bladePropertyBlock = new MaterialPropertyBlock();
            }
        }

        /// <summary>
        /// 出刀開始時にトレイルをクリア・発光開始します。
        /// </summary>
        public void OnAttackStarted()
        {
            if (swordTrail != null)
            {
                swordTrail.Clear();
                swordTrail.emitting = true;
            }
        }

        /// <summary>
        /// 攻撃進行度（0〜1）に応じた刀身流光を更新します。
        /// </summary>
        public void UpdateBladeGlow(float progress)
        {
            if (swordRenderer == null)
            {
                return;
            }

            if (bladePropertyBlock == null)
            {
                bladePropertyBlock = new MaterialPropertyBlock();
            }

            float glowFactor = 0f;
            if (progress >= 0.10f && progress <= 0.45f)
            {
                if (progress < 0.20f)
                {
                    glowFactor = (progress - 0.10f) / 0.10f;
                }
                else if (progress <= 0.35f)
                {
                    glowFactor = 1f;
                }
                else
                {
                    glowFactor = (0.45f - progress) / 0.10f;
                }
            }

            swordRenderer.GetPropertyBlock(bladePropertyBlock);
            bladePropertyBlock.SetColor(EmissionColorId, bladeGlowColor * glowFactor);
            swordRenderer.SetPropertyBlock(bladePropertyBlock);
        }

        /// <summary>
        /// 刀身流光を消灯・リセットします。
        /// </summary>
        public void ResetBladeGlow()
        {
            if (swordRenderer == null)
            {
                return;
            }

            if (bladePropertyBlock == null)
            {
                bladePropertyBlock = new MaterialPropertyBlock();
            }

            bladePropertyBlock.Clear();
            swordRenderer.SetPropertyBlock(bladePropertyBlock);
        }

        /// <summary>
        /// 出刀完了または中断時にエフェクトを消灯します。
        /// </summary>
        public void OnAttackEnded()
        {
            if (swordTrail != null)
            {
                swordTrail.emitting = false;
            }
            ResetBladeGlow();
        }

        /// <summary>
        /// コンポーネント無効化時のクリーンアップです。
        /// </summary>
        public void OnDisabled()
        {
            if (swordTrail != null)
            {
                swordTrail.emitting = false;
            }
            ResetBladeGlow();
        }
    }
}

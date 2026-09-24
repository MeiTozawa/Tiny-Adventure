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
        private Color bladeGlowColor;

        [Header("刀身発光タイミング")]
        [Tooltip("刀身流光の開始進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowStartProgress;

        [Tooltip("刀身流光が最大発光に達する進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowPeakStartProgress;

        [Tooltip("刀身流光の最大発光維持が終了する進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowPeakEndProgress;

        [Tooltip("刀身流光が完全に消灯する終了進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowEndProgress;

        [SerializeField]
        private Renderer swordRenderer;

        private MaterialPropertyBlock bladePropertyBlock;
        private MaterialPropertyBlock PropertyBlock => bladePropertyBlock ??= new MaterialPropertyBlock();
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public TrailRenderer SwordTrail => swordTrail;
        public Renderer SwordRenderer => swordRenderer;
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
            float glowFactor = 0f;
            if (progress >= glowStartProgress && progress <= glowEndProgress)
            {
                if (progress < glowPeakStartProgress && glowPeakStartProgress > glowStartProgress)
                {
                    glowFactor = (progress - glowStartProgress) / (glowPeakStartProgress - glowStartProgress);
                }
                else if (progress <= glowPeakEndProgress)
                {
                    glowFactor = 1f;
                }
                else if (glowEndProgress > glowPeakEndProgress)
                {
                    glowFactor = (glowEndProgress - progress) / (glowEndProgress - glowPeakEndProgress);
                }
            }

            MaterialPropertyBlock block = PropertyBlock;
            swordRenderer.GetPropertyBlock(block);
            block.SetColor(EmissionColorId, bladeGlowColor * glowFactor);
            swordRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// 刀身流光を消灯・リセットします。
        /// </summary>
        public void ResetBladeGlow()
        {
            MaterialPropertyBlock block = PropertyBlock;
            block.Clear();
            swordRenderer.SetPropertyBlock(block);
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

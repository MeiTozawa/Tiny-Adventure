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

        [Header("刀身発光タイミング")]
        [Tooltip("刀身流光の開始進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowStartProgress = 0.10f;

        [Tooltip("刀身流光が最大発光に達する進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowPeakStartProgress = 0.20f;

        [Tooltip("刀身流光の最大発光維持が終了する進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowPeakEndProgress = 0.35f;

        [Tooltip("刀身流光が完全に消灯する終了進行度です。")]
        [SerializeField, Range(0f, 1f)]
        private float glowEndProgress = 0.45f;

        private static readonly Color DefaultBladeGlowColor = new Color(2.5f, 2.2f, 1.4f, 1f);

        private Renderer swordRenderer;

        private MaterialPropertyBlock bladePropertyBlock;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private bool isInitialized;

        public TrailRenderer SwordTrail => swordTrail;
        public Renderer SwordRenderer => swordRenderer;
        public Color BladeGlowColor => bladeGlowColor;

        /// <summary>
        /// 外部宿主から渡されたRendererおよびTrailRendererでモジュールを初期化します。
        /// </summary>
        public void Initialize(Renderer renderer, TrailRenderer trail)
        {
            if (isInitialized) return;
            isInitialized = true;

            swordRenderer = renderer;
            swordTrail = trail;

            bladePropertyBlock = new MaterialPropertyBlock();
            swordTrail.emitting = false;

            var mats = swordRenderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m.IsKeywordEnabled("_EMISSION"))
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
            swordTrail.Clear();
            swordTrail.emitting = true;
        }

        /// <summary>
        /// 攻撃進行度（0〜1）に応じた刀身流光を更新します。
        /// </summary>
        public void UpdateBladeGlow(float progress)
        {
            float start = glowStartProgress > 0f || glowEndProgress > 0f ? glowStartProgress : 0.10f;
            float peakStart = glowPeakStartProgress > start ? glowPeakStartProgress : 0.20f;
            float peakEnd = glowPeakEndProgress >= peakStart ? glowPeakEndProgress : 0.35f;
            float end = glowEndProgress > peakEnd ? glowEndProgress : 0.45f;
            Color color = (bladeGlowColor.r > 0.01f || bladeGlowColor.g > 0.01f || bladeGlowColor.b > 0.01f)
                ? bladeGlowColor
                : DefaultBladeGlowColor;

            float glowFactor = 0f;
            if (progress >= start && progress <= end)
            {
                if (progress < peakStart && peakStart > start)
                {
                    glowFactor = (progress - start) / (peakStart - start);
                }
                else if (progress <= peakEnd)
                {
                    glowFactor = 1f;
                }
                else if (end > peakEnd)
                {
                    glowFactor = (end - progress) / (end - peakEnd);
                }
            }

            bladePropertyBlock.SetColor(EmissionColorId, color * glowFactor);
            swordRenderer.SetPropertyBlock(bladePropertyBlock);
        }

        /// <summary>
        /// 刀身流光を消灯・リセットします。
        /// </summary>
        public void ResetBladeGlow()
        {
            bladePropertyBlock.Clear();
            swordRenderer.SetPropertyBlock(bladePropertyBlock);
        }

        /// <summary>
        /// 出刀完了または中断時にエフェクトを消灯します。
        /// </summary>
        public void OnAttackEnded()
        {
            swordTrail.emitting = false;
            ResetBladeGlow();
        }

        /// <summary>
        /// コンポーネント無効化時のクリーンアップです。
        /// </summary>
        public void OnDisabled()
        {
            swordTrail.emitting = false;
            ResetBladeGlow();
        }
    }
}

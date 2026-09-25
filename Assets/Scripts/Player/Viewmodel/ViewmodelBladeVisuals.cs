using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 刀身視覚効果（流光・発光・タイミング）の設定データ構造です。
    /// </summary>
    [Serializable]
    public struct BladeVisualsConfig
    {
        [Tooltip("攻撃中の刀身流光発光色（HDR）。")]
        public Color bladeGlowColor;

        [Tooltip("刀身流光の開始進行度です。")]
        [Range(0f, 1f)]
        public float glowStartProgress;

        [Tooltip("刀身流光が最大発光に達する進行度です。")]
        [Range(0f, 1f)]
        public float glowPeakStartProgress;

        [Tooltip("刀身流光の最大発光維持が終了する進行度です。")]
        [Range(0f, 1f)]
        public float glowPeakEndProgress;

        [Tooltip("刀身流光が完全に消灯する終了進行度です。")]
        [Range(0f, 1f)]
        public float glowEndProgress;

        public static BladeVisualsConfig Default => new()
        {
            bladeGlowColor = new Color(2.5f, 2.2f, 1.4f, 1f),
            glowStartProgress = 0.10f,
            glowPeakStartProgress = 0.20f,
            glowPeakEndProgress = 0.35f,
            glowEndProgress = 0.45f
        };
    }

    /// <summary>
    /// 第一人称視口武器の刀光トレイルおよび刀身発光（Blade Glow / Emission）を制御する純粋ロジックモジュールです。
    /// 単一責任：武器の視覚エフェクト（TrailRenderer と MaterialPropertyBlock）の管理。
    /// </summary>
    public sealed class ViewmodelBladeVisuals
    {
        private static readonly Color DefaultBladeGlowColor = new Color(2.5f, 2.2f, 1.4f, 1f);
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private TrailRenderer swordTrail;
        private Renderer swordRenderer;
        private BladeVisualsConfig config = BladeVisualsConfig.Default;
        private MaterialPropertyBlock bladePropertyBlock;
        private bool isInitialized;

        public TrailRenderer SwordTrail => swordTrail;
        public Renderer SwordRenderer => swordRenderer;
        public Color BladeGlowColor => config.bladeGlowColor;

        /// <summary>
        /// 外部宿主から渡されたRenderer、TrailRenderer、および設定データでモジュールを初期化します。
        /// </summary>
        public void Initialize(Renderer renderer, TrailRenderer trail, BladeVisualsConfig config = default)
        {
            if (isInitialized) return;
            isInitialized = true;

            swordRenderer = renderer;
            swordTrail = trail;
            if (!config.Equals(default(BladeVisualsConfig)))
            {
                this.config = config;
            }

            bladePropertyBlock = new MaterialPropertyBlock();
            swordTrail.emitting = false;

            var mats = swordRenderer.sharedMaterials;
            foreach (var m in mats)
            {
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
            float start = config.glowStartProgress > 0f || config.glowEndProgress > 0f ? config.glowStartProgress : 0.10f;
            float peakStart = config.glowPeakStartProgress > start ? config.glowPeakStartProgress : 0.20f;
            float peakEnd = config.glowPeakEndProgress >= peakStart ? config.glowPeakEndProgress : 0.35f;
            float end = config.glowEndProgress > peakEnd ? config.glowEndProgress : 0.45f;
            Color color = (config.bladeGlowColor.r > 0.01f || config.bladeGlowColor.g > 0.01f || config.bladeGlowColor.b > 0.01f)
                ? config.bladeGlowColor
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

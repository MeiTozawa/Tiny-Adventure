using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾瞬態閃光（Hit Flash）受信コンポーネント。
    /// MaterialPropertyBlock を用いて配下の全 Renderer（SkinnedMeshRenderer、MeshRenderer）の
    /// _EmissionColor を瞬態的に発光させ、指定時間後に確実に復帰させます。
    /// マテリアル複製を行わないため、ゼロリークおよび 0 GC アロケーションを保証します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitFlashReceiver : MonoBehaviour, IHitFlashReceiver
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("発光パラメータ")]
        [Tooltip("通常ヒット時の発光継続時間（秒、非スケール実時間）。")]
        [SerializeField, Min(0.01f)]
        private float normalFlashDuration;

        [Tooltip("致命ヒット時の発光継続時間（秒、非スケール実時間）。")]
        [SerializeField, Min(0.01f)]
        private float lethalFlashDuration;

        [Tooltip("通常ヒット時の発光色（HDR）。")]
        [SerializeField]
        private Color normalFlashColor;

        [Tooltip("致命ヒット時の発光色（HDR）。")]
        [SerializeField]
        private Color lethalFlashColor;

        private Renderer[] renderers;
        private MaterialPropertyBlock propertyBlock;
        private bool isFlashing;
        private float flashTimer;

        public bool IsFlashing => isFlashing;
        public float NormalFlashDuration => normalFlashDuration;
        public float LethalFlashDuration => lethalFlashDuration;
        public Color NormalFlashColor => normalFlashColor;
        public Color LethalFlashColor => lethalFlashColor;

        private void Awake()
        {
            propertyBlock ??= new MaterialPropertyBlock();
            ResolveRenderers();
            EnsureEmissionKeywords();
            enabled = false;

            if (TryGetComponent<CombatantMarker>(out var marker))
            {
                marker.HitFeedbackReceived += OnHitFeedbackReceived;
            }
        }

        private void OnDestroy()
        {
            if (TryGetComponent<CombatantMarker>(out var marker))
            {
                marker.HitFeedbackReceived -= OnHitFeedbackReceived;
            }
        }

        private void OnHitFeedbackReceived(CombatFeedbackRequest request)
        {
            TriggerFlash(request.HitType);
        }

        private void OnEnable()
        {
            ResolveRenderers();
            EnsureEmissionKeywords();
        }

        private void Update()
        {
            flashTimer -= Time.unscaledDeltaTime;
            if (flashTimer <= 0f)
            {
                ResetFlash();
            }
        }

        private void OnDisable()
        {
            ResetFlash();
        }

        /// <summary>
        /// ヒットタイプに応じた瞬態発光をトリガーします。
        /// </summary>
        public void TriggerFlash(CombatHitType hitType)
        {
            float duration = hitType == CombatHitType.Lethal ? lethalFlashDuration : normalFlashDuration;
            Color color = hitType == CombatHitType.Lethal ? lethalFlashColor : normalFlashColor;
            TriggerFlash(duration, color);
        }

        /// <summary>
        /// 指定の持続時間と色で瞬態発光をトリガーします。
        /// </summary>
        public void TriggerFlash(float duration, Color color)
        {
            ResolveRenderers();
            if (renderers == null || renderers.Length == 0) return;

            EnsureEmissionKeywords();

            propertyBlock.SetColor(EmissionColorId, color);

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                r?.SetPropertyBlock(propertyBlock);
            }

            isFlashing = true;
            flashTimer = Mathf.Max(0.01f, duration);
            enabled = true;
        }

        /// <summary>
        /// 発光状態をリセットし、プロパティブロックを消去します（ゼロリーク保証）。
        /// </summary>
        public void ResetFlash()
        {
            if (!isFlashing) return;

            isFlashing = false;
            flashTimer = 0f;

            if (renderers != null)
            {
                propertyBlock.Clear();

                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    r?.SetPropertyBlock(propertyBlock);
                }
            }

            if (enabled)
            {
                enabled = false;
            }
        }

        /// <summary>
        /// レンダラーと設定を設定します。
        /// </summary>
        public void Configure(
            Renderer[] customRenderers,
            float normalDur,
            float lethalDur,
            Color normalColor = default,
            Color lethalColor = default)
        {
            renderers = customRenderers;
            normalFlashDuration = normalDur;
            lethalFlashDuration = lethalDur;
            if (normalColor != default) normalFlashColor = normalColor;
            if (lethalColor != default) lethalFlashColor = lethalColor;
            propertyBlock ??= new MaterialPropertyBlock();
            EnsureEmissionKeywords();
            ResetFlash();
        }

        private void ResolveRenderers()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private void EnsureEmissionKeywords()
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int j = 0; j < mats.Length; j++)
                {
                    var m = mats[j];
                    if (m != null && !m.IsKeywordEnabled("_EMISSION"))
                    {
                        m.EnableKeyword("_EMISSION");
                    }
                }
            }
        }
    }
}

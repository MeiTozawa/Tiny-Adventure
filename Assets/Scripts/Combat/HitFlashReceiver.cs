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
    public sealed class HitFlashReceiver : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("発光パラメータ")]
        [Tooltip("通常ヒット時の発光継続時間（秒、非スケール実時間）。")]
        [SerializeField, Min(0.01f)]
        private float normalFlashDuration = 0.04f;

        [Tooltip("致命ヒット時の発光継続時間（秒、非スケール実時間）。")]
        [SerializeField, Min(0.01f)]
        private float lethalFlashDuration = 0.08f;

        [Tooltip("通常ヒット時の発光色（HDR）。")]
        [SerializeField]
        private Color normalFlashColor = new Color(1.8f, 1.8f, 1.8f, 1f);

        [Tooltip("致命ヒット時の発光色（HDR）。")]
        [SerializeField]
        private Color lethalFlashColor = new Color(2.5f, 0.8f, 0.8f, 1f);

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
            ResolveRenderers();
            propertyBlock ??= new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (!isFlashing) return;

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

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.SetColor(EmissionColorId, color);

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null)
                {
                    r.SetPropertyBlock(propertyBlock);
                }
            }

            isFlashing = true;
            flashTimer = Mathf.Max(0.01f, duration);
        }

        /// <summary>
        /// 発光状態をリセットし、プロパティブロックを消去します（ゼロリーク保証）。
        /// </summary>
        public void ResetFlash()
        {
            if (!isFlashing && propertyBlock == null) return;

            isFlashing = false;
            flashTimer = 0f;

            if (renderers != null)
            {
                propertyBlock ??= new MaterialPropertyBlock();
                propertyBlock.Clear();

                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r != null)
                    {
                        r.SetPropertyBlock(propertyBlock);
                    }
                }
            }
        }

        /// <summary>
        /// テスト用にレンダラーと設定を注入します。
        /// </summary>
        public void ConfigureForTests(Renderer[] customRenderers, float normalDur = 0.04f, float lethalDur = 0.08f)
        {
            renderers = customRenderers;
            normalFlashDuration = normalDur;
            lethalFlashDuration = lethalDur;
            propertyBlock ??= new MaterialPropertyBlock();
            ResetFlash();
        }

        private void ResolveRenderers()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
        }
    }
}

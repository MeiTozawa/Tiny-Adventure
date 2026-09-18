using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 剣撃軌跡（トレイル）コントローラー。
    /// 攻撃モーション中にTrailRendererを有効化し、攻撃終了または中断時に停止します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwordTrailController : MonoBehaviour
    {
        [SerializeField]
        private TrailRenderer trailRenderer;

        public bool IsEmitting => trailRenderer != null && trailRenderer.emitting;

        private void Awake()
        {
            ResolveReferences();
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
            }
        }

        /// <summary>トレイル放出を開始します。</summary>
        public void BeginTrail(AttackFeedbackContext context)
        {
            ResolveReferences();
            if (trailRenderer != null)
            {
                trailRenderer.emitting = true;
            }
        }

        /// <summary>トレイル放出を停止します。</summary>
        public void EndTrail()
        {
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
            }
        }

        /// <summary>実行時状態をクリアし、トレイル描画をリセットします。</summary>
        public void ClearRuntimeState()
        {
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
                trailRenderer.Clear();
            }
        }

        private void ResolveReferences()
        {
            if (trailRenderer == null)
            {
                trailRenderer = GetComponentInChildren<TrailRenderer>(true);
            }
        }
    }
}

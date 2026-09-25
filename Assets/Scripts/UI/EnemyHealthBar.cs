using System;
using UnityEngine;
using UnityEngine.UI;

namespace TinyAdventure
{
    /// <summary>
    /// 敵の頭上に表示されるWorld-Space動的HPバー。
    /// 翡翠緑の即時残量バーと、黄白流光の受撃緩衝バー（ダメージディレイ追従）による二層構造を備え、
    /// プレイヤーカメラへのビルボード追従および受撃時のみのスムーズなフェードイン・フェードアウトを制御します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("監視対象のHealthComponent。")]
        [SerializeField]
        private HealthComponent targetHealth;

        [Tooltip("ビルボード追従対象のカメラ。")]
        [SerializeField]
        private Camera targetCamera;

        [Tooltip("透明度を制御するCanvasGroup。")]
        [SerializeField]
        private CanvasGroup canvasGroup;

        [Tooltip("現在のHPを即時反映するメインゲージ（緑色）。")]
        [SerializeField]
        private Image mainFillImage;

        [Tooltip("被弾ダメージ量を視覚化する緩衝追従ゲージ（黄白色）。")]
        [SerializeField]
        private Image bufferFillImage;

        [Header("挙動設定")]
        [Tooltip("被弾後にHPバーを表示し続ける秒数（脱戦/非被弾でフェードアウト）。")]
        [SerializeField, Min(0.5f)]
        private float showDurationAfterHit;

        [Tooltip("被弾後、緩衝バーが追従を開始するまでの待機秒数。")]
        [SerializeField, Min(0f)]
        private float bufferDelaySeconds;

        [Tooltip("緩衝バーの追従速度。")]
        [SerializeField, Min(0.1f)]
        private float bufferLerpSpeed;

        [Tooltip("フェードイン・フェードアウトの速度。")]
        [SerializeField, Min(0.1f)]
        private float fadeSpeed;

        private float targetFill = 1f;
        private float bufferFill = 1f;
        private float bufferTimer;
        private float visibleTimer;
        private float targetAlpha;
        private bool isDead;
        private Transform targetCameraTransform;

        /// <summary>現在のメインゲージ割合（0.0 ~ 1.0）です。</summary>
        public float TargetFill => targetFill;

        /// <summary>現在の緩衝ゲージ割合（0.0 ~ 1.0）です。</summary>
        public float BufferFill => bufferFill;

        /// <summary>現在の表示透明度（0.0 ~ 1.0）です。</summary>
        public float Alpha => canvasGroup.alpha;

        /// <summary>現在HPバーが表示状態（Alpha > 0.01）であるかを返します。</summary>
        public bool IsVisible => Alpha > 0.01f;

        public void SetTargetCamera(Camera cam)
        {
            targetCamera = cam;
            if (targetCamera != null)
            {
                targetCameraTransform = targetCamera.transform;
            }
        }

        private void Awake()
        {
            if (targetCamera != null)
            {
                targetCameraTransform = targetCamera.transform;
            }

            EnsureSprite(mainFillImage);
            EnsureSprite(bufferFillImage);
            canvasGroup.alpha = 0f;
            targetAlpha = 0f;
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void LateUpdate()
        {
            bool isBarFullyHidden = visibleTimer <= 0f &&
                                    canvasGroup.alpha <= 0.001f &&
                                    bufferTimer <= 0f &&
                                    Mathf.Abs(bufferFill - targetFill) <= 0.001f;

            if (isBarFullyHidden)
            {
                if (canvasGroup.alpha > 0f)
                {
                    canvasGroup.alpha = 0f;
                }
                return;
            }

            UpdateOrientation();
            UpdateBarAnimation(Time.deltaTime);
        }

        /// <summary>
        /// カメラ正面へのビルボード姿勢を更新します。
        /// </summary>
        private void UpdateOrientation()
        {
            if (targetCameraTransform != null)
            {
                transform.rotation = targetCameraTransform.rotation;
            }
        }

        /// <summary>
        /// 緩衝バーの追従およびCanvasGroupの透明度フェードを評価・更新します。
        /// </summary>
        public void UpdateBarAnimation(float deltaTime)
        {
            float safeDeltaTime = Mathf.Max(0f, deltaTime);

            // 緩衝バーのアニメーション
            if (bufferTimer > 0f)
            {
                bufferTimer -= safeDeltaTime;
                if (bufferTimer <= 0f)
                {
                    float leftoverTime = -bufferTimer;
                    bufferFill = Mathf.MoveTowards(bufferFill, targetFill, bufferLerpSpeed * leftoverTime);
                }
            }
            else
            {
                bufferFill = Mathf.MoveTowards(bufferFill, targetFill, bufferLerpSpeed * safeDeltaTime);
            }

            if (Mathf.Abs(bufferFillImage.fillAmount - bufferFill) > 0.0001f)
            {
                bufferFillImage.fillAmount = bufferFill;
            }

            // 表示タイマーとフェード処理
            if (!isDead && visibleTimer > 0f)
            {
                visibleTimer -= safeDeltaTime;
                targetAlpha = 1f;
            }
            else
            {
                targetAlpha = 0f;
            }

            float nextAlpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * safeDeltaTime);
            if (Mathf.Abs(canvasGroup.alpha - nextAlpha) > 0.0001f)
            {
                canvasGroup.alpha = nextAlpha;
            }
        }

        private static Sprite s_WhiteSprite;

        private static Sprite GetWhiteSprite()
        {
            if (s_WhiteSprite == null)
            {
                Texture2D tex = Texture2D.whiteTexture;
                s_WhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return s_WhiteSprite;
        }

        private static void EnsureSprite(Image img)
        {
            if (img != null && img.sprite == null)
            {
                img.sprite = GetWhiteSprite();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureSprite(mainFillImage);
            EnsureSprite(bufferFillImage);
        }
#endif

        /// <summary>
        /// 参照とパラメータを外部注入します。
        /// </summary>
        public void Construct(
            HealthComponent health,
            CanvasGroup group,
            Image main,
            Image buffer,
            float showDuration,
            float bufferDelay)
        {
            UnsubscribeEvents();
            targetHealth = health;
            canvasGroup = group;
            mainFillImage = main;
            bufferFillImage = buffer;
            EnsureSprite(mainFillImage);
            EnsureSprite(bufferFillImage);
            showDurationAfterHit = showDuration;
            bufferDelaySeconds = bufferDelay;
            targetFill = 1f;
            bufferFill = 1f;
            bufferTimer = 0f;
            visibleTimer = 0f;
            targetAlpha = 0f;
            isDead = false;
            SubscribeEvents();
        }



        private void SubscribeEvents()
        {
            if (targetHealth == null) return;

            targetHealth.HealthChanged -= HandleHealthChanged;
            targetHealth.HealthChanged += HandleHealthChanged;
            targetHealth.Died -= HandleDied;
            targetHealth.Died += HandleDied;

            if (targetHealth.MaximumHealth > 0f)
            {
                float currentRatio = Mathf.Clamp01(targetHealth.CurrentHealth / targetHealth.MaximumHealth);
                targetFill = currentRatio;
                bufferFill = currentRatio;
                mainFillImage.fillAmount = currentRatio;
                bufferFillImage.fillAmount = currentRatio;
            }
        }

        private void UnsubscribeEvents()
        {
            if (targetHealth == null) return;

            targetHealth.HealthChanged -= HandleHealthChanged;
            targetHealth.Died -= HandleDied;
        }

        private void HandleHealthChanged(float current, float maximum)
        {
            if (maximum <= 0f) return;

            float newFill = Mathf.Clamp01(current / maximum);

            if (newFill < targetFill)
            {
                // 被弾：メインゲージ即時削減、緩衝バー遅延追従開始、HPバー表示
                targetFill = newFill;
                mainFillImage.fillAmount = targetFill;
                bufferTimer = bufferDelaySeconds;
                visibleTimer = showDurationAfterHit;
            }
            else if (newFill > targetFill)
            {
                // 回復：両ゲージ即時上昇
                targetFill = newFill;
                bufferFill = newFill;
                mainFillImage.fillAmount = newFill;
                bufferFillImage.fillAmount = newFill;
                visibleTimer = showDurationAfterHit;
            }
        }

        private void HandleDied()
        {
            isDead = true;
            targetFill = 0f;
            bufferFill = 0f;
            mainFillImage.fillAmount = 0f;
            bufferFillImage.fillAmount = 0f;
            targetAlpha = 0f;
        }
    }
}

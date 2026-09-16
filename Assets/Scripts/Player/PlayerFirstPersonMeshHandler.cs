using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視点におけるプレイヤーのメッシュ（頭部・頭盔・胸甲・マント・両腕）の描画状態を管理します。
    /// 第一人称時はこれらを ShadowsOnly（影のみ描画）に切り替えることで、カメラへの穿孔や近クリップ面での
    /// 激しいポリゴン切断・画面ブレ、および視口武器（Viewmodel）と重複する空手腕の露出を防ぎつつ、
    /// 地面へのキャラクター全身の影投影を維持します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFirstPersonMeshHandler : MonoBehaviour
    {
        private static readonly string[] DefaultCulledPartNames =
        {
            "Knight_Head",
            "Knight_Helmet",
            "Knight_HelmetVisor",
            "Knight_Body",
            "Knight_Cape",
            "Knight_ArmLeft",
            "Knight_ArmRight"
        };

        [Header("非表示対象（頭部・頭盔・胸甲・マント・両腕）")]
        [Tooltip("第一人称時にShadowsOnlyへ切り替える頭部・頭盔・鎧・マント・両腕のRenderer一覧です。未設定時は自動検出します。")]
        [FormerlySerializedAs("headRenderers")]
        [SerializeField]
        private List<Renderer> culledRenderers = new List<Renderer>();

        [Header("カメラコントローラー参照")]
        [Tooltip("視点変更イベントを購読するThirdPersonCameraControllerです。未設定時は自動検出します。")]
        [SerializeField]
        private ThirdPersonCameraController cameraController;

        [Header("初期視点設定")]
        [SerializeField]
        private bool startInFirstPerson = true;

        private bool isFirstPerson;
        private bool initialized;

        /// <summary>現在第一人称のメッシュ遮蔽状態が適用されているかを示します。</summary>
        public bool IsFirstPerson => isFirstPerson;

        /// <summary>管理対象のRenderer一覧です。</summary>
        public IReadOnlyList<Renderer> CulledRenderers => culledRenderers;

        /// <summary>管理対象の頭部Renderer一覧です（後方互換性）。</summary>
        public IReadOnlyList<Renderer> HeadRenderers => culledRenderers;

        private void Awake()
        {
            ResolveReferences();
            InitializeRenderers();
            SetFirstPersonMode(startInFirstPerson);
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeCameraEvents();
            if (cameraController != null)
            {
                SetFirstPersonMode(cameraController.PerspectiveMode == CameraPerspectiveMode.FirstPerson);
            }
        }

        private void OnDisable()
        {
            UnsubscribeCameraEvents();
        }

        /// <summary>
        /// 第一人称モードの可視性を切り替えます。
        /// </summary>
        public void SetFirstPersonMode(bool firstPerson)
        {
            InitializeRenderers();
            isFirstPerson = firstPerson;
            ShadowCastingMode targetMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            for (int i = 0; i < culledRenderers.Count; i++)
            {
                Renderer r = culledRenderers[i];
                if (r != null)
                {
                    r.shadowCastingMode = targetMode;
                }
            }
        }

        /// <summary>
        /// テストや動的初期化向けに対象Rendererを設定します。
        /// </summary>
        public void ConfigureRenderers(IEnumerable<Renderer> renderers)
        {
            culledRenderers.Clear();
            if (renderers != null)
            {
                culledRenderers.AddRange(renderers);
            }
            SetFirstPersonMode(isFirstPerson);
        }

        private void ResolveReferences()
        {
            if (cameraController == null)
            {
                cameraController = FindAnyObjectByType<ThirdPersonCameraController>();
            }
        }

        private void SubscribeCameraEvents()
        {
            if (cameraController != null)
            {
                cameraController.PerspectiveChanged -= HandlePerspectiveChanged;
                cameraController.PerspectiveChanged += HandlePerspectiveChanged;
            }
        }

        private void UnsubscribeCameraEvents()
        {
            if (cameraController != null)
            {
                cameraController.PerspectiveChanged -= HandlePerspectiveChanged;
            }
        }

        private void HandlePerspectiveChanged(CameraPerspectiveMode mode)
        {
            SetFirstPersonMode(mode == CameraPerspectiveMode.FirstPerson);
        }

        private void InitializeRenderers()
        {
            if (culledRenderers.Count == 0)
            {
                AutoDiscoverCulledRenderers();
            }
            else
            {
                // 既存の保存データに胸甲やマントが未登録の場合は自動補完
                EnsureEssentialPartsIncluded();
            }

            initialized = true;
        }

        private void AutoDiscoverCulledRenderers()
        {
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer r = allRenderers[i];
                if (r == null) continue;

                for (int j = 0; j < DefaultCulledPartNames.Length; j++)
                {
                    if (r.name.Equals(DefaultCulledPartNames[j], StringComparison.OrdinalIgnoreCase))
                    {
                        if (!culledRenderers.Contains(r))
                        {
                            culledRenderers.Add(r);
                        }
                        break;
                    }
                }
            }
        }

        private void EnsureEssentialPartsIncluded()
        {
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer r = allRenderers[i];
                if (r == null) continue;

                for (int j = 0; j < DefaultCulledPartNames.Length; j++)
                {
                    if (r.name.Equals(DefaultCulledPartNames[j], StringComparison.OrdinalIgnoreCase))
                    {
                        if (!culledRenderers.Contains(r))
                        {
                            culledRenderers.Add(r);
                        }
                        break;
                    }
                }
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視点におけるプレイヤーの頭部・頭盔メッシュの描画状態を管理します。
    /// 第一人称時は頭部を ShadowsOnly（影のみ描画）に切り替えることで、カメラへの穿孔や視界遮断を防ぎつつ、
    /// 地面へのキャラクター全身の影投影を維持します。腕や武器は常に可視を保ちます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFirstPersonMeshHandler : MonoBehaviour
    {
        [Header("非表示対象（頭部・頭盔）")]
        [Tooltip("第一人称時にShadowsOnlyへ切り替える頭部・頭盔のRenderer一覧です。未設定時は自動検出します。")]
        [SerializeField]
        private List<Renderer> headRenderers = new List<Renderer>();

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

        /// <summary>管理対象の頭部Renderer一覧です。</summary>
        public IReadOnlyList<Renderer> HeadRenderers => headRenderers;

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
            isFirstPerson = firstPerson;
            ShadowCastingMode targetMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            for (int i = 0; i < headRenderers.Count; i++)
            {
                Renderer r = headRenderers[i];
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
            headRenderers.Clear();
            if (renderers != null)
            {
                headRenderers.AddRange(renderers);
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
            if (initialized && headRenderers.Count > 0)
            {
                return;
            }

            if (headRenderers.Count == 0)
            {
                AutoDiscoverHeadRenderers();
            }

            initialized = true;
        }

        private void AutoDiscoverHeadRenderers()
        {
            string[] headPartNames = { "Knight_Head", "Knight_Helmet", "Knight_HelmetVisor" };
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer r = allRenderers[i];
                if (r == null) continue;

                for (int j = 0; j < headPartNames.Length; j++)
                {
                    if (r.name.Equals(headPartNames[j], System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (!headRenderers.Contains(r))
                        {
                            headRenderers.Add(r);
                        }
                        break;
                    }
                }
            }
        }
    }
}

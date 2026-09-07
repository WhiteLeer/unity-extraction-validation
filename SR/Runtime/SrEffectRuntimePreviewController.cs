using System.Collections;
using System.Linq;
using UnityEngine;

namespace SrRuntimeEffectTools
{
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("SR/Runtime Effect Preview Controller")]
    public sealed class SrEffectRuntimePreviewController : MonoBehaviour
    {
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform effectRoot;
        [SerializeField] private bool playCharacterAnimator = true;
        [SerializeField] private bool loopPreview = true;
        [SerializeField] private bool holdAtCaptureTime;
        [SerializeField] private float captureTime = 1.25f;

        private float previewElapsed;
        private bool captureHeld;

        private void Awake()
        {
            EnsurePreviewCamera();

            if (characterRoot == null || effectRoot == null)
            {
                Debug.LogError("SR runtime effect preview requires both Character Root and Effect Root.", this);
                return;
            }

            foreach (var proxy in effectRoot.GetComponentsInChildren<SrEffectRuntimeProxy>(true))
                proxy.BindTarget(characterRoot);

            var animator = characterRoot.GetComponentInChildren<Animator>(true);
            if (playCharacterAnimator && animator != null)
            {
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            // Effect-only prefabs already start their particle playback when
            // the runtime proxies bind. Do not restart them on a fixed one
            // second timer; their serialized lifetime and loop settings are
            // the authoritative timeline in that case.
            if (animator != null && playCharacterAnimator)
                StartCoroutine(PreviewLoop(animator));
        }

        private void Update()
        {
            if (!holdAtCaptureTime || captureHeld)
                return;

            previewElapsed += Time.unscaledDeltaTime;
            if (previewElapsed < Mathf.Max(0.05f, captureTime))
                return;

            captureHeld = true;
            Time.timeScale = 0f;
            Debug.Log($"SR preview held at capture time {previewElapsed:F3}s.", this);
        }

        private void OnDestroy()
        {
            if (captureHeld)
                Time.timeScale = 1f;
        }

        private void OnDisable()
        {
            if (captureHeld)
                Time.timeScale = 1f;
        }

        private static void EnsurePreviewCamera()
        {
            var camera = Camera.main ?? FindObjectOfType<Camera>();
            if (camera == null)
                return;

            camera.enabled = true;
            // Soft-particle shader variants sample the camera depth texture.
            // Request it only for this preview controller; gameplay cameras are
            // not modified by the extracted asset package.
            camera.depthTextureMode |= DepthTextureMode.Depth;
            camera.targetDisplay = 0;
            camera.cullingMask = ~0;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.ResetWorldToCameraMatrix();
            camera.ResetProjectionMatrix();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
        }

        private IEnumerator PreviewLoop(Animator animator)
        {
            var proxies = effectRoot.GetComponentsInChildren<SrEffectRuntimeProxy>(true);
            var duration = animator?.runtimeAnimatorController?.animationClips
                .Where(clip => clip != null)
                .Select(clip => clip.length)
                .DefaultIfEmpty(1f)
                .Max() ?? 1f;

            do
            {
                if (animator != null && playCharacterAnimator)
                {
                    animator.Rebind();
                    animator.Play(0, 0, 0f);
                    animator.Update(0f);
                }

                foreach (var proxy in proxies)
                    proxy.RestartPlayback();

                yield return new WaitForSeconds(Mathf.Max(0.05f, duration));
            }
            while (loopPreview);
        }
    }
}

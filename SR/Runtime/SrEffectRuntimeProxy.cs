using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace SrRuntimeEffectTools
{
    [DisallowMultipleComponent]
    [AddComponentMenu("SR/Runtime Effect Proxy")]
    public sealed class SrEffectRuntimeProxy : MonoBehaviour
    {
        [Header("MonoEffect")]
        [SerializeField] private bool hasMonoEffect;
        [SerializeField] private float adaptScale = 1f;
        [SerializeField] private float delay;
        [SerializeField] private Vector3 localOffset;
        [SerializeField] private Vector3 localRotationOffset;
        [SerializeField] private Vector3 localScale;
        [SerializeField] private float maxLifeTime = -1f;
        [SerializeField] private string attachPoint;

        [Header("MonoEffectPluginFollow")]
        [SerializeField] private bool hasFollowPlugin;
        [SerializeField] private int positionOption;
        [SerializeField] private int rotationOption;
        [SerializeField] private int scaleOption;
        [SerializeField] private Vector3 baseFollowScale = Vector3.one;
        [SerializeField] private Vector3 maxFollowScale = new(-1f, -1f, -1f);

        [Header("Preview Binding")]
        [SerializeField] private Transform targetRoot;
        [SerializeField] private bool loopPlayback = true;

        private Transform target;
        private Vector3 initialLocalScale;
        private Coroutine playbackRoutine;
        private bool hasBoundTarget;

        public string AttachPoint => attachPoint;
        public Transform TargetRoot => targetRoot;
        public float AdaptScale => adaptScale;
        public bool HasMonoEffect => hasMonoEffect;
        public bool HasFollowPlugin => hasFollowPlugin;
        public int PlaybackCycleCount { get; private set; }

        // The editor capture path uses the same authored delay as runtime
        // playback, but advances the particle systems deterministically.
        public float PreviewDelay => Mathf.Max(0f, delay);

        public ParticleSystem[] PreviewParticles =>
            GetComponentsInChildren<ParticleSystem>(true);

        private void Awake()
        {
            initialLocalScale = transform.localScale;
        }

        private void Start()
        {
            if (targetRoot != null)
                BindTarget(targetRoot);
        }

        private void LateUpdate()
        {
            if (!hasBoundTarget || target == null)
                return;

            if (positionOption > 0)
                transform.position = target.TransformPoint(localOffset);

            if (rotationOption > 0)
                transform.rotation = target.rotation * Quaternion.Euler(localRotationOffset);

            ApplyScale();
        }

        public void SetMonoEffect(
            float newAdaptScale,
            float newDelay,
            Vector3 newLocalOffset,
            Vector3 newLocalRotationOffset,
            Vector3 newLocalScale,
            float newMaxLifeTime,
            string newAttachPoint)
        {
            hasMonoEffect = true;
            adaptScale = newAdaptScale;
            delay = newDelay;
            localOffset = newLocalOffset;
            localRotationOffset = newLocalRotationOffset;
            localScale = newLocalScale;
            maxLifeTime = newMaxLifeTime;
            attachPoint = newAttachPoint ?? string.Empty;
        }

        public void SetFollowPlugin(
            int newPositionOption,
            int newRotationOption,
            int newScaleOption,
            Vector3 newBaseFollowScale,
            Vector3 newMaxFollowScale)
        {
            hasFollowPlugin = true;
            positionOption = newPositionOption;
            rotationOption = newRotationOption;
            scaleOption = newScaleOption;
            baseFollowScale = newBaseFollowScale;
            maxFollowScale = newMaxFollowScale;
        }

        public void BindTarget(Transform root)
        {
            targetRoot = root;
            target = FindTarget(root);
            hasBoundTarget = target != null;
            initialLocalScale = transform.localScale;

            if (!hasBoundTarget)
            {
                Debug.LogWarning($"SR effect '{name}' could not resolve attach point '{attachPoint}'.", this);
                return;
            }

            ApplyScale();
            RestartPlayback();
        }

        private Transform FindTarget(Transform root)
        {
            if (root == null)
                return null;
            if (string.IsNullOrWhiteSpace(attachPoint))
                return root;

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, attachPoint, StringComparison.OrdinalIgnoreCase))
                    return child;

                if (NormalizeBoneName(child.name) == NormalizeBoneName(attachPoint))
                    return child;
            }

            // SR uses Origin as a logical effect root on prefabs that do not
            // serialize a separate Origin transform in the character rig.
            if (string.Equals(attachPoint, "Origin", StringComparison.OrdinalIgnoreCase))
                return root;

            return null;
        }

        private void ApplyScale()
        {
            var result = initialLocalScale;
            if (HasUsableScale(localScale))
                result = Vector3.Scale(result, localScale);

            // A value of zero is the SR runtime sentinel for "not explicitly set".
            // Preserve the authored Transform scale instead of collapsing the effect.
            if (hasMonoEffect && adaptScale > 0f && Mathf.Abs(adaptScale - 1f) > 0.000001f)
                result *= adaptScale;

            if (hasFollowPlugin && scaleOption > 0 && target != null)
            {
                var followScale = Vector3.Scale(target.lossyScale, baseFollowScale);
                followScale = ClampFollowScale(followScale);
                result = Vector3.Scale(result, followScale);
            }

            transform.localScale = result;
        }

        private Vector3 ClampFollowScale(Vector3 value)
        {
            if (maxFollowScale.x >= 0f)
                value.x = Mathf.Min(value.x, maxFollowScale.x);
            if (maxFollowScale.y >= 0f)
                value.y = Mathf.Min(value.y, maxFollowScale.y);
            if (maxFollowScale.z >= 0f)
                value.z = Mathf.Min(value.z, maxFollowScale.z);
            return value;
        }

        public void RestartPlayback()
        {
            if (playbackRoutine != null)
                StopCoroutine(playbackRoutine);
            playbackRoutine = StartCoroutine(PlaybackRoutine());
        }

        public void SimulatePreviewAtTime(float absoluteTime)
        {
            var localTime = absoluteTime - PreviewDelay;
            ResetPreview();
            if (localTime > 0f)
                SimulatePreviewDelta(localTime);
        }

        public void ResetPreview()
        {
            foreach (var particle in PreviewParticles)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Clear(false);
                particle.Simulate(0f, false, true, true);
                particle.Play(false);
            }
        }

        public void SimulatePreviewRange(float fromAbsoluteTime, float toAbsoluteTime)
        {
            var fromLocalTime = Mathf.Max(0f, fromAbsoluteTime - PreviewDelay);
            var toLocalTime = Mathf.Max(0f, toAbsoluteTime - PreviewDelay);
            var delta = toLocalTime - fromLocalTime;
            if (delta > 0f)
                SimulatePreviewDelta(delta);
        }

        private void SimulatePreviewDelta(float delta)
        {
            foreach (var particle in PreviewParticles)
                particle.Simulate(delta, false, false, true);
        }

        private IEnumerator PlaybackRoutine()
        {
            var particles = GetComponentsInChildren<ParticleSystem>(true);
            while (true)
            {
                PlaybackCycleCount++;
                foreach (var particle in particles)
                {
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particle.Clear(false);
                    particle.Simulate(0f, false, true);
                }

                if (delay > 0f)
                    yield return new WaitForSeconds(delay);

                foreach (var particle in particles)
                {
                    particle.Simulate(0f, false, true);
                    particle.Play(false);
                }

                // MonoEffect's maxLifeTime is the authored preview boundary. Unity's
                // particle main.duration can remain a long emission-window sentinel
                // (for example 16 seconds) even when the runtime effect lasts 3 seconds.
                var cycleDuration = maxLifeTime > 0f
                    ? maxLifeTime
                    : EstimateCycleDuration(particles);
                yield return new WaitForSeconds(cycleDuration);

                if (!loopPlayback)
                {
                    gameObject.SetActive(false);
                    yield break;
                }
            }
        }

        private static float EstimateCycleDuration(ParticleSystem[] particles)
        {
            var duration = 0.05f;
            foreach (var particle in particles)
            {
                var main = particle.main;
                duration = Mathf.Max(duration, main.duration + main.startLifetime.constantMax + main.startDelay.constantMax);
            }

            return duration;
        }

        private static bool HasUsableScale(Vector3 value) =>
            Mathf.Abs(value.x) > 0.000001f ||
            Mathf.Abs(value.y) > 0.000001f ||
            Mathf.Abs(value.z) > 0.000001f;

        private static string NormalizeBoneName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = string.Empty;
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                    normalized += char.ToLowerInvariant(character);
            }

            return normalized.EndsWith("jnt", StringComparison.Ordinal)
                ? normalized.Substring(0, normalized.Length - 3)
                : normalized;
        }
    }

}

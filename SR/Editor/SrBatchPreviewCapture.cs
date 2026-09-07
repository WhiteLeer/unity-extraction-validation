using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SrRuntimeEffectTools;

namespace SrEffectPrefabTools
{
    public static class SrBatchPreviewCapture
    {
        private const string ScenePath =
            "Assets/unity-extraction-validation/SR/effects/cerydra/Preview/Eff_Avatar_Cerydra_00_MazeSkill_01_RuntimePreview.unity";
        private const string OutputPath =
            "Assets/Screenshots/cerydra_chess_batch_1250ms.png";
        private const float ScanStartTime = 0.1f;
        private const float ScanEndTime = 2.5f;
        private const float ScanStep = 0.25f;
        private const int StableSamples = 3;

        [MenuItem("Tools/SR/Effects/Cerydra/Capture Stable Chess Preview")]
        public static void CaptureCerydra()
        {
            CaptureCerydraInternal(OutputPath);
        }

        private static void CaptureCerydraInternal(string outputPath)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                throw new InvalidOperationException($"Could not open preview scene: {ScenePath}");

            var animators = UnityEngine.Object.FindObjectsOfType<Animator>(true);
            var effectRoots = UnityEngine.Object.FindObjectsOfType<SrEffectRuntimeProxy>(true);
            var particleSystems = UnityEngine.Object.FindObjectsOfType<ParticleSystem>(true);
            var captureTime = FindStableChessTime(animators, effectRoots, particleSystems);
            SimulateSceneAtTime(captureTime, animators, effectRoots, particleSystems);
            EnableChessRdcColorPath();

            var camera = UnityEngine.Object.FindObjectsOfType<Camera>(true)
                .FirstOrDefault(candidate => candidate.CompareTag("MainCamera"))
                ?? UnityEngine.Object.FindObjectsOfType<Camera>(true).FirstOrDefault();
            if (camera == null)
                throw new MissingReferenceException("The preview scene has no Camera.");

            var width = 640;
            var height = 640;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            // The reference capture writes to HDR R11G11B10_FLOAT. Keep the
            // preview target HDR as well so bright particle colors are not
            // clipped before we inspect the result.
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "SR_BatchPreviewCapture"
            };
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);

            try
            {
                camera.targetTexture = renderTexture;
                // Some SR materials use depth fade without the legacy keyword.
                camera.depthTextureMode |= DepthTextureMode.Depth;
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                var absoluteOutput = Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath)!.FullName,
                    outputPath));
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
                File.WriteAllBytes(absoluteOutput, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void EnableChessRdcColorPath()
        {
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;

                    var isRdcChessMaterial = material.name.IndexOf("Mz_DissolveM_22", StringComparison.OrdinalIgnoreCase) >= 0;
                    var isChessNode = renderer.gameObject.name.IndexOf("Qizi", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isRdcChessMaterial && !isChessNode)
                        continue;

                    material.SetFloat("_RdcColorChannelMode", 1f);
                    if (material.shader?.name == "SR/Reconstructed Particle/UvMove" && material.HasProperty("_RdcChessPath"))
                        material.SetFloat("_RdcChessPath", 1f);
                }
            }
        }

        private static float FindStableChessTime(
            Animator[] animators,
            SrEffectRuntimeProxy[] effectRoots,
            ParticleSystem[] particleSystems)
        {
            var simulatedParticles = PrepareSceneForTimeline(animators, effectRoots, particleSystems);
            var samples = new List<(float time, int count)>();
            var previousTime = 0f;
            for (var time = ScanStartTime; time <= ScanEndTime + 0.0001f; time += ScanStep)
            {
                AdvanceSceneToTime(time, previousTime, animators, effectRoots, particleSystems, simulatedParticles);
                samples.Add((time, CountChessParticles(particleSystems)));
                previousTime = time;
            }

            for (var index = 0; index <= samples.Count - StableSamples; index++)
            {
                var window = samples.Skip(index).Take(StableSamples).ToArray();
                if (window.All(sample => sample.count > 0))
                {
                    var selected = window[StableSamples / 2];
                    Debug.Log($"SR stable chess window: {window[0].time:F2}-{window[^1].time:F2}s, " +
                              $"particleCount={string.Join(",", window.Select(sample => sample.count))}; " +
                              $"selected={selected.time:F2}s");
                    return selected.time;
                }
            }

            var fallback = samples.OrderByDescending(sample => sample.count).FirstOrDefault();
            Debug.LogWarning($"SR stable chess window was not found; selected highest particle sample " +
                             $"at {fallback.time:F2}s with particleCount={fallback.count}.");
            return fallback.time > 0f ? fallback.time : 1.25f;
        }

        private static HashSet<ParticleSystem> PrepareSceneForTimeline(
            Animator[] animators,
            SrEffectRuntimeProxy[] effectRoots,
            ParticleSystem[] particleSystems)
        {
            foreach (var animator in animators)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            var simulatedParticles = new HashSet<ParticleSystem>();
            foreach (var proxy in effectRoots)
            {
                proxy.ResetPreview();
                foreach (var particle in proxy.PreviewParticles)
                    simulatedParticles.Add(particle);
            }

            foreach (var particleSystem in particleSystems)
            {
                if (simulatedParticles.Contains(particleSystem))
                    continue;
                particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Clear(false);
                particleSystem.Simulate(0f, false, true, true);
                particleSystem.Play(false);
            }

            return simulatedParticles;
        }

        private static void AdvanceSceneToTime(
            float toTime,
            float fromTime,
            Animator[] animators,
            SrEffectRuntimeProxy[] effectRoots,
            ParticleSystem[] particleSystems,
            HashSet<ParticleSystem> simulatedParticles)
        {
            var delta = Mathf.Max(0f, toTime - fromTime);
            foreach (var animator in animators)
                animator.Update(delta);

            foreach (var proxy in effectRoots)
                proxy.SimulatePreviewRange(fromTime, toTime);

            foreach (var particleSystem in particleSystems)
            {
                if (simulatedParticles.Contains(particleSystem))
                    continue;
                particleSystem.Simulate(delta, false, false, true);
            }
        }

        private static int CountChessParticles(ParticleSystem[] particleSystems)
        {
            var chessSystems = particleSystems.Where(system =>
                system.name.IndexOf("qizi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                system.name.IndexOf("chess", StringComparison.OrdinalIgnoreCase) >= 0 ||
                system.transform.root.name.IndexOf("qizi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                system.transform.root.name.IndexOf("chess", StringComparison.OrdinalIgnoreCase) >= 0);
            var count = chessSystems.Sum(system => system.particleCount);
            return count > 0 ? count : particleSystems.Sum(system => system.particleCount);
        }

        private static void SimulateSceneAtTime(
            float time,
            Animator[] animators,
            SrEffectRuntimeProxy[] effectRoots,
            ParticleSystem[] particleSystems)
        {
            foreach (var animator in animators)
            {
                animator.Rebind();
                animator.Update(time);
            }

            var simulatedParticles = new System.Collections.Generic.HashSet<ParticleSystem>();
            foreach (var proxy in effectRoots)
            {
                proxy.SimulatePreviewAtTime(time);
                foreach (var particle in proxy.PreviewParticles)
                    simulatedParticles.Add(particle);
            }

            // A few extracted systems are not wrapped by a MonoEffect proxy.
            // They still need a deterministic global-time sample rather than
            // being left at their serialized edit-time state.
            foreach (var particleSystem in particleSystems)
            {
                if (simulatedParticles.Contains(particleSystem))
                    continue;

                particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Simulate(time, false, true, true);
            }
        }
    }
}

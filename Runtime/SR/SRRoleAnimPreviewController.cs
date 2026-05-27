using System.Collections.Generic;
using UnityEngine;

public class SRRoleAnimPreviewController : MonoBehaviour
{
    public Animator targetAnimator;
    public List<AnimationClip> clips = new List<AnimationClip>();
    public int clipIndex;

    [ContextMenu("Preview/Play Current")]
    public void PlayCurrent()
    {
        if (targetAnimator == null || clips == null || clips.Count == 0) return;
        clipIndex = Mathf.Clamp(clipIndex, 0, clips.Count - 1);
        var clip = clips[clipIndex];
        if (clip == null) return;
        targetAnimator.Play("Clip_" + clip.name, 0, 0f);
        targetAnimator.Update(0f);
    }

    [ContextMenu("Preview/Next")]
    public void Next()
    {
        if (clips == null || clips.Count == 0) return;
        clipIndex = (clipIndex + 1) % clips.Count;
        PlayCurrent();
    }

    [ContextMenu("Preview/Prev")]
    public void Prev()
    {
        if (clips == null || clips.Count == 0) return;
        clipIndex = (clipIndex - 1 + clips.Count) % clips.Count;
        PlayCurrent();
    }
}


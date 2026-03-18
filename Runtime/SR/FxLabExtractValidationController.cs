using System.Linq;
using UnityEngine;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public class FxLabExtractValidationController : MonoBehaviour
{
    [Header("References")]
    public Animator targetAnimator;
    public PlayableDirector targetDirector;

    [Header("Animation")]
    public string stateNamePrefix = "Clip_";
    [Min(0f)]
    public float crossFadeSeconds = 0.08f;
    public bool playFirstStateOnStart = true;

    private string[] _stateNames = new string[0];
    private int _stateIndex;

    private void Start()
    {
        if (targetAnimator == null)
        {
            targetAnimator = FindObjectOfType<Animator>();
        }

        if (targetDirector == null)
        {
            targetDirector = FindObjectOfType<PlayableDirector>();
        }

        CacheStates();
        if (playFirstStateOnStart)
        {
            PlayState(0);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Comma))
        {
            PrevState();
        }
        if (Input.GetKeyDown(KeyCode.Period))
        {
            NextState();
        }
        if (Input.GetKeyDown(KeyCode.Slash))
        {
            ToggleDirector();
        }
        if (Input.GetKeyDown(KeyCode.Backslash))
        {
            RestartDirector();
        }
    }

    public void PrevState()
    {
        if (_stateNames.Length == 0)
        {
            return;
        }
        _stateIndex = (_stateIndex - 1 + _stateNames.Length) % _stateNames.Length;
        PlayState(_stateIndex);
    }

    public void NextState()
    {
        if (_stateNames.Length == 0)
        {
            return;
        }
        _stateIndex = (_stateIndex + 1) % _stateNames.Length;
        PlayState(_stateIndex);
    }

    public void PlayState(int index)
    {
        if (targetAnimator == null || _stateNames.Length == 0)
        {
            return;
        }

        index = Mathf.Clamp(index, 0, _stateNames.Length - 1);
        _stateIndex = index;
        var stateName = _stateNames[_stateIndex];
        if (crossFadeSeconds <= 0f)
        {
            targetAnimator.Play(stateName, 0, 0f);
        }
        else
        {
            targetAnimator.CrossFade(stateName, crossFadeSeconds, 0, 0f);
        }
    }

    public void ToggleDirector()
    {
        if (targetDirector == null)
        {
            return;
        }

        if (targetDirector.state == UnityEngine.Playables.PlayState.Playing)
        {
            targetDirector.Pause();
        }
        else
        {
            targetDirector.Play();
        }
    }

    public void RestartDirector()
    {
        if (targetDirector == null)
        {
            return;
        }
        targetDirector.time = 0d;
        targetDirector.Evaluate();
        targetDirector.Play();
    }

    private void CacheStates()
    {
        _stateNames = new string[0];
        _stateIndex = 0;
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        var clips = targetAnimator.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        _stateNames = clips
            .Where(x => x != null)
            .Select(x => stateNamePrefix + x.name)
            .Distinct()
            .ToArray();
    }
}


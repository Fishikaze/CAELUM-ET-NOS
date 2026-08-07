using UnityEngine;

// Attach to a persistent object (e.g. an empty "SoundManager", DontDestroyOnLoad).
// Call SoundManager.Instance.PlaySFX(clip) from anywhere to play a one-shot sound.
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Range(0f, 1f)]
    public float volume = 1f;

    private AudioSource sfxSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
    }

    public void PlaySFX(AudioClip clip)
    {
        PlaySFX(clip, volume);
    }

    public void PlaySFX(AudioClip clip, float volumeScale)
    {
        if (clip == null) return;

        // PlayOneShot overlaps cleanly with itself, so a single AudioSource can play
        // multiple simultaneous sound effects without cutting each other off.
        sfxSource.PlayOneShot(clip, volumeScale);
    }
}
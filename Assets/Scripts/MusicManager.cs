using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Attach to a persistent object (e.g. an empty "MusicManager", DontDestroyOnLoad)
// and fill in the areas list in the Inspector.
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [System.Serializable]
    public class AreaTrack
    {
        public string areaName;

        [Tooltip("For a normal area, this is the song and it just loops forever. For the boss track, this plays once and then hands off to Loop Section.")]
        public AudioClip clip;

        [Tooltip("Leave empty for a normal looping song. Set this for the boss song: 'clip' plays once, then THIS loops forever afterward.")]
        public AudioClip loopSection;
    }

    [Header("Tracks")]
    public List<AreaTrack> areas = new List<AreaTrack>();

    [Header("Playback")]
    [Range(0f, 1f)]
    public float volume = 0.7f;

    private AudioSource musicSource;
    private string currentAreaName;
    private Coroutine playRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.volume = volume;
        musicSource.playOnAwake = false;
    }

    public void PlayAreaMusic(string areaName)
    {
        if (areaName == currentAreaName) return; // already playing this area's music

        AreaTrack track = areas.Find(a => a.areaName == areaName);
        if (track == null || track.clip == null)
        {
            Debug.Log("[MusicManager] no track found for area '" + areaName + "'");
            return;
        }

        currentAreaName = areaName;

        if (playRoutine != null) StopCoroutine(playRoutine);

        if (track.loopSection == null)
        {
            // Normal area: just loop the one clip.
            musicSource.clip = track.clip;
            musicSource.loop = true;
            musicSource.Play();
        }
        else
        {
            // Boss-style track: play the full clip once, then loop just the loop section.
            playRoutine = StartCoroutine(PlayWithLoopSection(track.clip, track.loopSection));
        }
    }

    private IEnumerator PlayWithLoopSection(AudioClip full, AudioClip loopSection)
    {
        musicSource.clip = full;
        musicSource.loop = false;
        musicSource.Play();

        yield return new WaitForSeconds(full.length);

        musicSource.clip = loopSection;
        musicSource.loop = true;
        musicSource.Play();

        playRoutine = null;
    }

    public void StopMusic()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
        musicSource.Stop();
        currentAreaName = null;
    }
}
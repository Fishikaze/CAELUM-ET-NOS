using UnityEngine;

// Attach to any object in your title screen scene and assign a clip. Starts
// looping automatically — no player or trigger needed.
[RequireComponent(typeof(AudioSource))]
public class TitleMusic : MonoBehaviour
{
    public AudioClip song;

    [Range(0f, 1f)]
    public float volume = 0.7f;

    private void Start()
    {
        AudioSource source = GetComponent<AudioSource>();
        source.clip = song;
        source.volume = volume;
        source.loop = true;
        source.Play();
    }
}
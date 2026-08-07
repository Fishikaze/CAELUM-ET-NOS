using UnityEngine;

// Put this on a trigger collider covering an area of the level. When the player
// enters, tells MusicManager to switch to that area's track (MusicManager ignores
// the call if that area is already playing, so overlapping zones for the same
// area are safe).
[RequireComponent(typeof(Collider2D))]
public class AreaMusicZone : MonoBehaviour
{
    public string areaName;
    public string playerTag = "Player";

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        MusicManager.Instance.PlayAreaMusic(areaName);
    }
}
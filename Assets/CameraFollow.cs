using UnityEngine;

// Attach this to the Main Camera. Assign the player's Transform in the Inspector.
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset")]
    [Tooltip("Keep Z at -10 (or whatever your camera's default Z is) so the camera stays in front of your sprites.")]
    public Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Smoothing")]
    [Tooltip("Lower = snappier, higher = smoother/laggier follow.")]
    public float smoothTime = 0.15f;

    private Vector3 velocity = Vector3.zero;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);
    }
}
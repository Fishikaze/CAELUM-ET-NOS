using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    [Header("Target")]
    public Transform target;              // The player to follow

    [Header("Offset")]
    public Vector2 offset = Vector2.zero; // Usually (0,0) or slight vertical offset

    [Header("Smoothing")]
    public float smoothSpeed = 5f;        // Higher = snappier, lower = smoother/laggier
    public bool useFixedUpdate = false;   // Use FixedUpdate if player uses Rigidbody2D physics

    [Header("Boundaries (optional)")]
    public bool useBounds = false;
    public Vector2 minBounds;             // Bottom-left world limit
    public Vector2 maxBounds;             // Top-right world limit

    private float zPos;                   // Keep camera's original Z (usually -10)
    private Camera cam;

    void Start()
    {
        if (target == null)
            Debug.LogWarning("CameraFollow2D: No target assigned!");

        zPos = transform.position.z;
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (!useFixedUpdate)
            FollowTarget();
    }

    void FixedUpdate()
    {
        if (useFixedUpdate)
            FollowTarget();
    }

    void FollowTarget()
    {
        if (target == null) return;

        Vector3 desiredPosition = new Vector3(
            target.position.x + offset.x,
            target.position.y + offset.y,
            zPos
        );

        if (useBounds && cam != null)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            float clampedX = Mathf.Clamp(desiredPosition.x, minBounds.x + halfWidth, maxBounds.x - halfWidth);
            float clampedY = Mathf.Clamp(desiredPosition.y, minBounds.y + halfHeight, maxBounds.y - halfHeight);

            desiredPosition = new Vector3(clampedX, clampedY, zPos);
        }

        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
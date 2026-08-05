using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    public Transform player; // Drag your player GameObject here in the Inspector

    void LateUpdate()
    {
        if (player == null) return;

        // Locks the camera directly to the player's exact X and Y coordinates
        // Keeps the camera's original Z depth so elements stay visible
        transform.position = new Vector3(player.position.x, player.position.y+2.1f, transform.position.z);
    }
}

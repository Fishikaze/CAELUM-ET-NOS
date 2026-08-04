using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovingPlatform : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private Transform[] waypoints; // Array allows adding 2 or more points easily
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float checkDistance = 0.05f; // Threshold to register reaching a point

    private int currentTargetIndex = 0;

    private void Update()
    {
        if (waypoints.Length == 0) return;

        // 1. Move the platform toward the current target waypoint
        Transform targetWaypoint = waypoints[currentTargetIndex];
        transform.position = Vector2.MoveTowards(
            transform.position,
            targetWaypoint.position,
            moveSpeed * Time.deltaTime
        );

        // 2. Switch to the next waypoint when close enough
        if (Vector2.Distance(transform.position, targetWaypoint.position) < checkDistance)
        {
            currentTargetIndex = (currentTargetIndex + 1) % waypoints.Length;
        }
    }

    // Optional: Makes the platform stick the player so they don't slide off
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            collision.transform.SetParent(transform);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            collision.transform.SetParent(null);
        }
    }
}
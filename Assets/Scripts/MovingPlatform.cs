using System;
using UnityEngine;

// Attach to the platform. Create two empty GameObjects in the scene marking
// the start/end points and drag them into pointA / pointB.
public class MovingPlatform : MonoBehaviour
{
    [Header("Endpoints")]
    public Transform pointA;
    public Transform pointB;
    public Boolean hi;

    [Header("Movement")]
    public float speed = 2f;

    private Transform currentTarget;

    private void Start()
    {
        currentTarget = pointB;
    }

    private void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, currentTarget.position, speed * Time.deltaTime);

        if (Vector3.Distance(transform.position, currentTarget.position) < 0.01f)
        {
            currentTarget = (currentTarget == pointA) ? pointB : pointA;
        }
    }
}
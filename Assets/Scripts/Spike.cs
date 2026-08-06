using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpikeTrap : MonoBehaviour
{
    [SerializeField] private float damage = 25f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        Health health = other.GetComponent<Health>();

        if (health != null)
        {
            health.TakeDamage(damage);
        }
    }
}
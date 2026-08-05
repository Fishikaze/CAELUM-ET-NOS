using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LandMine : MonoBehaviour
{
    public float damage = 50f;
    public GameObject explosionEffect;

    private bool exploded = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded)
            return;

        if (other.CompareTag("Player"))
        {
            exploded = true;

            // Spawn explosion effect
            if (explosionEffect != null)
                Instantiate(explosionEffect, transform.position, Quaternion.identity);

            // Damage player if they have a Health component
            Health health = other.GetComponent<Health>();
            if (health != null)
                health.TakeDamage(damage);

            Destroy(gameObject);
        }
    }
}
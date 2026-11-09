using System;
using UnityEngine;

public class DamageSystem : MonoBehaviour, IDamageable
{
    [SerializeField] int health = 3;
    [SerializeField] bool destroyOnDeath = true; // untick on the player for now

    public event Action<int> Damaged; // passes the damage amount

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void TakeDamage(int amount)
    {
        health -= amount;
        Debug.Log($"{name} took {amount}, health {health}");
        Damaged?.Invoke(amount);

        if (health <= 0 && destroyOnDeath) Destroy(gameObject);
    }
}

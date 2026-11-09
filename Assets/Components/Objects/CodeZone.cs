using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CodeZone : MonoBehaviour
{
    [SerializeField] CodeInputSystem codes;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void Reset() => GetComponent<Collider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) codes.EnterZone();
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) codes.ExitZone();
    }
}

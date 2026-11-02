using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    public Animator doorAnimator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    public void OnTriggerEnter(Collider other)
    {
        doorAnimator.SetTrigger("Open");
    }
}

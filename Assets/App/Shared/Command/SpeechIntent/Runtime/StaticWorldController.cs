using UnityEngine;

namespace SpeechIntent
{
    public class StaticWorldController : MonoBehaviour
    {
        public GameObject dynamicWorldRoot;
        public GameObject staticWorldRoot;

        public void SwitchToStaticWorld()
        {
            if (dynamicWorldRoot != null)
            {
                dynamicWorldRoot.SetActive(false);
            }

            if (staticWorldRoot != null)
            {
                staticWorldRoot.SetActive(true);
            }

            Debug.Log("Switched to static world.");
        }

        public void SwitchToDynamicWorld()
        {
            if (staticWorldRoot != null)
            {
                staticWorldRoot.SetActive(false);
            }

            if (dynamicWorldRoot != null)
            {
                dynamicWorldRoot.SetActive(true);
            }

            Debug.Log("Switched to dynamic world.");
        }
    }
}

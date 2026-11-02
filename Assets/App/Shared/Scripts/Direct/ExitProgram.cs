using UnityEngine;

public class ExitProgram : MonoBehaviour
{
    [SerializeField] private string playerRootName = "Me";
    [SerializeField] private string acceptedTag = "Player";
    [SerializeField] private bool logIgnoredColliders = true;

    public void OnTriggerEnter(Collider other)
    {
        if (IsPlayerCollider(other))
        {
            Debug.Log($"[ExitProgram] Exit trigger entered by '{other.name}'. Quitting application.", this);
            QuitApplication();
            return;
        }

        if (logIgnoredColliders)
            Debug.Log($"[ExitProgram] Ignored trigger enter from '{GetPath(other.transform)}' tag='{other.tag}'.", this);
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other == null)
            return false;

        if (!string.IsNullOrEmpty(acceptedTag) && other.CompareTag(acceptedTag))
            return true;

        Transform current = other.transform;
        while (current != null)
        {
            if (string.Equals(current.name, playerRootName, System.StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.parent;
        }

        return other.GetComponentInParent<CharacterController>() != null;
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void QuitApplication()
    {
        Application.Quit();

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            try
            {
                activity.Call("finishAndRemoveTask");
            }
            catch
            {
                activity.Call("finish");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ExitProgram] Android activity finish failed: {ex.Message}");
        }
#endif
    }
}

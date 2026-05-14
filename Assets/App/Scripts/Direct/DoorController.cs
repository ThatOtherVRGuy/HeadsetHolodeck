using UnityEngine;
using System.Collections.Generic;

public class DoorController : MonoBehaviour
{
    public GameObject leftDoor;
    public GameObject rightDoor;
    public BoxCollider doorTrigger;
    public string enteringRootName = "Me";
    [Header("Code Driven Motion")]
    public bool disableDoorAnimators = true;
    public float doorMoveSeconds = 0.5f;
    public Vector3 leftDoorOpenOffset = new Vector3(-51.4f, 0f, 0f);
    public Vector3 rightDoorOpenOffset = new Vector3(44.1f, 0f, 0f);
    [Tooltip("Seconds to wait after the last trigger contact before closing. Helps XR rigs that briefly flicker trigger exit/enter.")]
    public float closeDelaySeconds = 1f;
    [Tooltip("Minimum close delay even if the serialized scene value is zero.")]
    public float minimumCloseDelaySeconds = 0.75f;

    readonly HashSet<Collider> _insideColliders = new HashSet<Collider>();
    readonly Collider[] _overlapResults = new Collider[32];
    float _lastInsideTime = float.NegativeInfinity;
    Vector3 _leftDoorClosedPosition;
    Vector3 _rightDoorClosedPosition;
    Coroutine _doorMotion;
    bool _isOpen;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _leftDoorClosedPosition = GetLocalPosition(leftDoor);
        _rightDoorClosedPosition = GetLocalPosition(rightDoor);

        if (disableDoorAnimators)
        {
            DisableAnimator(leftDoor);
            DisableAnimator(rightDoor);
        }

        SetDoorPositions(0f);
    }

    public void OnTriggerEnter(Collider other)
    {
        if (!IsEnteringObject(other))
            return;

        MarkInside(other);
    }

    public void OnTriggerStay(Collider other)
    {
        if (!IsEnteringObject(other))
            return;

        MarkInside(other);
    }

    public void OnTriggerExit(Collider other)
    {
        if (!IsEnteringObject(other))
            return;

        _insideColliders.Remove(other);
    }

    void Update()
    {
        if (!_isOpen)
            return;

        if (IsEnteringObjectStillInsideTrigger())
        {
            _lastInsideTime = Time.time;
            return;
        }

        float closeDelay = Mathf.Max(closeDelaySeconds, minimumCloseDelaySeconds);
        if (Time.time - _lastInsideTime < closeDelay)
            return;

        Debug.Log("Player exited door trigger");
        CloseDoor();
    }

    public void OpenDoor()
    {
        if (_isOpen)
            return;

        _isOpen = true;
        MoveDoors(1f);
    }

    public void CloseDoor()
    {
        if (!_isOpen)
            return;

        _isOpen = false;
        MoveDoors(0f);
    }

    void MarkInside(Collider other)
    {
        bool wasEmpty = _insideColliders.Count == 0;
        _insideColliders.Add(other);
        _lastInsideTime = Time.time;

        if (!wasEmpty && _isOpen)
            return;

        Debug.Log("Player entered door trigger");
        OpenDoor();
    }

    void MoveDoors(float targetOpenAmount)
    {
        if (_doorMotion != null)
            StopCoroutine(_doorMotion);

        _doorMotion = StartCoroutine(MoveDoorsRoutine(targetOpenAmount));
    }

    System.Collections.IEnumerator MoveDoorsRoutine(float targetOpenAmount)
    {
        float duration = Mathf.Max(0.01f, doorMoveSeconds);
        float startOpenAmount = GetCurrentOpenAmount();
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(startOpenAmount, targetOpenAmount, t);
            SetDoorPositions(eased);
            yield return null;
        }

        SetDoorPositions(targetOpenAmount);
        _doorMotion = null;
    }

    float GetCurrentOpenAmount()
    {
        if (leftDoor == null || Mathf.Approximately(leftDoorOpenOffset.sqrMagnitude, 0f))
            return _isOpen ? 1f : 0f;

        Vector3 currentOffset = leftDoor.transform.localPosition - _leftDoorClosedPosition;
        return Mathf.Clamp01(Vector3.Dot(currentOffset, leftDoorOpenOffset) / leftDoorOpenOffset.sqrMagnitude);
    }

    void SetDoorPositions(float openAmount)
    {
        SetLocalPosition(leftDoor, _leftDoorClosedPosition + leftDoorOpenOffset * openAmount);
        SetLocalPosition(rightDoor, _rightDoorClosedPosition + rightDoorOpenOffset * openAmount);
    }

    static Vector3 GetLocalPosition(GameObject target)
    {
        return target != null ? target.transform.localPosition : Vector3.zero;
    }

    static void SetLocalPosition(GameObject target, Vector3 localPosition)
    {
        if (target != null)
            target.transform.localPosition = localPosition;
    }

    static void DisableAnimator(GameObject target)
    {
        if (target == null)
            return;

        Animator animator = target.GetComponent<Animator>();
        if (animator != null)
            animator.enabled = false;
    }

    bool IsEnteringObjectStillInsideTrigger()
    {
        BoxCollider trigger = doorTrigger != null ? doorTrigger : GetComponent<BoxCollider>();
        if (trigger == null)
            return _insideColliders.Count > 0;

        Vector3 worldCenter = trigger.transform.TransformPoint(trigger.center);
        Vector3 worldHalfExtents = Vector3.Scale(trigger.size * 0.5f, Abs(trigger.transform.lossyScale));
        int hitCount = Physics.OverlapBoxNonAlloc(
            worldCenter,
            worldHalfExtents,
            _overlapResults,
            trigger.transform.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _overlapResults[i];
            if (hit == null || hit == trigger)
                continue;

            if (IsEnteringObject(hit))
                return true;
        }

        return false;
    }

    static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    bool IsEnteringObject(Collider other)
    {
        if (other == null)
            return false;

        if (string.IsNullOrWhiteSpace(enteringRootName))
            return true;

        Transform root = other.transform.root;
        return root != null && root.name == enteringRootName;
    }
}

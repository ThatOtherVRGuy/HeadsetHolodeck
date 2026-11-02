using UnityEngine;
using SpeechIntent.HandInput;

namespace SpeechIntent
{
    /// <summary>
    /// Centralizes conversational focus policy.
    /// "this/that" should resolve to what the user is currently indicating.
    /// "it" should resolve to the current discourse object from memory.
    /// </summary>
    public class SceneFocusContext : MonoBehaviour
    {
        public InteractionMemory interactionMemory;

        [Header("Held Objects")]
        public GameObject leftHeldObject;
        public GameObject rightHeldObject;

        public void SetHeldObject(HandedInputSide side, GameObject heldObject)
        {
            if (side == HandedInputSide.Left)
                leftHeldObject = heldObject;
            else
                rightHeldObject = heldObject;

            if (heldObject != null)
                interactionMemory?.RegisterSelection(heldObject);
        }

        public bool TryResolveIndicatedTarget(
            SpatialSnapshot spatial,
            HandSelection handSelection,
            System.Func<GameObject, GameObject> normalizeTarget,
            System.Func<string, GameObject> findByHierarchyPath,
            System.Func<string, GameObject> findByNameOrAlias,
            System.Func<SpatialSnapshot, GameObject> resolveGaze,
            out GameObject target)
        {
            target = null;

            if (TryResolveHeldObject(handSelection, out target))
            {
                target = Normalize(normalizeTarget, target);
                if (target != null)
                    return true;
            }

            if (TryResolvePointedObject(spatial, handSelection, findByHierarchyPath, findByNameOrAlias, out target))
            {
                target = Normalize(normalizeTarget, target);
                if (target != null)
                    return true;
            }

            target = resolveGaze != null ? resolveGaze(spatial) : null;
            target = Normalize(normalizeTarget, target);
            return target != null;
        }

        public GameObject ResolveRememberedTarget()
        {
            if (interactionMemory == null)
                return null;

            if (interactionMemory.lastModifiedTarget != null)
                return interactionMemory.lastModifiedTarget;

            if (interactionMemory.lastInteractedTarget != null)
                return interactionMemory.lastInteractedTarget;

            if (interactionMemory.lastCreatedObject != null)
                return interactionMemory.lastCreatedObject;

            if (interactionMemory.currentSelection != null)
                return interactionMemory.currentSelection;

            return interactionMemory.currentWorldRoot;
        }

        public GameObject ResolveCurrentSelection()
        {
            if (TryResolveHeldObject(HandSelection.Either, out GameObject held))
                return held;

            return interactionMemory != null ? interactionMemory.currentSelection : null;
        }

        bool TryResolveHeldObject(HandSelection handSelection, out GameObject target)
        {
            target = null;

            if ((handSelection == HandSelection.Left || handSelection == HandSelection.Both) && leftHeldObject != null)
            {
                target = leftHeldObject;
                return true;
            }

            if ((handSelection == HandSelection.Right || handSelection == HandSelection.Both) && rightHeldObject != null)
            {
                target = rightHeldObject;
                return true;
            }

            if (handSelection == HandSelection.None || handSelection == HandSelection.Either)
            {
                if (rightHeldObject != null)
                {
                    target = rightHeldObject;
                    return true;
                }

                if (leftHeldObject != null)
                {
                    target = leftHeldObject;
                    return true;
                }
            }

            return false;
        }

        static bool TryResolvePointedObject(
            SpatialSnapshot spatial,
            HandSelection handSelection,
            System.Func<string, GameObject> findByHierarchyPath,
            System.Func<string, GameObject> findByNameOrAlias,
            out GameObject target)
        {
            target = null;
            if (spatial == null)
                return false;

            if (AllowsRight(handSelection) &&
                TryResolveHandHit(spatial.right_hand, true, findByHierarchyPath, findByNameOrAlias, out target))
                return true;

            if (AllowsLeft(handSelection) &&
                TryResolveHandHit(spatial.left_hand, true, findByHierarchyPath, findByNameOrAlias, out target))
                return true;

            if (AllowsRight(handSelection) &&
                TryResolveHandHit(spatial.right_hand, false, findByHierarchyPath, findByNameOrAlias, out target))
                return true;

            if (AllowsLeft(handSelection) &&
                TryResolveHandHit(spatial.left_hand, false, findByHierarchyPath, findByNameOrAlias, out target))
                return true;

            return false;
        }

        static bool TryResolveHandHit(
            HandRaySnapshot hand,
            bool preferActionable,
            System.Func<string, GameObject> findByHierarchyPath,
            System.Func<string, GameObject> findByNameOrAlias,
            out GameObject target)
        {
            target = null;
            if (hand == null || !hand.is_available)
                return false;

            bool hasHit = preferActionable ? hand.has_actionable_hit : hand.has_hit;
            if (!hasHit)
                return false;

            string hitPath = preferActionable ? hand.actionable_hit_object_path : hand.hit_object_path;
            string hitName = preferActionable ? hand.actionable_hit_object_name : hand.hit_object_name;

            target = findByHierarchyPath != null ? findByHierarchyPath(hitPath) : null;
            if (target != null)
                return true;

            target = findByNameOrAlias != null ? findByNameOrAlias(hitName) : null;
            return target != null;
        }

        static bool AllowsLeft(HandSelection handSelection)
        {
            return handSelection == HandSelection.None ||
                   handSelection == HandSelection.Left ||
                   handSelection == HandSelection.Either ||
                   handSelection == HandSelection.Both;
        }

        static bool AllowsRight(HandSelection handSelection)
        {
            return handSelection == HandSelection.None ||
                   handSelection == HandSelection.Right ||
                   handSelection == HandSelection.Either ||
                   handSelection == HandSelection.Both;
        }

        static GameObject Normalize(System.Func<GameObject, GameObject> normalizeTarget, GameObject target)
        {
            return normalizeTarget != null ? normalizeTarget(target) : target;
        }
    }
}

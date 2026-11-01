using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lives on the player. Tracks which interactables the player is standing in,
/// and uses the closest one when the Interact button is pressed.
/// Requires the player's Rigidbody2D + Collider2D so trigger messages arrive here.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference interactAction;

    // Every interactable trigger we are currently overlapping
    private readonly List<IInteractable> inRange = new List<IInteractable>();

    private void OnEnable()
    {
        interactAction.action.Enable();
    }

    private void OnDisable()
    {
        interactAction.action.Disable();
        inRange.Clear();
    }

    private void Update()
    {
        // WasPressedThisFrame fires on the button going down, regardless of any Hold interaction
        if (!interactAction.action.WasPressedThisFrame()) return;

        IInteractable target = GetClosest();
        if (target != null && target.CanInteract)
            target.Interact(gameObject);
    }

    private IInteractable GetClosest()
    {
        IInteractable best = null;
        float bestDist = float.MaxValue;

        for (int i = inRange.Count - 1; i >= 0; i--)
        {
            // Remove anything destroyed while we were standing in it
            var mb = inRange[i] as MonoBehaviour;
            if (mb == null) { inRange.RemoveAt(i); continue; }

            float d = ((Vector2)(mb.transform.position - transform.position)).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = inRange[i];
            }
        }
        return best;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var interactable = other.GetComponentInParent<IInteractable>();
        if (interactable != null && !inRange.Contains(interactable))
            inRange.Add(interactable);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var interactable = other.GetComponentInParent<IInteractable>();
        if (interactable != null)
            inRange.Remove(interactable);
    }
}
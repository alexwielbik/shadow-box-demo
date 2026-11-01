using UnityEngine;

/// <summary>
/// Anything the player can use with the Interact button (switches, doors, levers, NPCs...).
/// PlayerInteractor finds these through trigger colliders and calls Interact().
/// </summary>
public interface IInteractable
{
    /// <summary>Called when a player presses Interact while in range.</summary>
    void Interact(GameObject interactor);

    /// <summary>Return false to temporarily block interaction (e.g. while the lever is still moving).</summary>
    bool CanInteract { get; }
}
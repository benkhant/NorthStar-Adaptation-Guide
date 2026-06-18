using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// This script talks directly to ARCore through AR Foundation's
// ARTrackedImageManager. Whenever a marker card is seen, moved, or
// lost, this is the script that hears about it first. It spawns the
// right object for each card (a node prefab, or a small colored dot
// for tails/heads), keeps their positions updated, and then passes
// the current set of objects along to ArrowManager and TaskManager
// so they can do their own logic with up to date information.
public class TrackedImageInfo : MonoBehaviour
{
    [SerializeField] ARTrackedImageManager m_TrackedImageManager;

    [Header("Node Prefabs")]
    [SerializeField] GameObject Node_10_Prefab;
    [SerializeField] GameObject Node_15_Prefab;
    [SerializeField] GameObject Node_20_Prefab;
    [SerializeField] GameObject Node_30_Prefab;
    [SerializeField] GameObject Node_45_Prefab;

    [Header("Managers")]
    [SerializeField] ArrowManager arrowManager;
    [SerializeField] TaskManager taskManager;

    // These three dictionaries are the "single source of truth" for
    // what cards currently exist in the scene. ArrowManager and
    // TaskManager both get a copy of these every time something
    // changes, instead of looking at ARCore directly themselves.
    public Dictionary<string, GameObject> spawnedNodes =
        new Dictionary<string, GameObject>();
    public Dictionary<string, GameObject> spawnedTails =
        new Dictionary<string, GameObject>();
    public Dictionary<string, GameObject> spawnedHeads =
        new Dictionary<string, GameObject>();

    // Card names in here are considered permanently hidden, even if
    // ARCore is still actively tracking them. This is how we hide
    // cards on purpose (like node_20 once it's "deleted") without
    // fighting against ARCore trying to show them again.
    private HashSet<string> lockedHidden = new HashSet<string>();

    // Subscribe/unsubscribe to ARCore's tracked image events. This is
    // how we get notified whenever a card appears, moves, or
    // disappears from the camera's view.
    void OnEnable() => m_TrackedImageManager.trackedImagesChanged += OnChanged;
    void OnDisable() => m_TrackedImageManager.trackedImagesChanged -= OnChanged;

    // ARCore fires this whenever something changes about any tracked
    // image. It can report multiple new, updated, and removed images
    // all in one call, so we just hand each one off to the right
    // method below.
    void OnChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (var newImage in eventArgs.added)
            HandleNewMarker(newImage);

        foreach (var updatedImage in eventArgs.updated)
            UpdateMarker(updatedImage);

        foreach (var removedImage in eventArgs.removed)
            RemoveMarker(removedImage);
    }

    // Called the very first time a specific marker card is detected.
    // Figures out what kind of card it is from its name, and spawns
    // the right object for it — a full node prefab for node cards,
    // or just a small colored sphere for tail/head cards.
    void HandleNewMarker(ARTrackedImage trackedImage)
    {
        string name = trackedImage.referenceImage.name;

        // Lift the object up slightly above the card so it renders
        // above the marker instead of clipping into it.
        Vector3 spawnPosition = trackedImage.transform.position +
            new Vector3(0, 0.05f, 0);

        // Rotate the object to face the camera so labels and numbers
        // are readable instead of facing some random direction.
        Vector3 directionToCamera = -(Camera.main.transform.position - spawnPosition);
        directionToCamera.y = 0;
        Quaternion faceCamera = directionToCamera != Vector3.zero ?
            Quaternion.LookRotation(directionToCamera) : Quaternion.identity;

        if (name.StartsWith("node_"))
        {
            // Node cards get their own dedicated prefab (cube + label)
            GameObject prefabToSpawn = GetNodePrefab(name);
            if (prefabToSpawn != null)
            {
                GameObject spawnedNode = Instantiate(prefabToSpawn,
                    spawnPosition, faceCamera);
                spawnedNodes[name] = spawnedNode;
            }
        }
        else if (name.StartsWith("tail_"))
        {
            // Tail cards (outgoing pointers) just get a simple orange dot
            GameObject dot = CreateDot(spawnPosition);
            var renderer = dot.GetComponent<Renderer>();
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = new Color(1f, 0.5f, 0f);
            spawnedTails[name] = dot;
        }
        else if (name.StartsWith("head_"))
        {
            // Head cards (incoming pointers) get a purple dot instead,
            // so it's easy to tell tails and heads apart at a glance
            GameObject dot = CreateDot(spawnPosition);
            var renderer = dot.GetComponent<Renderer>();
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = new Color(0.5f, 0f, 1f);
            spawnedHeads[name] = dot;
        }

        // Let ArrowManager and TaskManager know about the new card
        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    // Called every time ARCore reports a position or tracking state
    // change for a card it has already seen before. This is what
    // keeps everything moving smoothly as cards get shuffled around
    // on the table.
    void UpdateMarker(ARTrackedImage updatedImage)
    {
        string name = updatedImage.referenceImage.name;

        // If we've deliberately hidden this card, don't let ARCore
        // bring it back just because it's still technically tracking it.
        if (lockedHidden.Contains(name)) return;

        Vector3 updatedPosition = updatedImage.transform.position +
            new Vector3(0, 0.05f, 0);
        Vector3 directionToCamera = -(Camera.main.transform.position - updatedPosition);
        directionToCamera.y = 0;
        Quaternion faceCamera = directionToCamera != Vector3.zero ?
            Quaternion.LookRotation(directionToCamera) : Quaternion.identity;

        // We treat both Tracking and Limited as "show the object",
        // since ARCore drops to Limited very easily (even just from
        // slight motion blur) and treating it as fully lost would
        // make objects flicker on and off constantly.
        if (updatedImage.trackingState == TrackingState.Tracking ||
            updatedImage.trackingState == TrackingState.Limited)
        {
            if (spawnedNodes.ContainsKey(name))
            {
                spawnedNodes[name].SetActive(true);
                spawnedNodes[name].transform.position = updatedPosition;
                spawnedNodes[name].transform.rotation = faceCamera;
            }
            else if (spawnedTails.ContainsKey(name))
            {
                spawnedTails[name].SetActive(true);
                spawnedTails[name].transform.position = updatedPosition;
            }
            else if (spawnedHeads.ContainsKey(name))
            {
                spawnedHeads[name].SetActive(true);
                spawnedHeads[name].transform.position = updatedPosition;
            }
        }
        else
        {
            // Tracking fully lost (the "None" state) — hide the object.
            // Note this rarely happens in practice; cards usually sit
            // in Limited rather than dropping all the way to None,
            // which is why we can't rely on this branch alone to know
            // when a card has been physically removed.
            if (spawnedNodes.ContainsKey(name))
                spawnedNodes[name].SetActive(false);
            else if (spawnedTails.ContainsKey(name))
                spawnedTails[name].SetActive(false);
            else if (spawnedHeads.ContainsKey(name))
                spawnedHeads[name].SetActive(false);
        }

        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    // Called if ARCore completely removes a tracked image from its
    // internal list (different from just losing tracking — this
    // actually destroys the object). Cleans up the corresponding
    // Unity object so we don't leak GameObjects over a long session.
    void RemoveMarker(ARTrackedImage removedImage)
    {
        string name = removedImage.referenceImage.name;

        if (spawnedNodes.ContainsKey(name))
        {
            Destroy(spawnedNodes[name]);
            spawnedNodes.Remove(name);
        }
        else if (spawnedTails.ContainsKey(name))
        {
            Destroy(spawnedTails[name]);
            spawnedTails.Remove(name);
        }
        else if (spawnedHeads.ContainsKey(name))
        {
            Destroy(spawnedHeads[name]);
            spawnedHeads.Remove(name);
        }

        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    // Called by TaskManager when a card should be hidden for good —
    // for example node_20 once deletion is complete, or tail_20 and
    // head_20 once they're no longer relevant. Once a name is added
    // here, UpdateMarker will keep ignoring it even if ARCore is
    // still technically tracking the card.
    public void LockHidden(string name)
    {
        lockedHidden.Add(name);
        if (spawnedNodes.ContainsKey(name))
            spawnedNodes[name].SetActive(false);
        else if (spawnedTails.ContainsKey(name))
            spawnedTails[name].SetActive(false);
        else if (spawnedHeads.ContainsKey(name))
            spawnedHeads[name].SetActive(false);
    }

    // Small helper that spawns a plain sphere to represent a tail or
    // head pointer. The collider is removed since these are purely
    // visual and don't need to interact physically with anything.
    GameObject CreateDot(Vector3 position)
    {
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.transform.position = position;
        dot.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
        Destroy(dot.GetComponent<Collider>());
        return dot;
    }

    // Maps a card's name to the matching prefab reference set in the
    // Inspector. Just a lookup table so HandleNewMarker doesn't need
    // a long if/else chain.
    GameObject GetNodePrefab(string name)
    {
        switch (name)
        {
            case "node_10": return Node_10_Prefab;
            case "node_15": return Node_15_Prefab;
            case "node_20": return Node_20_Prefab;
            case "node_30": return Node_30_Prefab;
            case "node_45": return Node_45_Prefab;
            default: return null;
        }
    }
}
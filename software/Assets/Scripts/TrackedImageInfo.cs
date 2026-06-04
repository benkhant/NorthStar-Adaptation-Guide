using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// This script is the brain of the AR tracking system.
// It watches the camera feed and responds when marker cards are detected,
// moved, or removed from view.
// There are three types of marker cards:
//   - node cards  → spawn a blue numbered box (the linked list node)
//   - tail cards  → spawn a small orange dot (represents outgoing pointer)
//   - head cards  → spawn a small purple dot (represents incoming connection)
public class TrackedImageInfo : MonoBehaviour
{
    // The AR component that does the actual image detection
    [SerializeField] ARTrackedImageManager m_TrackedImageManager;

    // The 3D node objects that appear above each node card
    [Header("Node Prefabs")]
    [SerializeField] GameObject Node_10_Prefab;
    [SerializeField] GameObject Node_15_Prefab;
    [SerializeField] GameObject Node_20_Prefab;
    [SerializeField] GameObject Node_30_Prefab;
    [SerializeField] GameObject Node_45_Prefab;

    // Other scripts that need to know where all the cards are
    [Header("Managers")]
    [SerializeField] ArrowManager arrowManager;  // draws arrows between cards
    [SerializeField] TaskManager taskManager;    // shows task instructions

    // Keep track of everything currently visible on the table
    // Key = marker name (e.g. "node_10"), Value = the spawned 3D object
    public Dictionary<string, GameObject> spawnedNodes =
        new Dictionary<string, GameObject>();
    public Dictionary<string, GameObject> spawnedTails =
        new Dictionary<string, GameObject>();
    public Dictionary<string, GameObject> spawnedHeads =
        new Dictionary<string, GameObject>();

    // Start listening for marker events when this script turns on
    void OnEnable() => m_TrackedImageManager.trackedImagesChanged += OnChanged;

    // Stop listening when this script turns off to avoid errors
    void OnDisable() => m_TrackedImageManager.trackedImagesChanged -= OnChanged;

    // This runs automatically whenever any marker card changes state
    // ARFoundation gives us three lists: newly seen, still visible, and gone
    void OnChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (var newImage in eventArgs.added)
            HandleNewMarker(newImage);      // first time seeing this card

        foreach (var updatedImage in eventArgs.updated)
            UpdateMarker(updatedImage);     // card already seen, may have moved

        foreach (var removedImage in eventArgs.removed)
            RemoveMarker(removedImage);     // card left the camera view
    }

    // Called the first time a marker card is detected
    // Figures out what type of card it is and creates the right AR object above it
    void HandleNewMarker(ARTrackedImage trackedImage)
    {
        string name = trackedImage.referenceImage.name;

        // Spawn the AR object 5cm above the physical card so it floats visibly
        Vector3 spawnPosition = trackedImage.transform.position +
            new Vector3(0, 0.05f, 0);

        // Make the AR object face toward whoever is holding the tablet
        // The direction is negated because the prefab's front face points backward
        Vector3 directionToCamera = -(Camera.main.transform.position - spawnPosition);
        directionToCamera.y = 0; // keep it upright, don't tilt up or down
        Quaternion faceCamera = directionToCamera != Vector3.zero ?
            Quaternion.LookRotation(directionToCamera) : Quaternion.identity;

        if (name.StartsWith("node_"))
        {
            // Spawn the matching blue numbered node box
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
            // Spawn a small orange dot — orange = outgoing pointer
            GameObject dot = CreateDot(spawnPosition);
            var renderer = dot.GetComponent<Renderer>();
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = new Color(1f, 0.5f, 0f); // orange
            spawnedTails[name] = dot;
        }
        else if (name.StartsWith("head_"))
        {
            // Spawn a small purple dot — purple = incoming connection
            GameObject dot = CreateDot(spawnPosition);
            var renderer = dot.GetComponent<Renderer>();
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = new Color(0.5f, 0f, 1f); // purple
            spawnedHeads[name] = dot;
        }

        // Tell ArrowManager and TaskManager about the new card
        // so arrows and instructions update right away
        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    // Makes a small sphere to use as a dot indicator above tail and head cards
    // The collider is removed because the dot is just visual, not physical
    GameObject CreateDot(Vector3 position)
    {
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.transform.position = position;
        dot.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f); // 2cm wide
        Destroy(dot.GetComponent<Collider>());
        return dot;
    }

    // Called every frame for cards that are already being tracked
    // Updates position if the card moved, or hides the AR object if tracking was lost
    void UpdateMarker(ARTrackedImage updatedImage)
    {
        string name = updatedImage.referenceImage.name;

        // Recalculate where the AR object should be based on current card position
        Vector3 updatedPosition = updatedImage.transform.position +
            new Vector3(0, 0.05f, 0);
        Vector3 directionToCamera = -(Camera.main.transform.position - updatedPosition);
        directionToCamera.y = 0;
        Quaternion faceCamera = directionToCamera != Vector3.zero ?
            Quaternion.LookRotation(directionToCamera) : Quaternion.identity;

        if (updatedImage.trackingState == TrackingState.Tracking)
        {
            // Card is clearly visible — show and move the AR object to match
            if (spawnedNodes.ContainsKey(name))
            {
                spawnedNodes[name].SetActive(true);
                spawnedNodes[name].transform.position = updatedPosition;
                spawnedNodes[name].transform.rotation = faceCamera; // nodes always face camera
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
            // Card is not clearly visible — hide the AR object until it comes back
            if (spawnedNodes.ContainsKey(name))
                spawnedNodes[name].SetActive(false);
            else if (spawnedTails.ContainsKey(name))
                spawnedTails[name].SetActive(false);
            else if (spawnedHeads.ContainsKey(name))
                spawnedHeads[name].SetActive(false);
        }

        // Keep managers in sync with latest card positions
        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    // Called when a card fully leaves the camera view
    // Destroys the AR object and removes it from the dictionary to free memory
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

        // Update managers so they stop referencing the removed card
        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    // Matches a node marker name to its prefab
    // Returns null if the name is not recognized
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
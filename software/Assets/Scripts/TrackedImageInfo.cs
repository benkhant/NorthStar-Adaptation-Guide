using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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

    public Dictionary<string, GameObject> spawnedNodes =
        new Dictionary<string, GameObject>();
    public Dictionary<string, GameObject> spawnedTails =
        new Dictionary<string, GameObject>();
    public Dictionary<string, GameObject> spawnedHeads =
        new Dictionary<string, GameObject>();

    private HashSet<string> lockedHidden = new HashSet<string>();

    void OnEnable() => m_TrackedImageManager.trackedImagesChanged += OnChanged;
    void OnDisable() => m_TrackedImageManager.trackedImagesChanged -= OnChanged;

    void OnChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (var newImage in eventArgs.added)
            HandleNewMarker(newImage);

        foreach (var updatedImage in eventArgs.updated)
            UpdateMarker(updatedImage);

        foreach (var removedImage in eventArgs.removed)
            RemoveMarker(removedImage);
    }

    void HandleNewMarker(ARTrackedImage trackedImage)
    {
        string name = trackedImage.referenceImage.name;

        Vector3 spawnPosition = trackedImage.transform.position +
            new Vector3(0, 0.05f, 0);

        Vector3 directionToCamera = -(Camera.main.transform.position - spawnPosition);
        directionToCamera.y = 0;
        Quaternion faceCamera = directionToCamera != Vector3.zero ?
            Quaternion.LookRotation(directionToCamera) : Quaternion.identity;

        if (name.StartsWith("node_"))
        {
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
            GameObject dot = CreateDot(spawnPosition);
            var renderer = dot.GetComponent<Renderer>();
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = new Color(1f, 0.5f, 0f);
            spawnedTails[name] = dot;
        }
        else if (name.StartsWith("head_"))
        {
            GameObject dot = CreateDot(spawnPosition);
            var renderer = dot.GetComponent<Renderer>();
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = new Color(0.5f, 0f, 1f);
            spawnedHeads[name] = dot;
        }

        arrowManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
        taskManager.UpdateMarkers(spawnedNodes, spawnedTails, spawnedHeads);
    }

    void UpdateMarker(ARTrackedImage updatedImage)
    {
        string name = updatedImage.referenceImage.name;

        if (lockedHidden.Contains(name)) return;

        Vector3 updatedPosition = updatedImage.transform.position +
            new Vector3(0, 0.05f, 0);
        Vector3 directionToCamera = -(Camera.main.transform.position - updatedPosition);
        directionToCamera.y = 0;
        Quaternion faceCamera = directionToCamera != Vector3.zero ?
            Quaternion.LookRotation(directionToCamera) : Quaternion.identity;

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

    GameObject CreateDot(Vector3 position)
    {
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.transform.position = position;
        dot.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
        Destroy(dot.GetComponent<Collider>());
        return dot;
    }

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
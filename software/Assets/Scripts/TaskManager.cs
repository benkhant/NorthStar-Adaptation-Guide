using System.Collections.Generic;
using UnityEngine;
using TMPro;

// This script manages the task instructions and tracks progress
// through the linked list deletion activity.
// Students remove node 20 from the list 10 -> 20 -> 30
// by first updating the linkages then physically removing the node card.
public class TaskManager : MonoBehaviour
{
    [SerializeField] TextMeshPro instructionText;
    [SerializeField] TrackedImageInfo trackedImageInfo;

    private Dictionary<string, GameObject> spawnedNodes;
    private Dictionary<string, GameObject> spawnedTails;
    private Dictionary<string, GameObject> spawnedHeads;

    private float deletionTimer = 0f;
    private bool countingDown = false;

    private float connectionThreshold = 0.15f;

    public enum TaskState
    {
        PlacingNodes,       // waiting for all 3 node cards
        BuildingStartList,  // guides student to build 10 -> 20 -> 30
        ShowingStartList,   // all nodes placed — showing starting state
        RemoveNode20,       // remove node_20 card from table
        DeletionComplete    // node_20 removed — done
    }

    public TaskState currentState = TaskState.PlacingNodes;

    // Correct starting connections: 10 -> 20 -> 30
    private Dictionary<string, string> correctBefore =
        new Dictionary<string, string>
    {
        { "tail_10", "head_20" },
        { "tail_20", "head_30" }
    };

    // Correct connections after updating linkages: 10 -> 30
    private Dictionary<string, string> correctAfter =
        new Dictionary<string, string>
    {
        { "tail_10", "head_30" }
    };

    void Update()
    {
        // timer runs after student is told to remove node_20 group
        if (countingDown)
        {
            deletionTimer += Time.deltaTime;
            if (deletionTimer >= 10f)
            {
                currentState = TaskState.DeletionComplete;
                instructionText.text = "Deletion complete!\n10 -> 30\nNode 20 has been removed from memory";
                countingDown = false;

                // hide node_20 group immediately on completion
                trackedImageInfo.LockHidden("node_20");
            }
            return;
        }

        if (currentState == TaskState.DeletionComplete) return;

        if (spawnedNodes == null || spawnedNodes.Count == 0)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Point camera at node cards to begin";
            return;
        }

        int visibleNodes = CountVisible(spawnedNodes);

        if (visibleNodes < 3)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Place all 3 node cards on the table\n"
                + visibleNodes + "/3 nodes detected";
            UpdateNodeColors();
            return;
        }

        // Step 3 — pointer redirected, start countdown
        bool tail10Correct = IsTailCorrectlyConnected("tail_10", "head_30");
        if (tail10Correct && !countingDown)
        {
            currentState = TaskState.RemoveNode20;
            instructionText.text = "Pointer updated!\nNode 20 is now unreachable\nRemove node_20, tail_20, and head_20 cards from the table";
            countingDown = true;
            deletionTimer = 0f;
            UpdateNodeColors();

            trackedImageInfo.LockHidden("tail_20");
            trackedImageInfo.LockHidden("head_20");

            return;
        }

        // Step 2 — starting list built
        if (IsLinkageCorrect(correctBefore))
        {
            currentState = TaskState.ShowingStartList;
            instructionText.text = "The list is: 10 -> 20 -> 30\nTask: Remove node 20\nMove tail_10 to head_30 to redirect the pointer";
            UpdateNodeColors();
            return;
        }

        // Step 1b — one connection done
        bool tail10Connected = IsTailCorrectlyConnected("tail_10", "head_20");
        bool tail20Connected = IsTailCorrectlyConnected("tail_20", "head_30");

        if (tail10Connected && !tail20Connected)
        {
            currentState = TaskState.BuildingStartList;
            instructionText.text = "Good! Now connect tail_20 to head_30";
            UpdateNodeColors();
            return;
        }

        if (!tail10Connected && tail20Connected)
        {
            currentState = TaskState.BuildingStartList;
            instructionText.text = "Good! Now connect tail_10 to head_20";
            UpdateNodeColors();
            return;
        }

        // Step 1a — nothing connected yet
        currentState = TaskState.BuildingStartList;
        instructionText.text = "All nodes placed!\nConnect tail_10 to head_20\nand tail_20 to head_30";
        UpdateNodeColors();
    }

    void LateUpdate()
    {
        if (Camera.main != null)
        {
            instructionText.transform.position = Camera.main.transform.position +
                Camera.main.transform.forward * 0.4f +
                Vector3.up * 0.1f;
            Vector3 directionToCamera = Camera.main.transform.position -
                instructionText.transform.position;
            directionToCamera.y = 0;
            if (directionToCamera != Vector3.zero)
            {
                instructionText.transform.rotation =
                    Quaternion.LookRotation(-directionToCamera);
            }
        }
    }

    public void UpdateMarkers(
        Dictionary<string, GameObject> nodes,
        Dictionary<string, GameObject> tails,
        Dictionary<string, GameObject> heads)
    {
        spawnedNodes = nodes;
        spawnedTails = tails;
        spawnedHeads = heads;
    }

    int CountVisible(Dictionary<string, GameObject> dict)
    {
        int count = 0;
        foreach (var obj in dict.Values)
        {
            if (obj.activeSelf) count++;
        }
        return count;
    }

    // Checks if a specific tail is connected to a specific head
    bool IsTailCorrectlyConnected(string tailName, string headName)
    {
        if (spawnedTails == null || spawnedHeads == null) return false;
        if (!spawnedTails.ContainsKey(tailName) ||
            !spawnedTails[tailName].activeSelf) return false;
        if (!spawnedHeads.ContainsKey(headName) ||
            !spawnedHeads[headName].activeSelf) return false;

        float distance = Vector3.Distance(
            spawnedTails[tailName].transform.position,
            spawnedHeads[headName].transform.position);

        return distance <= connectionThreshold;
    }

    bool IsLinkageCorrect(Dictionary<string, string> expectedLinkage)
    {
        if (spawnedTails == null || spawnedHeads == null) return false;

        foreach (var link in expectedLinkage)
        {
            string tailName = link.Key;
            string expectedHead = link.Value;

            if (!spawnedTails.ContainsKey(tailName) ||
                !spawnedTails[tailName].activeSelf) return false;

            if (!spawnedHeads.ContainsKey(expectedHead) ||
                !spawnedHeads[expectedHead].activeSelf) return false;

            float distance = Vector3.Distance(
                spawnedTails[tailName].transform.position,
                spawnedHeads[expectedHead].transform.position);

            if (distance > connectionThreshold) return false;
        }
        return true;
    }

    void UpdateNodeColors()
    {
        if (spawnedNodes == null) return;

        string[] allNodes = { "node_10", "node_20", "node_30" };

        foreach (var key in allNodes)
        {
            if (!spawnedNodes.ContainsKey(key) ||
                !spawnedNodes[key].activeSelf) continue;

            NodeColorController colorController =
                spawnedNodes[key].GetComponent<NodeColorController>();
            if (colorController == null) continue;

            if (key == "node_30")
            {
                if (currentState == TaskState.ShowingStartList ||
                    currentState == TaskState.RemoveNode20 ||
                    currentState == TaskState.DeletionComplete)
                    colorController.SetCorrect();
                else
                    colorController.SetDefault();
                continue;
            }

            string tailName = "tail_" + key.Split('_')[1];

            if (IsNodeCorrectlyLinked(tailName))
                colorController.SetCorrect();
            else
                colorController.SetDefault();
        }
    }

    bool IsNodeCorrectlyLinked(string tailName)
    {
        if (spawnedTails == null || spawnedHeads == null) return false;
        if (!spawnedTails.ContainsKey(tailName) ||
            !spawnedTails[tailName].activeSelf) return false;

        // Use correctAfter during deletion stages
        // Use correctBefore during setup stage
        Dictionary<string, string> targetLinkage =
            currentState == TaskState.DeletionComplete ||
            currentState == TaskState.RemoveNode20
            ? correctAfter : correctBefore;

        if (!targetLinkage.ContainsKey(tailName)) return false;

        string expectedHead = targetLinkage[tailName];

        if (!spawnedHeads.ContainsKey(expectedHead) ||
            !spawnedHeads[expectedHead].activeSelf) return false;

        float distance = Vector3.Distance(
            spawnedTails[tailName].transform.position,
            spawnedHeads[expectedHead].transform.position);

        return distance <= connectionThreshold;
    }

    public Dictionary<string, string> GetCorrectBefore() => correctBefore;
    public Dictionary<string, string> GetCorrectAfter() => correctAfter;
}
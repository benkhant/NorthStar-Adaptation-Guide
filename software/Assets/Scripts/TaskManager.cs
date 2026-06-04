using System.Collections.Generic;
using UnityEngine;
using TMPro;

// This script manages the task instructions and tracks progress
// through the linked list insertion activity.
// It watches what cards are on the table, figures out what stage
// the student is at, shows the right instruction text, and colors
// nodes green or blue based on whether they are correctly linked.
public class TaskManager : MonoBehaviour
{
    // The floating text that shows instructions to the student
    [SerializeField] TextMeshPro instructionText;

    // Latest positions of all detected marker cards
    private Dictionary<string, GameObject> spawnedNodes;
    private Dictionary<string, GameObject> spawnedTails;
    private Dictionary<string, GameObject> spawnedHeads;

    // Same threshold as ArrowManager — cards must be within 15cm to count as connected
    private float connectionThreshold = 0.15f;

    // The four stages of the activity
    // The current state determines what instruction is shown and
    // which connections count as correct for node color feedback
    public enum TaskState
    {
        PlacingNodes,       // not enough node cards on the table yet
        BuildingStartList,  // all nodes visible, building 10->15->30->45
        InsertingNode20,    // start list correct, now inserting node 20
        InsertionComplete   // full list 10->15->20->30->45 is correct
    }

    // Tracks which stage the student is currently at
    // Public so ArrowManager can read it for color feedback
    public TaskState currentState = TaskState.PlacingNodes;

    // The correct connections before node 20 is inserted
    // 10 -> 15 -> 30 -> 45
    private Dictionary<string, string> correctBefore =
        new Dictionary<string, string>
    {
        { "tail_10", "head_15" },
        { "tail_15", "head_30" },
        { "tail_30", "head_45" }
    };

    // The correct connections after node 20 is inserted
    // 10 -> 15 -> 20 -> 30 -> 45
    private Dictionary<string, string> correctInsertion =
        new Dictionary<string, string>
    {
        { "tail_10", "head_15" },
        { "tail_15", "head_20" },
        { "tail_20", "head_30" },
        { "tail_30", "head_45" }
    };

    // Runs every frame — checks the current state of the table
    // and shows the appropriate instruction text
    void Update()
    {
        // No cards detected yet — show the starting message
        if (spawnedNodes == null || spawnedNodes.Count == 0)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Point camera at node cards to begin";
            return;
        }

        int visibleNodes = CountVisible(spawnedNodes);

        // Not enough node cards on the table yet
        if (visibleNodes < 4)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Place node cards on the table\n"
                + visibleNodes + " nodes detected";
            UpdateNodeColors();
            return;
        }

        // Base 4 nodes are visible but node 20 has not been placed yet
        bool node20Visible = spawnedNodes.ContainsKey("node_20") &&
                             spawnedNodes["node_20"].activeSelf;
        if (!node20Visible && visibleNodes >= 4)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Good! Now place node 20 on the table\nbetween node 15 and node 30";
            UpdateNodeColors();
            return;
        }

        // Check insertion complete first — it is the goal state
        if (IsLinkageCorrect(correctInsertion))
        {
            currentState = TaskState.InsertionComplete;
            instructionText.text = "Insertion complete!\n10 -> 15 -> 20 -> 30 -> 45";
            UpdateNodeColors();
            return;
        }

        // Starting list is correct — guide student to perform the insertion
        if (IsLinkageCorrect(correctBefore))
        {
            currentState = TaskState.BuildingStartList;
            instructionText.text = "Starting list correct!\nNow insert node 20\nMove tail_15 to head_20 and tail_20 to head_30";
            UpdateNodeColors();
            return;
        }

        // All nodes visible but connections not correct yet
        currentState = TaskState.InsertingNode20;
        instructionText.text = "Connect tail and head cards\nto build: 10 -> 15 -> 30 -> 45";
        UpdateNodeColors();
    }

    // Runs after Update every frame — keeps the instruction text
    // floating in front of the camera at a comfortable reading position
    void LateUpdate()
    {
        if (Camera.main != null)
        {
            // Place text 40cm in front of camera and 10cm above center
            instructionText.transform.position = Camera.main.transform.position +
                Camera.main.transform.forward * 0.4f +
                Vector3.up * 0.1f;

            // Rotate text to face the camera while staying upright
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

    // Called by TrackedImageInfo whenever any marker card changes
    // Keeps the local card positions up to date
    public void UpdateMarkers(
        Dictionary<string, GameObject> nodes,
        Dictionary<string, GameObject> tails,
        Dictionary<string, GameObject> heads)
    {
        spawnedNodes = nodes;
        spawnedTails = tails;
        spawnedHeads = heads;
    }

    // Counts how many objects in a dictionary are currently visible
    int CountVisible(Dictionary<string, GameObject> dict)
    {
        int count = 0;
        foreach (var obj in dict.Values)
        {
            if (obj.activeSelf) count++;
        }
        return count;
    }

    // Checks whether the physical card arrangement matches an expected linkage
    // Every tail in the expected linkage must be within 15cm of its expected head
    // If even one pair is missing or too far apart the whole check fails
    bool IsLinkageCorrect(Dictionary<string, string> expectedLinkage)
    {
        if (spawnedTails == null || spawnedHeads == null) return false;

        foreach (var link in expectedLinkage)
        {
            string tailName = link.Key;
            string expectedHead = link.Value;

            // Tail card must be visible
            if (!spawnedTails.ContainsKey(tailName) ||
                !spawnedTails[tailName].activeSelf) return false;

            // Expected head card must be visible
            if (!spawnedHeads.ContainsKey(expectedHead) ||
                !spawnedHeads[expectedHead].activeSelf) return false;

            // Tail and head must be physically close enough to count as connected
            float distance = Vector3.Distance(
                spawnedTails[tailName].transform.position,
                spawnedHeads[expectedHead].transform.position);

            if (distance > connectionThreshold) return false;
        }
        return true;
    }

    // Colors each visible node green if its tail is correctly linked
    // or resets it to default blue if not
    void UpdateNodeColors()
    {
        if (spawnedNodes == null) return;

        string[] allNodes = { "node_10", "node_15", "node_20",
                              "node_30", "node_45" };

        foreach (var key in allNodes)
        {
            if (!spawnedNodes.ContainsKey(key) ||
                !spawnedNodes[key].activeSelf) continue;

            NodeColorController colorController =
                spawnedNodes[key].GetComponent<NodeColorController>();
            if (colorController == null) continue;

            // Convert node name to tail name e.g. "node_15" -> "tail_15"
            string tailName = "tail_" + key.Split('_')[1];

            if (IsNodeCorrectlyLinked(tailName))
                colorController.SetCorrect(); // green
            else
                colorController.SetDefault(); // blue
        }
    }

    // Checks whether a specific tail card is correctly connected
    // based on the current task state
    // Uses correctInsertion during insertion stage and correctBefore otherwise
    bool IsNodeCorrectlyLinked(string tailName)
    {
        if (spawnedTails == null || spawnedHeads == null) return false;
        if (!spawnedTails.ContainsKey(tailName) ||
            !spawnedTails[tailName].activeSelf) return false;

        // Pick the right answer key based on what stage the student is at
        Dictionary<string, string> targetLinkage =
            currentState == TaskState.InsertionComplete ||
            currentState == TaskState.InsertingNode20
            ? correctInsertion : correctBefore;

        if (!targetLinkage.ContainsKey(tailName)) return false;

        string expectedHead = targetLinkage[tailName];

        if (!spawnedHeads.ContainsKey(expectedHead) ||
            !spawnedHeads[expectedHead].activeSelf) return false;

        // Check the tail and its expected head are physically close enough
        float distance = Vector3.Distance(
            spawnedTails[tailName].transform.position,
            spawnedHeads[expectedHead].transform.position);

        return distance <= connectionThreshold;
    }

    // These allow ArrowManager to access the correct linkage dictionaries
    // without making them fully public
    public Dictionary<string, string> GetCorrectBefore() => correctBefore;
    public Dictionary<string, string> GetCorrectInsertion() => correctInsertion;
}
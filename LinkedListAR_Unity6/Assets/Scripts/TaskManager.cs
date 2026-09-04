using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Tests")]

// This script runs the whole deletion activity./
// It keeps track of what step the student is on, checks if cards
// are connected correctly, and updates the instruction text.
// The goal: students remove node 20 from the list 10 -> 20 -> 30.
public class TaskManager : MonoBehaviour
{
    [SerializeField] TextMeshPro instructionText;
    [SerializeField] TrackedImageInfo trackedImageInfo;
    [SerializeField] ArrowManager arrowManager;

    // These get filled in every frame by the script that talks to
    // ARCore. Each dictionary just maps a card name (like "tail_10")
    // to the actual object Unity is showing for it.
    private Dictionary<string, GameObject> spawnedNodes;
    private Dictionary<string, GameObject> spawnedTails;
    private Dictionary<string, GameObject> spawnedHeads;

    // Once the pointer is redirected correctly, we count down 10
    // seconds before calling the deletion "done".
    private float deletionTimer = 0f;
    private bool countingDown = false;

    // How close a tail and head need to be (in Unity units) to count
    // as "connected". Found this number by testing on the tablet.
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

    // What the list should look like before deletion: 10 -> 20 -> 30
    private Dictionary<string, string> correctBefore =
        new Dictionary<string, string>
    {
        { "tail_10", "head_20" },
        { "tail_20", "head_30" }
    };

    // What the list should look like after deletion: 10 -> 30
    // (this is what happens in code when you reassign the next pointer)
    private Dictionary<string, string> correctAfter =
        new Dictionary<string, string>
    {
        { "tail_10", "head_30" }
    };

    // This runs every single frame and is basically the brain of the
    // activity. Each block below checks one thing, and if it's true,
    // moves to the next step. Order matters — once one block runs and
    // returns, nothing below it runs that frame.
    void Update()
    {
        // Let the arrow script know what step we're on, so it can
        // decide which connections should look green or red.
        arrowManager.currentTaskState = currentState;

        // We're counting down to "deletion complete". This starts
        // once the student redirects the pointer correctly. We use a
        // plain timer instead of checking if the node_20 card is
        // gone, because ARCore doesn't reliably tell us when a card
        // has actually been removed — it tends to leave a "ghost"
        // behind.
        if (countingDown)
        {
            deletionTimer += Time.deltaTime;
            if (deletionTimer >= 10f)
            {
                currentState = TaskState.DeletionComplete;
                instructionText.text = "Deletion complete!\n10 -> 30\nNode 20 has been removed from memory";
                countingDown = false;

                // Hide node_20 so it doesn't keep showing up
                // at its last known spot.
                if (trackedImageInfo != null) trackedImageInfo.LockHidden("node_20");
            }
            return;
        }

        // Once we're done, stay done. Without this, the checks below
        // could accidentally send us back to an earlier step.
        if (currentState == TaskState.DeletionComplete) return;

        // No cards detected at all yet.
        if (spawnedNodes == null || spawnedNodes.Count == 0)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Point camera at node cards to begin";
            return;
        }

        int visibleNodes = CountVisible(spawnedNodes);

        // Not all 3 node cards are on the table yet — keep waiting.
        if (visibleNodes < 3)
        {
            currentState = TaskState.PlacingNodes;
            instructionText.text = "Place all 3 node cards on the table\n"
                + visibleNodes + "/3 nodes detected";
            UpdateNodeColors();
            return;
        }

        // This is the actual "delete the node" moment — the student
        // moved tail_10 onto head_30, skipping node_20 entirely. We
        // only let this fire once (countingDown guard) and only after
        // the starting list was already confirmed, so it can't
        // accidentally trigger early.
        bool tail10Correct = IsTailCorrectlyConnected("tail_10", "head_30");
        if (tail10Correct && !countingDown && currentState == TaskState.ShowingStartList)
        {
            currentState = TaskState.RemoveNode20;
            instructionText.text = "Pointer updated!\nNode 20 is now unreachable\nRemove node_20, tail_20, and head_20 cards from the table";
            countingDown = true;
            deletionTimer = 0f;

            // tail_20 and head_20 don't mean anything anymore once
            // the list has been redirected, so we hide them right
            // away to keep things visually clean. Only node_20
            // actually needs to be physically removed for the timer
            // above to count as "done" though.
            if (trackedImageInfo != null) trackedImageInfo.LockHidden("tail_20");
            if (trackedImageInfo != null) trackedImageInfo.LockHidden("head_20"); UpdateNodeColors();
            return;
        }

        // Both starting connections are made — let the student know
        // the list is built correctly before asking them to delete
        // anything.
        if (IsLinkageCorrect(correctBefore))
        {
            currentState = TaskState.ShowingStartList;
            instructionText.text = "The list is: 10 -> 20 -> 30\nTask: Remove node 20\nMove tail_10 to head_30 to redirect the pointer";
            UpdateNodeColors();
            return;
        }

        // Only one of the two connections is made so far — tell the
        // student exactly which one is still missing instead of
        // repeating the full instructions.
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

        // All 3 nodes are down but neither connection has been made
        // yet — this is the default starting instruction.
        currentState = TaskState.BuildingStartList;
        instructionText.text = "All nodes placed!\nConnect tail_10 to head_20\nand tail_20 to head_30";
        UpdateNodeColors();
    }

    // Keeps the instruction text floating in front of the camera and
    // facing the student, like a heads-up display, instead of being
    // stuck to one card.
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

    // Called every frame by the ARCore tracking script to pass along
    // whatever cards it can currently see.
    public void UpdateMarkers(
        Dictionary<string, GameObject> nodes,
        Dictionary<string, GameObject> tails,
        Dictionary<string, GameObject> heads)
    {
        spawnedNodes = nodes;
        spawnedTails = tails;
        spawnedHeads = heads;
    }

    // Simple helper — counts how many cards in a dictionary are
    // currently visible to the camera.
    int CountVisible(Dictionary<string, GameObject> dict)
    {
        int count = 0;
        foreach (var obj in dict.Values)
        {
            if (obj.activeSelf) count++;
        }
        return count;
    }

    // Checks if one specific tail card is close enough to one
    // specific head card to count as connected. Used when we just
    // need to check a single pair, like "is tail_10 on head_30 yet?"
    internal bool IsTailCorrectlyConnected(string tailName, string headName)
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

    // Checks if every tail-to-head pair in a given list is connected
    // correctly. Used when we want to check a whole structure at
    // once, like "is the full starting list built?"
    internal bool IsLinkageCorrect(Dictionary<string, string> expectedLinkage)
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

    // Updates the color of each node so students get visual feedback
    // on whether things are connected right. node_30 is a special
    // case since it has no outgoing pointer of its own — it just
    // turns "correct" once the list has reached ShowingStartList or
    // later.
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

    // Checks if a node's outgoing tail is connected the "right" way
    // for whatever step we're currently on. Before deletion, "right"
    // means matching the original list (correctBefore). After the
    // pointer redirect, "right" means matching the updated list
    // (correctAfter) instead.
    internal bool IsNodeCorrectlyLinked(string tailName)
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
    internal void SetMarkersForTesting(
    Dictionary<string, GameObject> tails,
    Dictionary<string, GameObject> heads)
    {
        spawnedTails = tails;
        spawnedHeads = heads;
    }

    // Lets other scripts (like ArrowManager) grab these linkages
    // directly instead of copying the same dictionaries again.
    public Dictionary<string, string> GetCorrectBefore() => correctBefore;
    public Dictionary<string, string> GetCorrectAfter() => correctAfter;
}